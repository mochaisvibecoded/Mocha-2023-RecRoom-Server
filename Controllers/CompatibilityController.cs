using System.Text.Json;
using Mocha2023.Auth;
using Mocha2023.Classes.DBs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using static Mocha2023.Classes.DBs.DBClasses.PlayerDBClasses;
using static Mocha2023.Classes.DBs.DBClasses.RoomDBClasses;

namespace Mocha2023.Controllers
{

    [ApiController]
    [Mocha2023.Classes.ApiProtection]
    public sealed class CompatibilityController : ControllerBase
    {
        [HttpGet("/roomserver/showcase/{accountId:long}")]
        public IActionResult GetRoomShowcase(long accountId)
        {
            if (PlayerDB.Players.FindById(accountId) == null)
                return NotFound();

            long[] roomIds = RoomDB.Rooms.Find(room =>
                    room.CreatorAccountId == accountId && room.State != RoomState.MarkedForDelete)
                .OrderByDescending(room => room.CreatedAt)
                .Take(20)
                .Select(room => room.RoomId)
                .ToArray();
            return Ok(roomIds);
        }

        [HttpGet("/rooms/{roomId:long}")]
        public IActionResult GetLegacyRoom(long roomId)
        {
            var room = RoomDB.GetRoom(roomId);
            return room == null ? NotFound() : Ok(RoomDB.PrepareRoomForClient(room));
        }

        [HttpGet("/api/avatar/v2/{accountId:long}")]
        public IActionResult GetAccountAvatar(long accountId)
        {
            var player = PlayerDB.Players.FindById(accountId);
            return player?.Player == null
                ? NotFound()
                : Ok(player.Player.PlayerExtra?.Avatar ?? new Avatar());
        }

        [HttpGet("/api/inventions/v2/search")]
        [HttpGet("/api/inventions/v1/search")]
        public IActionResult SearchInventions(
            [FromQuery] string? query = null,
            [FromQuery] string? q = null,
            [FromQuery] string? tag = null,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100)
        {
            string? search = string.IsNullOrWhiteSpace(query) ? q : query;
            var rows = CreatorFeatureDB.SearchInventions(
                    search,
                    tag: tag,
                    skip: skip,
                    take: take)
                .Select(CreatorFeatureDB.ToClientInvention)
                .ToArray();
            return Ok(rows);
        }

        [HttpGet("/api/inventions/v1/fromcreators")]
        public IActionResult GetInventionsFromCreators(
            [FromQuery] long creatorAccountId = 0,
            [FromQuery] long accountId = 0,
            [FromQuery] long creatorPlayerId = 0,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100)
        {
            long creatorId = creatorAccountId > 0
                ? creatorAccountId
                : accountId > 0
                    ? accountId
                    : creatorPlayerId;
            var rows = CreatorFeatureDB.SearchInventions(
                    creatorAccountId: creatorId > 0 ? creatorId : null,
                    skip: skip,
                    take: take)
                .Select(CreatorFeatureDB.ToClientInvention)
                .ToArray();
            return Ok(rows);
        }

        [HttpGet("/api/inventions/v1/featureddormskins")]
        public IActionResult GetFeaturedDormSkins(
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100)
        {
            var rows = CreatorFeatureDB.SearchInventions(
                    tag: "Dorm Skin",
                    skip: skip,
                    take: take)
                .Select(CreatorFeatureDB.ToClientInvention)
                .ToArray();
            return Ok(rows);
        }

        [HttpGet("/api/inventions/v2/mine")]
        [HttpGet("/api/inventions/v1/mine")]
        public IActionResult GetMyInventions(
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            var rows = CreatorFeatureDB.SearchInventions(
                    creatorAccountId: accountId.Value,
                    includeUnpublished: true,
                    skip: skip,
                    take: take)
                .Select(CreatorFeatureDB.ToClientInvention)
                .ToArray();
            return Ok(rows);
        }

