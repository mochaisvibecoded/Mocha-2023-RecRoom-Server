using System;
using Mocha2023.Auth;
using Mocha2023.Classes.DBs;
using Microsoft.AspNetCore.Mvc;

namespace Mocha2023.Controllers
{

    [ApiController]
    [Mocha2023.Classes.ApiProtection]
    public class PlatformNotificationController : ControllerBase
    {
        [HttpGet("/accounts/{accountId:long}/receives/{category}")]
        public IActionResult AccountReceivesCategory(
            long accountId,
            string category)
        {
            long? requesterPlayerId = AuthStuff.GetPlayerId(Request);
            if (!requesterPlayerId.HasValue)
                return Unauthorized();

            if (accountId <= 0 ||
                PlayerDB.Players.FindById(accountId)?.Player == null)
            {
                return NotFound(false);
            }

            bool supportedCategory = category.Equals(
                    "GameplayInvites",
                    StringComparison.OrdinalIgnoreCase) ||
                category.Equals("Friends", StringComparison.OrdinalIgnoreCase) ||
                category.Equals("Chat", StringComparison.OrdinalIgnoreCase) ||
                category.Equals("Events", StringComparison.OrdinalIgnoreCase) ||
                category.Equals("RoomNotifications", StringComparison.OrdinalIgnoreCase) ||
                category.Equals("FavoriteFriendOnline", StringComparison.OrdinalIgnoreCase) ||
                category.Equals("Feed", StringComparison.OrdinalIgnoreCase) ||
                category.Equals("Creator", StringComparison.OrdinalIgnoreCase) ||
                category.Equals("Store", StringComparison.OrdinalIgnoreCase);

            if (supportedCategory &&
                category.Equals(
                    "GameplayInvites",
                    StringComparison.OrdinalIgnoreCase))
            {
                var relationship = RelationshipDB.GetClientRelationship(
                    accountId,
                    requesterPlayerId.Value);
                bool recipientBlockedRequester = relationship?.Ignored is 1 or 3;
                if (recipientBlockedRequester)
                    return Ok(false);
            }

            return Ok(supportedCategory);
        }
    }
}
