#nullable disable

#pragma warning disable CS1591, SA1300

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Reflection;
using System.Runtime;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using BitFaster.Caching;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.ImdbRatings;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using static MediaBrowser.Providers.Plugins.Imdb.ImdbItemProvider;

namespace MediaBrowser.Providers.Plugins.Imdb
{
    public class ImdbItemProvider : IRemoteMetadataProvider<Series, SeriesInfo>,
        IRemoteMetadataProvider<Movie, MovieInfo>, IRemoteMetadataProvider<Episode, EpisodeInfo>,
        IRemoteMetadataProvider<Season, SeasonInfo>, ICustomMetadataProvider<Season>, IHasOrder, IDisposable
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILibraryManager _libraryManager;
        private readonly IProviderManager _providerManager;
        private readonly ILogger _logger;
        private IMDbRatingsManager _cache;
        private bool _disposed;

        public ImdbItemProvider(
            IHttpClientFactory httpClientFactory,
            ILibraryManager libraryManager,
            IFileSystem fileSystem,
            IServerConfigurationManager configurationManager,
            IProviderManager providerManager,
            ILogger<ImdbItemProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _libraryManager = libraryManager;
            _providerManager = providerManager;
            _logger = logger;
            _cache = new IMDbRatingsManager(_logger);
        }

        public string Name => "The Internet Movie Database Ratings";

        // Run after all primary metadata fetchers (TMDb, TheTVDB, OMDb, etc.) so IMDb IDs are already populated
        public int Order => 100;

        private async Task<MetadataResult<TBase>> GetResult<TBase, TLookupInfo>(TLookupInfo info, CancellationToken cancellationToken)
                        where TBase : BaseItem, IHasLookupInfo<TLookupInfo>, new()
                        where TLookupInfo : ItemLookupInfo, new()
        {
            var result = new MetadataResult<TBase>
            {
                QueriedById = true,
                Item = new TBase(),
                HasMetadata = false
            };

            var imdbId = info.GetProviderId(MetadataProvider.Imdb);
            if (string.IsNullOrWhiteSpace(imdbId))
            {
                return result;
            }

            float? rating = await _cache.GetRatingAsync(imdbId).ConfigureAwait(false);

            _logger.LogInformation("Fetched IMDb rating for ID '{0}': {1}", imdbId, rating);

            if (rating.HasValue)
            {
                result.Item.CommunityRating = rating;
                result.HasMetadata = true;
            }

            return result;
        }

        public Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            return GetResult<Series, SeriesInfo>(info, cancellationToken);
        }

        public Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
        {
            return GetResult<Movie, MovieInfo>(info, cancellationToken);
        }

        public Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            return GetResult<Episode, EpisodeInfo>(info, cancellationToken);
        }

        public Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken cancellationToken)
        {
            return Task.FromResult(new MetadataResult<Season>
            {
                QueriedById = true,
                Item = new Season(),
                HasMetadata = false
            });
        }

        public Task<ItemUpdateType> FetchAsync(Season item, MetadataRefreshOptions options, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(item);

            var episodes = item.GetEpisodes().OfType<Episode>().ToList();
            if (episodes.Count == 0)
            {
                episodes = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    ParentId = item.Id,
                    IncludeItemTypes = new[] { BaseItemKind.Episode },
                    IsVirtualItem = false
                }).OfType<Episode>().ToList();
            }

            int minPercentage = Plugin.Instance?.Configuration.MinEpisodePercentageForSeasonRating ?? 0;
            var avgRating = SeasonRatingCalculator.CalculateAverageRating(episodes, minPercentage);
            if (avgRating.HasValue && item.CommunityRating != avgRating.Value)
            {
                _logger.LogInformation("Calculated average IMDb rating {Rating} for season '{SeasonName}' from episodes", avgRating.Value, item.Name);
                item.CommunityRating = avgRating.Value;
                return Task.FromResult(ItemUpdateType.MetadataEdit);
            }

            return Task.FromResult(ItemUpdateType.None);
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            return GetSearchResultsInternal(searchInfo, true, cancellationToken);
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
        {
            return GetSearchResultsInternal(searchInfo, true, cancellationToken);
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            return GetSearchResultsInternal(searchInfo, true, cancellationToken);
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo searchInfo, CancellationToken cancellationToken)
        {
            return GetSearchResultsInternal(searchInfo, true, cancellationToken);
        }

        private async Task<IEnumerable<RemoteSearchResult>> GetSearchResultsInternal(ItemLookupInfo searchInfo, bool isSearch, CancellationToken cancellationToken)
        {
            await Task.Run(() => { }, cancellationToken).ConfigureAwait(false);

            return Enumerable.Empty<RemoteSearchResult>();
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);
        }

        /// <summary>
        /// Disposes of the resources used by the manager.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the IMDbRatingsManager and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cache.Dispose();
                }

                _disposed = true;
            }
        }

        internal sealed class ImdbRating
        {
            public float ratingValue { get; set; }
        }

        internal sealed class ImdbData
        {
            public ImdbRating aggregateRating { get; set; }
        }
    }
}
