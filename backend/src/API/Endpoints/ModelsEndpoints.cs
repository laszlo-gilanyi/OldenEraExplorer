using GameData.Indexing;
using GameData.Services;
using Localization.Indexing;
using Localization.Resolution;
using API.Contracts;
using API.Services;
using static API.Helpers.IconPaths;
using static API.Helpers.LocalizationHelper;

namespace API.Endpoints;

public static class ModelsEndpoints
{
    private const string OrphanGlbSuffix = "_glb";

    public static IEndpointRouteBuilder MapModelsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/models")
            .WithTags("Models")
            ;

        group.MapGet("/units", ListUnitModels)
            .WithName("ListUnitModels")
            .WithSummary("List all available unit models")
            .Produces<List<UnitListItemDto>>(200)
            .Produces<ErrorDto>(503);

        group.MapGet("/map-objects", ListMapObjectModels)
            .WithName("ListMapObjectModels")
            .WithSummary("List all available map object models")
            .Produces<List<MapObjectListItemDto>>(200)
            .Produces<ErrorDto>(503);

        group.MapGet("/artifacts", ListArtifactModels)
            .WithName("ListArtifactModels")
            .WithSummary("List all available artifact models")
            .Produces<List<ArtifactListItemDto>>(200)
            .Produces<ErrorDto>(503);

        group.MapGet("/extracted", ListExtractedModels)
            .WithName("ListExtractedModels")
            .WithSummary("List all models that have been extracted")
            .Produces<List<ExtractedModelDto>>(200);

        group.MapGet("/unit/{id}/glb", GetUnitModelGlb)
            .WithName("GetUnitModelGlb")
            .WithSummary("Get a unit's GLB file")
            .Produces(200, contentType: "model/gltf-binary")
            .Produces<ErrorDto>(404)
            .Produces<ErrorDto>(503);

        group.MapGet("/map-object/{category}/{name}/glb", GetMapObjectModelGlb)
            .WithName("GetMapObjectModelGlb")
            .WithSummary("Get a map object's GLB file")
            .Produces(200, contentType: "model/gltf-binary")
            .Produces<ErrorDto>(404)
            .Produces<ErrorDto>(503);

        group.MapGet("/unit/{id}/status", GetUnitModelStatus)
            .WithName("GetUnitModelStatus")
            .WithSummary("Check if a unit's GLB file exists")
            .Produces<ModelStatusDto>(200);

        group.MapGet("/map-object/{category}/{name}/status", GetMapObjectModelStatus)
            .WithName("GetMapObjectModelStatus")
            .WithSummary("Check if a map object's GLB file exists")
            .Produces<ModelStatusDto>(200);

