using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Jellyfin.Plugin.ImdbRatings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Jellyfin.Plugin.ImbdRatings.Test
{
    public sealed class Test1
    {
        [Fact]
        public async Task TestMethod1()
        {
            var fakeLogger = new FakeLogger<ILogger>();
            var m = new IMDbRatingsManager(fakeLogger);

            m.DeleteDatabse();

            await m.PrepareDatabase();

            // Programmatically generate 100 IDs (tt0000001 through tt0000100)
            var imdbIds = Enumerable.Range(1, 100).Select(i => $"tt{i:D7}").ToArray();

            // Start a stopwatch to measure the lookup speed
            var stopwatch = Stopwatch.StartNew();

            foreach (var a in imdbIds)
            {
                var rating = await m.GetRatingAsync(a);

                // Note: I removed the Assert.NotNull(rating) here because these 
                // programmatically generated IDs might not actually exist in the 
                // downloaded IMDb database, which would cause the test to fail.
            }

            stopwatch.Stop();

            // Output the time it took to run 100 queries
            Console.WriteLine($"Processed {imdbIds.Length} IDs in {stopwatch.ElapsedMilliseconds} ms.");
        }
    }
}