        [HttpGet("/api/inventions/v1/batch")]
        [HttpGet("/api/inventions/v2/batch")]
        [HttpGet("/api/inventions/v3/batch")]
        [HttpPost("/api/inventions/v1/batch")]
        [HttpPost("/api/inventions/v2/batch")]
        [HttpPost("/api/inventions/v3/batch")]
        [RequestSizeLimit(128 * 1024)]
        public async Task<IActionResult> GetInventionBatch()
        {
            long? callerAccountId = AuthStuff.GetPlayerId(Request);
            List<long> requestedIds = ReadInventionIdsFromQuery();

            if (Request.HasFormContentType)
            {
                IFormCollection form = await Request.ReadFormAsync(
                    HttpContext.RequestAborted);
                foreach ((string key, Microsoft.Extensions.Primitives.StringValues values)
                         in form)
                {
                    string normalizedKey = key.TrimEnd('[', ']').ToLowerInvariant();
                    if (normalizedKey is not "ids" and not "id" and
                        not "inventionids" and not "inventionid" and
                        not "inventions")
                    {
                        continue;
                    }
                    foreach (string? raw in values)
                        AddLongValues(requestedIds, raw);
                }
            }
            else if ((Request.ContentLength ?? 0) > 0)
            {
                try
                {
                    using JsonDocument document = await JsonDocument.ParseAsync(
                        Request.Body,
                        cancellationToken: HttpContext.RequestAborted);
                    AddLongValues(requestedIds, document.RootElement);
                }
                catch (JsonException)
                {
                    return BadRequest(new
                    {
                        Success = false,
                        Error = "invalid_invention_batch"
                    });
                }
            }

            if (requestedIds.Count == 0)
                return Ok(Array.Empty<object>());

            object[] rows = requestedIds
                .Distinct()
                .Take(100)
                .Select(CreatorFeatureDB.GetInvention)
                .Where(value => value != null)
                .Where(value => value!.IsPublished ||
                    (callerAccountId.HasValue &&
                     value.CreatorAccountId == callerAccountId.Value))
                .Select(value => CreatorFeatureDB.ToClientInvention(value!))
                .ToArray();
            return Ok(rows);
        }

        [HttpGet("/api/inventions/{inventionId:long}")]
        [HttpGet("/api/inventions/v1/{inventionId:long}")]
        [HttpGet("/api/inventions/v2/{inventionId:long}")]
        [HttpGet("/api/inventions/v3/{inventionId:long}")]
        public IActionResult GetInvention(long inventionId)
        {
            var invention = CreatorFeatureDB.GetInvention(inventionId);
            return invention == null
                ? NotFound()
                : Ok(CreatorFeatureDB.ToClientInvention(invention));
        }

        [HttpGet("/api/inventions/{inventionId:long}/versions")]
        [HttpGet("/api/inventions/v1/{inventionId:long}/versions")]
        [HttpGet("/api/inventions/v2/{inventionId:long}/versions")]
        [HttpGet("/api/inventions/v3/{inventionId:long}/versions")]
        public IActionResult GetInventionVersions(long inventionId)
        {
            CreatorFeatureDB.InventionRecord? invention =
                CreatorFeatureDB.GetInvention(inventionId);
            if (invention == null)
                return NotFound();

            object? version = CreatorFeatureDB.ToClientInventionVersion(invention);
            return version == null
                ? StatusCode(StatusCodes.Status409Conflict,
                    new { Success = false, Error = "invention_object_blob_missing" })
                : Ok(new[] { version });
        }

        [HttpGet("/api/inventions/{inventionId:long}/versions/{versionNumber:int}")]
        [HttpGet("/api/inventions/{inventionId:long}/version/{versionNumber:int}")]
        [HttpGet("/api/inventions/{inventionId:long}/{versionNumber:int}")]
        [HttpGet("/api/inventions/v1/{inventionId:long}/versions/{versionNumber:int}")]
        [HttpGet("/api/inventions/v2/{inventionId:long}/versions/{versionNumber:int}")]
        [HttpGet("/api/inventions/v3/{inventionId:long}/versions/{versionNumber:int}")]
        public IActionResult GetSpecificInventionVersion(
            long inventionId,
            int versionNumber)
        {
            CreatorFeatureDB.InventionRecord? invention =
                CreatorFeatureDB.GetInvention(inventionId);
            if (invention == null)
                return NotFound();

            if (versionNumber > 0 && versionNumber != Math.Max(1, invention.Version))
                return NotFound();

            object? version = CreatorFeatureDB.ToClientInventionVersion(invention);
            return version == null
                ? StatusCode(StatusCodes.Status409Conflict,
                    new { Success = false, Error = "invention_object_blob_missing" })
                : Ok(version);
        }

        [HttpGet("/api/inventions/v1/get")]
        [HttpGet("/api/inventions/v2/get")]
        public IActionResult GetInventionFromQuery(
            [FromQuery] long inventionId = 0,
            [FromQuery] long id = 0)
        {
            long resolvedId = inventionId > 0 ? inventionId : id;
            var invention = CreatorFeatureDB.GetInvention(resolvedId);
            return invention == null
                ? NotFound()
                : Ok(CreatorFeatureDB.ToClientInvention(invention));
        }

