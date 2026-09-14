namespace Jellyfin.Plugin.ImdbRatingsNg.Configuration;

/// <summary>
/// Specifies the target field where IMDb ratings should be saved.
/// </summary>
public enum RatingTarget
{
    /// <summary>
    /// Save as community rating.
    /// </summary>
    Community,

    /// <summary>
    /// Save as critic rating.
    /// </summary>
    Critic,

    /// <summary>
    /// Save as both community and critic rating.
    /// </summary>
    Both,
}
