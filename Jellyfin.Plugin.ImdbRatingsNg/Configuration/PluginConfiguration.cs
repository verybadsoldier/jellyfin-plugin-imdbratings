using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ImdbRatingsNg.Configuration;

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
        EpisodeDatasetUrl = "https://datasets.imdbws.com/title.episode.tsv.gz";
        EnableEpisodeResolution = true;
        RatingTarget = RatingTarget.Community;
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

    /// <summary>
    /// Gets or sets the URL to download the IMDb title episode dataset from.
    /// </summary>
    public string EpisodeDatasetUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether missing episode IMDb IDs should be resolved using the IMDb episode dataset.
    /// </summary>
    public bool EnableEpisodeResolution { get; set; }

    /// <summary>
    /// Gets or sets the target field where IMDb ratings should be saved.
    /// </summary>
    public RatingTarget RatingTarget { get; set; }
}