        [HttpGet("/api/inventions/v1")]
        [HttpGet("/api/inventions/v2")]
        public IActionResult GetInventionDetailsFromRoot(
            [FromQuery] long inventionId = 0,
            [FromQuery] long id = 0)
        {
            long resolvedId = inventionId > 0 ? inventionId : id;
            var invention = CreatorFeatureDB.GetInvention(resolvedId);
            return invention == null
                ? NotFound(new { Success = false, Error = "invention_not_found" })
                : Ok(CreatorFeatureDB.ToClientInvention(invention));
        }

        [HttpGet("/api/inventions/v1/details")]
        [HttpGet("/api/inventions/v2/details")]
        public IActionResult GetInventionTagDetails(
            [FromQuery] long inventionId = 0,
            [FromQuery] long id = 0)
        {
            long resolvedId = inventionId > 0 ? inventionId : id;
            var invention = CreatorFeatureDB.GetInvention(resolvedId);
            if (invention == null)
                return NotFound(new { Success = false, Error = "invention_not_found" });

            object client = CreatorFeatureDB.ToClientInvention(invention);
            CreatorFeatureDB.TryGetInventionBlob(
                invention,
                out _,
                out string blobPath,
                out string blobName,
                out string blobHash,
                out long blobSize);

            object? currentVersion =
                CreatorFeatureDB.ToClientInventionVersion(invention);
            bool canSpawn = CreatorFeatureDB.HasValidInventionBlob(invention);
            return Ok(new
            {
                InventionId = invention.InventionId,
                Id = invention.InventionId,
                Tags = invention.Tags ?? new List<string>(),
                TagNames = invention.Tags ?? new List<string>(),
                Version = Math.Max(1, invention.Version),
                CurrentVersion = currentVersion,
                InventionVersion = currentVersion,
                CanSpawn = canSpawn,
                DataBlob = blobName,
                DataBlobPath = blobPath,
                DataBlobHash = blobHash,
                DataBlobSize = blobSize,
                Value = client,
                Invention = client
            });
        }

        [HttpGet("/api/inventions/v1/personaldetails/{inventionId:long}")]
        [HttpGet("/api/inventions/v2/personaldetails/{inventionId:long}")]
        public IActionResult GetPersonalInventionDetails(long inventionId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            var invention = CreatorFeatureDB.GetInvention(inventionId);
            if (invention == null)
                return NotFound(new { Success = false, Error = "invention_not_found" });

            bool isCreator = invention.CreatorAccountId == accountId.Value;
            object client = CreatorFeatureDB.ToClientInvention(invention);
            return Ok(new
            {
                InventionId = invention.InventionId,
                Id = invention.InventionId,
                IsCreator = isCreator,
                IsOwner = isCreator,
                IsSaved = isCreator,
                CanEdit = isCreator,
                CanDelete = isCreator,
                CanSpawn = CreatorFeatureDB.HasValidInventionBlob(invention),
                Cheered = CreatorFeatureDB.IsInventionCheered(
                    invention.InventionId,
                    accountId.Value),
                IsCheered = CreatorFeatureDB.IsInventionCheered(
                    invention.InventionId,
                    accountId.Value),
                CheerCount = CreatorFeatureDB.GetInventionCheerCount(
                    invention.InventionId),
                IsPublished = invention.IsPublished,
                Version = Math.Max(1, invention.Version),
                Value = client,
                Invention = client
            });
        }

        [HttpGet("/api/inventions/v1/personaldetails")]
        [HttpGet("/api/inventions/v2/personaldetails")]
        public IActionResult GetPersonalInventionDetailsFromQuery(
            [FromQuery] long inventionId = 0,
            [FromQuery] long id = 0) =>
            GetPersonalInventionDetails(inventionId > 0 ? inventionId : id);

        [HttpGet("/api/inventions/v1/cheer")]
        [HttpGet("/api/inventions/v2/cheer")]
        [HttpGet("/api/inventions/v1/{inventionId:long}/cheer")]
        [HttpGet("/api/inventions/v2/{inventionId:long}/cheer")]
        public IActionResult GetInventionCheer(long inventionId = 0)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            inventionId = ResolveInventionId(inventionId, Request.Query);
            if (inventionId <= 0)
                return BadRequest(new { Success = false, Error = "invention_id_required" });
            if (CreatorFeatureDB.GetInvention(inventionId) == null)
                return NotFound(new { Success = false, Error = "invention_not_found" });

            bool cheered = CreatorFeatureDB.IsInventionCheered(
                inventionId,
                accountId.Value);
            return Ok(new
            {
                InventionId = inventionId,
                Id = inventionId,
                Cheered = cheered,
                IsCheered = cheered,
                CheerCount = CreatorFeatureDB.GetInventionCheerCount(inventionId)
            });
        }

