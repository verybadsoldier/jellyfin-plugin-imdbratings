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
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.Imdb;
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
        IRemoteMetadataProvider<Movie, MovieInfo>, IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILibraryManager _libraryManager;
        private readonly IProviderManager _providerManager;
        private readonly ILogger _logger;
        private ImdbCache _cache;

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
            _cache = new ImdbCache(TimeSpan.FromHours(24));
        }

        public string Name => "The Internet Movie Database Ratings";

        // After primary option
        public int Order => 2;

        private async Task<float?> GetImdbRating(string imdbId)
        {
            var itemUrl = $"https://www.imdb.com/title/{imdbId}/";

            using (var client = _httpClientFactory.CreateClient(NamedClient.Default))
            {
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
                client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
                client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
                client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");

                for (var i = 0; i < 10; ++i)
                {
                    HttpResponseMessage response = await client.GetAsync(itemUrl).ConfigureAwait(false);

                    // Check if the request was successful.
                    if (!response.IsSuccessStatusCode)
                    {
                        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                        {
                            int waitTime = 240;
                            _logger.LogInformation("We were rate limited by IMDb. Current IMDb ID: {ID}. We wait for {Time} seconds now...", imdbId, waitTime);
                            await Task.Delay(waitTime * 1000).ConfigureAwait(false);
                            continue;
                        }
                        else
                        {
                            throw new InvalidOperationException($"Error querying IMDb URL: {itemUrl}. HTTP StatusCode: {response.StatusCode}");
                        }
                    }

                    // Read the response content as a string.
                    string responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    Regex rx = new Regex("<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Compiled);

                    MatchCollection matches = rx.Matches(responseContent);

                    if (matches.Count == 0)
                    {
                        throw new InvalidOperationException("Error parsing IMDb website. Received empty HTTP response from URL: " + itemUrl);
                    }

                    var jsonData = matches[0].Groups[1].Value;
                    var imdbData = JsonSerializer.Deserialize<ImdbData>(jsonData);

                    if (imdbData.aggregateRating == null)
                    {
                        _logger.LogInformation("No IMDb rating found IMDb ID {ID}", imdbId);
                        return null;
                    }

                    _logger.LogInformation("Retrieved IMDb rating for ID {ID}: {Rating}", imdbId, imdbData.aggregateRating.ratingValue);

                    return imdbData.aggregateRating.ratingValue;
                }

                _logger.LogError("Failed getting an IMDb rating for ID {ID}", imdbId);

                return null;
            }
        }

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

            float? rating = _cache.Query(imdbId);

            if (rating == null)
            {
                rating = await GetImdbRating(imdbId).ConfigureAwait(false);

                if (rating != null)
                {
                    _cache.Add(imdbId, rating.Value);
                }
            }

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

        internal class ImdbRating
        {
            public float ratingValue { get; set; }
        }

        internal class ImdbData
        {
            public ImdbRating aggregateRating { get; set; }
        }
    }
}
