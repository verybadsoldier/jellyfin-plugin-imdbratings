using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ICU4N.Logging;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ImdbRatings
{
    /// <summary>
    /// Manages the downloading, caching, and retrieval of IMDb ratings.
    /// </summary>
    public class IMDbRatingsManager : IDisposable
    {
        private static readonly SemaphoreSlim _updateLock = new SemaphoreSlim(1, 1);

        private static Dictionary<int, float> _ratingsCache = new Dictionary<int, float>();
        private static DateTime _lastUpdate = DateTime.MinValue;
        private static bool _isLoaded = false;

        private readonly ILogger _logger;

        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="IMDbRatingsManager"/> class.
        /// </summary>
        /// <param name="logger">Logger.</param>
        public IMDbRatingsManager(ILogger logger)
        {
            _logger = logger;
        }

        private bool TryGetRating(string imdbId, out float rating)
        {
            rating = 0f;

            if (string.IsNullOrWhiteSpace(imdbId) || !imdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Invalid IMDb ID '{0}'", imdbId);
                return false;
            }

            if (int.TryParse(imdbId.AsSpan(2), out int numericId))
            {
                return _ratingsCache.TryGetValue(numericId, out rating);
            }

            return false;
        }

        /// <summary>
        /// Load or update the database.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task EnsureRatingsLoadedAsync()
        {
            if (_isLoaded && (DateTime.UtcNow - _lastUpdate).TotalHours < 24)
            {
                return;
            }

            await _updateLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Check if DB has been updated while we waited for the lock?
                if (_isLoaded && (DateTime.UtcNow - _lastUpdate).TotalHours < 24)
                {
                    return;
                }

                await UpdateRatingsCacheAsync().ConfigureAwait(false);
            }
            finally
            {
                _updateLock.Release();
            }
        }

        /// <summary>
        /// Gets the IMDb rating for a specific title ID, updating the cache if it is older than 24 hours.
        /// </summary>
        /// <param name="imdbId">The IMDb ID (e.g., tt0111161).</param>
        /// <returns>The average rating, or null if not found.</returns>
        public async Task<float?> GetRatingAsync(string imdbId)
        {
            await EnsureRatingsLoadedAsync().ConfigureAwait(false);

            if (TryGetRating(imdbId, out float rating))
            {
                return rating;
            }

            return null;
        }

        private async Task UpdateRatingsCacheAsync()
        {
            GC.Collect();
            long memoryBefore = GC.GetTotalMemory(true);

            string url = "https://datasets.imdbws.com/title.ratings.tsv.gz";
            using var client = new HttpClient();

            _logger.LogInformation("Downloading IMDb rating flat file from: {0}", url);

            using var responseStream = await client.GetStreamAsync(url).ConfigureAwait(false);
            using var gzipStream = new GZipStream(responseStream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzipStream);

            var newCache = new Dictionary<int, float>();

            await reader.ReadLineAsync().ConfigureAwait(false);

            string? line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                var parts = line.Split('\t');
                if (parts.Length >= 2)
                {
                    string imdbIdStr = parts[0];

                    if (!imdbIdStr.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("Invalid IMDb ID in IMDb database: '{}'", imdbIdStr);
                        continue;
                    }

                    if (int.TryParse(imdbIdStr.AsSpan(2), out int numericId))
                    {
                        if (float.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out float rating))
                        {
                            newCache[numericId] = rating;
                        }
                    }
                }
            }

            _ratingsCache = newCache;
            _lastUpdate = DateTime.UtcNow;
            _isLoaded = true;
            _logger.LogInformation("Finished updating IMDb rating DB. Number of entries: {0}", _ratingsCache.Count);

            GC.Collect();
            long memoryAfter = GC.GetTotalMemory(true);

            long sizeInMegabytes = (memoryAfter - memoryBefore) / 1024 / 1024;
            _logger.LogInformation("IMDb ratings database loaded. Memory footprint: {0} MB", sizeInMegabytes);
        }

        /// <summary>
        /// Disposes of the resources used by the manager.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the IMDbRatingsManager and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _updateLock.Dispose();
                }

                _disposed = true;
            }
        }
    }
}
