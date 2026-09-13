#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.ImdbRatings;
using Jellyfin.Plugin.ImdbRatings.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ImdbRatings.Tasks
{
    public class UpdateImdbRatingsTask : IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<UpdateImdbRatingsTask> _logger;

        public UpdateImdbRatingsTask(
            ILibraryManager libraryManager,
            IHttpClientFactory httpClientFactory,
            ILogger<UpdateImdbRatingsTask> logger)
        {
            _libraryManager = libraryManager;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public string Name => "Update IMDb Ratings";

        public string Key => "UpdateImdbRatingsTask";

        public string Description => "Regularly updates the IMDb ratings for movies, series, seasons, and episodes.";

        public string Category => "Library";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.DailyTrigger,
                    TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
                }
            };
        }

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Executing task to update IMDb ratings...");
            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode },
                IsVirtualItem = false
            };

            var items = _libraryManager.GetItemList(query);
            var seasonQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Season },
                IsVirtualItem = false
            };

            var seasons = _libraryManager.GetItemList(seasonQuery).OfType<Season>().ToList();
            int totalItems = items.Count + seasons.Count;
            int processed = 0;

            void ReportProgress()
            {
                processed++;
                progress.Report(totalItems > 0 ? (double)processed / totalItems * 100.0 : 100.0);
            }

            // Instantiate the manager once outside the loop
            var cache = new IMDbRatingsManager(_logger);
            await cache.PrepareDatabase().ConfigureAwait(false);

            var providerName = "The Internet Movie Database Ratings";

            int episodeTotal = 0;
            int episodeMissingId = 0;
            int episodeResolved = 0;

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var itemType = item.GetType().Name; // e.g., "Movie", "Series", "Episode"
                bool isProviderEnabled = false;

                // Safely get the library options for this specific item
                var options = _libraryManager.GetLibraryOptions(item);
                if (options != null)
                {
                    var typeOptions = options.TypeOptions?.FirstOrDefault(t =>
                        string.Equals(t.Type, itemType, StringComparison.OrdinalIgnoreCase));

                    if (typeOptions != null && typeOptions.MetadataFetchers != null)
                    {
                        // Check if our provider is enabled for this library/type
                        isProviderEnabled = typeOptions.MetadataFetchers.Contains(
                            providerName, StringComparer.OrdinalIgnoreCase);
                    }
                }

                // If disabled, skip this item
                if (!isProviderEnabled)
                {
                    ReportProgress();
                    continue;
                }

                var imdbId = item.GetProviderId(MetadataProvider.Imdb);

                if (item is Episode episode)
                {
                    episodeTotal++;
                    if (string.IsNullOrEmpty(imdbId))
                    {
                        episodeMissingId++;
                        if (Plugin.Instance?.Configuration.EnableEpisodeResolution ?? true)
                        {
                            var series = episode.Series ?? (episode.SeriesId != Guid.Empty ? _libraryManager.GetItemById(episode.SeriesId) as Series : null);
                            var seriesImdbId = series?.GetProviderId(MetadataProvider.Imdb);
                            var seasonNum = episode.ParentIndexNumber;
                            var episodeNum = episode.IndexNumber;

                            if (!string.IsNullOrEmpty(seriesImdbId) && seasonNum.HasValue && episodeNum.HasValue)
                            {
                                imdbId = await cache.GetEpisodeImdbIdAsync(seriesImdbId, seasonNum.Value, episodeNum.Value).ConfigureAwait(false);
                                if (!string.IsNullOrEmpty(imdbId))
                                {
                                    episodeResolved++;
                                    _logger.LogInformation(
                                        "Resolved in-memory IMDb ID '{0}' for episode '{1}' (S{2:D2}E{3:D2}) of series '{4}' to fetch rating",
                                        imdbId,
                                        episode.Name,
                                        seasonNum.Value,
                                        episodeNum.Value,
                                        series?.Name);
                                }
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(imdbId))
                {
                    try
                    {
                        var rating = await cache.GetRatingAsync(imdbId).ConfigureAwait(false);
                        var target = Plugin.Instance?.Configuration.RatingTarget ?? RatingTarget.Community;

                        bool ratingUpdated = RatingHelper.ApplyRating(item, rating, target, _logger);
                        if (ratingUpdated)
                        {
                            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error updating rating for {Name}", item.Name);
                    }
                }

                ReportProgress();
            }

            if (episodeTotal > 0)
            {
                _logger.LogInformation(
                    "Episode IMDb ID Resolution Summary: {Total} total episodes checked. " +
                    "{Missing} were missing. " +
                    "{Resolved} successfully resolved ({Percent:F1}%).",
                    episodeTotal,
                    episodeMissingId,
                    episodeResolved,
                    episodeMissingId > 0 ? (double)episodeResolved / episodeMissingId * 100.0 : 0.0);

                await cache.SaveLibraryEpisodeStatsAsync(
                    episodeTotal,
                    episodeMissingId,
                    episodeResolved,
                    DateTime.UtcNow).ConfigureAwait(false);
            }

            _logger.LogInformation("Calculating IMDb ratings for {Count} seasons...", seasons.Count);

            foreach (var season in seasons)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool isProviderEnabled = false;
                var options = _libraryManager.GetLibraryOptions(season);
                if (options != null)
                {
                    var seasonOptions = options.TypeOptions?.FirstOrDefault(t =>
                        string.Equals(t.Type, "Season", StringComparison.OrdinalIgnoreCase));

                    if (seasonOptions != null && seasonOptions.MetadataFetchers != null)
                    {
                        isProviderEnabled = seasonOptions.MetadataFetchers.Contains(
                            providerName, StringComparer.OrdinalIgnoreCase);
                    }
                }

                if (!isProviderEnabled)
                {
                    ReportProgress();
                    continue;
                }

                try
                {
                    var episodes = season.GetEpisodes().OfType<Episode>().ToList();
                    if (episodes.Count == 0)
                    {
                        episodes = _libraryManager.GetItemList(new InternalItemsQuery
                        {
                            ParentId = season.Id,
                            IncludeItemTypes = new[] { BaseItemKind.Episode },
                            IsVirtualItem = false
                        }).OfType<Episode>().ToList();
                    }

                    var target = Plugin.Instance?.Configuration.RatingTarget ?? RatingTarget.Community;
                    int minPercentage = Plugin.Instance?.Configuration.MinEpisodePercentageForSeasonRating ?? 0;
                    var avgRating = SeasonRatingCalculator.CalculateAverageRating(episodes, minPercentage, target);
                    if (avgRating.HasValue && RatingHelper.ApplyRating(season, avgRating, target, _logger))
                    {
                        await season.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating calculated rating for season {Name}", season.Name);
                }

                ReportProgress();
            }

            _logger.LogInformation("IMDb ratings update task finished");
        }
    }
}
