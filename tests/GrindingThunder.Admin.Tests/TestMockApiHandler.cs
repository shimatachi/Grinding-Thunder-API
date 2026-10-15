using System.Net;
using System.Text;
using System.Text.Json;

namespace GrindingThunder.Admin.Tests;

public class TestMockApiHandler : HttpMessageHandler
{
    public static readonly Guid NationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid OtherNationId = Guid.Parse("11111112-1111-1112-1112-111111111112");
    public static readonly Guid OtherTreeId = Guid.Parse("55555556-5555-5556-5556-555555555556");
    public static readonly Guid GroundTypeId = Guid.Parse("22222223-2222-2223-2223-222222222223");
    public static readonly Guid OtherVehicleId = Guid.Parse("33333335-3333-3335-3335-333333333335");
    public static readonly Guid VehicleTypeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid VehicleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid VehicleId2 = Guid.Parse("33333334-3333-3334-3334-333333333334");
    public static readonly Guid AvailableVehicleId = Guid.Parse("33333336-3333-3336-3336-333333333336");
    public static readonly Guid CreatedVehicleId = Guid.Parse("33333337-3333-3337-3337-333333333337");
    public static readonly Guid GameUpdateId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid GameUpdate2Id = Guid.Parse("44444445-4444-4445-4445-444444444445");
    public static readonly Guid ResearchTreeId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    public static readonly Guid DraftVersionId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    public static readonly Guid PublishedVersionId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    public static readonly Guid RankId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    public static readonly Guid Rank2Id = Guid.Parse("88888889-8888-8889-8889-888888888889");
    public static readonly Guid SparseDraftRankId = Guid.Parse("8888888a-8888-888a-888a-88888888888a");
    public static readonly Guid SparsePublishedRankId = Guid.Parse("8888888b-8888-888b-888b-88888888888b");
    public static readonly Guid EntryId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    public static readonly Guid EntryId2 = Guid.Parse("9999999a-9999-999a-999a-99999999999a");
    public static readonly Guid AdminAccountId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public List<(string Path, string Body)> RecordedJsonRequests { get; } = [];
    public List<HttpRequestMessage> RecordedRequests { get; } = [];
    public bool ConflictNextCatalogMutation { get; set; }
    public bool ConflictNextDelete { get; set; }
    public bool InvalidNextValidation { get; set; }
    public bool InvalidNextImportPreview { get; set; }
    public bool FailNextPublish { get; set; }
    public bool ConflictNextAccountMutation { get; set; }
    public bool ConflictNextEntryUpdate { get; set; }
    public bool DraftPublished { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RecordedRequests.Add(request);

        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        if (request.Content?.Headers.ContentType?.MediaType == "application/json")
            RecordedJsonRequests.Add((path, await request.Content.ReadAsStringAsync(cancellationToken)));

        if (request.Method == HttpMethod.Post && path.EndsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase))
        {
            var content = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : string.Empty;
            using var doc = JsonDocument.Parse(content);
            var username = doc.RootElement.GetProperty("username").GetString();
            var password = doc.RootElement.GetProperty("password").GetString();

            var role = username switch
            {
                "admin" => "Admin",
                "editor" => "Editor",
                "publisher" => "Publisher",
                _ => null
            };
            if (role is not null && password == $"{username}123")
            {
                var responseBody = JsonSerializer.Serialize(new
                {
                    token = username == "admin" ? "test-jwt-token-xyz" : $"test-{username}-token",
                    tokenType = "Bearer",
                    expiresAt = DateTime.UtcNow.AddHours(2),
                    username,
                    roles = new[] { role }
                });

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
                };
            }

