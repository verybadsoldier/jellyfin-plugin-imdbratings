using System;
using System.IO;
using Jellyfin.Plugin.ImdbRatingsNg;
using Xunit;
using Plugin = Jellyfin.Plugin.ImdbRatingsNg.Plugin;

namespace Jellyfin.Plugin.ImbdRatingsNg.Test
{
    public sealed class MigrationTests : IDisposable
    {
        private readonly string _tempDir;

        public MigrationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"migration_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch
            {
                // Ignore cleanup issues in temp dir
            }
        }

        [Fact]
        public void MigrateConfiguration_MigratesFromOldPluginXml()
        {
            var oldConfigPath = Path.Combine(_tempDir, "Jellyfin.Plugin.ImdbRatings.xml");
            var newConfigPath = Path.Combine(_tempDir, "Jellyfin.Plugin.ImdbRatingsNg.xml");

            const string ExpectedXml = "<PluginConfiguration><DatasetUrl>https://custom.url</DatasetUrl></PluginConfiguration>";
            File.WriteAllText(oldConfigPath, ExpectedXml);

            bool migrated = global::Jellyfin.Plugin.ImdbRatingsNg.Plugin.TryMigrateConfiguration(newConfigPath, _tempDir);

            Assert.True(migrated);
            Assert.True(File.Exists(newConfigPath));
            Assert.Equal(ExpectedXml, File.ReadAllText(newConfigPath));
        }

        [Fact]
        public void MigrateConfiguration_MigratesFromLegacyImdbXml()
        {
            var oldConfigPath = Path.Combine(_tempDir, "Jellyfin.Plugin.Imdb.xml");
            var newConfigPath = Path.Combine(_tempDir, "Jellyfin.Plugin.ImdbRatingsNg.xml");

            const string ExpectedXml = "<PluginConfiguration><DatasetUrl>https://legacy.url</DatasetUrl></PluginConfiguration>";
            File.WriteAllText(oldConfigPath, ExpectedXml);

            bool migrated = global::Jellyfin.Plugin.ImdbRatingsNg.Plugin.TryMigrateConfiguration(newConfigPath, _tempDir);

            Assert.True(migrated);
            Assert.True(File.Exists(newConfigPath));
            Assert.Equal(ExpectedXml, File.ReadAllText(newConfigPath));
        }

        [Fact]
        public void MigrateConfiguration_DoesNotOverwriteExistingConfig()
        {
            var oldConfigPath = Path.Combine(_tempDir, "Jellyfin.Plugin.ImdbRatings.xml");
            var newConfigPath = Path.Combine(_tempDir, "Jellyfin.Plugin.ImdbRatingsNg.xml");

            File.WriteAllText(oldConfigPath, "<OldConfig />");
            File.WriteAllText(newConfigPath, "<NewConfig />");

            bool migrated = global::Jellyfin.Plugin.ImdbRatingsNg.Plugin.TryMigrateConfiguration(newConfigPath, _tempDir);

            Assert.False(migrated);
            Assert.Equal("<NewConfig />", File.ReadAllText(newConfigPath));
        }

        [Fact]
        public void MigrateDatabase_MigratesFromOldDatabaseFolder()
        {
            var oldDataDir = Path.Combine(_tempDir, "IMDb Ratings");
            var newDataDir = Path.Combine(_tempDir, "IMDb Ratings NG");
            Directory.CreateDirectory(oldDataDir);

            var oldDbPath = Path.Combine(oldDataDir, "imdbratings.db");
            var newDbPath = Path.Combine(newDataDir, "imdbratings.db");

            File.WriteAllText(oldDbPath, "MOCK_DB_CONTENT");
            File.WriteAllText(oldDbPath + "-wal", "MOCK_WAL_CONTENT");
            File.WriteAllText(oldDbPath + "-shm", "MOCK_SHM_CONTENT");

            bool migrated = IMDbRatingsManager.TryMigrateDatabase(newDbPath, newDataDir);

            Assert.True(migrated);
            Assert.True(File.Exists(newDbPath));
            Assert.True(File.Exists(newDbPath + "-wal"));
            Assert.True(File.Exists(newDbPath + "-shm"));
            Assert.Equal("MOCK_DB_CONTENT", File.ReadAllText(newDbPath));
            Assert.Equal("MOCK_WAL_CONTENT", File.ReadAllText(newDbPath + "-wal"));
            Assert.Equal("MOCK_SHM_CONTENT", File.ReadAllText(newDbPath + "-shm"));
        }

        [Fact]
        public void MigrateDatabase_DoesNotOverwriteExistingDatabase()
        {
            var oldDataDir = Path.Combine(_tempDir, "IMDb Ratings");
            var newDataDir = Path.Combine(_tempDir, "IMDb Ratings NG");
            Directory.CreateDirectory(oldDataDir);
            Directory.CreateDirectory(newDataDir);

            var oldDbPath = Path.Combine(oldDataDir, "imdbratings.db");
            var newDbPath = Path.Combine(newDataDir, "imdbratings.db");

            File.WriteAllText(oldDbPath, "OLD_DB");
            File.WriteAllText(newDbPath, "EXISTING_NEW_DB");

            bool migrated = IMDbRatingsManager.TryMigrateDatabase(newDbPath, newDataDir);

            Assert.False(migrated);
            Assert.Equal("EXISTING_NEW_DB", File.ReadAllText(newDbPath));
        }
    }
}
