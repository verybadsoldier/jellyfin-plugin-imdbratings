using System;
using System.Net.Mime;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ImdbRatings.Api
{
    /// <summary>
    /// API controller for IMDb ratings plugin management and status.
    /// </summary>
    [ApiController]
    [Route("Plugins/ImdbRatings")]
    [Produces(MediaTypeNames.Application.Json)]
    public class ImdbRatingsController : ControllerBase
    {
        private readonly ILogger<ImdbRatingsController> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImdbRatingsController"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        public ImdbRatingsController(ILogger<ImdbRatingsController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Gets the current status of the IMDb ratings database.
        /// </summary>
        /// <response code="200">Database status returned.</response>
        /// <returns>A <see cref="DatabaseStatus"/> object.</returns>
        [HttpGet("Status")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<DatabaseStatus>> GetStatus()
        {
            using var manager = new IMDbRatingsManager(_logger);
            var status = await manager.GetStatusAsync().ConfigureAwait(false);
            return Ok(status);
        }
    }
}