        [HttpPost("/api/inventions/v1/cheer")]
        [HttpPut("/api/inventions/v1/cheer")]
        [HttpDelete("/api/inventions/v1/cheer")]
        [HttpPost("/api/inventions/v2/cheer")]
        [HttpPut("/api/inventions/v2/cheer")]
        [HttpDelete("/api/inventions/v2/cheer")]
        [HttpPost("/api/inventions/v1/{inventionId:long}/cheer")]
        [HttpPut("/api/inventions/v1/{inventionId:long}/cheer")]
        [HttpDelete("/api/inventions/v1/{inventionId:long}/cheer")]
        [HttpPost("/api/inventions/v2/{inventionId:long}/cheer")]
        [HttpPut("/api/inventions/v2/{inventionId:long}/cheer")]
        [HttpDelete("/api/inventions/v2/{inventionId:long}/cheer")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> SetInventionCheer(long inventionId = 0)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            bool? requestedState = ReadBooleanValue(
                Request.Query,
                "cheer", "cheered", "isCheered", "state", "value");
            inventionId = ResolveInventionId(inventionId, Request.Query);

            if (Request.HasFormContentType)
            {
                IFormCollection form = await Request.ReadFormAsync(
                    HttpContext.RequestAborted);
                inventionId = ResolveInventionId(inventionId, form);
                requestedState ??= ReadBooleanValue(
                    form,
                    "cheer", "cheered", "isCheered", "state", "value");
            }
            else if ((Request.ContentLength ?? 0) > 0)
            {
                try
                {
                    using JsonDocument body = await JsonDocument.ParseAsync(
                        Request.Body,
                        cancellationToken: HttpContext.RequestAborted);
                    inventionId = ResolveInventionId(inventionId, body.RootElement);
                    requestedState ??= ReadBooleanValue(body.RootElement);
                }
                catch (JsonException)
                {
                    return BadRequest(new
                    {
                        Success = false,
                        Error = "invalid_cheer_payload"
                    });
                }
            }

            if (inventionId <= 0)
                return BadRequest(new { Success = false, Error = "invention_id_required" });
            if (CreatorFeatureDB.GetInvention(inventionId) == null)
                return NotFound(new { Success = false, Error = "invention_not_found" });

            bool cheered = HttpMethods.IsDelete(Request.Method)
                ? false
                : requestedState ?? true;
            CreatorFeatureDB.SetInventionCheer(
                inventionId,
                accountId.Value,
                cheered);
            return Ok(new
            {
                Success = true,
                InventionId = inventionId,
                Id = inventionId,
                Cheered = cheered,
                IsCheered = cheered,
                CheerCount = CreatorFeatureDB.GetInventionCheerCount(inventionId)
            });
        }

        [HttpGet("/api/storefronts/v1/buyInvention")]
        [HttpPost("/api/storefronts/v1/buyInvention")]
        [HttpGet("/api/storefronts/v2/buyInvention")]
        [HttpPost("/api/storefronts/v2/buyInvention")]
        [HttpGet("/api/storefronts/v3/buyInvention")]
        [HttpPost("/api/storefronts/v3/buyInvention")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> BuyInvention(
            [FromQuery] long inventionId = 0,
            [FromQuery] long id = 0)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            long requestedId = inventionId > 0 ? inventionId : id;
            requestedId = ResolveInventionId(requestedId, Request.Query);

            if (Request.HasFormContentType)
            {
                IFormCollection form = await Request.ReadFormAsync(
                    HttpContext.RequestAborted);
                requestedId = ResolveInventionId(requestedId, form);
            }
            else if ((Request.ContentLength ?? 0) > 0)
            {
                try
                {
                    using JsonDocument body = await JsonDocument.ParseAsync(
                        Request.Body,
                        cancellationToken: HttpContext.RequestAborted);
                    requestedId = ResolveInventionId(requestedId, body.RootElement);
                }
                catch (JsonException)
                {
                    return BadRequest();
                }
            }

            CreatorFeatureDB.InventionRecord? invention =
                CreatorFeatureDB.GetInvention(requestedId);
            if (invention == null)
                return NotFound();
            if (!invention.IsPublished && invention.CreatorAccountId != accountId.Value)
                return StatusCode(403);
            if (!CreatorFeatureDB.HasValidInventionBlob(invention))
                return StatusCode(StatusCodes.Status409Conflict);

            if (!CreatorFeatureDB.RecordInventionPurchase(
                    accountId.Value,
                    invention.InventionId,
                    pricePaid: 0))
            {
                return BadRequest();
            }

            int balance = PlayerDB.GetCurrencyBalance(
                accountId.Value,
                CurrencyType.RecCenterTokens);
            object inventionValue = CreatorFeatureDB.ToClientInvention(invention);
            object? versionValue = CreatorFeatureDB.ToClientInventionVersion(invention);
            if (versionValue == null)
            {
                return StatusCode(
                    StatusCodes.Status409Conflict,
                    new
                    {
                        Success = false,
                        Error = "invention_object_blob_missing",
                        InventionId = invention.InventionId
                    });
            }

            var inventionResponse = new
            {
                Result = 0,
                Success = true,
                Invention = inventionValue,
                InventionDetails = inventionValue,
                InventionVersion = versionValue,
                CurrentVersion = versionValue,
                Version = versionValue
            };

            int balanceType = (int)BalanceType.NonPlayStationNonPurchasedP2P;
            var balanceUpdateResponse = new
            {
                Balance = (long)balance,
                CurrencyType = (int)CurrencyType.RecCenterTokens,
                BalanceType = balanceType,
                BalanceAddType = balanceType,

                BalanceUpdates = PlayerDB.GetAllCurrencyBalances(accountId.Value)
                    .Select(entry => new
                    {
                        Result = 0,
                        PurchaseResult = 0,
                        Data = entry.CurrencyType == CurrencyType.RecCenterTokens
                            ? (object)inventionValue
                            : Array.Empty<object>(),
                        Invention = entry.CurrencyType == CurrencyType.RecCenterTokens
                            ? inventionValue
                            : null
                    })
                    .ToArray()
            };

            Response.Headers["X-Mocha-Invention-Id"] = invention.InventionId.ToString();
            Response.Headers["X-Mocha-Token-Balance"] = balance.ToString();
            Console.WriteLine(
                $"[INVENTION PURCHASE] account={accountId.Value} " +
                $"invention={invention.InventionId} result=Success balance={balance}");
            return Ok(new
            {
                Success = true,
                InventionResponse = inventionResponse,
                BalanceUpdateResponse = balanceUpdateResponse
            });
        }