            var errorBody = JsonSerializer.Serialize(new
            {
                message = "Invalid username or password."
            });

            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(errorBody, Encoding.UTF8, "application/json")
            };
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/api/auth/me", StringComparison.OrdinalIgnoreCase))
        {
            var authHeader = request.Headers.Authorization;
            var username = UsernameForToken(authHeader?.Parameter);
            if (authHeader?.Scheme == "Bearer" && username is not null)
            {
                var responseBody = JsonSerializer.Serialize(new
                {
                    username,
                    roles = new[] { username == "admin" ? "Admin" : username == "editor" ? "Editor" : "Publisher" }
                });

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
                };
            }

            var unauthorizedBody = JsonSerializer.Serialize(new
            {
                message = "Unauthorized"
            });

            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(unauthorizedBody, Encoding.UTF8, "application/json")
            };
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/api/nations", StringComparison.OrdinalIgnoreCase))
        {
            return JsonResponse(HttpStatusCode.OK, new object[]
            {
                new
                {
                    id = NationId,
                    name = "USA",
                    researchTrees = new object[]
                    {
                        new
                        {
                            id = ResearchTreeId,
                            vehicleTypeId = VehicleTypeId,
                            vehicleTypeName = "Aviation",
                            versions = new object[]
                            {
                                new { id = DraftVersionId, status = DraftPublished ? 1 : 0, updateVersion = "2.50", treeRanks = new object[] { new { id = RankId, rankNumber = 1, requiredVehiclesUnlocked = 5 }, new { id = SparseDraftRankId, rankNumber = 4, requiredVehiclesUnlocked = 2 } } },
                                new { id = PublishedVersionId, status = 1, updateVersion = "2.49", treeRanks = new object[] { new { id = Rank2Id, rankNumber = 1, requiredVehiclesUnlocked = 4 }, new { id = SparsePublishedRankId, rankNumber = 6, requiredVehiclesUnlocked = 3 } } }
                            }
                        }
                    }
                },
                new
                {
                    id = OtherNationId, name = "Germany",
                    researchTrees = new object[] { new { id = OtherTreeId, vehicleTypeId = GroundTypeId, vehicleTypeName = "Ground", versions = Array.Empty<object>() } }
                }
            });
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/api/vehicles/tree", StringComparison.OrdinalIgnoreCase))
        {
            return JsonResponse(HttpStatusCode.OK, new object[]
            {
                new { id = VehicleId, treeRankId = Rank2Id, researchTreeVersionId = PublishedVersionId, name = "F-16A", imageUrl = (string?)null, rpCost = 210000, slCost = 330000, acquisitionType = 0, isFolderParent = false, folderParentId = (Guid?)null, treeColumn = 0, treeRow = 0, prerequisiteIds = Array.Empty<Guid>() },
                new { id = VehicleId2, treeRankId = Rank2Id, researchTreeVersionId = PublishedVersionId, name = "F-4E", imageUrl = (string?)null, rpCost = 180000, slCost = 290000, acquisitionType = 1, isFolderParent = false, folderParentId = (Guid?)VehicleId, treeColumn = 1, treeRow = 0, prerequisiteIds = new[] { VehicleId } },
                // Same tree and stable vehicle, different snapshot: must never leak into the selected published view.
                new { id = VehicleId, treeRankId = RankId, researchTreeVersionId = DraftVersionId, name = "Other snapshot entry", imageUrl = (string?)null, rpCost = 987654, slCost = 987655, acquisitionType = 0, isFolderParent = false, folderParentId = (Guid?)null, treeColumn = 0, treeRow = 0, prerequisiteIds = Array.Empty<Guid>() }
            });
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/api/research-tree-versions/", StringComparison.OrdinalIgnoreCase))
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 4 && segments[3].Equals("validate", StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParse(segments[2], out var validateId) && (validateId == DraftVersionId || validateId == PublishedVersionId))
            {
                var invalid = InvalidNextValidation;
                InvalidNextValidation = false;
                return JsonResponse(HttpStatusCode.OK, new
                {
                    isValid = !invalid,
                    errors = invalid ? new[] { "Prerequisite cycle detected between F-16A and F-4E.", "Rank gate quota is infeasible." } : Array.Empty<string>(),
                    warnings = new[] { "Check folder placement before publishing." }
                });
            }

            if (path.Equals("/api/research-tree-versions/diff", StringComparison.OrdinalIgnoreCase))
            {
                var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
                if (!Guid.TryParse(query["sourceVersionId"], out var sourceId) || !Guid.TryParse(query["targetVersionId"], out var targetId) ||
                    sourceId == targetId || !new[] { DraftVersionId, PublishedVersionId }.Contains(sourceId) ||
                    !new[] { DraftVersionId, PublishedVersionId }.Contains(targetId))
                    return TextResponse(HttpStatusCode.BadRequest, "Choose two different versions from the same tree.");
                return JsonResponse(HttpStatusCode.OK, new
                {
                    sourceVersionId = sourceId, targetVersionId = targetId, sourceGameUpdateVersion = "2.49", targetGameUpdateVersion = "2.50",
                    addedVehicles = new[] { new { vehicleId = VehicleId2, name = "F-4E", rank = 1, rpCost = 180000, slCost = 290000 } },
                    removedVehicles = new[] { new { vehicleId = VehicleId, name = "F-16A", rank = 1, rpCost = 210000, slCost = 330000 } },
                    modifiedVehicles = new[] { new { vehicleId = VehicleId, name = "F-16A", oldRpCost = 210000, newRpCost = 220000, rpDelta = 10000, oldSlCost = 330000, newSlCost = 350000, slDelta = 20000, oldRankNumber = 1, newRankNumber = 1, oldAcquisitionType = 0, newAcquisitionType = 0, oldColumn = 0, newColumn = 1, oldRow = 0, newRow = 0, oldParentVehicleId = (Guid?)null, newParentVehicleId = (Guid?)null } },
                    addedPrerequisites = new[] { new { vehicleId = VehicleId2, vehicleName = "F-4E", prerequisiteVehicleId = VehicleId, prerequisiteVehicleName = "F-16A" } },
                    removedPrerequisites = Array.Empty<object>(),
                    rankChanges = new[] { new { rankNumber = 1, oldRequiredVehiclesUnlocked = 4, newRequiredVehiclesUnlocked = 5, changeType = "Modified" } },
                    summary = new { addedVehiclesCount = 1, removedVehiclesCount = 1, modifiedVehiclesCount = 1, addedPrerequisitesCount = 1, removedPrerequisitesCount = 0, netRpDelta = 10000L, netSlDelta = 20000L }
                });
            }
        }

        if (path.Contains("/api/admin/", StringComparison.OrdinalIgnoreCase))
        {
            var authHeader = request.Headers.Authorization;
            if (authHeader?.Scheme != "Bearer" || UsernameForToken(authHeader.Parameter) is null)
            {
                return JsonResponse(HttpStatusCode.Unauthorized, new { message = "Unauthorized" });
            }

            var username = UsernameForToken(authHeader.Parameter);
            var role = RoleForUsername(username);
            var requiredPolicy = GetRequiredPolicy(request, path);

            if (!IsAuthorized(role, requiredPolicy))
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            if (ConflictNextCatalogMutation && request.Method is { } method && (method == HttpMethod.Post || method == HttpMethod.Put))
            {
                ConflictNextCatalogMutation = false;
                return TextResponse(HttpStatusCode.Conflict, "A matching catalog item already exists.");
            }

            if (ConflictNextDelete && request.Method == HttpMethod.Delete)
            {
                ConflictNextDelete = false;
                return TextResponse(HttpStatusCode.Conflict, "Cannot delete this catalog item because it is in use.");
            }

            if (path.StartsWith("/api/admin/accounts", StringComparison.OrdinalIgnoreCase))
            {
                return await HandleAccountRequestAsync(request, path);
            }

            if (path.Contains("/api/admin/research-tree-versions", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/api/admin/drafts", StringComparison.OrdinalIgnoreCase))
            {
                return await HandleTreeVersionRequestAsync(request, path);
            }

            return await HandleCatalogRequestAsync(request, path);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static async Task<HttpResponseMessage> HandleCatalogRequestAsync(HttpRequestMessage request, string path)
    {
        if (path.Equals("/api/admin/nations", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Method == HttpMethod.Get) return JsonResponse(HttpStatusCode.OK, new[] { new { id = NationId, name = "USA" } });
            if (request.Method == HttpMethod.Post) return JsonResponse(HttpStatusCode.Created, new { id = NationId, name = "USA" });
        }
        if (path.Equals($"/api/admin/nations/{NationId}", StringComparison.OrdinalIgnoreCase))
            return EntityResponse(request, new { id = NationId, name = "USA" });

        if (path.Equals("/api/admin/vehicle-types", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Method == HttpMethod.Get) return JsonResponse(HttpStatusCode.OK, new[] { new { id = VehicleTypeId, name = "Aviation" }, new { id = GroundTypeId, name = "Ground" } });
            if (request.Method == HttpMethod.Post) return JsonResponse(HttpStatusCode.Created, new { id = VehicleTypeId, name = "Aviation" });
        }
        if (path.Equals($"/api/admin/vehicle-types/{VehicleTypeId}", StringComparison.OrdinalIgnoreCase))
            return EntityResponse(request, new { id = VehicleTypeId, name = "Aviation" });

        if (request.Method == HttpMethod.Get && path.StartsWith("/api/admin/vehicles/tree/", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(path.Split('/').Last(), out var treeId) || (treeId != ResearchTreeId && treeId != OtherTreeId))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            using var all = await HandleCatalogRequestAsync(request, "/api/admin/vehicles");
            using var doc = JsonDocument.Parse(await all.Content.ReadAsStringAsync());
            return JsonResponse(HttpStatusCode.OK, doc.RootElement.EnumerateArray()
                .Where(v => v.GetProperty("researchTreeId").GetGuid() == treeId).ToArray());
        }
        if (path.Equals($"/api/admin/vehicles/{OtherVehicleId}", StringComparison.OrdinalIgnoreCase))
            return EntityResponse(request, new { id = OtherVehicleId, name = "Other tree vehicle", imageUrl = (string?)null,
                researchTreeId = OtherTreeId, nationId = OtherNationId, nationName = "Germany", vehicleTypeId = GroundTypeId, vehicleTypeName = "Ground" });

        if (path.Equals("/api/admin/vehicles", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Method == HttpMethod.Get) return JsonResponse(HttpStatusCode.OK, new object[] {
                new { id = VehicleId, name = "F-16A", imageUrl = "https://example.test/f16.png", researchTreeId = ResearchTreeId, nationId = NationId, nationName = "USA", vehicleTypeId = VehicleTypeId, vehicleTypeName = "Aviation" },
                new { id = VehicleId2, name = "F-4E", imageUrl = (string?)null, researchTreeId = ResearchTreeId, nationId = NationId, nationName = "USA", vehicleTypeId = VehicleTypeId, vehicleTypeName = "Aviation" },
                new { id = AvailableVehicleId, name = "F-15C", imageUrl = "https://example.test/f15.png", researchTreeId = ResearchTreeId, nationId = NationId, nationName = "USA", vehicleTypeId = VehicleTypeId, vehicleTypeName = "Aviation" },
                new { id = OtherVehicleId, name = "Other tree vehicle", imageUrl = (string?)null, researchTreeId = OtherTreeId, nationId = OtherNationId, nationName = "Germany", vehicleTypeId = GroundTypeId, vehicleTypeName = "Ground" }
            });
            if (request.Method == HttpMethod.Post)
            {
                var body = await ReadJsonAsync(request);
                var treeId = body.EnumerateObject()
                    .FirstOrDefault(p => p.Name.Equals("ResearchTreeId", StringComparison.OrdinalIgnoreCase)).Value;
                if (treeId.ValueKind != JsonValueKind.String || !treeId.TryGetGuid(out var ownerId) || ownerId != ResearchTreeId)
                    return TextResponse(HttpStatusCode.BadRequest, "An existing ResearchTreeId is required.");
                var name = body.GetPropertyIgnoreCase("Name").GetString();
                var imageUrl = body.GetPropertyIgnoreCase("ImageUrl").ValueKind == JsonValueKind.Null
                    ? null
                    : body.GetPropertyIgnoreCase("ImageUrl").GetString();
                return JsonResponse(HttpStatusCode.Created, new { id = CreatedVehicleId, name, imageUrl, researchTreeId = ResearchTreeId, nationId = NationId, nationName = "USA", vehicleTypeId = VehicleTypeId, vehicleTypeName = "Aviation" });
            }
        }
        if (path.Equals($"/api/admin/vehicles/{VehicleId}", StringComparison.OrdinalIgnoreCase))
            return EntityResponse(request, new { id = VehicleId, name = "F-16A", imageUrl = "https://example.test/f16.png", researchTreeId = ResearchTreeId, nationId = NationId, nationName = "USA", vehicleTypeId = VehicleTypeId, vehicleTypeName = "Aviation" });

        if (path.Equals("/api/admin/game-updates", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Method == HttpMethod.Get) return JsonResponse(HttpStatusCode.OK, new[] { new { id = GameUpdateId, version = "2.50", name = "Test Update", releaseDate = "2026-10-12" } });
            if (request.Method == HttpMethod.Post) return JsonResponse(HttpStatusCode.Created, new { id = GameUpdateId, version = "2.50", name = "Test Update", releaseDate = "2026-10-12" });
        }
        if (path.Equals($"/api/admin/game-updates/{GameUpdateId}", StringComparison.OrdinalIgnoreCase))
            return EntityResponse(request, new { id = GameUpdateId, version = "2.50", name = "Test Update", releaseDate = "2026-10-12" });

        if (path.Equals("/api/admin/research-trees", StringComparison.OrdinalIgnoreCase))
        {
            var tree = new { id = ResearchTreeId, nationId = NationId, nationName = "USA", vehicleTypeId = VehicleTypeId, vehicleTypeName = "Aviation" };
            if (request.Method == HttpMethod.Get) return JsonResponse(HttpStatusCode.OK, new[] { tree });
            if (request.Method == HttpMethod.Post) return JsonResponse(HttpStatusCode.Created, tree);
        }
        if (path.Equals($"/api/admin/research-trees/{ResearchTreeId}", StringComparison.OrdinalIgnoreCase))
            return EntityResponse(request, new { id = ResearchTreeId, nationId = NationId, nationName = "USA", vehicleTypeId = VehicleTypeId, vehicleTypeName = "Aviation" });

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage EntityResponse(HttpRequestMessage request, object entity)
    {
        if (request.Method == HttpMethod.Get) return JsonResponse(HttpStatusCode.OK, entity);
        if (request.Method == HttpMethod.Put || request.Method == HttpMethod.Delete) return new HttpResponseMessage(HttpStatusCode.NoContent);
        return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
    }

    private async Task<HttpResponseMessage> HandleTreeVersionRequestAsync(HttpRequestMessage request, string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length >= 3 && segments[2].Equals("research-tree-versions", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleResearchTreeVersionRequestAsync(request, segments);
        }

        if (segments.Length >= 3 && segments[2].Equals("drafts", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleDraftRequestAsync(request, segments);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> HandleResearchTreeVersionRequestAsync(HttpRequestMessage request, string[] segments)
    {
        // POST /api/admin/research-tree-versions — create a blank draft.
        if (segments.Length == 3 && request.Method == HttpMethod.Post)
        {
            var body = await ReadJsonAsync(request);
            return JsonResponse(HttpStatusCode.Created, new
            {
                id = DraftVersionId,
                researchTreeId = Guid.Parse(body.GetPropertyIgnoreCase("ResearchTreeId").GetString()!),
                gameUpdateId = Guid.Parse(body.GetPropertyIgnoreCase("GameUpdateId").GetString()!),
                status = "Draft"
            });
        }

        if (segments.Length == 4)
        {
            var versionId = Guid.Parse(segments[3]);

            if (request.Method == HttpMethod.Get)
            {
                if (versionId == DraftVersionId)
                {
                    return JsonResponse(HttpStatusCode.OK, new { id = DraftVersionId, researchTreeId = ResearchTreeId, gameUpdateId = GameUpdateId, gameUpdateVersion = "2.50", status = DraftPublished ? "Published" : "Draft", rankCount = 2, entryCount = 2 });
                }

                if (versionId == PublishedVersionId)
                {
                    return JsonResponse(HttpStatusCode.OK, new { id = PublishedVersionId, researchTreeId = ResearchTreeId, gameUpdateId = GameUpdate2Id, gameUpdateVersion = "2.49", status = "Published", rankCount = 2, entryCount = 2 });
                }

                return TextResponse(HttpStatusCode.NotFound, $"Research tree version '{versionId}' was not found.");
            }

            if (request.Method == HttpMethod.Delete)
            {
                if (versionId == PublishedVersionId)
                {
                    return TextResponse(HttpStatusCode.Conflict, "Published versions cannot be deleted.");
                }

                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        }

        if (segments.Length == 5 && request.Method == HttpMethod.Post && segments[4].Equals("clone", StringComparison.OrdinalIgnoreCase))
        {
            var body = await ReadJsonAsync(request);
            return JsonResponse(HttpStatusCode.Created, new
            {
                id = DraftVersionId,
                researchTreeId = ResearchTreeId,
                gameUpdateId = Guid.Parse(body.GetPropertyIgnoreCase("TargetGameUpdateId").GetString()!),
                status = "Draft"
            });
        }

        if (segments.Length == 5 && request.Method == HttpMethod.Post && segments[4].Equals("publish", StringComparison.OrdinalIgnoreCase))
        {
            if (Guid.Parse(segments[3]) != DraftVersionId) return TextResponse(HttpStatusCode.Conflict, "Version is already published.");
            if (FailNextPublish)
            {
                FailNextPublish = false;
                return TextResponse(HttpStatusCode.BadRequest, "Version validation failed: prerequisite cycle.");
            }
            if (DraftPublished) return TextResponse(HttpStatusCode.Conflict, "Version is already published.");
            DraftPublished = true;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> HandleDraftRequestAsync(HttpRequestMessage request, string[] segments)
    {
        if (segments.Length < 4)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var versionId = Guid.Parse(segments[3]);

        // GET /api/admin/drafts/{versionId} — draft overview; 409 for published versions.
        if (segments.Length == 4 && request.Method == HttpMethod.Get)
        {
            if (versionId != DraftVersionId || DraftPublished)
            {
                return TextResponse(HttpStatusCode.Conflict, $"Research tree version '{versionId}' is not in Draft status (status: Published). Published versions are immutable.");
            }

            return JsonResponse(HttpStatusCode.OK, BuildDraftOverview());
        }

        if (segments.Length is 5 or 6 && segments[4].Equals("import", StringComparison.OrdinalIgnoreCase) && request.Method == HttpMethod.Post)
        {
            if (versionId != DraftVersionId || DraftPublished)
                return TextResponse(HttpStatusCode.Conflict, "Published versions are immutable.");
            if (request.Content is not MultipartFormDataContent multipart ||
                request.Content.Headers.ContentType?.MediaType != "multipart/form-data" ||
                !multipart.Any(part => part.Headers.ContentDisposition?.Name?.Trim('"') == "file"))
                return TextResponse(HttpStatusCode.UnsupportedMediaType, "Multipart form required.");
            var isPreview = segments.Length == 6 && segments[5].Equals("preview", StringComparison.OrdinalIgnoreCase);
            if (isPreview)
            {
                var invalid = InvalidNextImportPreview;
                InvalidNextImportPreview = false;
                return JsonResponse(HttpStatusCode.OK, new
                {
                    isValid = !invalid, totalRowsDetected = 2, newEntriesCount = 1, updatedEntriesCount = 1,
                    autoCreatedVehiclesCount = 1, createdRanksCount = 1,
                    entries = new[] { new { rowNumber = 2, vehicleName = "F-16A", rankNumber = 1, rpCost = 220000, slCost = 350000, treeColumn = 0, treeRow = 0, acquisitionType = 0, folderParentVehicleName = (string?)null, prerequisiteVehicleNames = Array.Empty<string>(), action = "Update", vehicleExistsInCatalog = true } },
                    errors = invalid ? new[] { "Coordinate collision at column 0, row 0." } : Array.Empty<string>(),
                    warnings = new[] { "One vehicle will be created." }
                });
            }
            return JsonResponse(HttpStatusCode.OK, new { success = true, importedEntriesCount = 2, newEntriesCount = 1, updatedEntriesCount = 1, autoCreatedVehiclesCount = 1, createdRanksCount = 1, messages = new[] { "Import committed." }, errors = Array.Empty<string>() });
        }

        // POST /api/admin/drafts/{versionId}/ranks
        if (segments.Length == 5 && segments[4].Equals("ranks", StringComparison.OrdinalIgnoreCase) && request.Method == HttpMethod.Post)
        {
            var body = await ReadJsonAsync(request);
            var rankNumber = body.GetPropertyIgnoreCase("RankNumber").GetInt32();
            var requiredVehiclesUnlocked = body.GetPropertyIgnoreCase("RequiredVehiclesUnlocked").GetInt32();
            if (rankNumber <= 0) return TextResponse(HttpStatusCode.BadRequest, "Rank number must be positive.");
            if (requiredVehiclesUnlocked < 0) return TextResponse(HttpStatusCode.BadRequest, "Required vehicles unlocked cannot be negative.");
            if (rankNumber == 1) return TextResponse(HttpStatusCode.Conflict, $"Rank {rankNumber} already exists in draft version '{versionId}'.");
            return JsonResponse(HttpStatusCode.Created, new { id = Guid.NewGuid(), researchTreeVersionId = versionId, rankNumber, requiredVehiclesUnlocked });
        }

        // PUT/DELETE /api/admin/drafts/{versionId}/ranks/{rankId}
        if (segments.Length == 6 && segments[4].Equals("ranks", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Method == HttpMethod.Put)
            {
                var body = await ReadJsonAsync(request);
                if (body.GetPropertyIgnoreCase("RequiredVehiclesUnlocked").GetInt32() < 0)
                {
                    return TextResponse(HttpStatusCode.BadRequest, "Required vehicles unlocked cannot be negative.");
                }

                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Delete)
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        }

        // ENTRY_AND_PREREQ_SECTIONS

        // POST /api/admin/drafts/{versionId}/entries
        if (segments.Length == 5 && segments[4].Equals("entries", StringComparison.OrdinalIgnoreCase) && request.Method == HttpMethod.Post)
        {
            var body = await ReadJsonAsync(request);
            var vehicleId = Guid.Parse(body.GetPropertyIgnoreCase("VehicleId").GetString()!);
            var rpCost = body.GetPropertyIgnoreCase("RpCost").GetInt32();
            var slCost = body.GetPropertyIgnoreCase("SlCost").GetInt32();
            var treeColumn = body.GetPropertyIgnoreCase("TreeColumn").GetInt32();
            var treeRow = body.GetPropertyIgnoreCase("TreeRow").GetInt32();
            if (rpCost < 0 || slCost < 0) return TextResponse(HttpStatusCode.BadRequest, "RP and SL costs cannot be negative.");
            if (treeColumn < 0 || treeRow < 0) return TextResponse(HttpStatusCode.BadRequest, "TreeColumn and TreeRow must be non-negative.");
            if (vehicleId == VehicleId) return TextResponse(HttpStatusCode.Conflict, $"Vehicle '{vehicleId}' already has an entry in draft version '{versionId}'.");
            return JsonResponse(HttpStatusCode.Created, new
            {
                id = Guid.NewGuid(),
                researchTreeVersionId = versionId,
                vehicleId,
                treeRankId = RankId,
                rpCost,
                slCost,
                treeColumn,
                treeRow,
                acquisitionType = 0,
                folderParentEntryId = (Guid?)null,
                vehicleName = "New Vehicle"
            });
        }

        // PUT/DELETE /api/admin/drafts/{versionId}/entries/{entryId}
        if (segments.Length == 6 && segments[4].Equals("entries", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Method == HttpMethod.Put)
            {
                if (ConflictNextEntryUpdate)
                {
                    ConflictNextEntryUpdate = false;
                    return TextResponse(HttpStatusCode.Conflict, "Coordinate collision: another root entry already occupies that tree position.");
                }

                var body = await ReadJsonAsync(request);
                if (body.GetPropertyIgnoreCase("RpCost").GetInt32() < 0 || body.GetPropertyIgnoreCase("SlCost").GetInt32() < 0)
                    return TextResponse(HttpStatusCode.BadRequest, "RP and SL costs cannot be negative.");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Delete)
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        }

        // POST /api/admin/drafts/{versionId}/prerequisites
        if (segments.Length == 5 && segments[4].Equals("prerequisites", StringComparison.OrdinalIgnoreCase) && request.Method == HttpMethod.Post)
        {
            var body = await ReadJsonAsync(request);
            var vehicleTreeEntryId = Guid.Parse(body.GetPropertyIgnoreCase("VehicleTreeEntryId").GetString()!);
            var prerequisiteVehicleTreeEntryId = Guid.Parse(body.GetPropertyIgnoreCase("PrerequisiteVehicleTreeEntryId").GetString()!);
            if (vehicleTreeEntryId == prerequisiteVehicleTreeEntryId)
            {
                return TextResponse(HttpStatusCode.BadRequest, "An entry cannot be a prerequisite of itself.");
            }

            if (vehicleTreeEntryId == EntryId2 && prerequisiteVehicleTreeEntryId == EntryId)
            {
                return TextResponse(HttpStatusCode.Conflict, "Prerequisite edge already exists in this draft version.");
            }

            return JsonResponse(HttpStatusCode.Created, new
            {
                vehicleTreeEntryId,
                prerequisiteVehicleTreeEntryId,
                researchTreeVersionId = versionId
            });
        }

        // DELETE /api/admin/drafts/{versionId}/prerequisites/{entryId}/{prerequisiteEntryId}
        if (segments.Length == 7 && segments[4].Equals("prerequisites", StringComparison.OrdinalIgnoreCase) && request.Method == HttpMethod.Delete)
        {
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static object BuildDraftOverview() => new
    {
        id = DraftVersionId,
        researchTreeId = ResearchTreeId,
        gameUpdateId = GameUpdateId,
        status = "Draft",
        treeRanks = new object[]
        {
            new { id = RankId, researchTreeVersionId = DraftVersionId, rankNumber = 1, requiredVehiclesUnlocked = 5 },
            new { id = SparseDraftRankId, researchTreeVersionId = DraftVersionId, rankNumber = 4, requiredVehiclesUnlocked = 2 }
        },
        entries = new object[]
        {
            new { id = EntryId, researchTreeVersionId = DraftVersionId, vehicleId = VehicleId, treeRankId = RankId, rpCost = 220000, slCost = 350000, treeColumn = 0, treeRow = 0, acquisitionType = 0, folderParentEntryId = (Guid?)null, vehicleName = "F-16A" },
            new { id = EntryId2, researchTreeVersionId = DraftVersionId, vehicleId = VehicleId2, treeRankId = RankId, rpCost = 180000, slCost = 290000, treeColumn = 1, treeRow = 0, acquisitionType = 1, folderParentEntryId = (Guid?)EntryId, vehicleName = "F-4E" }
        },
        prerequisites = new object[]
        {
            new { vehicleTreeEntryId = EntryId2, prerequisiteVehicleTreeEntryId = EntryId, researchTreeVersionId = DraftVersionId }
        }
    };

    private static async Task<JsonElement> ReadJsonAsync(HttpRequestMessage request)
    {
        var content = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }

    private static string? UsernameForToken(string? token) => token switch
    {
        "test-jwt-token-xyz" => "admin",
        "test-editor-token" => "editor",
        "test-publisher-token" => "publisher",
        _ => null
    };

    private static string? RoleForUsername(string? username) => username switch
    {
        "admin" => "Admin",
        "editor" => "Editor",
        "publisher" => "Publisher",
        _ => null
    };

    private async Task<HttpResponseMessage> HandleAccountRequestAsync(HttpRequestMessage request, string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (path.Equals("/api/admin/accounts", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse(HttpStatusCode.OK, new object[]
                {
                    new { id = AdminAccountId, username = "admin", roles = new[] { "Admin" }, isActive = true },
                    new { id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), username = "editor", roles = new[] { "Editor" }, isActive = true }
                });
            }

            if (request.Method == HttpMethod.Post)
            {
                if (ConflictNextAccountMutation)
                {
                    ConflictNextAccountMutation = false;
                    return JsonResponse(HttpStatusCode.Conflict, new { message = "Username is already taken." });
                }

                var body = await ReadJsonAsync(request);
                var roles = body.GetPropertyIgnoreCase("Roles").EnumerateArray().Select(r => r.GetString()).ToArray();
                return JsonResponse(HttpStatusCode.Created, new
                {
                    id = Guid.NewGuid(),
                    username = body.GetPropertyIgnoreCase("Username").GetString(),
                    roles,
                    isActive = true
                });
            }
        }

        if (segments.Length == 4 && Guid.TryParse(segments[3], out var accountId))
        {
            if (accountId != AdminAccountId)
            {
                return JsonResponse(HttpStatusCode.NotFound, new { message = "Account was not found." });
            }

            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse(HttpStatusCode.OK, new { id = AdminAccountId, username = "admin", roles = new[] { "Admin" }, isActive = true });
            }

            if (request.Method == HttpMethod.Put)
            {
                if (ConflictNextAccountMutation)
                {
                    ConflictNextAccountMutation = false;
                    return JsonResponse(HttpStatusCode.Conflict, new { message = "This change would leave the system without an active Admin account." });
                }

                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        }

        if (segments.Length == 5 && segments[4].Equals("password", StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(segments[3], out var passwordAccountId))
        {
            if (passwordAccountId != AdminAccountId)
            {
                return JsonResponse(HttpStatusCode.NotFound, new { message = "Account was not found." });
            }

            if (request.Method == HttpMethod.Put)
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static string GetRequiredPolicy(HttpRequestMessage request, string path)
    {
        if (request.Method == HttpMethod.Get && path.StartsWith("/api/admin/vehicles/tree/", StringComparison.OrdinalIgnoreCase))
            return "AdminRead";
        if (path.StartsWith("/api/admin/accounts", StringComparison.OrdinalIgnoreCase))
        {
            return "RequireAdminRole";
        }

        if (request.Method == HttpMethod.Delete &&
            (path.StartsWith("/api/admin/research-tree-versions/", StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith("/api/admin/nations/", StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith("/api/admin/vehicle-types/", StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith("/api/admin/vehicles/", StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith("/api/admin/game-updates/", StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith("/api/admin/research-trees/", StringComparison.OrdinalIgnoreCase)))
        {
            return "RequireAdminRole";
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/publish", StringComparison.OrdinalIgnoreCase))
        {
            return "RequirePublisherRole";
        }

        return "RequireEditorRole";
    }

    private static bool IsAuthorized(string? role, string requiredPolicy) => requiredPolicy switch
    {
        "AdminRead" => role is "Admin" or "Editor" or "Publisher",
        "RequireAdminRole" => role == "Admin",
        "RequirePublisherRole" => role == "Publisher" || role == "Admin",
        "RequireEditorRole" => role == "Editor" || role == "Admin",
        _ => false
    };

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object body) => new(statusCode)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage TextResponse(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/plain")
    };
}

internal static class JsonElementExtensions
{
    public static JsonElement GetPropertyIgnoreCase(this JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        throw new KeyNotFoundException($"Property '{name}' was not found.");
    }
}

