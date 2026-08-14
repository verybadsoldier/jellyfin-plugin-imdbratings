using System.Collections.Generic;
using Jellyfin.Plugin.ImdbRatings;
using Jellyfin.Plugin.ImdbRatings.Configuration;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.ImbdRatings.Test
{
    public sealed class SeasonRatingCalculatorTests
    {
        [Fact]
        public void CalculateAverageRating_WithNullList_ReturnsNull()
        {
            List<Episode>? episodes = null;
            var result = SeasonRatingCalculator.CalculateAverageRating(episodes);
            Assert.Null(result);

            List<float?>? ratings = null;
            var ratingResult = SeasonRatingCalculator.CalculateAverageRating(ratings);
            Assert.Null(ratingResult);
        }

        [Fact]
        public void CalculateAverageRating_WithEmptyList_ReturnsNull()
        {
            var episodes = new List<Episode>();
            var result = SeasonRatingCalculator.CalculateAverageRating(episodes);
            Assert.Null(result);

            var ratings = new List<float?>();
            var ratingResult = SeasonRatingCalculator.CalculateAverageRating(ratings);
            Assert.Null(ratingResult);
        }

        [Fact]
        public void CalculateAverageRating_WithNoRatedEpisodes_ReturnsNull()
        {
            var episodes = new List<Episode>
            {
                new Episode { CommunityRating = null },
                new Episode { CommunityRating = null }
            };
            var result = SeasonRatingCalculator.CalculateAverageRating(episodes);
            Assert.Null(result);

            var ratings = new List<float?> { null, null };
            var ratingResult = SeasonRatingCalculator.CalculateAverageRating(ratings);
            Assert.Null(ratingResult);
        }

        [Fact]
        public void CalculateAverageRating_WithSingleRatedEpisode_ReturnsSameRating()
        {
            var episodes = new List<Episode>
            {
                new Episode { CommunityRating = 8.4f }
            };
            var result = SeasonRatingCalculator.CalculateAverageRating(episodes);
            Assert.Equal(8.4f, result);
        }

        [Fact]
        public void CalculateAverageRating_WithMultipleRatedEpisodes_ReturnsCorrectAverage()
        {
            var episodes = new List<Episode>
            {
                new Episode { CommunityRating = 8.0f },
                new Episode { CommunityRating = 9.0f }
            };
            var result = SeasonRatingCalculator.CalculateAverageRating(episodes);
            Assert.Equal(8.5f, result);
        }

        [Fact]
        public void CalculateAverageRating_WithPartiallyRatedEpisodes_IgnoresUnratedEpisodes()
        {
            var episodes = new List<Episode>
            {
                new Episode { CommunityRating = 7.0f },
                new Episode { CommunityRating = null },
                new Episode { CommunityRating = 9.0f }
            };
            var result = SeasonRatingCalculator.CalculateAverageRating(episodes);
            Assert.Equal(8.0f, result);
        }

        [Fact]
        public void CalculateAverageRating_RoundsToSingleDecimalPlace()
        {
            // (8.1 + 8.2 + 8.2) / 3 = 24.5 / 3 = 8.166666... -> rounds to 8.2
            var episodes = new List<Episode>
            {
                new Episode { CommunityRating = 8.1f },
                new Episode { CommunityRating = 8.2f },
                new Episode { CommunityRating = 8.2f }
            };
            var result = SeasonRatingCalculator.CalculateAverageRating(episodes);
            Assert.Equal(8.2f, result);
        }

        [Theory]
        [InlineData(0, 8.0f)]
        [InlineData(25, 8.0f)]
        [InlineData(50, 8.0f)]
        [InlineData(51, null)]
        [InlineData(75, null)]
        [InlineData(100, null)]
        public void CalculateAverageRating_WithMinPercentage_RespectsThreshold(int minPercentage, float? expectedRating)
        {
            // 4 episodes total, 2 rated (50% rated)
            var episodes = new List<Episode>
            {
                new Episode { CommunityRating = 7.0f },
                new Episode { CommunityRating = 9.0f },
                new Episode { CommunityRating = null },
                new Episode { CommunityRating = null }
            };

            var result = SeasonRatingCalculator.CalculateAverageRating(episodes, minPercentage);
            Assert.Equal(expectedRating, result);
        }

        [Fact]
        public void PluginConfiguration_DefaultValues_AreCorrect()
        {
            var config = new PluginConfiguration();
            Assert.Equal(24, config.DatabaseRefreshIntervalHours);
            Assert.Equal(0, config.MinEpisodePercentageForSeasonRating);
            Assert.Equal("https://datasets.imdbws.com/title.ratings.tsv.gz", config.DatasetUrl);

            config.DatabaseRefreshIntervalHours = 48;
            config.MinEpisodePercentageForSeasonRating = 50;
            config.DatasetUrl = "https://custom.mirror/title.ratings.tsv.gz";

            Assert.Equal(48, config.DatabaseRefreshIntervalHours);
            Assert.Equal(50, config.MinEpisodePercentageForSeasonRating);
            Assert.Equal("https://custom.mirror/title.ratings.tsv.gz", config.DatasetUrl);
        }
    }
}