        [HttpPost("/api/inventions/v1/settags")]
        [HttpPut("/api/inventions/v1/settags")]
        [HttpPatch("/api/inventions/v1/settags")]
        [HttpPost("/api/inventions/v2/settags")]
        [HttpPut("/api/inventions/v2/settags")]
        [HttpPatch("/api/inventions/v2/settags")]
        [HttpPost("/api/inventions/v3/settags")]
        [HttpPut("/api/inventions/v3/settags")]
        [HttpPatch("/api/inventions/v3/settags")]
        [HttpPost("/api/inventions/v1/{inventionId:long}/settags")]
        [HttpPut("/api/inventions/v1/{inventionId:long}/settags")]
        [HttpPatch("/api/inventions/v1/{inventionId:long}/settags")]
        [HttpPost("/api/inventions/v2/{inventionId:long}/settags")]
        [HttpPut("/api/inventions/v2/{inventionId:long}/settags")]
        [HttpPatch("/api/inventions/v2/{inventionId:long}/settags")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> SetInventionTags(long inventionId = 0)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            var tags = new List<string>();
            inventionId = ResolveInventionId(inventionId, Request.Query);
            AddTagsFromQuery(tags, Request.Query);

            if (Request.HasFormContentType)
            {
                IFormCollection form = await Request.ReadFormAsync(
                    HttpContext.RequestAborted);
                inventionId = ResolveInventionId(inventionId, form);
                AddTagsFromForm(tags, form);
            }
            else if ((Request.ContentLength ?? 0) > 0)
            {
                try
                {
                    using JsonDocument document = await JsonDocument.ParseAsync(
                        Request.Body,
                        cancellationToken: HttpContext.RequestAborted);
                    inventionId = ResolveInventionId(
                        inventionId,
                        document.RootElement);
                    AddTagsFromJson(tags, document.RootElement);
                }
                catch (JsonException)
                {
                    return BadRequest(new
                    {
                        Success = false,
                        Error = "invalid_tag_payload"
                    });
                }
            }

            if (inventionId <= 0)
            {
                return BadRequest(new
                {
                    Success = false,
                    Error = "invention_id_required"
                });
            }

            tags = NormalizeTags(tags);
            try
            {
                CreatorFeatureDB.InventionRecord? updated =
                    CreatorFeatureDB.SetInventionTags(
                        inventionId,
                        accountId.Value,
                        tags);
                return updated == null
                    ? NotFound(new
                    {
                        Success = false,
                        Error = "invention_not_found"
                    })
                    : Ok(CreatorFeatureDB.ToClientInvention(updated));
            }
            catch (UnauthorizedAccessException)
            {
                return StatusCode(403);
            }
        }

