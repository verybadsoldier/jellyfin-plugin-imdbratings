#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.ImdbRatings;
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

        public string Description => "Regularly updates the IMDb community ratings for movies, series, seasons, and episodes.";

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
                if (!string.IsNullOrEmpty(imdbId))
                {
                    try
                    {
                        var rating = await cache.GetRatingAsync(imdbId).ConfigureAwait(false);

                        if (rating.HasValue && item.CommunityRating != rating.Value)
                        {
                            _logger.LogInformation("Updating IMDb rating for '{Name}' from {OldRating} to {NewRating}", item.Name, item.CommunityRating, rating.Value);
                            item.CommunityRating = rating.Value;
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

                    int minPercentage = Plugin.Instance?.Configuration.MinEpisodePercentageForSeasonRating ?? 0;
                    var avgRating = SeasonRatingCalculator.CalculateAverageRating(episodes, minPercentage);
                    if (avgRating.HasValue && season.CommunityRating != avgRating.Value)
                    {
                        _logger.LogInformation("Updating calculated IMDb rating for season '{Name}' from {OldRating} to {NewRating}", season.Name, season.CommunityRating, avgRating.Value);
                        season.CommunityRating = avgRating.Value;
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
