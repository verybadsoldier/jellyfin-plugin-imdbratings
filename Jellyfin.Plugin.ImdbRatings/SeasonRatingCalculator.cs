using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.ImdbRatings
{
    /// <summary>
    /// Helper class to calculate aggregated IMDb ratings for seasons.
    /// </summary>
    public static class SeasonRatingCalculator
    {
        /// <summary>
        /// Calculates the average community rating from a collection of episodes.
        /// </summary>
        /// <param name="episodes">The episodes to calculate the average from.</param>
        /// <param name="minPercentage">The minimum percentage (0-100) of rated episodes required.</param>
        /// <returns>The rounded average rating, or null if no valid ratings are present or threshold is not met.</returns>
        public static float? CalculateAverageRating(IEnumerable<Episode>? episodes, int minPercentage = 0)
        {
            if (episodes == null)
            {
                return null;
            }

            var episodeList = episodes as IReadOnlyCollection<Episode> ?? episodes.ToList();
            if (episodeList.Count == 0)
            {
                return null;
            }

            return CalculateAverageRating(episodeList.Select(e => e.CommunityRating), episodeList.Count, minPercentage);
        }

        /// <summary>
        /// Calculates the average rating from a collection of nullable rating values.
        /// </summary>
        /// <param name="ratings">The collection of ratings.</param>
        /// <param name="totalEpisodeCount">Total number of episodes in the season.</param>
        /// <param name="minPercentage">The minimum percentage (0-100) of rated episodes required.</param>
        /// <returns>The rounded average rating, or null if no valid ratings are present or threshold is not met.</returns>
        public static float? CalculateAverageRating(IEnumerable<float?>? ratings, int totalEpisodeCount = 0, int minPercentage = 0)
        {
            if (ratings == null)
            {
                return null;
            }

            var validRatings = ratings
                .Where(r => r.HasValue && !float.IsNaN(r.Value) && r.Value > 0)
                .Select(r => r!.Value)
                .ToList();

            if (validRatings.Count == 0)
            {
                return null;
            }

            int total = totalEpisodeCount > 0 ? totalEpisodeCount : validRatings.Count;
            if (minPercentage > 0)
            {
                double percentage = (double)validRatings.Count / total * 100.0;
                if (percentage < minPercentage)
                {
                    return null;
                }
            }

            double average = validRatings.Average();
            return (float)Math.Round(average, 1, MidpointRounding.AwayFromZero);
        }
    }
}
