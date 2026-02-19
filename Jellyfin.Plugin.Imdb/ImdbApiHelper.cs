#pragma warning disable CS1591

using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Imdb
{
    public static class ImdbApiHelper
    {
        public static async Task<float?> GetImdbRating(string imdbId, IHttpClientFactory httpClientFactory, ILogger logger)
        {
            var itemUrl = $"https://www.imdb.com/title/{imdbId}/";

            using (var client = httpClientFactory.CreateClient(NamedClient.Default))
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

                    if (!response.IsSuccessStatusCode)
                    {
                        if (response.StatusCode == HttpStatusCode.ServiceUnavailable || response.StatusCode == HttpStatusCode.TooManyRequests)
                        {
                            int waitTime = 240;
                            logger.LogInformation("We were rate limited by IMDb. Current IMDb ID: {ID}. We wait for {Time} seconds now...", imdbId, waitTime);
                            await Task.Delay(waitTime * 1000).ConfigureAwait(false);
                            continue;
                        }
                        else
                        {
                            throw new InvalidOperationException($"Error querying IMDb URL: {itemUrl}. HTTP StatusCode: {response.StatusCode}");
                        }
                    }

                    string responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Regex rx = new Regex("<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Compiled);
                    MatchCollection matches = rx.Matches(responseContent);

                    if (matches.Count == 0)
                    {
                        throw new InvalidOperationException("Error parsing IMDb website. Received empty HTTP response from URL: " + itemUrl);
                    }

                    var jsonData = matches[0].Groups[1].Value;
                    var imdbData = JsonSerializer.Deserialize<ImdbData>(jsonData);

                    if (imdbData?.aggregateRating == null)
                    {
                        logger.LogInformation("No IMDb rating found IMDb ID {ID}", imdbId);
                        return null;
                    }

                    logger.LogInformation("Retrieved IMDb rating for ID {ID}: {Rating}", imdbId, imdbData.aggregateRating.ratingValue);
                    return imdbData.aggregateRating.ratingValue;
                }

                logger.LogError("Failed getting an IMDb rating for ID {ID}", imdbId);
                return null;
            }
        }

        private class ImdbData
        {
            [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1300:Element should begin with upper-case letter", Justification = "Casing needs to match actual JSON data")]
            public ImdbRating? aggregateRating { get; set; }
        }

        private class ImdbRating
        {
            [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1300:Element should begin with upper-case letter", Justification = "Casing needs to match actual JSON data")]
            public float? ratingValue { get; set; }
        }
    }
}
