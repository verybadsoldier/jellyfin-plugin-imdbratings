using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ImdbRatingsNg
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
        private readonly bool _isCustomDbPath;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="IMDbRatingsManager"/> class.
        /// </summary>
        /// <param name="logger">Logger.</param>
        public IMDbRatingsManager(ILogger logger)
        {
            _logger = logger;
            _isCustomDbPath = false;

            // Store the database inside the Jellyfin Plugin Data folder
            var dataPath = Plugin.Instance?.DataFolderPath ?? Path.GetTempPath();
            Directory.CreateDirectory(dataPath);
            _dbPath = Path.Combine(dataPath, "imdbratings.db");
            MigrateOldDatabase(dataPath);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IMDbRatingsManager"/> class with a custom database path (used for testing).
        /// </summary>
        /// <param name="logger">Logger.</param>
        /// <param name="dbPath">Database path.</param>
        internal IMDbRatingsManager(ILogger logger, string dbPath)
        {
            _logger = logger;
            _dbPath = dbPath;
            _isCustomDbPath = true;
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
            int? episodeCount = null;
            int? libraryEpisodesTotal = null;
            int? libraryEpisodesMissingId = null;
            int? libraryEpisodesResolved = null;
            DateTime? lastLibraryScanUtc = null;

            if (exists && (!_isUpdating || _isCustomDbPath))
            {
                try
                {
                    using var connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly;Pooling=False");
                    await connection.OpenAsync().ConfigureAwait(false);

                    using (var ratingsCheckCmd = connection.CreateCommand())
                    {
                        ratingsCheckCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Ratings'";
                        var checkResult = await ratingsCheckCmd.ExecuteScalarAsync().ConfigureAwait(false);
                        bool hasRatings = Convert.ToInt32(checkResult, CultureInfo.InvariantCulture) > 0;
                        if (hasRatings)
                        {
                            using var command = connection.CreateCommand();
                            command.CommandText = "SELECT COUNT(*) FROM Ratings";
                            var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
                            if (result != null && result != DBNull.Value)
                            {
                                entryCount = Convert.ToInt32(result, CultureInfo.InvariantCulture);
                            }
                        }
                    }

                    using (var epCheckCmd = connection.CreateCommand())
                    {
                        epCheckCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Episodes'";
                        var checkResult = await epCheckCmd.ExecuteScalarAsync().ConfigureAwait(false);
                        bool hasEpisodes = Convert.ToInt32(checkResult, CultureInfo.InvariantCulture) > 0;
                        if (hasEpisodes)
                        {
                            using var epCmd = connection.CreateCommand();
                            epCmd.CommandText = "SELECT COUNT(*) FROM Episodes";
                            var epResult = await epCmd.ExecuteScalarAsync().ConfigureAwait(false);
                            if (epResult != null && epResult != DBNull.Value)
                            {
                                episodeCount = Convert.ToInt32(epResult, CultureInfo.InvariantCulture);
                            }
                        }
                    }

                    using (var statsCheckCmd = connection.CreateCommand())
                    {
                        statsCheckCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='LibraryStats'";
                        var checkResult = await statsCheckCmd.ExecuteScalarAsync().ConfigureAwait(false);
                        bool hasStats = Convert.ToInt32(checkResult, CultureInfo.InvariantCulture) > 0;
                        if (hasStats)
                        {
                            using var statsCmd = connection.CreateCommand();
                            statsCmd.CommandText = "SELECT LastScanUtc, EpisodesTotal, EpisodesMissingId, EpisodesResolved FROM LibraryStats WHERE Id = 1";
                            using var reader = await statsCmd.ExecuteReaderAsync().ConfigureAwait(false);
                            if (await reader.ReadAsync().ConfigureAwait(false))
                            {
                                if (!await reader.IsDBNullAsync(0).ConfigureAwait(false))
                                {
                                    var dateStr = reader.GetString(0);
                                    if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate))
                                    {
                                        lastLibraryScanUtc = parsedDate;
                                    }
                                }

                                libraryEpisodesTotal = await reader.IsDBNullAsync(1).ConfigureAwait(false) ? null : reader.GetInt32(1);
                                libraryEpisodesMissingId = await reader.IsDBNullAsync(2).ConfigureAwait(false) ? null : reader.GetInt32(2);
                                libraryEpisodesResolved = await reader.IsDBNullAsync(3).ConfigureAwait(false) ? null : reader.GetInt32(3);
                            }
                        }
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
                TotalEpisodes = episodeCount,
                EnableEpisodeResolution = Plugin.Instance?.Configuration.EnableEpisodeResolution ?? true,
                EpisodeDatasetUrl = string.IsNullOrWhiteSpace(Plugin.Instance?.Configuration.EpisodeDatasetUrl)
                    ? "https://datasets.imdbws.com/title.episode.tsv.gz"
                    : Plugin.Instance.Configuration.EpisodeDatasetUrl,
                LibraryEpisodesTotal = libraryEpisodesTotal,
                LibraryEpisodesMissingId = libraryEpisodesMissingId,
                LibraryEpisodesResolved = libraryEpisodesResolved,
                LastLibraryScanUtc = lastLibraryScanUtc,
                IsUpdating = _isCustomDbPath ? false : _isUpdating,
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

        /// <summary>
        /// Resolves an episode's IMDb ID by querying the series IMDb ID, season number, and episode number in the local database.
        /// </summary>
        /// <param name="seriesImdbId">The series IMDb ID (e.g., tt0903747).</param>
        /// <param name="seasonNumber">The season number.</param>
        /// <param name="episodeNumber">The episode number within the season.</param>
        /// <returns>The episode's IMDb ID (e.g., tt0959621), or null if not found.</returns>
        public async Task<string?> GetEpisodeImdbIdAsync(string seriesImdbId, int seasonNumber, int episodeNumber)
        {
            await PrepareDatabase().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(seriesImdbId) || !seriesImdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!int.TryParse(seriesImdbId.AsSpan(2), out int parentId))
            {
                return null;
            }

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly;Pooling=False");
                await connection.OpenAsync().ConfigureAwait(false);

                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Id FROM Episodes WHERE ParentId = @parentId AND Season = @season AND Episode = @episode";
                command.Parameters.AddWithValue("@parentId", parentId);
                command.Parameters.AddWithValue("@season", seasonNumber);
                command.Parameters.AddWithValue("@episode", episodeNumber);

                var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
                if (result != null && result != DBNull.Value)
                {
                    int episodeId = Convert.ToInt32(result, CultureInfo.InvariantCulture);
                    return $"tt{episodeId:D7}";
                }
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
            {
                // Table doesn't exist yet
                _logger.LogDebug(ex, "Episodes table not found in database");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error querying episode IMDb ID for series '{0}' S{1}E{2}", seriesImdbId, seasonNumber, episodeNumber);
            }

            return null;
        }

        /// <summary>
        /// Persists episode resolution scan statistics for display on the plugin configuration page.
        /// </summary>
        /// <param name="total">Total episodes evaluated.</param>
        /// <param name="missingId">Episodes that were missing an IMDb ID.</param>
        /// <param name="resolved">Missing episodes successfully resolved.</param>
        /// <param name="scanTimeUtc">The UTC timestamp of the scan.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task SaveLibraryEpisodeStatsAsync(
            int total,
            int missingId,
            int resolved,
            DateTime scanTimeUtc)
        {
            var dataPath = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dataPath))
            {
                Directory.CreateDirectory(dataPath);
            }

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
                await connection.OpenAsync().ConfigureAwait(false);

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS LibraryStats (
                        Id INTEGER PRIMARY KEY CHECK (Id = 1),
                        LastScanUtc TEXT,
                        EpisodesTotal INTEGER,
                        EpisodesMissingId INTEGER,
                        EpisodesResolved INTEGER
                    );
                    INSERT OR REPLACE INTO LibraryStats (Id, LastScanUtc, EpisodesTotal, EpisodesMissingId, EpisodesResolved)
                    VALUES (1, @lastScan, @total, @missing, @resolved);";

                command.Parameters.AddWithValue("@lastScan", scanTimeUtc.ToString("o", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("@total", total);
                command.Parameters.AddWithValue("@missing", missingId);
                command.Parameters.AddWithValue("@resolved", resolved);

                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not save library episode stats to database");
            }
        }

        internal static async Task<int> ImportRatingsAsync(SqliteConnection connection, TextReader reader)
        {
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

            int entryCount = 0;
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
            return entryCount;
        }

        internal static async Task<int> ImportEpisodesAsync(SqliteConnection connection, TextReader reader)
        {
            // Create table without rowid for compactness
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = "CREATE TABLE IF NOT EXISTS Episodes (ParentId INTEGER, Season INTEGER, Episode INTEGER, Id INTEGER, PRIMARY KEY (ParentId, Season, Episode)) WITHOUT ROWID";
            await createCmd.ExecuteNonQueryAsync().ConfigureAwait(false);

            using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);

            using var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = "INSERT OR REPLACE INTO Episodes (ParentId, Season, Episode, Id) VALUES (@parentId, @season, @episode, @id)";
            var parentParam = insertCmd.Parameters.Add("@parentId", SqliteType.Integer);
            var seasonParam = insertCmd.Parameters.Add("@season", SqliteType.Integer);
            var epParam = insertCmd.Parameters.Add("@episode", SqliteType.Integer);
            var idParam = insertCmd.Parameters.Add("@id", SqliteType.Integer);

            await reader.ReadLineAsync().ConfigureAwait(false); // Skip header

            int entryCount = 0;
            string? line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                // Format: tconst \t parentTconst \t seasonNumber \t episodeNumber
                ReadOnlySpan<char> span = line.AsSpan();

                int firstTab = span.IndexOf('\t');
                if (firstTab < 0)
                {
                    continue;
                }

                ReadOnlySpan<char> idSpan = span.Slice(0, firstTab);
                ReadOnlySpan<char> rem1 = span.Slice(firstTab + 1);

                int secondTab = rem1.IndexOf('\t');
                if (secondTab < 0)
                {
                    continue;
                }

                ReadOnlySpan<char> parentSpan = rem1.Slice(0, secondTab);
                ReadOnlySpan<char> rem2 = rem1.Slice(secondTab + 1);

                int thirdTab = rem2.IndexOf('\t');
                ReadOnlySpan<char> seasonSpan = thirdTab >= 0 ? rem2.Slice(0, thirdTab) : rem2;
                ReadOnlySpan<char> episodeSpan = thirdTab >= 0 ? rem2.Slice(thirdTab + 1) : ReadOnlySpan<char>.Empty;

                int fourthTab = episodeSpan.IndexOf('\t');
                if (fourthTab >= 0)
                {
                    episodeSpan = episodeSpan.Slice(0, fourthTab);
                }

                if (idSpan.StartsWith("tt") && int.TryParse(idSpan.Slice(2), out int episodeId)
                    && parentSpan.StartsWith("tt") && int.TryParse(parentSpan.Slice(2), out int parentId)
                    && int.TryParse(seasonSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seasonNum)
                    && int.TryParse(episodeSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int episodeNum))
                {
                    parentParam.Value = parentId;
                    seasonParam.Value = seasonNum;
                    epParam.Value = episodeNum;
                    idParam.Value = episodeId;
                    await insertCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    entryCount++;
                }
            }

            await transaction.CommitAsync().ConfigureAwait(false);
            return entryCount;
        }

        private bool HasEpisodesTable()
        {
            if (!File.Exists(_dbPath))
            {
                return false;
            }

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly;Pooling=False");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Episodes'";
                var result = command.ExecuteScalar();
                return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
            }
            catch
            {
                return false;
            }
        }

        private void ClearDatabasePool(string dbPath)
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            SqliteConnection.ClearPool(conn);
        }

        private void MigrateOldDatabase(string dataPath)
        {
            TryMigrateDatabase(_dbPath, dataPath, _logger);
        }

        internal static bool TryMigrateDatabase(string currentDbPath, string dataPath, ILogger? logger = null)
        {
            if (File.Exists(currentDbPath))
            {
                return false;
            }

            var parentDir = Path.GetDirectoryName(dataPath);
            if (string.IsNullOrEmpty(parentDir))
            {
                return false;
            }

            var oldDbPath = Path.Combine(parentDir, "IMDb Ratings", "imdbratings.db");
            if (File.Exists(oldDbPath))
            {
                try
                {
                    logger?.LogInformation("Found existing IMDb ratings database at {0}. Migrating to {1}...", oldDbPath, currentDbPath);

                    var currentDir = Path.GetDirectoryName(currentDbPath);
                    if (!string.IsNullOrEmpty(currentDir))
                    {
                        Directory.CreateDirectory(currentDir);
                    }

                    File.Copy(oldDbPath, currentDbPath, overwrite: false);

                    if (File.Exists(oldDbPath + "-wal"))
                    {
                        File.Copy(oldDbPath + "-wal", currentDbPath + "-wal", overwrite: true);
                    }

                    if (File.Exists(oldDbPath + "-shm"))
                    {
                        File.Copy(oldDbPath + "-shm", currentDbPath + "-shm", overwrite: true);
                    }

                    logger?.LogInformation("Successfully migrated IMDb ratings database.");
                    return true;
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to migrate old IMDb ratings database from {0}", oldDbPath);
                }
            }

            return false;
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

            string? ratingsUrl = Plugin.Instance?.Configuration.DatasetUrl;
            if (string.IsNullOrWhiteSpace(ratingsUrl))
            {
                ratingsUrl = "https://datasets.imdbws.com/title.ratings.tsv.gz";
            }

            bool enableEpisodes = Plugin.Instance?.Configuration.EnableEpisodeResolution ?? true;
            string? episodeUrl = Plugin.Instance?.Configuration.EpisodeDatasetUrl;
            if (string.IsNullOrWhiteSpace(episodeUrl))
            {
                episodeUrl = "https://datasets.imdbws.com/title.episode.tsv.gz";
            }

            using var client = new HttpClient();

            _logger.LogInformation("Opening temporary database from path: {0}", tempDbPath);

            int ratingsCount = 0;
            int episodesCount = 0;

            // Use Pooling=False to ensure the file is closed immediately when disposed
            using (var connection = new SqliteConnection($"Data Source={tempDbPath};Pooling=False"))
            {
                await connection.OpenAsync().ConfigureAwait(false);

                // Apply PRAGMAs for fast bulk insertion into temporary database
                using (var pragmaCmd = connection.CreateCommand())
                {
                    pragmaCmd.CommandText = "PRAGMA synchronous = OFF; PRAGMA journal_mode = OFF; PRAGMA temp_store = MEMORY; PRAGMA cache_size = -64000;";
                    await pragmaCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                // 1. Download and import ratings
                _logger.LogInformation("Downloading IMDb rating flat file from: {0}", ratingsUrl);
                using (var ratingsResponse = await client.GetStreamAsync(ratingsUrl).ConfigureAwait(false))
                using (var gzipRatings = new GZipStream(ratingsResponse, CompressionMode.Decompress))
                using (var ratingsReader = new StreamReader(gzipRatings))
                {
                    ratingsCount = await ImportRatingsAsync(connection, ratingsReader).ConfigureAwait(false);
                }

                // 2. Download and import episodes if enabled
                if (enableEpisodes)
                {
                    _logger.LogInformation("Downloading IMDb episode flat file from: {0}", episodeUrl);
                    using (var epResponse = await client.GetStreamAsync(episodeUrl).ConfigureAwait(false))
                    using (var gzipEp = new GZipStream(epResponse, CompressionMode.Decompress))
                    using (var epReader = new StreamReader(gzipEp))
                    {
                        episodesCount = await ImportEpisodesAsync(connection, epReader).ConfigureAwait(false);
                    }
                }
            }

            // Clear the connection pool for the main database before replacing it
            ClearDatabasePool(_dbPath);

            File.Move(tempDbPath, _dbPath, true);

            // "Touch" the file so GetLastWriteTimeUtc is reset to right now
            File.SetLastWriteTimeUtc(_dbPath, DateTime.UtcNow);

            _logger.LogInformation("Finished updating IMDb database. Ratings: {0}, Episodes: {1}", ratingsCount, episodesCount);
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
