#nullable enable
using System.Collections.Concurrent;
using System.Numerics;
using AssetExtractor.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetExtractor.Extraction;

public class MaterialExtractor
{
    private readonly ILogger<MaterialExtractor> _logger;
    private readonly ThreadSafeTextureCache _textureCache;
    private readonly ConcurrentDictionary<long, MaterialData> _materialCache = new();

    public MaterialExtractor(ILogger<MaterialExtractor>? logger = null)
    {
        _logger = logger ?? NullLogger<MaterialExtractor>.Instance;
        _textureCache = new ThreadSafeTextureCache(logger: NullLogger<ThreadSafeTextureCache>.Instance);
    }

    public List<MaterialData> ExtractMaterials(UnityReader.SkinnedMeshRenderer smr, PrefabData prefabData, AssetLoader assetLoader)
    {
        var materials = new List<MaterialData>();
        foreach (var material in smr.Materials)
        {
            if (material == null) continue;
            ProcessMaterial(material, prefabData, materials, assetLoader, enableDebugLogging: false);
        }
        return materials;
    }

    public List<MaterialData> ExtractMaterials(UnityReader.MeshRenderer meshRenderer, PrefabData prefabData, AssetLoader assetLoader)
    {
        var materials = new List<MaterialData>();
        foreach (var material in meshRenderer.Materials)
        {
            if (material == null) continue;
            ProcessMaterial(material, prefabData, materials, assetLoader, enableDebugLogging: true);
        }
        return materials;
    }

    private void ProcessMaterial(UnityReader.Material material, PrefabData prefabData,
        List<MaterialData> materials, AssetLoader assetLoader, bool enableDebugLogging)
    {
        if (_materialCache.TryGetValue(material.PathId, out var cachedMaterial))
        {
            materials.Add(cachedMaterial);
            lock (prefabData.Materials)
            {
                if (!prefabData.Materials.Any(m => m.Name == cachedMaterial.Name))
                    prefabData.Materials.Add(cachedMaterial);
            }
            if (!string.IsNullOrEmpty(cachedMaterial.MainTextureName))
            {
                var cachedTexture = _textureCache.GetAllCached().FirstOrDefault(t => t.Name == cachedMaterial.MainTextureName);
                if (cachedTexture != null)
                    AddTextureUnique(prefabData, cachedTexture);
            }
            return;
        }

        var materialData = ExtractMaterial(material, prefabData, assetLoader);
        if (materialData != null)
        {
            _materialCache.TryAdd(material.PathId, materialData);
            materials.Add(materialData);
            lock (prefabData.Materials)
            {
                if (!prefabData.Materials.Any(m => m.Name == materialData.Name))
                    prefabData.Materials.Add(materialData);
            }
        }
    }

    public MaterialData? ExtractMaterial(UnityReader.Material material, PrefabData prefabData, AssetLoader assetLoader)
    {
        var materialData = new MaterialData
        {
            Name = string.IsNullOrEmpty(material.Name) ? "UnknownMaterial" : material.Name
        };

        try
        {
            ExtractMainTexture(material, materialData, prefabData);
            ApplyTexturePathFallback(material, materialData, prefabData, assetLoader);
            ExtractEmissiveTexture(material, materialData, prefabData);
            ExtractColors(material, materialData);
            ExtractFloats(material, materialData);
            CheckEmissionKeyword(material, materialData);
            DeriveAlphaMode(material, materialData);
            ExtractDoubleSided(material, materialData);
            HandleVFXMaterial(materialData);
            ApplyContentWorkaround(materialData, prefabData, assetLoader);
            return materialData;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract material: {MaterialName}", materialData.Name);
            return materialData;
        }
    }

    private void ExtractMainTexture(UnityReader.Material material, MaterialData materialData, PrefabData prefabData)
        => TryResolveTextureProperty(material, prefabData, ShaderProperties.MainTexCandidates,
            (md, name) => md.MainTextureName = name, materialData);

    private void ExtractEmissiveTexture(UnityReader.Material material, MaterialData materialData, PrefabData prefabData)
        => TryResolveTextureProperty(material, prefabData, ShaderProperties.EmissiveTexCandidates,
            (md, name) => md.EmissiveTextureName = name, materialData);

