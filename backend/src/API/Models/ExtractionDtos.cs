namespace API.Models;

public record StartExtractionRequest(
    bool ExtractPng = true,
    bool ExtractGlb = true,
    bool ForceReExtract = false
);

public record StartExtractionResponse(string JobId);

public record ExtractionProgressDto(
    string JobId,
    string Phase,
    int Current,
    int Total,
    double Percent,
    string CurrentAsset,
    string Elapsed,
    string? EstimatedRemaining
);

public record ExtractionStatusDto(
    string Status,
    ExtractionProgressDto? Progress,
    DateTime? LastExtractedAt = null,
    int IconCount = 0,
    int ModelCount = 0,
    string? GameVersion = null,
    string? Error = null
);

public record AutoExtractSettingDto(bool Enabled);

public record UpdateAutoExtractRequest(bool Enabled);
