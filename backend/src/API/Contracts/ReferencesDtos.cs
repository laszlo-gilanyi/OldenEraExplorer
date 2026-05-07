namespace API.Contracts;

public record EntityReferenceDto(
    string EntityId,
    string EntityType,
    string? DisplayName,
    string PropertyPath,
    string? IconPath
);

public record EntityReferencesResponse(
    string EntityId,
    string EntityType,
    List<EntityReferenceDto> ReferencedBy
);