    private void TryResolveTextureProperty(
        UnityReader.Material material,
        PrefabData prefabData,
        IReadOnlyList<string> candidatePropertyNames,
        Action<MaterialData, string> applyTextureName,
        MaterialData materialData)
    {
        foreach (var texName in candidatePropertyNames)
        {
            if (!material.TextureProperties.TryGetValue(texName, out var texRef) || texRef.Texture == null)
                continue;

            var textureData = _textureCache.GetOrLoad(texRef.Texture);
            if (textureData == null) return;

            AddTextureUnique(prefabData, textureData);
            applyTextureName(materialData, textureData.Name);
            return;
        }
    }

    private static void AddTextureUnique(PrefabData prefabData, TextureData textureData)
    {
        lock (prefabData.Textures)
        {
            if (!prefabData.Textures.Any(t => t.Name == textureData.Name))
                prefabData.Textures.Add(textureData);
        }
    }

    private void ApplyTexturePathFallback(UnityReader.Material material, MaterialData materialData, PrefabData prefabData, AssetLoader assetLoader)
    {
        if (!string.IsNullOrEmpty(materialData.MainTextureName)) return;

        var prefabResourcePath = GetPrefabResourcePath(prefabData, assetLoader);
        if (string.IsNullOrEmpty(prefabResourcePath)) return;

        if (prefabResourcePath.Contains("objects/artifact/book_artifact", StringComparison.OrdinalIgnoreCase))
        {
            var textureAsset = assetLoader.FindTextureByResourcePath("objects/artifact/models/book_artifact/book_texture");
            if (textureAsset == null) return;

            var textureData = _textureCache.GetOrLoad(textureAsset);
            if (textureData == null) return;

            AddTextureUnique(prefabData, textureData);
            materialData.MainTextureName = textureData.Name;
        }
    }

    private void ApplyContentWorkaround(MaterialData materialData, PrefabData prefabData, AssetLoader assetLoader)
    {
        var ovr = ContentWorkarounds.Lookup(prefabData.Name, materialData.Name);
        if (ovr == null) return;

        var matches = assetLoader.SearchTextures(ovr.TextureName);
        var texture = matches.FirstOrDefault(t => string.Equals(t.Name, ovr.TextureName, StringComparison.OrdinalIgnoreCase)).Texture;
        if (texture == null)
        {
            _logger.LogWarning("Content workaround for {Prefab}/{Material}: texture '{TextureName}' not found", prefabData.Name, materialData.Name, ovr.TextureName);
            return;
        }

        var textureData = _textureCache.GetOrLoad(texture);
        if (textureData == null) return;

        AddTextureUnique(prefabData, textureData);
        materialData.MainTextureName = textureData.Name;
        materialData.BaseColor = new Models.Vector4(ovr.BaseColorR, ovr.BaseColorG, ovr.BaseColorB, ovr.BaseColorA);
        materialData.AlphaMode = 3;
    }

    private string? GetPrefabResourcePath(PrefabData prefabData, AssetLoader assetLoader)
    {
        var resourcePaths = assetLoader.ListResourcePaths(prefabData.Name);
        return resourcePaths.Count > 0 ? resourcePaths[0] : null;
    }

    private void ExtractColors(UnityReader.Material material, MaterialData materialData)
    {
        foreach (var kv in material.Colors)
        {
            var name = kv.Key;
            var color = kv.Value;
            if (name == ShaderProperties.Color || name == ShaderProperties.BaseColor)
                materialData.BaseColor = new Models.Vector4(color.X, color.Y, color.Z, color.W);
            else if (name == ShaderProperties.EmissionColor)
                materialData.EmissiveColor = new Models.Vector4(color.X, color.Y, color.Z, color.W);
        }
    }

    private void ExtractFloats(UnityReader.Material material, MaterialData materialData)
    {
        float? alphaClipEnabled = null;
        float? alphaCutoff = null;
        bool foundEmissionEnabled = false;

        foreach (var kv in material.Floats)
        {
            if (kv.Key.Contains("mission", StringComparison.OrdinalIgnoreCase))
            {
                foundEmissionEnabled |= kv.Key == ShaderProperties.EmissionEnabled;
            }
            ProcessFloatProperty(kv.Key, kv.Value, materialData, ref alphaClipEnabled, ref alphaCutoff);
        }

        if (!foundEmissionEnabled && !string.IsNullOrEmpty(materialData.EmissiveTextureName))
        {
            materialData.EmissionEnabled = materialData.EmissionStrength > 0.01f;
        }

        if (alphaClipEnabled.HasValue && alphaClipEnabled.Value > 0.5f)
        {
            materialData.AlphaMode = 1;
            if (alphaCutoff.HasValue) materialData.AlphaCutoff = alphaCutoff.Value;
        }
    }

