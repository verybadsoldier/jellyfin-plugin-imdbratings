using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ImdbRatings.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        DatabaseRefreshIntervalHours = 24;
        MinEpisodePercentageForSeasonRating = 0;
        DatasetUrl = "https://datasets.imdbws.com/title.ratings.tsv.gz";
    }

    /// <summary>
    /// Gets or sets the database refresh interval in hours before re-downloading the IMDb ratings dataset.
    /// </summary>
    public int DatabaseRefreshIntervalHours { get; set; }

    /// <summary>
    /// Gets or sets the minimum percentage of rated episodes (0-100) required to calculate a season rating.
    /// </summary>
    public int MinEpisodePercentageForSeasonRating { get; set; }

    /// <summary>
    /// Gets or sets the URL to download the IMDb title ratings dataset from.
    /// </summary>
    public string DatasetUrl { get; set; }
}
