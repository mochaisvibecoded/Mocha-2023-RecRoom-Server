using System;
using Mocha2023.Classes.DBs;
using Microsoft.AspNetCore.Mvc;

namespace Mocha2023.Controllers
{

    [ApiController]
    [Mocha2023.Classes.ApiProtection]
    public class PlayerEventController : ControllerBase
    {
        [HttpGet("/api/playerevents/v1/searchlive")]
        public IActionResult SearchLiveEvents(
            [FromQuery] string? query = null)
        {
            return Ok(Array.Empty<object>());
        }

        [HttpGet("/api/playerevents/v1/search")]
        public IActionResult SearchEvents(
            [FromQuery] string? query = null,
            [FromQuery] string? sort = null,
            [FromQuery] string? scheduleFilter = null)
        {
            return Ok(Array.Empty<object>());
        }

    }
}
