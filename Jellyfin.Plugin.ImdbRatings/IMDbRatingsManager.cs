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
        private static volatile bool _isUpdating;
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
            Directory.CreateDirectory(dataPath);
            _dbPath = Path.Combine(dataPath, "imdbratings.db");
        }

        /// <summary>
        /// Gets a value indicating whether a database update is currently in progress.
        /// </summary>
        public static bool IsUpdating => _isUpdating;

        /// <summary>
        /// Delete the database file.
        /// </summary>
        public void DeleteDatabse()
        {
            File.Delete(_dbPath);
        }

        /// <summary>
        /// Load or update the database.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task PrepareDatabase()
        {
            int refreshIntervalHours = Plugin.Instance?.Configuration.DatabaseRefreshIntervalHours ?? 24;
            if (refreshIntervalHours <= 0)
            {
                refreshIntervalHours = 24;
            }

            // Check if the database file exists and was modified within the configured interval
            if (File.Exists(_dbPath))
            {
                var lastWrite = File.GetLastWriteTimeUtc(_dbPath);
                if ((DateTime.UtcNow - lastWrite).TotalHours < refreshIntervalHours)
                {
                    return; // DB is fresh, skip update
                }
            }

            await _updateLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _isUpdating = true;

                // Double check in case another thread updated it while we waited for the lock
                if (File.Exists(_dbPath))
                {
                    var lastWrite = File.GetLastWriteTimeUtc(_dbPath);
                    if ((DateTime.UtcNow - lastWrite).TotalHours < refreshIntervalHours)
                    {
                        return;
                    }
                }

                await RefreshDatabase().ConfigureAwait(false);
            }
            finally
            {
                _isUpdating = false;
                _updateLock.Release();
            }
        }

        /// <summary>
        /// Gets the current status of the IMDb ratings database.
        /// </summary>
        /// <returns>A <see cref="DatabaseStatus"/> instance.</returns>
        public async Task<DatabaseStatus> GetStatusAsync()
        {
            bool exists = File.Exists(_dbPath);
            DateTime? lastModifiedUtc = exists ? File.GetLastWriteTimeUtc(_dbPath) : null;
            long sizeBytes = exists ? new FileInfo(_dbPath).Length : 0;
            int? entryCount = null;

            if (exists && !_isUpdating)
            {
                try
                {
                    using var connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
                    await connection.OpenAsync().ConfigureAwait(false);
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT COUNT(*) FROM Ratings";
                    var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
                    if (result != null && result != DBNull.Value)
                    {
                        entryCount = Convert.ToInt32(result, CultureInfo.InvariantCulture);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not query entry count from database");
                }
            }

            return new DatabaseStatus
            {
                DatabaseExists = exists,
                LastModifiedUtc = lastModifiedUtc,
                SizeBytes = sizeBytes,
                TotalRatings = entryCount,
                IsUpdating = _isUpdating,
                RefreshIntervalHours = Plugin.Instance?.Configuration.DatabaseRefreshIntervalHours ?? 24,
                DatasetUrl = string.IsNullOrWhiteSpace(Plugin.Instance?.Configuration.DatasetUrl)
                    ? "https://datasets.imdbws.com/title.ratings.tsv.gz"
                    : Plugin.Instance.Configuration.DatasetUrl
            };
        }

        /// <summary>
        /// Gets the IMDb rating for a specific title ID, updating the cache if needed.
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

        private void ClearDatabasePool(string dbPath)
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            SqliteConnection.ClearPool(conn);
        }

        private async Task RefreshDatabase()
        {
            string tempDbPath = _dbPath + ".tmp";

            // Clear pool for temp database just in case a previous run left connections open
            ClearDatabasePool(tempDbPath);

            if (File.Exists(tempDbPath))
            {
                File.Delete(tempDbPath);
            }

            string? url = Plugin.Instance?.Configuration.DatasetUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                url = "https://datasets.imdbws.com/title.ratings.tsv.gz";
            }

            using var client = new HttpClient();

            _logger.LogInformation("Downloading IMDb rating flat file from: {0}", url);

            using var responseStream = await client.GetStreamAsync(url).ConfigureAwait(false);
            using var gzipStream = new GZipStream(responseStream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzipStream);

            _logger.LogInformation("Opening temporary database from path: {0}", tempDbPath);

            int entryCount = 0;

            // Use Pooling=False to ensure the file is closed immediately when disposed
            using (var connection = new SqliteConnection($"Data Source={tempDbPath};Pooling=False"))
            {
                await connection.OpenAsync().ConfigureAwait(false);

                // Create table
                using var createCmd = connection.CreateCommand();
                createCmd.CommandText = "CREATE TABLE IF NOT EXISTS Ratings (Id INTEGER PRIMARY KEY, Rating REAL)";
                await createCmd.ExecuteNonQueryAsync().ConfigureAwait(false);

                using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);

                // Prepare reusable insert command
                using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = "INSERT INTO Ratings (Id, Rating) VALUES (@id, @rating)";
                var idParam = insertCmd.Parameters.Add("@id", SqliteType.Integer);
                var ratingParam = insertCmd.Parameters.Add("@rating", SqliteType.Real);

                await reader.ReadLineAsync().ConfigureAwait(false); // Skip header

                string? line;
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    // 1. Treat the line as a mathematical window in memory
                    ReadOnlySpan<char> span = line.AsSpan();

                    // 2. Find the first tab character
                    int firstTab = span.IndexOf('\t');
                    if (firstTab < 0)
                    {
                        continue;
                    }

                    // 3. Slice the window to get the ID and the rest of the line
                    ReadOnlySpan<char> idSpan = span.Slice(0, firstTab);
                    ReadOnlySpan<char> remainder = span.Slice(firstTab + 1);

                    // 4. Find the second tab character
                    int secondTab = remainder.IndexOf('\t');
                    ReadOnlySpan<char> ratingSpan = secondTab >= 0 ? remainder.Slice(0, secondTab) : remainder;

                    // 5. Parse the spans directly into numbers
                    if (idSpan.StartsWith("tt") && int.TryParse(idSpan.Slice(2), out int numericId))
                    {
                        if (float.TryParse(ratingSpan, NumberStyles.Any, CultureInfo.InvariantCulture, out float rating))
                        {
                            // Insert into database
                            idParam.Value = numericId;
                            ratingParam.Value = rating;
                            await insertCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                            entryCount++;
                        }
                    }
                }

                await transaction.CommitAsync().ConfigureAwait(false);
            }

            // Clear the connection pool for the main database before replacing it
            ClearDatabasePool(_dbPath);

            File.Move(tempDbPath, _dbPath, true);

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
                _disposed = true;
            }
        }
    }
}
