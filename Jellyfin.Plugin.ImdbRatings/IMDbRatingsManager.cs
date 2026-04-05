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
        private readonly SemaphoreSlim _updateLock = new SemaphoreSlim(1, 1);
        private readonly ILogger _logger;

        private Dictionary<string, float> _ratingsCache = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastUpdated = DateTime.MinValue;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="IMDbRatingsManager"/> class.
        /// </summary>
        /// <param name="logger">Logger.</param>
        public IMDbRatingsManager(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Gets the IMDb rating for a specific title ID, updating the cache if it is older than 24 hours.
        /// </summary>
        /// <param name="imdbId">The IMDb ID (e.g., tt0111161).</param>
        /// <returns>The average rating, or null if not found.</returns>
        public async Task<float?> GetRatingAsync(string imdbId)
        {
            if (DateTime.UtcNow - _lastUpdated > TimeSpan.FromHours(24))
            {
                await _updateLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (DateTime.UtcNow - _lastUpdated > TimeSpan.FromHours(24))
                    {
                        _logger.LogInformation("IMDb ratings data base too old (Last update: {0}), refreshing...", _lastUpdated);
                        await UpdateRatingsCacheAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    _updateLock.Release();
                }
            }

            if (_ratingsCache.TryGetValue(imdbId, out float rating))
            {
                _logger.LogInformation("Fetched IMDb rating from cache: {0}", rating);
                return rating;
            }

            return null;
        }

        private async Task UpdateRatingsCacheAsync()
        {
            string url = "https://datasets.imdbws.com/title.ratings.tsv.gz";
            using var client = new HttpClient();

            _logger.LogInformation("Downloading IMDb rating flat file from: {0}", url);

            using var responseStream = await client.GetStreamAsync(url).ConfigureAwait(false);
            using var gzipStream = new GZipStream(responseStream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzipStream);

            var newCache = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            await reader.ReadLineAsync().ConfigureAwait(false);

            string? line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                var parts = line.Split('\t');
                if (parts.Length >= 2)
                {
                    string tconst = parts[0];

                    // FIXED: Force the parser to always expect a dot as a decimal separator
                    if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float rating))
                    {
                        newCache[tconst] = rating;
                    }
                }
            }

            _ratingsCache = newCache;
            _lastUpdated = DateTime.UtcNow;
            _logger.LogInformation("Finished updating IMDb rating DB. Number of entries: {0}", _ratingsCache.Count);
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