    private void ProcessFloatProperty(string name, float value, MaterialData materialData, ref float? alphaClipEnabled, ref float? alphaCutoff)
    {
        switch (name)
        {
            case ShaderProperties.Metallic:        materialData.Metallic = value; break;
            case ShaderProperties.Glossiness:      materialData.Roughness = 1.0f - value; break;
            case ShaderProperties.Smoothness:      if (materialData.Roughness >= 1.0f) materialData.Roughness = 1.0f - value; break;
            case ShaderProperties.Mode:            materialData.AlphaMode = (int)value; break;
            case ShaderProperties.Cutoff:          materialData.AlphaCutoff = value; break;
            case ShaderProperties.AlphaClipEnabled: alphaClipEnabled = value; break;
            case ShaderProperties.AlphaCutoff:     alphaCutoff = value; break;
            case ShaderProperties.EmissionEnabled: materialData.EmissionEnabled = value > 0.5f; break;
            case ShaderProperties.EmissionMinPower: materialData.EmissionStrength = value; break;
        }
    }

    private void DeriveAlphaMode(UnityReader.Material material, MaterialData materialData)
    {
        if (materialData.AlphaMode != 0) return;
        int customRenderQueue = material.CustomRenderQueue;
        if (customRenderQueue >= 0)
        {
            if (customRenderQueue >= 2450 && customRenderQueue < 3000) materialData.AlphaMode = 1;
            else if (customRenderQueue >= 3000) materialData.AlphaMode = 3;
        }
    }

    private void HandleVFXMaterial(MaterialData materialData)
    {
        if (materialData.AlphaMode == 0 && materialData.Name.Contains("vfx", StringComparison.OrdinalIgnoreCase))
        {
            materialData.AlphaMode = 3;
            if (string.IsNullOrEmpty(materialData.MainTextureName)) materialData.BaseColor.W = 0.0f;
        }
        if (materialData.AlphaMode == 0 && materialData.Name.Contains("curv", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(materialData.MainTextureName))
        {
            materialData.AlphaMode = 3;
        }
    }

    private void ExtractDoubleSided(UnityReader.Material material, MaterialData materialData)
    {
        materialData.IsDoubleSided = false;

        if (material.Floats.TryGetValue(ShaderProperties.Cull, out var cullValue))
        {
            // Unity CullMode.Off = 0 (double-sided)
            materialData.IsDoubleSided = (int)cullValue == 0;
            return;
        }

        var shader = material.Shader;
        if (shader != null)
        {
            var shaderName = shader.Name ?? string.Empty;
            if (shaderName.Contains("TwoSide", StringComparison.OrdinalIgnoreCase) ||
                shaderName.Contains("DoubleSide", StringComparison.OrdinalIgnoreCase) ||
                shaderName.Contains("2Side", StringComparison.OrdinalIgnoreCase) ||
                shaderName.Contains("Cull Off", StringComparison.OrdinalIgnoreCase) ||
                shaderName.Contains("CullOff", StringComparison.OrdinalIgnoreCase))
            {
                materialData.IsDoubleSided = true;
            }
        }
    }

    private void CheckEmissionKeyword(UnityReader.Material material, MaterialData materialData)
    {
        if (string.IsNullOrEmpty(materialData.EmissiveTextureName)) return;

        if (CheckShaderKeyword(material, ShaderKeywords.HexEmissionMapEnabled))
        {
            materialData.EmissionEnabled = true;
        }
        else
        {
            bool isHexShader = CheckShaderKeyword(material, ShaderKeywords.HexAlphaClipEnabled) ||
                               CheckShaderKeyword(material, ShaderKeywords.HexDitherFadeEnabled);
            if (isHexShader) materialData.EmissionEnabled = false;
        }
    }

    private static bool CheckShaderKeyword(UnityReader.Material material, string keyword)
    {
        foreach (var kw in material.ValidKeywords)
            if (kw.Equals(keyword, StringComparison.OrdinalIgnoreCase)) return true;

        var legacy = material.LegacyShaderKeywords;
        if (!string.IsNullOrEmpty(legacy))
        {
            var parts = legacy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var kw in parts)
                if (kw.Equals(keyword, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    public TextureData? GetTextureByName(string name) => _textureCache.GetAllCached().FirstOrDefault(t => t.Name == name);

    public void ClearCaches()
    {
        _textureCache.Clear();
        _materialCache.Clear();
    }

    public int TextureCacheCount => _textureCache.Count;
}
