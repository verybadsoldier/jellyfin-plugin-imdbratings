#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Imdb.Tasks
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

        public string Description => "Regularly updates the IMDb community ratings for all movies, series, and episodes.";

        public string Category => "Library";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // Run the task weekly as a default
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerDaily,
                    TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
                }
            };
        }

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode },
                IsVirtualItem = false
            };

            var items = _libraryManager.GetItemList(query);
            int totalItems = items.Count;
            int processed = 0;

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Inside the Execute method loop:
                var imdbId = item.GetProviderId(MetadataProvider.Imdb);
                if (!string.IsNullOrEmpty(imdbId))
                {
                    try
                    {
                        // Call the shared helper instead of a local method
                        var rating = await ImdbApiHelper.GetImdbRating(imdbId, _httpClientFactory, _logger).ConfigureAwait(false);

                        if (rating.HasValue && item.CommunityRating != rating.Value)
                        {
                            _logger.LogInformation("Updating IMDb rating for {Name} from {OldRating} to {NewRating}", item.Name, item.CommunityRating, rating.Value);
                            item.CommunityRating = rating.Value;
                            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error updating rating for {Name}", item.Name);
                    }
                }

                processed++;
                progress.Report((double)processed / totalItems * 100);
            }
        }
    }
}