        [HttpGet("/api/inventions/v1/{inventionId:long}/spawn")]
        [HttpPost("/api/inventions/v1/{inventionId:long}/spawn")]
        [HttpPut("/api/inventions/v1/{inventionId:long}/spawn")]
        [HttpGet("/api/inventions/v2/{inventionId:long}/spawn")]
        [HttpPost("/api/inventions/v2/{inventionId:long}/spawn")]
        [HttpPut("/api/inventions/v2/{inventionId:long}/spawn")]
        [HttpGet("/api/inventions/v1/spawn/{inventionId:long}")]
        [HttpGet("/api/inventions/v2/spawn/{inventionId:long}")]
        [HttpPost("/api/inventions/v1/{inventionId:long}/use")]
        [HttpPut("/api/inventions/v1/{inventionId:long}/use")]
        public IActionResult RecordInventionSpawn(long inventionId)
        {
            if (!AuthStuff.GetPlayerId(Request).HasValue)
                return Unauthorized();

            CreatorFeatureDB.InventionRecord? existing =
                CreatorFeatureDB.GetInvention(inventionId);
            if (existing == null)
                return NotFound(new { Success = false, Error = "invention_not_found" });
            if (!CreatorFeatureDB.HasValidInventionBlob(existing))
            {
                return StatusCode(
                    StatusCodes.Status409Conflict,
                    new
                    {
                        Success = false,
                        Error = "invention_object_blob_missing",
                        InventionId = inventionId
                    });
            }

            CreatorFeatureDB.InventionRecord invention =
                CreatorFeatureDB.IncrementInventionUse(inventionId)!;
            CreatorFeatureDB.TryGetInventionBlob(
                invention,
                out _,
                out string blobPath,
                out string blobName,
                out string blobHash,
                out long blobSize);
            object client = CreatorFeatureDB.ToClientInvention(invention);
            object? currentVersion =
                CreatorFeatureDB.ToClientInventionVersion(invention);
            return Ok(new
            {
                Success = true,
                InventionId = invention.InventionId,
                Id = invention.InventionId,
                Uses = invention.Uses,
                DataBlob = blobName,
                DataBlobPath = blobPath,
                DataBlobHash = blobHash,
                DataBlobSize = blobSize,
                CurrentVersion = currentVersion,
                InventionVersion = currentVersion,
                Version = currentVersion,
                Value = client,
                Invention = client
            });
        }

        [HttpGet("/api/inventions/{inventionId:long}/data")]
        [HttpGet("/api/inventions/{inventionId:long}/download")]
        [HttpGet("/api/inventions/{inventionId:long}/blob")]
        [HttpGet("/api/inventions/v1/{inventionId:long}/data")]
        [HttpGet("/api/inventions/v2/{inventionId:long}/data")]
        [HttpGet("/api/inventions/v1/{inventionId:long}/download")]
        [HttpGet("/api/inventions/v2/{inventionId:long}/download")]
        [HttpGet("/api/inventions/v1/{inventionId:long}/blob")]
        [HttpGet("/api/inventions/v2/{inventionId:long}/blob")]
        [HttpGet("/api/inventions/v1/data/{inventionId:long}")]
        [HttpGet("/api/inventions/v2/data/{inventionId:long}")]
        public IActionResult DownloadInventionData(long inventionId)
        {
            CreatorFeatureDB.InventionRecord? invention =
                CreatorFeatureDB.GetInvention(inventionId);
            if (invention == null)
                return NotFound(new { Success = false, Error = "invention_not_found" });

            if (!CreatorFeatureDB.TryGetInventionBlob(
                    invention,
                    out string fullPath,
                    out _,
                    out string filename,
                    out string hashBase64,
                    out long size))
            {
                return NotFound(new
                {
                    Success = false,
                    Error = "invention_object_blob_missing",
                    InventionId = inventionId
                });
            }

            Response.Headers["X-Content-SHA256"] = hashBase64;
            Response.Headers["X-Content-Length"] = size.ToString();
            Response.Headers.CacheControl = "public,max-age=31536000,immutable";
            return PhysicalFile(
                fullPath,
                "application/octet-stream",
                filename,
                enableRangeProcessing: true);
        }

        [HttpDelete("/api/inventions/v1/{inventionId:long}")]
        [HttpDelete("/api/inventions/v2/{inventionId:long}")]
        public IActionResult DeleteInvention(long inventionId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();
            return CreatorFeatureDB.DeleteInvention(inventionId, accountId.Value)
                ? Ok(new { Success = true, InventionId = inventionId })
                : NotFound(new { Success = false, Error = "invention_not_found" });
        }

        [HttpGet("/api/inventions/v1/tagfilters")]
        public IActionResult GetInventionTagFilters() => Ok(new[]
        {
            "Dorm Skin", "Decoration", "Furniture", "Game", "Gadget",
            "Prop", "Costume", "Art", "Music", "Template"
        });

