#nullable enable

namespace AssetExtractor.Extraction.Interfaces;

public interface IGameObjectProvider
{
    UnityReader.GameObject? FindGameObjectByPathId(string sourceFile, long pathId);
    UnityReader.GameObject? FindPrefabByName(string prefabName);
    AssetLoader Loader { get; }
}
