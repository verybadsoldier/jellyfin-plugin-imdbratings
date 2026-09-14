using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Jellyfin.Plugin.ImdbRatingsNg.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.ImdbRatingsNg
{
    /// <summary>
    /// The main plugin.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Plugin"/> class.
        /// </summary>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            MigrateOldConfiguration(applicationPaths, xmlSerializer);
        }

        /// <inheritdoc />
        public override string Name => "IMDb Ratings NG";

        /// <inheritdoc />
        public override Guid Id => Guid.Parse("12418add-9a9d-422d-8e35-dde91cf5baf9");

        /// <summary>
        /// Gets the current plugin instance.
        /// </summary>
        public static Plugin? Instance { get; private set; }

        /// <summary>
        /// Gets Description.
        /// </summary>
        public override string Description => "Get ratings for movies, series, seasons, and episodes from IMDb.";

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = this.Name,
                    EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
                }
            };
        }

        private void MigrateOldConfiguration(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        {
            TryMigrateConfiguration(ConfigurationFilePath, applicationPaths.PluginConfigurationsPath, xmlSerializer, UpdateConfiguration);
        }

        internal static bool TryMigrateConfiguration(
            string configurationFilePath,
            string pluginConfigurationsPath,
            IXmlSerializer? xmlSerializer = null,
            Action<PluginConfiguration>? onConfigLoaded = null)
        {
            if (File.Exists(configurationFilePath))
            {
                return false;
            }

            var oldConfigCandidates = new[]
            {
                Path.Combine(pluginConfigurationsPath, "Jellyfin.Plugin.ImdbRatings.xml"),
                Path.Combine(pluginConfigurationsPath, "Jellyfin.Plugin.Imdb.xml")
            };

            foreach (var oldConfigPath in oldConfigCandidates)
            {
                if (File.Exists(oldConfigPath))
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(configurationFilePath);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }

                        File.Copy(oldConfigPath, configurationFilePath, overwrite: false);

                        if (xmlSerializer != null && xmlSerializer.DeserializeFromFile(typeof(PluginConfiguration), configurationFilePath) is PluginConfiguration migratedConfig)
                        {
                            onConfigLoaded?.Invoke(migratedConfig);
                        }

                        return true;
                    }
                    catch
                    {
                        // Fall back to default configuration
                    }
                }
            }

            return false;
        }
    }
}