        [HttpGet("/api/customAvatarItems/v1/minPriceForPublicItem")]
        public IActionResult GetMinimumCustomAvatarItemPrice() => Ok(100);

        [HttpPost("/api/externalfriendinvite/v1/getplatformreferrers")]
        public IActionResult GetPlatformReferrers() => Ok(Array.Empty<object>());

        [HttpPost("/api/consumables/v1/updateActive")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> UpdateActiveConsumable()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            string json;
            try
            {
                using JsonDocument body = await JsonDocument.ParseAsync(Request.Body);
                json = body.RootElement.GetRawText();
            }
            catch (JsonException)
            {
                return BadRequest(new { success = false, error = "invalid_request" });
            }

            PlayerDB.SetPlayerSetting("Consumables.Active", json, accountId.Value);
            return Ok(new { success = true });
        }

        private List<long> ReadInventionIdsFromQuery()
        {
            var output = new List<long>();
            foreach ((string key, Microsoft.Extensions.Primitives.StringValues values)
                     in Request.Query)
            {
                string normalizedKey = key.TrimEnd('[', ']').ToLowerInvariant();
                if (normalizedKey is not "ids" and not "id" and
                    not "inventionids" and not "inventionid" and
                    not "inventions")
                {
                    continue;
                }

                foreach (string? raw in values)
                    AddLongValues(output, raw);
            }
            return output.Where(value => value > 0).ToList();
        }

