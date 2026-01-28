using System.IO.Compression;
using System.Text.Json;
using GameData.Shared.Utils;

namespace GameData.Indexing;

public class DifficultiesIndex
{
    public List<DifficultyLevel> GuardDifficulties { get; private set; } = new();

    public void Scan(string streamingAssetsRoot)
    {
        var zipPath = Path.Combine(streamingAssetsRoot, "Core.zip");
        if (!File.Exists(zipPath))
            return;

        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.GetEntry("DB/difficulties_lobby.json");
            if (entry == null)
                return;

            using var stream = entry.Open();
            var doc = JsonDocument.Parse(stream);

            if (doc.RootElement.TryGetProperty("guard", out var guardArray) && guardArray.ValueKind == JsonValueKind.Array)
            {
                var difficulties = new List<DifficultyLevel>();

                var index = 0;
                foreach (var item in guardArray.EnumerateArray())
                {
                    if (!item.TryGetProperty("name", out var nameProp) ||
                        !item.TryGetProperty("power", out var powerProp))
                    {
                        continue;
                    }

                    var name = nameProp.GetString();
                    if (string.IsNullOrEmpty(name))
                        continue;

                    if (!powerProp.TryGetDouble(out var power))
                        continue;

                    // Generate icon path based on index (icon_difficulty_0.png, icon_difficulty_1.png, etc.)
                    var iconPath = $"Assets/Texture2D/icon_difficulty_{index}.png";

                    difficulties.Add(new DifficultyLevel(name, power, iconPath));

                    index++;
                }

                GuardDifficulties = difficulties;
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Trace($"[DifficultiesIndex] Error parsing difficulties_lobby.json", ex);
        }
    }
}

public record DifficultyLevel(string Name, double Power, string Icon);
