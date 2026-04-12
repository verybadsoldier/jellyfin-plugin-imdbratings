using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ImdbRatings
{
    /// <summary>
    /// Manages the downloading, caching, and retrieval of IMDb ratings using an embedded SQLite database.
    /// </summary>
    public class IMDbRatingsManager : IDisposable
    {
        private static readonly SemaphoreSlim _updateLock = new SemaphoreSlim(1, 1);
        private readonly ILogger _logger;
        private readonly string _dbPath;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="IMDbRatingsManager"/> class.
        /// </summary>
        /// <param name="logger">Logger.</param>
        public IMDbRatingsManager(ILogger logger)
        {
            _logger = logger;

            // Store the database inside the Jellyfin Plugin Data folder
            var dataPath = Plugin.Instance?.DataFolderPath ?? Path.GetTempPath();
            _dbPath = Path.Combine(dataPath, "imdbratings.db");
        }

        /// <summary>
        /// Load or update the database.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task PrepareDatabase()
        {
            // Check if the database file exists and was modified in the last 24 hours
            if (File.Exists(_dbPath))
            {
                var lastWrite = File.GetLastWriteTimeUtc(_dbPath);
                if ((DateTime.UtcNow - lastWrite).TotalHours < 24)
                {
                    return; // DB is fresh, skip update
                }
            }

            await _updateLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Double check in case another thread updated it while we waited for the lock
                if (File.Exists(_dbPath))
                {
                    var lastWrite = File.GetLastWriteTimeUtc(_dbPath);
                    if ((DateTime.UtcNow - lastWrite).TotalHours < 24)
                    {
                        return;
                    }
                }

                await RefreshDatabase().ConfigureAwait(false);
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
            await PrepareDatabase().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(imdbId) || !imdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Invalid IMDb ID '{0}'", imdbId);
                return null;
            }

            if (!int.TryParse(imdbId.AsSpan(2), out int numericId))
            {
                return null;
            }

            // Query the database directly instead of RAM
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Rating FROM Ratings WHERE Id = @id";
            command.Parameters.AddWithValue("@id", numericId);

            var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
            if (result != null && result != DBNull.Value)
            {
                return Convert.ToSingle(result, CultureInfo.InvariantCulture);
            }

            return null;
        }

        private async Task RefreshDatabase()
        {
            string url = "https://datasets.imdbws.com/title.ratings.tsv.gz";
            using var client = new HttpClient();

            _logger.LogInformation("Downloading IMDb rating flat file from: {0}", url);

            using var responseStream = await client.GetStreamAsync(url).ConfigureAwait(false);
            using var gzipStream = new GZipStream(responseStream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzipStream);

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync().ConfigureAwait(false);

            // Create table
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = "CREATE TABLE IF NOT EXISTS Ratings (Id INTEGER PRIMARY KEY, Rating REAL)";
            await createCmd.ExecuteNonQueryAsync().ConfigureAwait(false);

            using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);

            // Clear the old data
            using var clearCmd = connection.CreateCommand();
            clearCmd.Transaction = transaction;
            clearCmd.CommandText = "DELETE FROM Ratings";
            await clearCmd.ExecuteNonQueryAsync().ConfigureAwait(false);

            // Prepare reusable insert command
            using var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = "INSERT INTO Ratings (Id, Rating) VALUES (@id, @rating)";
            var idParam = insertCmd.Parameters.Add("@id", SqliteType.Integer);
            var ratingParam = insertCmd.Parameters.Add("@rating", SqliteType.Real);

            int entryCount = 0;

            await reader.ReadLineAsync().ConfigureAwait(false); // Skip header

            string? line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                var parts = line.Split('\t');
                if (parts.Length >= 2)
                {
                    string imdbIdStr = parts[0];

                    if (!imdbIdStr.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (int.TryParse(imdbIdStr.AsSpan(2), out int numericId))
                    {
                        if (float.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out float rating))
                        {
                            idParam.Value = numericId;
                            ratingParam.Value = rating;
                            await insertCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                            entryCount++;
                        }
                    }
                }
            }

            await transaction.CommitAsync().ConfigureAwait(false);

            // "Touch" the file so GetLastWriteTimeUtc is reset to right now
            File.SetLastWriteTimeUtc(_dbPath, DateTime.UtcNow);

            _logger.LogInformation("Finished updating IMDb rating DB. Number of entries: {0}", entryCount);
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
