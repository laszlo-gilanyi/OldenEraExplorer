using GameData.Indexing;
using API.Contracts;
using API.Models;
using API.Services;
using static API.Helpers.IconPaths;

namespace API.Endpoints;

public static class ReferencesEndpoints
{
    public static IEndpointRouteBuilder MapReferencesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/references")
            .WithTags("References")
            ;

        // GET /api/references/{entityType}/{id} - Get entities that reference this entity
        group.MapGet("/{entityType}/{id}", GetReferences)
            .WithName("GetEntityReferences")
            .WithSummary("Get entities that reference this entity")
            .WithDescription("Returns a list of entities that reference the specified entity (backward links). " +
                           "For example, for a spell, this returns all units that can cast it.")
            .Produces<EntityReferencesResponse>(200)
            .Produces<ErrorDto>(400)
            .Produces<ErrorDto>(404)
            .Produces<ErrorDto>(503);

        return endpoints;
    }

    private static IResult GetReferences(
        string entityType,
        string id,
        IReferenceIndexService referenceService,
        IGameDataService dataService)
    {
        if (!dataService.IsLoaded || dataService.Data is null)
        {
            return Results.Json(
                new ErrorDto("Game data not loaded", "Please load game data first using POST /api/game/load"),
                statusCode: 503
            );
        }

        if (!TryParseEntityType(entityType, out var parsedType))
        {
            return Results.BadRequest(new ErrorDto(
                $"Invalid entity type: {entityType}",
                $"Valid types are: {string.Join(", ", GetValidEntityTypes())}"
            ));
        }

        // Verify entity exists
        if (!EntityExists(parsedType, id, dataService))
        {
            return Results.NotFound(new ErrorDto(
                $"{entityType} '{id}' not found",
                $"Check the {entityType} ID and try again."
            ));
        }

        var references = referenceService.GetReferencedBy(id, parsedType);

        // Map to DTOs, excluding entities that are not visible in the app
        var referenceDtos = references
            .Where(r => IsEntityVisible(r.EntityType, r.EntityId, dataService))
            .Select(r => new EntityReferenceDto(
                EntityId: r.EntityId,
                EntityType: EntityTypeToString(r.EntityType),
                DisplayName: r.DisplayName,
                PropertyPath: r.PropertyPath,
                IconPath: GetIconPathForReference(r.EntityType, r.EntityId, dataService.Data!)
            ))
            .ToList();

        var response = new EntityReferencesResponse(
            EntityId: id,
            EntityType: entityType,
            ReferencedBy: referenceDtos
        );

        return Results.Ok(response);
    }

    private static bool TryParseEntityType(string typeString, out EntityType entityType)
    {
        var lowerType = typeString.ToLowerInvariant();

        entityType = lowerType switch
        {
            "unit" or "units" => EntityType.Unit,
            "hero" or "heroes" => EntityType.Hero,
            "skill" or "skills" => EntityType.Skill,
            "ability" or "abilities" => EntityType.Ability,
            "spell" or "spells" => EntityType.Spell,
            "artifact" or "artifacts" => EntityType.Artifact,
            "building" or "buildings" => EntityType.Building,
            "subclass" or "subclasses" => EntityType.Subclass,
            "mapobject" or "mapobjects" or "map-object" or "map-objects" => EntityType.MapObject,
            "text" or "texts" => EntityType.Text,
            _ => (EntityType)(-1)  // Invalid value
        };

        return (int)entityType >= 0;
    }

    private static string EntityTypeToString(EntityType entityType)
    {
        return entityType switch
        {
            EntityType.Unit => "unit",
            EntityType.Hero => "hero",
            EntityType.Skill => "skill",
            EntityType.Ability => "ability",
            EntityType.Spell => "spell",
            EntityType.Artifact => "artifact",
            EntityType.Building => "building",
            EntityType.Subclass => "subclass",
            EntityType.MapObject => "mapobject",
            EntityType.Text => "text",
            _ => entityType.ToString().ToLowerInvariant()
        };
    }

    private static IEnumerable<string> GetValidEntityTypes()
    {
        return new[]
        {
            "unit", "hero", "skill", "ability", "spell",
            "artifact", "building", "subclass", "mapobject", "text"
        };
    }

    private static string? GetIconPathForReference(EntityType entityType, string id, GameDataLoadResult data)
    {
        switch (entityType)
        {
            case EntityType.Hero:
                if (data.HeroesIndex.Heroes.TryGetValue(id, out var hero) && !string.IsNullOrEmpty(hero.Icon))
                    return HeroLargePortrait(hero.Icon);
                return null;
            case EntityType.Unit:
                return UnitHexPortrait(id);
            default:
                return null;
        }
    }

    private static bool EntityExists(EntityType entityType, string id, IGameDataService dataService)
    {
        var data = dataService.Data!;

        return entityType switch
        {
            EntityType.Unit => data.Units.Any(u => u.Id.Equals(id, StringComparison.OrdinalIgnoreCase)),
            EntityType.Hero => data.HeroesIndex.Heroes.ContainsKey(id),
            EntityType.Skill => data.SkillsIndex.Skills.ContainsKey(id),
            EntityType.Ability => data.AbilityIndex.Abilities.ContainsKey(id),
            EntityType.Spell => data.SpellsIndex.Spells.ContainsKey(id),
            EntityType.Artifact => data.ArtifactsIndex.Artifacts.ContainsKey(id),
            EntityType.Building => data.BuildingsIndex.Buildings.ContainsKey(id),
            EntityType.Subclass => data.SubclassesIndex.Subclasses.ContainsKey(id),
            EntityType.MapObject => data.MapObjectsIndex.MapObjects.ContainsKey(id),
            _ => false
        };
    }

    private static bool IsEntityVisible(EntityType entityType, string id, IGameDataService dataService)
    {
        var data = dataService.Data!;

        return entityType switch
        {
            // Heroes: exclude tutorial/campaign heroes
            EntityType.Hero => data.HeroesIndex.Heroes.TryGetValue(id, out var hero) &&
                              !hero.IsTutorialOrCampaignHero,

            // Skills: exclude pseudo-skills, campaign_* skills, and skills without localization
            EntityType.Skill => data.SkillsIndex.Skills.TryGetValue(id, out var skill) &&
                               !skill.IsPseudoSkill &&
                               !skill.SkillId.StartsWith("campaign_", StringComparison.OrdinalIgnoreCase) &&
                               HasSkillLocalization(skill, data.Lang),

            // Artifacts: require name OR description localization
            EntityType.Artifact => data.ArtifactsIndex.Artifacts.TryGetValue(id, out var artifact) &&
                                  HasArtifactLocalization(artifact, data.Lang),

            // Subclasses: require name OR description localization
            EntityType.Subclass => data.SubclassesIndex.Subclasses.TryGetValue(id, out var subclass) &&
                                  HasSubclassLocalization(subclass, data.Lang),

            // All other entity types are visible if they exist
            _ => EntityExists(entityType, id, dataService)
        };
    }

    private static bool HasSkillLocalization(
        SkillsIndex.SkillRecord skill,
        Localization.Indexing.LangIndex langIndex)
    {
        var nameInLang = !string.IsNullOrWhiteSpace(skill.NameSid)
            ? langIndex.ResolveText(skill.NameSid)
            : null;
        var descInLang = !string.IsNullOrWhiteSpace(skill.DescSid)
            ? langIndex.ResolveText(skill.DescSid)
            : null;

        return nameInLang != null || descInLang != null;
    }

    private static bool HasArtifactLocalization(
        ArtifactsIndex.ArtifactRecord artifact,
        Localization.Indexing.LangIndex langIndex)
    {
        var nameInLang = !string.IsNullOrWhiteSpace(artifact.NameSid)
            ? langIndex.ResolveText(artifact.NameSid)
            : null;
        var descInLang = !string.IsNullOrWhiteSpace(artifact.DescSid)
            ? langIndex.ResolveText(artifact.DescSid)
            : null;

        return nameInLang != null || descInLang != null;
    }

    private static bool HasSubclassLocalization(
        SubclassesIndex.SubclassRecord subclass,
        Localization.Indexing.LangIndex langIndex)
    {
        var nameInLang = !string.IsNullOrWhiteSpace(subclass.NameSid)
            ? langIndex.ResolveText(subclass.NameSid)
            : null;
        var descInLang = !string.IsNullOrWhiteSpace(subclass.DescSid)
            ? langIndex.ResolveText(subclass.DescSid)
            : null;

        return nameInLang != null || descInLang != null;
    }
}
