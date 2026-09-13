using System;
using System.IO;
using System.Threading.Tasks;
using Jellyfin.Plugin.ImdbRatings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Jellyfin.Plugin.ImbdRatings.Test
{
    public sealed class EpisodeResolutionTests : IDisposable
    {
        private readonly string _testDbPath;
        private readonly FakeLogger<ILogger> _logger;

        public EpisodeResolutionTests()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"imdbratings_test_{Guid.NewGuid():N}.db");
            _logger = new FakeLogger<ILogger>();
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            if (File.Exists(_testDbPath))
            {
                try
                {
                    File.Delete(_testDbPath);
                }
                catch (IOException)
                {
                    // Ignore transient lock during cleanup in temp dir
                }
            }
        }

        [Fact]
        public async Task ImportEpisodesAsync_ParsesValidTsvAndIgnoresInvalidLines()
        {
            using (var connection = new SqliteConnection($"Data Source={_testDbPath};Pooling=False"))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);

                string tsvData = "tconst\tparentTconst\tseasonNumber\tepisodeNumber\n" +
                                 "tt0959621\ttt0903747\t1\t1\n" +
                                 "tt0959622\ttt0903747\t1\t2\n" +
                                 "tt0959623\ttt0903747\t2\t1\n" +
                                 "tt0000001\ttt0000002\t\\N\t1\n" +
                                 "tt0000003\ttt0000004\t1\t\\N\n" +
                                 "invalid\ttt0903747\t1\t3\n";

                using var reader = new StringReader(tsvData);
                int count = await IMDbRatingsManager.ImportEpisodesAsync(connection, reader);

                Assert.Equal(3, count);
            }

            var manager = new IMDbRatingsManager(_logger, _testDbPath);

            var ep1 = await manager.GetEpisodeImdbIdAsync("tt0903747", 1, 1);
            Assert.Equal("tt0959621", ep1);

            var ep2 = await manager.GetEpisodeImdbIdAsync("tt0903747", 1, 2);
            Assert.Equal("tt0959622", ep2);

            var ep3 = await manager.GetEpisodeImdbIdAsync("tt0903747", 2, 1);
            Assert.Equal("tt0959623", ep3);

            var notFound = await manager.GetEpisodeImdbIdAsync("tt0903747", 9, 9);
            Assert.Null(notFound);
        }

        [Fact]
        public async Task GetEpisodeImdbIdAsync_HandlesInvalidInputsGracefully()
        {
            var manager = new IMDbRatingsManager(_logger, _testDbPath);

            Assert.Null(await manager.GetEpisodeImdbIdAsync(string.Empty, 1, 1));
            Assert.Null(await manager.GetEpisodeImdbIdAsync("invalid", 1, 1));
            Assert.Null(await manager.GetEpisodeImdbIdAsync("ttNotANumber", 1, 1));
        }

        [Fact]
        public async Task ImportRatingsAndEpisodes_ResolvesEpisodeAndRatingTogether()
        {
            using (var connection = new SqliteConnection($"Data Source={_testDbPath};Pooling=False"))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);

                string ratingsTsv = "tconst\taverageRating\tnumVotes\n" +
                                    "tt0959621\t9.0\t50000\n" +
                                    "tt0959622\t8.7\t40000\n";

                string episodesTsv = "tconst\tparentTconst\tseasonNumber\tepisodeNumber\n" +
                                     "tt0959621\ttt0903747\t1\t1\n" +
                                     "tt0959622\ttt0903747\t1\t2\n";

                using (var ratingsReader = new StringReader(ratingsTsv))
                {
                    await IMDbRatingsManager.ImportRatingsAsync(connection, ratingsReader);
                }

                using (var epReader = new StringReader(episodesTsv))
                {
                    await IMDbRatingsManager.ImportEpisodesAsync(connection, epReader);
                }
            }

            var manager = new IMDbRatingsManager(_logger, _testDbPath);

            // 1. Resolve episode ID
            var resolvedId = await manager.GetEpisodeImdbIdAsync("tt0903747", 1, 1);
            Assert.Equal("tt0959621", resolvedId);

            // 2. Fetch rating using resolved ID
            var rating = await manager.GetRatingAsync(resolvedId!);
            Assert.NotNull(rating);
            Assert.Equal(9.0f, rating.Value);
        }

        [Fact]
        public async Task SaveLibraryEpisodeStatsAsync_PersistsAndReadsBackInStatus()
        {
            var manager = new IMDbRatingsManager(_logger, _testDbPath);

            // Create empty DB first
            using (var connection = new SqliteConnection($"Data Source={_testDbPath};Pooling=False"))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);
            }

            var scanTime = new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);
            await manager.SaveLibraryEpisodeStatsAsync(100, 30, 25, scanTime);

            var status = await manager.GetStatusAsync();

            Assert.Equal(100, status.LibraryEpisodesTotal);
            Assert.Equal(30, status.LibraryEpisodesMissingId);
            Assert.Equal(25, status.LibraryEpisodesResolved);
            Assert.Equal(scanTime, status.LastLibraryScanUtc);
        }
    }
}
