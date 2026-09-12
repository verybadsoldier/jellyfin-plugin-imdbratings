using Jellyfin.Plugin.ImdbRatings.Configuration;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ImdbRatings;

/// <summary>
/// Helper methods for applying ratings to media items.
/// </summary>
public static class RatingHelper
{
    /// <summary>
    /// Applies an IMDb rating to the specified item according to the configured rating target.
    /// </summary>
    /// <param name="item">The media item.</param>
    /// <param name="rating">The rating value.</param>
    /// <param name="target">The target rating field(s).</param>
    /// <param name="logger">Optional logger for recording changes.</param>
    /// <returns>True if any rating property was updated; otherwise, false.</returns>
    public static bool ApplyRating(BaseItem? item, float? rating, RatingTarget target, ILogger? logger = null)
    {
        if (item == null || !rating.HasValue)
        {
            return false;
        }

        bool updated = false;
        if (target is RatingTarget.Community or RatingTarget.Both)
        {
            if (item.CommunityRating != rating.Value)
            {
                logger?.LogInformation("Updating IMDb community rating for '{Name}' from {OldRating} to {NewRating}", item.Name, item.CommunityRating, rating.Value);
                item.CommunityRating = rating.Value;
                updated = true;
            }
        }

        if (target is RatingTarget.Critic or RatingTarget.Both)
        {
            if (item.CriticRating != rating.Value)
            {
                logger?.LogInformation("Updating IMDb critic rating for '{Name}' from {OldRating} to {NewRating}", item.Name, item.CriticRating, rating.Value);
                item.CriticRating = rating.Value;
                updated = true;
            }
        }

        return updated;
    }
}