        return endpoints;
    }

    private static IResult ListUnitModels(
        IGameDataService dataService,
        IAssetServingService assetService,
        IGamePathService gamePathService,
        FactionMapper factionMapper,
        string? search = null)
    {
        var items = new List<UnitListItemDto>();

        if (dataService.IsLoaded && dataService.Data is not null)
        {
            var data = dataService.Data;
            var lang = data.Lang;
            var resolver = data.ResolverFacade;
            var locale = gamePathService.CurrentLocale;
            IEnumerable<GameData.Indexing.DbIndex.UnitRecord> units = data.Units;

            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchTerm = search.Trim();
                var tierLabel = lang.ResolveText("label_unit_tier");

                units = units.Where(u =>
                {
                    if (u.Id.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (u.Fraction.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                        return true;

                    var factionDisplay = factionMapper.MapFactionDisplay(u.Fraction);
                    if (factionDisplay?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true)
                        return true;

                    if (u.Tier.ToString().Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                        return true;

                    var tierText = !string.IsNullOrWhiteSpace(tierLabel) && tierLabel != "label_unit_tier"
                        ? $"{tierLabel} {u.Tier}"
                        : $"Tier {u.Tier}";
                    if (tierText.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                        return true;

                    var localizedName = GetLocalizedUnitName(resolver, lang, u.Id, locale);
                    if (localizedName?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true)
                        return true;

                    return false;
                });
            }

            var dedupedUnits = DeduplicateByMesh(
                units,
                u => u.Mesh,
                u => u.Id);

            items.AddRange(dedupedUnits.Select(u => new UnitListItemDto(
                u.Id,
                GetLocalizedUnitName(resolver, lang, u.Id, locale) ?? u.Id,
                string.IsNullOrEmpty(u.Fraction) ? null : u.Fraction,
                factionMapper.MapFactionDisplay(u.Fraction),
                u.Tier > 0 ? u.Tier : null,
                UnitHexPortrait(u.Id),
                IsOrphan: false,
                Scale: u.Scale,
                PrefabPath: u.Mesh)));
        }

        var extractedDir = assetService.ExtractedAssetsDirectory;
        var langForOrphans = dataService.IsLoaded && dataService.Data is not null
            ? dataService.Data.Lang
            : null;

        if (Directory.Exists(extractedDir))
        {
            var knownModelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingUnitIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (dataService.IsLoaded && dataService.Data is not null)
            {
                foreach (var unit in dataService.Data.Units)
                {
                    existingUnitIds.Add(unit.Id);

                    if (!string.IsNullOrEmpty(unit.Mesh))
                    {
                        var meshModelName = Path.GetFileName(unit.Mesh);
                        knownModelNames.Add(meshModelName);
                    }
                }
            }

            var versionDirs = Directory.GetDirectories(extractedDir, "Assets-*");
            var oldIconVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var versionDir in versionDirs)
            {
                var iconsDir = Path.Combine(versionDir, "Assets", "Resources", "icons", "units", "hex_portraits");
                if (Directory.Exists(iconsDir))
                {
                    foreach (var oldIcon in Directory.GetFiles(iconsDir, "*_old.png"))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(oldIcon);
                        if (fileName.EndsWith("_old", StringComparison.OrdinalIgnoreCase))
                        {
                            var baseName = fileName.Substring(0, fileName.Length - "_old".Length);
                            oldIconVariants.Add(baseName);
                        }
                    }
                }
            }

            var orphanGlbs = new Dictionary<string, (string path, string faction, string glbFileName)>(StringComparer.OrdinalIgnoreCase);

            foreach (var versionDir in versionDirs)
            {
                var unitsDir = Path.Combine(versionDir, "Assets", "Resources", "units");
                if (!Directory.Exists(unitsDir))
                    continue;

                foreach (var glbFile in Directory.GetFiles(unitsDir, "*.glb", SearchOption.AllDirectories))
                {
                    var glbFileName = Path.GetFileNameWithoutExtension(glbFile);

                    if (knownModelNames.Contains(glbFileName))
                        continue;

                    var orphanId = glbFileName;
                    var suffixCounter = 1;
                    while (existingUnitIds.Contains(orphanId))
                    {
                        orphanId = suffixCounter == 1
                            ? glbFileName + OrphanGlbSuffix
                            : $"{glbFileName}{OrphanGlbSuffix}{suffixCounter}";
                        suffixCounter++;
                    }

                    if (orphanGlbs.ContainsKey(orphanId))
                        continue;

                    var relativePath = Path.GetRelativePath(unitsDir, glbFile);
                    var parts = relativePath.Split(Path.DirectorySeparatorChar);
                    var faction = parts.Length > 1 ? parts[0] : null;

                    orphanGlbs[orphanId] = (glbFile, faction ?? "unknown", glbFileName);
                }
            }

            foreach (var (orphanId, (path, faction, glbFileName)) in orphanGlbs)
            {
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var searchTerm = search.Trim();

                    // Special case: "orphan"/"unused" search shows all orphan GLBs regardless of name
                    var isOrphanSearch = searchTerm.Equals("orphan", StringComparison.OrdinalIgnoreCase) ||
                                         searchTerm.Equals("unused", StringComparison.OrdinalIgnoreCase);

                    if (!isOrphanSearch)
                    {
                        var factionDisplay = factionMapper.MapFactionDisplay(faction);

                        if (!glbFileName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) &&
                            !faction.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) &&
                            (factionDisplay == null || !factionDisplay.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)))
                            continue;
                    }
                }

                string iconSuffix = oldIconVariants.Contains(glbFileName) ? "_old" : "_upg";

                items.Add(new UnitListItemDto(
                    orphanId,
                    glbFileName,
                    faction,
                    factionMapper.MapFactionDisplay(faction),
                    null,
                    $"icons/units/hex_portraits/{glbFileName}{iconSuffix}",
                    IsOrphan: true,
                    Scale: null,
                    PrefabPath: null
                ));
            }
        }

        items = items
            .OrderBy(x => x.IsOrphan)
            .ThenBy(x => x.Faction)
            .ThenBy(x => x.Name)
            .ToList();

        return Results.Ok(items);
    }

    private static IResult ListMapObjectModels(
        IGameDataService dataService,
        IAssetServingService assetService,
        IGamePathService gamePathService,
        FactionMapper factionMapper,
        string? search = null,
        string? category = null)
    {
        if (!dataService.IsLoaded || dataService.Data is null)
        {
            return Results.Json(new ErrorDto("Game data not loaded"), statusCode: 503);
        }

        var data = dataService.Data;
        var lang = data.Lang;
        var resolver = data.ResolverFacade;
        var locale = gamePathService.CurrentLocale;

        IEnumerable<MapObjectsIndex.MapObjectRecord> mapObjects =
            data.MapObjectsIndex.MapObjects.Values
                .Where(mo => mo.IsInteractable)
                .Where(mo => !string.Equals(mo.Tag, "Artifact", StringComparison.OrdinalIgnoreCase));

        // Full list needed for orphan detection (before search filter narrows results)
        var expectedGlbNames = data.MapObjectsIndex.MapObjects.Values
            .Where(mo => mo.IsInteractable)
            .Where(mo => !string.Equals(mo.Tag, "Artifact", StringComparison.OrdinalIgnoreCase))
            .Select(mo => mo.PrefabPath)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(path => Path.GetFileName(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(category))
        {
            mapObjects = mapObjects.Where(mo =>
                category.Equals(GetCategory(mo.PrefabPath), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();
            mapObjects = mapObjects.Where(mo =>
                mo.Id.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                GetMapObjectLocalizedName(mo, lang, resolver, locale)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true);
        }

        var dedupedMapObjects = DeduplicateByMesh(
            mapObjects,
            mo => mo.PrefabPath,
            mo => mo.Id);

        var items = dedupedMapObjects
            .Select(mo => MapToMapObjectModelItem(mo, lang, resolver, locale))
            .ToList();

        var extractedDir = assetService.ExtractedAssetsDirectory;
        if (Directory.Exists(extractedDir))
        {
            var categories = new[] { "barracks", "interactive", "resource" };
            var actualGlbFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var glbPathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // name -> category/name

            var versionDirs = Directory.GetDirectories(extractedDir, "Assets-*");
            foreach (var versionDir in versionDirs)
            {
                foreach (var cat in categories)
                {
                    var glbDir = Path.Combine(versionDir, "Assets", "Resources", "objects", cat);
                    if (!Directory.Exists(glbDir)) continue;

                    var files = Directory.GetFiles(glbDir, "*.glb", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        var relativePath = Path.GetRelativePath(glbDir, file);

                        if (relativePath.Contains("debug_objects", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("magic_portal_hex", StringComparison.OrdinalIgnoreCase))
                            continue;

                        actualGlbFiles.Add(name);
                        // Store full path as category/name (e.g., "interactive/castle")
                        glbPathMap[name] = $"{cat}/{name}";
                    }
                }
            }

            var orphanGlbs = actualGlbFiles.Except(expectedGlbNames, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var orphanName in orphanGlbs)
            {
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var searchTerm = search.Trim();
                    var isOrphanSearch = searchTerm.Equals("orphan", StringComparison.OrdinalIgnoreCase) ||
                                         searchTerm.Equals("unused", StringComparison.OrdinalIgnoreCase);
                    if (!isOrphanSearch && !orphanName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                var prefabPath = glbPathMap.TryGetValue(orphanName, out var path) ? path : null;
                var orphanCategory = prefabPath?.Split('/')[0] ?? "Unknown";

                if (!string.IsNullOrWhiteSpace(category))
                {
                    if (!orphanCategory.Equals(category, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                items.Add(new MapObjectListItemDto(
                    orphanName,
                    orphanName,
                    orphanCategory,
                    "icons/placeholder",
                    IsOrphan: true,
                    PrefabPath: prefabPath));
            }
        }

        return Results.Ok(items);
    }

    private static IResult ListArtifactModels(
        IGameDataService dataService,
        IAssetServingService assetService,
        IGamePathService gamePathService,
        FactionMapper factionMapper,
        string? search = null)
    {
        if (!dataService.IsLoaded || dataService.Data is null)
        {
            return Results.Json(new ErrorDto("Game data not loaded"), statusCode: 503);
        }

        var data = dataService.Data;
        var lang = data.Lang;
        var artifactsIndex = data.ArtifactsIndex;
        var mapObjectsIndex = data.MapObjectsIndex;
        var items = new List<ArtifactListItemDto>();

        var artifactsWithPath = artifactsIndex.Artifacts.Values
            .Where(a => HasArtifactLocalization(a, lang))
            .Select(a =>
            {
                MapObjectsIndex.MapObjectRecord? mapObject = null;

                if (mapObjectsIndex.MapObjects.TryGetValue(a.Id, out var mo))
                {
                    mapObject = mo;
                }
                // Scroll boxes use different MapObject IDs than artifact IDs
                else if (a.Id.Contains("scroll_artifact", StringComparison.OrdinalIgnoreCase))
                {
                    string? scrollBoxId = null;
                    if (a.Id.StartsWith("mythic_magic_scroll_artifact", StringComparison.OrdinalIgnoreCase))
                        scrollBoxId = "mythic_scroll_box";
                    else if (a.Id.StartsWith("enchanted_magic_scroll_artifact", StringComparison.OrdinalIgnoreCase))
                        scrollBoxId = "enchanted_scroll_box";
                    else if (a.Id.StartsWith("magic_scroll_artifact", StringComparison.OrdinalIgnoreCase))
                        scrollBoxId = "scroll_box";

                    if (scrollBoxId != null && mapObjectsIndex.MapObjects.TryGetValue(scrollBoxId, out mo))
                    {
                        mapObject = mo;
                    }
                }

                return new
                {
                    Artifact = a,
                    MapObject = mapObject
                };
            })
            .Where(x => x.MapObject != null)
            .ToList();

        var existingPrefabPaths = new HashSet<string>(
            artifactsWithPath
                .Select(x => x.MapObject?.PrefabPath)
                .Where(p => !string.IsNullOrEmpty(p))!,
            StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();
            artifactsWithPath = artifactsWithPath.Where(x =>
                x.Artifact.Id.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (lang.ResolveText(x.Artifact.NameSid)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true) ||
                x.Artifact.Rarity.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.Artifact.Slot.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (GetArtifactRaritySlotText(lang, x.Artifact.Rarity, x.Artifact.Slot)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true))
                .ToList();
        }

        var dedupedArtifacts = DeduplicateByMesh(
            artifactsWithPath,
            item => item.MapObject?.PrefabPath,
            item => item.Artifact.Id);

        foreach (var item in dedupedArtifacts)
        {
            var localizedName = lang.ResolveText(item.Artifact.NameSid);
            var raritySlotText = GetArtifactRaritySlotText(lang, item.Artifact.Rarity, item.Artifact.Slot);
            var prefabPath = item.MapObject?.PrefabPath ?? $"artifact/{item.Artifact.Id}";
            var cleanId = prefabPath.StartsWith("artifact/", StringComparison.OrdinalIgnoreCase)
                ? prefabPath.Substring("artifact/".Length)
                : prefabPath;

            var iconPath = ArtifactGenericScrollIcon(item.Artifact.Id)
                ?? $"icons/artifacts/{item.Artifact.Icon}";

            items.Add(new ArtifactListItemDto(
                cleanId,
                localizedName ?? item.Artifact.Id,
                item.Artifact.Rarity,
                item.Artifact.Slot,
                raritySlotText,
                iconPath,
                IsOrphan: false,
                PrefabPath: prefabPath));
        }

        var extractedDir = assetService.ExtractedAssetsDirectory;
        if (Directory.Exists(extractedDir))
        {
            var versionDirs = Directory.GetDirectories(extractedDir, "Assets-*");
            var orphanGlbs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var versionDir in versionDirs)
            {
                var artifactsDir = Path.Combine(versionDir, "Assets", "Resources", "objects", "artifact");
                if (!Directory.Exists(artifactsDir))
                    continue;

                foreach (var glbFile in Directory.GetFiles(artifactsDir, "*.glb", SearchOption.AllDirectories))
                {
                    var glbFileName = Path.GetFileNameWithoutExtension(glbFile);
                    var prefabPath = $"artifact/{glbFileName}";

                    if (existingPrefabPaths.Contains(prefabPath) || orphanGlbs.ContainsKey(prefabPath))
                        continue;

                    orphanGlbs[prefabPath] = glbFile;
                }
            }

            foreach (var (prefabPath, path) in orphanGlbs)
            {
                var glbFileName = Path.GetFileName(prefabPath);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var searchTerm = search.Trim();
                    var isOrphanSearch = searchTerm.Equals("orphan", StringComparison.OrdinalIgnoreCase) ||
                                         searchTerm.Equals("unused", StringComparison.OrdinalIgnoreCase);
                    if (!isOrphanSearch && !glbFileName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                var cleanId = prefabPath.StartsWith("artifact/", StringComparison.OrdinalIgnoreCase)
                    ? prefabPath.Substring("artifact/".Length)
                    : glbFileName;

                items.Add(new ArtifactListItemDto(
                    cleanId,
                    glbFileName,
                    null,
                    null,
                    null,
                    $"icons/artifacts/{glbFileName}",
                    IsOrphan: true,
                    PrefabPath: prefabPath));
            }
        }

        var result = items
            .OrderBy(x => x.IsOrphan)
            .ThenBy(x => x.Name)
            .ToList();

        return Results.Ok(result);
    }

    private static MapObjectListItemDto MapToMapObjectModelItem(
        MapObjectsIndex.MapObjectRecord mapObject,
        LangIndex lang,
        ITextResolver resolver,
        string? locale)
    {
        var prefabPath = mapObject.PrefabPath;
        var prefabCategory = GetCategory(prefabPath);
        var prefabName = GetName(prefabPath);

        var name = GetMapObjectLocalizedName(mapObject, lang, resolver, locale) ?? prefabName;
        var icon = BuildMapObjectIconPath(prefabPath);

        return new MapObjectListItemDto(
            prefabName,
            name,
            prefabCategory,
            icon,
            IsOrphan: false,
            PrefabPath: prefabPath);
    }

    private static string? GetMapObjectLocalizedName(
        MapObjectsIndex.MapObjectRecord mapObject,
        LangIndex lang,
        ITextResolver resolver,
        string? locale)
    {
        if (!string.IsNullOrWhiteSpace(mapObject.NameSid))
        {
            var result = TryResolveText(resolver, mapObject.NameSid, locale);
            if (!string.IsNullOrWhiteSpace(result))
                return result;

            var langResult = lang.ResolveText(mapObject.NameSid);
            if (!string.IsNullOrWhiteSpace(langResult) && langResult != mapObject.NameSid)
                return langResult;
        }

        var patterns = new[]
        {
            $"{mapObject.Id}_name",
            $"mapobject.{mapObject.Id}.name",
            $"object.{mapObject.Id}.name"
        };

        foreach (var pattern in patterns)
        {
            var result = TryResolveText(resolver, pattern, locale);
            if (!string.IsNullOrWhiteSpace(result))
                return result;

            var langResult = lang.ResolveText(pattern);
            if (!string.IsNullOrWhiteSpace(langResult) && langResult != pattern)
                return langResult;
        }

        return null;
    }

    private static string? BuildMapObjectIconPath(string? prefabPath)
    {
        if (string.IsNullOrWhiteSpace(prefabPath))
            return null;

        return $"objects/{prefabPath.Replace('\\', '/')}";
    }

    private static IResult ListExtractedModels(IAssetServingService assetService)
    {
        var extractedDir = assetService.ExtractedAssetsDirectory;
        var extractedModels = new List<ExtractedModelDto>();

        if (!Directory.Exists(extractedDir))
        {
            return Results.Ok(extractedModels);
        }

        var glbFiles = Directory.GetFiles(extractedDir, "*.glb", SearchOption.AllDirectories);

        foreach (var file in glbFiles)
        {
            var fileInfo = new FileInfo(file);
            var relativePath = Path.GetRelativePath(extractedDir, file).Replace('\\', '/');

            var isUnit = relativePath.Contains("/units/", StringComparison.OrdinalIgnoreCase);
            var isMapObject = relativePath.Contains("/objects/", StringComparison.OrdinalIgnoreCase);

            string id;
            string type;

            if (isUnit)
            {
                id = Path.GetFileNameWithoutExtension(file);
                type = "unit";
            }
            else if (isMapObject)
            {
                var parts = relativePath.Split('/');
                var objectsIndex = Array.FindIndex(parts, p => p.Equals("objects", StringComparison.OrdinalIgnoreCase));
                if (objectsIndex >= 0 && objectsIndex + 2 < parts.Length)
                {
                    var cat = parts[objectsIndex + 1];
                    var name = Path.GetFileNameWithoutExtension(parts[objectsIndex + 2]);
                    id = $"{cat}/{name}";
                }
                else
                {
                    id = Path.GetFileNameWithoutExtension(file);
                }
                type = "map-object";
            }
            else
            {
                continue;
            }

            extractedModels.Add(new ExtractedModelDto(
                id,
                type,
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc));
        }

        var result = extractedModels
            .GroupBy(m => (m.Id, m.Type))
            .Select(g => g.OrderByDescending(m => m.LastExtractedUtc).First())
            .OrderByDescending(m => m.LastExtractedUtc)
            .ToList();

        return Results.Ok(result);
    }

    private static IResult GetUnitModelGlb(
        string id,
        IAssetServingService assetService,
        IGameDataService dataService)
    {
        if (!dataService.IsLoaded || dataService.Data is null)
        {
            return Results.Json(new ErrorDto("Game data not loaded"), statusCode: 503);
        }

        var unit = dataService.Data.Units.FirstOrDefault(u =>
            u.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        var glbPath = ResolveUnitGlbPath(
            assetService.ExtractedAssetsDirectory,
            assetService.CurrentVersion,
            id,
            unit?.Fraction,
            unit?.Mesh);

        if (glbPath == null || !File.Exists(glbPath))
        {
            return Results.NotFound(new ErrorDto($"Model '{id}' not found"));
        }

        var fileBytes = File.ReadAllBytes(glbPath);
        return Results.File(fileBytes, "model/gltf-binary", $"{id}.glb");
    }

    private static IResult GetMapObjectModelGlb(
        string category,
        string name,
        IAssetServingService assetService)
    {
        var glbPath = ResolveMapObjectGlbPath(
            assetService.ExtractedAssetsDirectory,
            assetService.CurrentVersion,
            category,
            name);

        if (glbPath == null || !File.Exists(glbPath))
        {
            return Results.NotFound(new ErrorDto($"Model '{category}/{name}' not found"));
        }

        var fileBytes = File.ReadAllBytes(glbPath);
        var filename = $"{category}_{name}.glb";
        return Results.File(fileBytes, "model/gltf-binary", filename);
    }

    private static IResult GetUnitModelStatus(
        string id,
        IAssetServingService assetService,
        IGameDataService dataService)
    {
        string? faction = null;
        string? mesh = null;
        if (dataService.IsLoaded && dataService.Data is not null)
        {
            var unit = dataService.Data.Units.FirstOrDefault(u =>
                u.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            faction = unit?.Fraction;
            mesh = unit?.Mesh;
        }

        var glbPath = ResolveUnitGlbPath(
            assetService.ExtractedAssetsDirectory,
            assetService.CurrentVersion,
            id,
            faction,
            mesh);

        if (glbPath != null && File.Exists(glbPath))
        {
            var fileInfo = new FileInfo(glbPath);
            return Results.Ok(new ModelStatusDto(true, fileInfo.Length, fileInfo.LastWriteTimeUtc));
        }

        return Results.Ok(new ModelStatusDto(false, null, null));
    }

    private static IResult GetMapObjectModelStatus(
        string category,
        string name,
        IAssetServingService assetService)
    {
        var glbPath = ResolveMapObjectGlbPath(
            assetService.ExtractedAssetsDirectory,
            assetService.CurrentVersion,
            category,
            name);

        if (glbPath != null && File.Exists(glbPath))
        {
            var fileInfo = new FileInfo(glbPath);
            return Results.Ok(new ModelStatusDto(true, fileInfo.Length, fileInfo.LastWriteTimeUtc));
        }

        return Results.Ok(new ModelStatusDto(false, null, null));
    }

    // Search order: current version -> shared -> other versions
    private static string? ResolveUnitGlbPath(
        string extractedDir,
        string? currentVersion,
        string unitId,
        string? faction,
        string? mesh)
    {
        if (!Directory.Exists(extractedDir))
            return null;

        var modelName = unitId;

        if (unitId.EndsWith(OrphanGlbSuffix, StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(mesh))
        {
            modelName = unitId.Substring(0, unitId.Length - OrphanGlbSuffix.Length);
        }
        else if (!string.IsNullOrEmpty(mesh))
        {
            var lastSlash = mesh.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash < mesh.Length - 1)
            {
                modelName = mesh.Substring(lastSlash + 1);
            }
            else
            {
                modelName = mesh;
            }
        }

        if (!string.IsNullOrEmpty(currentVersion))
        {
            var currentVersionDir = Path.Combine(extractedDir, $"Assets-{currentVersion}");
            var result = SearchForUnitGlb(currentVersionDir, modelName, unitId, faction);
            if (result != null) return result;
        }

        // Priority 2: Shared directory
        var sharedDir = Path.Combine(extractedDir, "Assets-shared");
        var sharedResult = SearchForUnitGlb(sharedDir, modelName, unitId, faction);
        if (sharedResult != null) return sharedResult;

        // Priority 3: Other version directories (fallback)
        var versionDirs = Directory.GetDirectories(extractedDir, "Assets-*")
            .Where(d => !d.EndsWith("Assets-shared") &&
                        (string.IsNullOrEmpty(currentVersion) || !d.EndsWith($"Assets-{currentVersion}")))
            .OrderByDescending(d => d); // Prefer newer versions

        foreach (var versionDir in versionDirs)
        {
            var result = SearchForUnitGlb(versionDir, modelName, unitId, faction);
            if (result != null) return result;
        }

        return null;
    }

    private static string? SearchForUnitGlb(string versionDir, string modelName, string unitId, string? faction)
    {
        if (!Directory.Exists(versionDir))
            return null;

        if (!string.IsNullOrEmpty(faction))
        {
            var specificPath = Path.Combine(versionDir, "Assets", "Resources", "units",
                faction.ToLowerInvariant(), $"{modelName.ToLowerInvariant()}.glb");
            if (File.Exists(specificPath))
                return specificPath;
        }

        var unitsDir = Path.Combine(versionDir, "Assets", "Resources", "units");
        if (Directory.Exists(unitsDir))
        {
            var glbFiles = Directory.GetFiles(unitsDir, $"{modelName.ToLowerInvariant()}.glb", SearchOption.AllDirectories);
            if (glbFiles.Length > 0)
                return glbFiles[0];

            if (modelName != unitId)
            {
                glbFiles = Directory.GetFiles(unitsDir, $"{unitId.ToLowerInvariant()}.glb", SearchOption.AllDirectories);
                if (glbFiles.Length > 0)
                    return glbFiles[0];
            }
        }

        return null;
    }

    private static string? ResolveMapObjectGlbPath(
        string extractedDir,
        string? currentVersion,
        string category,
        string name)
    {
        if (!Directory.Exists(extractedDir))
            return null;

        if (!string.IsNullOrEmpty(currentVersion))
        {
            var currentVersionDir = Path.Combine(extractedDir, $"Assets-{currentVersion}");
            var result = SearchForMapObjectGlb(currentVersionDir, category, name);
            if (result != null) return result;
        }

        var sharedDir = Path.Combine(extractedDir, "Assets-shared");
        var sharedResult = SearchForMapObjectGlb(sharedDir, category, name);
        if (sharedResult != null) return sharedResult;

        var versionDirs = Directory.GetDirectories(extractedDir, "Assets-*")
            .Where(d => !d.EndsWith("Assets-shared") &&
                        (string.IsNullOrEmpty(currentVersion) || !d.EndsWith($"Assets-{currentVersion}")))
            .OrderByDescending(d => d);

        foreach (var versionDir in versionDirs)
        {
            var result = SearchForMapObjectGlb(versionDir, category, name);
            if (result != null) return result;
        }

        return null;
    }

    private static string? SearchForMapObjectGlb(string versionDir, string category, string name)
    {
        if (!Directory.Exists(versionDir))
            return null;

        var objectPath = Path.Combine(versionDir, "Assets", "Resources", "objects",
            category.ToLowerInvariant(), $"{name.ToLowerInvariant()}.glb");
        return File.Exists(objectPath) ? objectPath : null;
    }

    private static string? GetCategory(string id)
    {
        var slashIndex = id.IndexOf('/');
        return slashIndex > 0 ? id.Substring(0, slashIndex) : null;
    }

    private static string GetName(string id)
    {
        var slashIndex = id.IndexOf('/');
        return slashIndex > 0 && slashIndex < id.Length - 1 ? id.Substring(slashIndex + 1) : id;
    }

    private static string? GetLocalizedUnitName(
        ITextResolver resolver,
        LangIndex lang,
        string unitId,
        string? locale)
    {
        var nameSid = $"{unitId}_name";

        var name = TryResolveText(resolver, nameSid, locale);
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        // Try direct lang lookup
        name = lang.ResolveText(nameSid);
        if (!string.IsNullOrWhiteSpace(name) && name != nameSid)
            return name;

        name = TryResolveText(resolver, unitId, locale);
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        name = lang.ResolveText(unitId);
        if (!string.IsNullOrWhiteSpace(name) && name != unitId)
            return name;

        return nameSid;
    }

    private static bool HasArtifactLocalization(
        ArtifactsIndex.ArtifactRecord artifact,
        LangIndex lang)
    {
        var nameInLang = !string.IsNullOrWhiteSpace(artifact.NameSid)
            ? lang.ResolveText(artifact.NameSid)
            : null;
        var descInLang = !string.IsNullOrWhiteSpace(artifact.DescSid)
            ? lang.ResolveText(artifact.DescSid)
            : null;

        return nameInLang != null || descInLang != null;
    }

    private static string? GetArtifactRaritySlotText(
        LangIndex lang,
        string? rarity,
        string? slot)
    {
        if (string.IsNullOrWhiteSpace(rarity) || string.IsNullOrWhiteSpace(slot))
            return null;

        var rarityLower = rarity.ToLowerInvariant();
        var slotNormalized = NormalizeSlotForSid(slot);
        var combinedSid = $"artifactRarity_{rarityLower}_{slotNormalized}";
        var raritySlotText = lang.ResolveText(combinedSid);

        // Future-proof fallback: try British spelling if American spelling not found
        if (raritySlotText == null && slotNormalized == "ARMOR")
        {
            var combinedSidBritish = $"artifactRarity_{rarityLower}_ARMOUR";
            raritySlotText = lang.ResolveText(combinedSidBritish);
        }

        return raritySlotText ?? $"{rarity} {slot}";
    }

    private static string NormalizeSlotForSid(string slot)
    {
        return slot.ToLowerInvariant().Replace(" ", "_") switch
        {
            "armour" or "armor" => "ARMOR",
            "back" => "BACK",
            "belt" => "BELT",
            "boots" => "BOOTS",
            "head" => "HEAD",
            "left_hand" or "main_hand" => "LEFT_HAND",
            "right_hand" or "off_hand" => "RIGHT_HAND",
            "ring" => "RING",
            "unique_slot" or "unic_slot" => "UNIQUE_SLOT",
            _ => slot.ToUpperInvariant().Replace(" ", "_")
        };
    }

    private static List<T> DeduplicateByMesh<T>(
        IEnumerable<T> items,
        Func<T, string?> getMeshPath,
        Func<T, string> getId)
    {
        var itemsByMesh = new Dictionary<string, List<T>>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var meshPath = getMeshPath(item);
            if (string.IsNullOrEmpty(meshPath)) continue;

            var meshName = Path.GetFileName(meshPath);
            if (string.IsNullOrEmpty(meshName)) continue;

            if (!itemsByMesh.ContainsKey(meshName))
                itemsByMesh[meshName] = new List<T>();
            itemsByMesh[meshName].Add(item);
        }

        var deduplicated = new List<T>();
        foreach (var (meshName, meshItems) in itemsByMesh)
        {
            const int PreferredPriority = 0;
            const int FallbackPriority = 1;

            // Prefer items whose ID matches the mesh name for clearer naming
            var representative = meshItems
                .OrderBy(item => getId(item).Equals(meshName, StringComparison.OrdinalIgnoreCase)
                    ? PreferredPriority
                    : FallbackPriority)
                .ThenBy(item => getId(item))
                .First();
            deduplicated.Add(representative);
        }

        return deduplicated;
    }
}
