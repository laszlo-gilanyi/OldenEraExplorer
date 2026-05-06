using API.Models;

namespace API.Services;

public interface IAssetExtractionService
{
    string StartExtraction(StartExtractionRequest request);

    void CancelExtraction();

    void DismissTerminalState();

    ExtractionStatusDto GetStatus();

    bool IsRunning { get; }

    event Action<ExtractionProgressDto>? OnProgressChanged;

    event Action<string>? OnStatusChanged;
}
