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
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.Imdb;
using Jellyfin.Plugin.IMDb;
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
        IRemoteMetadataProvider<Movie, MovieInfo>, IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder, IDisposable
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

        // After primary option
        public int Order => 2;

        private BaseItem GetBaseItemFromPath(string path)
        {
            var items = _libraryManager.GetItemList(new InternalItemsQuery());

            // Find the BaseItem with the matching file path
            BaseItem item = items.FirstOrDefault(item => item.Path == path);
            if (item == null)
            {
                throw new ArgumentException($"Could not find item with path '{path}' in the library. This should not happen?!");
            }

            return item;
        }

        private IEnumerable<IMetadataProvider<TItem>> GetMetaDataProviders<TItem>(BaseItem item, LibraryOptions options)
            where TItem : BaseItem, new()
        {
            var providerType = _providerManager.GetType();
            var metGetMeta = providerType.GetMethod("GetMetadataProviders");

            MethodInfo genMetGetMeta = metGetMeta.MakeGenericMethod(typeof(TItem));

            return (IEnumerable<IMetadataProvider<TItem>>)genMetGetMeta.Invoke(_providerManager, new object[] { item, options });
        }

        /*
         * Use other providers to obtain IMDb ID
         */
        private async Task<string> GetImdbId<TItem, TInfo>(TInfo info, CancellationToken cancellationToken)
            where TItem : BaseItem, IHasLookupInfo<TInfo>, new()
            where TInfo : ItemLookupInfo, new()
        {
            // get the item related to this search info. We need it to properly get all providers and options
            var item = GetBaseItemFromPath(info.Path);

            var options = _libraryManager.GetLibraryOptions(item);
            IEnumerable<IMetadataProvider<TItem>> providers = GetMetaDataProviders<TItem>(item, options);

            // filter for provides that can handle this media type and also ignore ourselves
            var providerList = providers.OfType<IRemoteMetadataProvider<TItem, TInfo>>().Where(x => x != this).ToList();

            foreach (var provider in providerList)
            {
                MetadataResult<TItem> localItem = await provider.GetMetadata(info, cancellationToken).ConfigureAwait(false);

                if (localItem.HasMetadata)
                {
                    var id = localItem.Item.GetProviderId(MetadataProvider.Imdb);
                    if (id != null)
                    {
                        return id;
                    }
                }
            }

            _logger.LogWarning("Could not get an IMDb ID for ítem: {Item}", item.Path);

            return null;
        }

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
            if (imdbId == null)
            {
                imdbId = await GetImdbId<TBase, TLookupInfo>(info, cancellationToken).ConfigureAwait(false);
            }

            if (imdbId == null)
            {
                return result;
            }

            float? rating = await _cache.GetRatingAsync(imdbId).ConfigureAwait(false);

            _logger.LogInformation("Fetched IMDb rating for ID '{0}': {1}", imdbId, rating);

            result.Item.CommunityRating = rating;
            result.HasMetadata = true;
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