        private static void AddLongValues(ICollection<long> output, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            string trimmed = raw.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal) ||
                trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(trimmed);
                    AddLongValues(output, document.RootElement);
                    return;
                }
                catch (JsonException)
                {
                }
            }

            foreach (string token in trimmed.Split(
                         new[] { ',', ';', '|', ' ', '\t', '\r', '\n', '[', ']', '"' },
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
            {
                if (long.TryParse(token, out long value) && value > 0)
                    output.Add(value);
            }
        }

        private static void AddLongValues(
            ICollection<long> output,
            JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number &&
                element.TryGetInt64(out long value) && value > 0)
            {
                output.Add(value);
                return;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                AddLongValues(output, element.GetString());
                return;
            }

            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                    AddLongValues(output, item);
                return;
            }

            if (element.ValueKind != JsonValueKind.Object)
                return;

            foreach (JsonProperty property in element.EnumerateObject())
            {
                string name = property.Name.ToLowerInvariant();
                if (name.Contains("invention", StringComparison.Ordinal) ||
                    name is "id" or "ids")
                {
                    AddLongValues(output, property.Value);
                }
            }
        }

        private static long ResolveInventionId(
            long current,
            Microsoft.AspNetCore.Http.IQueryCollection values)
        {
            if (current > 0)
                return current;
            foreach (string key in new[]
                     {
                         "inventionId", "InventionId", "id", "Id"
                     })
            {
                foreach (string? raw in values[key])
                {
                    if (long.TryParse(raw, out long parsed) && parsed > 0)
                        return parsed;
                }
            }
            return current;
        }

        private static long ResolveInventionId(
            long current,
            IFormCollection values)
        {
            if (current > 0)
                return current;
            foreach (string key in new[]
                     {
                         "inventionId", "InventionId", "id", "Id"
                     })
            {
                foreach (string? raw in values[key])
                {
                    if (long.TryParse(raw, out long parsed) && parsed > 0)
                        return parsed;
                }
            }
            return current;
        }

        private static long ResolveInventionId(
            long current,
            JsonElement element)
        {
            if (current > 0)
                return current;
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Name.Equals(
                            "inventionId",
                            StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Equals("id", StringComparison.OrdinalIgnoreCase))
                    {
                        if (property.Value.ValueKind == JsonValueKind.Number &&
                            property.Value.TryGetInt64(out long number) && number > 0)
                        {
                            return number;
                        }
                        if (long.TryParse(property.Value.ToString(), out number) &&
                            number > 0)
                        {
                            return number;
                        }
                    }

                    long nested = ResolveInventionId(0, property.Value);
                    if (nested > 0)
                        return nested;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    long nested = ResolveInventionId(0, item);
                    if (nested > 0)
                        return nested;
                }
            }
            return current;
        }

        private static bool? ReadBooleanValue(
            Microsoft.AspNetCore.Http.IQueryCollection values,
            params string[] keys)
        {
            foreach (string key in keys)
            {
                foreach (string? raw in values[key])
                {
                    bool? parsed = ParseBooleanValue(raw);
                    if (parsed.HasValue)
                        return parsed;
                }
            }
            return null;
        }

        private static bool? ReadBooleanValue(
            IFormCollection values,
            params string[] keys)
        {
            foreach (string key in keys)
            {
                foreach (string? raw in values[key])
                {
                    bool? parsed = ParseBooleanValue(raw);
                    if (parsed.HasValue)
                        return parsed;
                }
            }
            return null;
        }

        private static bool? ReadBooleanValue(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.True)
                return true;
            if (element.ValueKind == JsonValueKind.False)
                return false;
            if (element.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                return ParseBooleanValue(element.ToString());
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    bool? value = ReadBooleanValue(item);
                    if (value.HasValue)
                        return value;
                }
                return null;
            }
            if (element.ValueKind != JsonValueKind.Object)
                return null;

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Name.Equals("cheer", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("cheered", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("isCheered", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("state", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("value", StringComparison.OrdinalIgnoreCase))
                {
                    bool? direct = ReadBooleanValue(property.Value);
                    if (direct.HasValue)
                        return direct;
                }
            }
            return null;
        }

        private static bool? ParseBooleanValue(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            if (bool.TryParse(raw, out bool boolean))
                return boolean;
            if (long.TryParse(raw, out long number))
                return number != 0;
            return raw.Trim().ToLowerInvariant() switch
            {
                "yes" or "on" or "cheer" or "add" => true,
                "no" or "off" or "uncheer" or "remove" => false,
                _ => null
            };
        }

        private static void AddTagsFromQuery(
            ICollection<string> output,
            Microsoft.AspNetCore.Http.IQueryCollection values)
        {
            foreach ((string key, Microsoft.Extensions.Primitives.StringValues items)
                     in values)
            {
                if (!IsTagKey(key))
                    continue;
                foreach (string? raw in items)
                    AddTags(output, raw);
            }
        }

        private static void AddTagsFromForm(
            ICollection<string> output,
            IFormCollection values)
        {
            foreach ((string key, Microsoft.Extensions.Primitives.StringValues items)
                     in values)
            {
                if (!IsTagKey(key))
                    continue;
                foreach (string? raw in items)
                    AddTags(output, raw);
            }
        }

        private static void AddTagsFromJson(
            ICollection<string> output,
            JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                    AddTagsFromJson(output, item);
                return;
            }

            if (element.ValueKind != JsonValueKind.Object)
                return;

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (IsTagKey(property.Name))
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement item in property.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                                AddTags(output, item.GetString());
                            else if (item.ValueKind == JsonValueKind.Object)
                                AddTagObject(output, item);
                            else
                                AddTags(output, item.ToString());
                        }
                    }
                    else if (property.Value.ValueKind == JsonValueKind.Object)
                    {
                        AddTagObject(output, property.Value);
                    }
                    else
                    {
                        AddTags(output, property.Value.ToString());
                    }
                }
                else if (property.Value.ValueKind is
                         JsonValueKind.Object or JsonValueKind.Array)
                {
                    AddTagsFromJson(output, property.Value);
                }
                else if (property.Value.ValueKind == JsonValueKind.String)
                {
                    string? raw = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(raw) &&
                        (raw.TrimStart().StartsWith("[", StringComparison.Ordinal) ||
                         raw.TrimStart().StartsWith("{", StringComparison.Ordinal)))
                    {
                        try
                        {
                            using JsonDocument embedded = JsonDocument.Parse(raw);
                            AddTagsFromJson(output, embedded.RootElement);
                        }
                        catch (JsonException)
                        {
                        }
                    }
                }
            }
        }

        private static void AddTagObject(
            ICollection<string> output,
            JsonElement element)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Name.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("tag", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("value", StringComparison.OrdinalIgnoreCase))
                {
                    AddTags(output, property.Value.ToString());
                }
            }
        }

        private static bool IsTagKey(string key)
        {
            string normalized = key.TrimEnd('[', ']').ToLowerInvariant();
            return normalized is "tag" or "tags" or "tagname" or "tagnames";
        }

        private static void AddTags(ICollection<string> output, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;
            string trimmed = raw.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal) ||
                trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(trimmed);
                    AddTagsFromJson(output, document.RootElement);
                    return;
                }
                catch (JsonException)
                {
                }
            }

            foreach (string value in trimmed.Split(
                         new[] { ',', ';', '|' },
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
            {
                output.Add(value);
            }
        }

        private static List<string> NormalizeTags(IEnumerable<string> values) =>
            values
                .Select(value => value.Trim())
                .Where(value => value.Length is > 0 and <= 32)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(30)
                .ToList();

        [HttpGet("/favicon.ico")]
        public IActionResult Favicon() => NoContent();
    }
}
