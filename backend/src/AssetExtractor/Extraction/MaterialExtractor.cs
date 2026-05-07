#nullable enable
using System.Collections.Concurrent;
using AssetRipper.Assets;
using AssetRipper.Assets.Generics;
using AssetRipper.Export.Modules.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_21;   // IMaterial
using AssetRipper.SourceGenerated.Classes.ClassID_23;   // IMeshRenderer
using AssetRipper.SourceGenerated.Classes.ClassID_28;   // ITexture2D
using AssetRipper.SourceGenerated.Classes.ClassID_48;   // IShader
using AssetRipper.SourceGenerated.Classes.ClassID_137;  // ISkinnedMeshRenderer
using AssetRipper.SourceGenerated.Enums;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.SerializedShader;
using AssetRipper.SourceGenerated.Subclasses.FastPropertyName;
using AssetRipper.SourceGenerated.Subclasses.UnityPropertySheet;
using AssetRipper.SourceGenerated.Subclasses.UnityTexEnv;
using AssetExtractor.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetExtractor.Extraction;

// Thread-safety: ThreadSafeTextureCache serializes stream reads (AssetRipper streams not thread-safe).
// PrefabData list modifications protected by locks.
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

    private void ProcessMaterialPPtr<TRenderer>(
        AssetRipper.Assets.Metadata.IPPtr<IMaterial> materialPPtr,
        TRenderer renderer,
        PrefabData prefabData,
        List<MaterialData> materials,
        AssetLoader assetLoader,
        bool enableDebugLogging = false) where TRenderer : IUnityObjectBase
    {
        var materialAsset = materialPPtr.TryGetAsset(renderer.Collection);
        if (materialAsset is not IMaterial material)
            return;

        // Check cache first
        if (_materialCache.TryGetValue(material.PathID, out var cachedMaterial))
        {
            if (enableDebugLogging)
            {
                _logger.LogDebug(
                    "Using cached material: {MaterialName} (MainTexture: {MainTextureName})",
                    cachedMaterial.Name,
                    cachedMaterial.MainTextureName ?? "none");
            }

            materials.Add(cachedMaterial);

            // Thread-safe: add cached material to prefab's list if not already present
            lock (prefabData.Materials)
            {
                if (!prefabData.Materials.Any(m => m.Name == cachedMaterial.Name))
                {
                    prefabData.Materials.Add(cachedMaterial);
                }
            }

            // Thread-safe: add associated texture if present
            if (!string.IsNullOrEmpty(cachedMaterial.MainTextureName))
            {
                var cachedTexture = _textureCache.GetAllCached().FirstOrDefault(t => t.Name == cachedMaterial.MainTextureName);
                if (enableDebugLogging)
                {
                    _logger.LogDebug(
                        "Looking for texture '{TextureName}' in cache of {CacheCount} textures, found: {Found}",
                        cachedMaterial.MainTextureName,
                        _textureCache.Count,
                        cachedTexture != null);
                }
                if (cachedTexture != null)
                {
                    lock (prefabData.Textures)
                    {
                        if (!prefabData.Textures.Any(t => t.Name == cachedTexture.Name))
                        {
                            prefabData.Textures.Add(cachedTexture);
                        }
                    }
                }
            }
            return;
        }

        var materialData = ExtractMaterial(material, prefabData, assetLoader);
        if (materialData != null)
        {
            _materialCache.TryAdd(material.PathID, materialData);
            materials.Add(materialData);

            // Thread-safe: add to prefab's material list if not already present
            lock (prefabData.Materials)
            {
                if (!prefabData.Materials.Any(m => m.Name == materialData.Name))
                {
                    prefabData.Materials.Add(materialData);
                }
            }
        }
    }

    public List<MaterialData> ExtractMaterials(ISkinnedMeshRenderer smr, PrefabData prefabData, AssetLoader assetLoader)
    {
        var materials = new List<MaterialData>();
        foreach (var materialPPtr in smr.Materials_C25)
        {
            ProcessMaterialPPtr(materialPPtr, smr, prefabData, materials, assetLoader, enableDebugLogging: false);
        }
        return materials;
    }

    public List<MaterialData> ExtractMaterials(IMeshRenderer meshRenderer, PrefabData prefabData, AssetLoader assetLoader)
    {
        var materials = new List<MaterialData>();
        foreach (var materialPPtr in meshRenderer.Materials_C25)
        {
            ProcessMaterialPPtr(materialPPtr, meshRenderer, prefabData, materials, assetLoader, enableDebugLogging: true);
        }
        return materials;
    }

    public MaterialData? ExtractMaterial(IMaterial material, PrefabData prefabData, AssetLoader assetLoader)
    {
        var materialData = new MaterialData
        {
            Name = material.Name ?? "UnknownMaterial"
        };

        _logger.LogInformation("Extracting material: {MaterialName}", materialData.Name);

        try
        {
            var savedProps = material.SavedProperties_C21;

            // Extract main texture
            ExtractMainTexture(material, savedProps, materialData, prefabData);

            // Apply path-based texture fallback for known cases (e.g., book_artifact)
            ApplyTexturePathFallback(material, materialData, prefabData, assetLoader);

            // Extract emissive texture
            ExtractEmissiveTexture(material, materialData, prefabData);

            // Extract colors (BaseColor, EmissiveColor)
            ExtractColors(savedProps, materialData);

            // Extract float properties (Metallic, Roughness/Smoothness, AlphaMode, etc.)
            ExtractFloats(savedProps, materialData);

            // Check shader keywords for emission (Hex/Lit shader uses keywords, not float properties)
            // This OVERRIDES the float-based detection in ExtractFloats
            CheckEmissionKeyword(material, materialData);

            // Derive alpha mode from custom shaders if not set
            DeriveAlphaMode(material, materialData);

            // Extract double-sided setting from shader cull mode
            ExtractDoubleSided(material, savedProps, materialData);

            // Handle VFX materials
            HandleVFXMaterial(materialData);

            ApplyContentWorkaround(materialData, prefabData, assetLoader);

            _logger.LogDebug(
                "Material properties: BaseColor=({R:F2}, {G:F2}, {B:F2}, {A:F2})",
                materialData.BaseColor.X, materialData.BaseColor.Y, materialData.BaseColor.Z, materialData.BaseColor.W);
            _logger.LogDebug(
                "AlphaMode: {AlphaMode}, AlphaCutoff: {AlphaCutoff:F2}",
                materialData.AlphaMode, materialData.AlphaCutoff);
            _logger.LogDebug("DoubleSided: {DoubleSided}", materialData.IsDoubleSided);
            _logger.LogDebug("MainTexture: {MainTextureName}", materialData.MainTextureName ?? "none");

            return materialData;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract material: {MaterialName}", materialData.Name);
            return materialData;
        }
    }

    // Thread-safe: ThreadSafeTextureCache for loading, locks for PrefabData modification
    private void ExtractMainTexture(IMaterial material, IUnityPropertySheet savedProps, MaterialData materialData, PrefabData prefabData)
    {
        // Try _MainTex first, then _BaseMap (URP)
        string[] mainTexNames = { "_MainTex", "_BaseMap" };

        foreach (var texName in mainTexNames)
        {
            if (material.TryGetTextureProperty(texName, out var texEnv))
            {
                var textureAsset = texEnv.Texture.TryGetAsset(material.Collection);
                if (textureAsset is ITexture2D texture)
                {
                    // Thread-safe texture loading via cache
                    // GetOrLoad handles locking internally - only one thread reads from streams at a time
                    var textureData = _textureCache.GetOrLoad(texture);

                    if (textureData != null)
                    {
                        // Thread-safe: add to prefab's texture list if not already present
                        lock (prefabData.Textures)
                        {
                            if (!prefabData.Textures.Any(t => t.Name == textureData.Name))
                            {
                                prefabData.Textures.Add(textureData);
                            }
                        }

                        // Set MainTextureName after successful extraction
                        materialData.MainTextureName = textureData.Name;
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Failed to extract main texture: {TextureName} - material will have no texture",
                            texture.Name);
                    }
                    return;
                }
            }
        }
    }

    // Thread-safe: ThreadSafeTextureCache for loading, locks for PrefabData modification
    private void ExtractEmissiveTexture(IMaterial material, MaterialData materialData, PrefabData prefabData)
    {
        // Try various emissive texture property names
        string[] emissiveTexNames = { "_EmissionTex", "_EmissionMap", "_EmissiveMap" };

        foreach (var texName in emissiveTexNames)
        {
            if (material.TryGetTextureProperty(texName, out var texEnv))
            {
                var textureAsset = texEnv.Texture.TryGetAsset(material.Collection);
                if (textureAsset is ITexture2D texture)
                {
                    // Thread-safe texture loading via cache
                    // GetOrLoad handles locking internally - only one thread reads from streams at a time
                    var textureData = _textureCache.GetOrLoad(texture);

                    if (textureData != null)
                    {
                        // Thread-safe: add to prefab's texture list if not already present
                        lock (prefabData.Textures)
                        {
                            if (!prefabData.Textures.Any(t => t.Name == textureData.Name))
                            {
                                prefabData.Textures.Add(textureData);
                            }
                        }

                        // Set EmissiveTextureName after successful extraction
                        materialData.EmissiveTextureName = textureData.Name;
                        _logger.LogInformation(
                            "Found emissive texture: {TextureName} (via {PropertyName})",
                            textureData.Name,
                            texName);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Failed to extract emissive texture: {TextureName}",
                            texture.Name);
                    }
                    return;
                }
            }
        }
    }

    // Workaround: book_artifact materials have null texture refs - manually resolve to book_texture.png
    private void ApplyTexturePathFallback(
        IMaterial material,
        MaterialData materialData,
        PrefabData prefabData,
        AssetLoader assetLoader)
    {
        // Only apply fallback if material has no texture
        if (!string.IsNullOrEmpty(materialData.MainTextureName))
            return;

        // Get the prefab's resource path from the resource name cache
        // This tells us which object this material belongs to
        var prefabResourcePath = GetPrefabResourcePath(prefabData, assetLoader);
        if (string.IsNullOrEmpty(prefabResourcePath))
            return;

        // SPECIFIC FIX: book_artifact objects → book_texture.png
        // Pattern: objects/artifact/book_artifact* → objects/artifact/models/book_artifact/book_texture
        if (prefabResourcePath.Contains("objects/artifact/book_artifact", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Applying book_texture fallback for material '{MaterialName}' in prefab '{PrefabName}' (path: {PrefabPath})",
                materialData.Name,
                prefabData.Name,
                prefabResourcePath);

            // Try to find book_texture in the models directory
            var textureAsset = assetLoader.FindTextureByResourcePath("objects/artifact/models/book_artifact/book_texture");
            if (textureAsset != null)
            {
                // Load the texture through the cache
                var textureData = _textureCache.GetOrLoad(textureAsset);
                if (textureData != null)
                {
                    // Thread-safe: add to prefab's texture list
                    lock (prefabData.Textures)
                    {
                        if (!prefabData.Textures.Any(t => t.Name == textureData.Name))
                        {
                            prefabData.Textures.Add(textureData);
                        }
                    }

                    materialData.MainTextureName = textureData.Name;

                    _logger.LogInformation(
                        "Successfully applied book_texture fallback: {TextureName}",
                        textureData.Name);
                }
                else
                {
                    _logger.LogWarning(
                        "Found book_texture asset (PathID: {PathID}) but failed to load texture data",
                        textureAsset.PathID);
                }
            }
            else
            {
                _logger.LogWarning(
                    "Could not find book_texture in resource path: objects/artifact/models/book_artifact/book_texture");
            }
        }

        // Add more specific fallbacks here as needed:
        // else if (prefabResourcePath.Contains("objects/artifact/other_object", ...))
        // {
        //     // Apply other_object texture fallback
        // }
    }

    private void ApplyContentWorkaround(MaterialData materialData, PrefabData prefabData, AssetLoader assetLoader)
    {
        var ovr = ContentWorkarounds.Lookup(prefabData.Name, materialData.Name);
        if (ovr == null)
            return;

        var matches = assetLoader.SearchTextures(ovr.TextureName);
        var texture = matches.FirstOrDefault(t => string.Equals(t.Name, ovr.TextureName, StringComparison.OrdinalIgnoreCase)).Texture;
        if (texture == null)
        {
            _logger.LogWarning(
                "Content workaround for {Prefab}/{Material}: texture '{TextureName}' not found",
                prefabData.Name, materialData.Name, ovr.TextureName);
            return;
        }

        var textureData = _textureCache.GetOrLoad(texture);
        if (textureData == null)
        {
            _logger.LogWarning(
                "Content workaround for {Prefab}/{Material}: failed to load texture '{TextureName}'",
                prefabData.Name, materialData.Name, ovr.TextureName);
            return;
        }

        lock (prefabData.Textures)
        {
            if (!prefabData.Textures.Any(t => t.Name == textureData.Name))
            {
                prefabData.Textures.Add(textureData);
            }
        }

        materialData.MainTextureName = textureData.Name;
        materialData.BaseColor = new Vector4(ovr.BaseColorR, ovr.BaseColorG, ovr.BaseColorB, ovr.BaseColorA);
        materialData.AlphaMode = 3;

        _logger.LogInformation(
            "Applied content workaround for {Prefab}/{Material}: texture={TextureName}, color=({R:F2},{G:F2},{B:F2},{A:F2})",
            prefabData.Name, materialData.Name, textureData.Name,
            ovr.BaseColorR, ovr.BaseColorG, ovr.BaseColorB, ovr.BaseColorA);
    }

    private string? GetPrefabResourcePath(PrefabData prefabData, AssetLoader assetLoader)
    {
        // Find the prefab GameObject in the resource name cache to get its path
        var resourcePaths = assetLoader.ListResourcePaths(prefabData.Name);
        if (resourcePaths.Count > 0)
        {
            // Return the first match (usually there's only one)
            return resourcePaths[0];
        }

        return null;
    }

    private void ExtractColors(IUnityPropertySheet savedProps, MaterialData materialData)
    {
        // Try different property sheet formats for colors
        if (savedProps.Has_Colors_AssetDictionary_FastPropertyName_ColorRGBAf())
        {
            foreach (var pair in savedProps.Colors_AssetDictionary_FastPropertyName_ColorRGBAf)
            {
                var name = pair.Key.Name.String;
                var color = pair.Value;

                if (name == "_Color" || name == "_BaseColor")
                {
                    materialData.BaseColor = new Vector4(color.R, color.G, color.B, color.A);
                }
                else if (name == "_EmissionColor")
                {
                    materialData.EmissiveColor = new Vector4(color.R, color.G, color.B, color.A);
                    _logger.LogDebug(
                        "EmissionColor: ({R:F2}, {G:F2}, {B:F2}, {A:F2})",
                        color.R, color.G, color.B, color.A);
                }
            }
        }
        else if (savedProps.Has_Colors_AssetDictionary_Utf8String_ColorRGBAf())
        {
            foreach (var pair in savedProps.Colors_AssetDictionary_Utf8String_ColorRGBAf)
            {
                var name = pair.Key.String;
                var color = pair.Value;

                if (name == "_Color" || name == "_BaseColor")
                {
                    materialData.BaseColor = new Vector4(color.R, color.G, color.B, color.A);
                }
                else if (name == "_EmissionColor")
                {
                    materialData.EmissiveColor = new Vector4(color.R, color.G, color.B, color.A);
                    _logger.LogDebug(
                        "EmissionColor: ({R:F2}, {G:F2}, {B:F2}, {A:F2})",
                        color.R, color.G, color.B, color.A);
                }
            }
        }
    }

    private void ExtractFloats(IUnityPropertySheet savedProps, MaterialData materialData)
    {
        float? alphaClipEnabled = null;
        float? alphaCutoff = null;
        bool foundEmissionEnabled = false;

        // Try different property sheet formats for floats
        if (savedProps.Has_Floats_AssetDictionary_FastPropertyName_Single())
        {
            foreach (var pair in savedProps.Floats_AssetDictionary_FastPropertyName_Single)
            {
                var propName = pair.Key.Name.String;
                // Log emission-related properties for debugging
                if (propName.Contains("mission", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug(
                        "Float property: {PropertyName} = {Value}",
                        propName,
                        pair.Value);
                    foundEmissionEnabled = propName == "_EmissionEnabled";
                }
                ProcessFloatProperty(propName, pair.Value, materialData, ref alphaClipEnabled, ref alphaCutoff);
            }
        }
        else if (savedProps.Has_Floats_AssetDictionary_Utf8String_Single())
        {
            foreach (var pair in savedProps.Floats_AssetDictionary_Utf8String_Single)
            {
                var propName = pair.Key.String;
                // Log emission-related properties for debugging
                if (propName.Contains("mission", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug(
                        "Float property: {PropertyName} = {Value}",
                        propName,
                        pair.Value);
                    foundEmissionEnabled = propName == "_EmissionEnabled";
                }
                ProcessFloatProperty(propName, pair.Value, materialData, ref alphaClipEnabled, ref alphaCutoff);
            }
        }

        // If no _EmissionEnabled property found, check for alternative emission indicators
        if (!foundEmissionEnabled && !string.IsNullOrEmpty(materialData.EmissiveTextureName))
        {
            // Hex/Lit shader uses _EmissionMinPower instead of _EmissionEnabled
            // If EmissionStrength was set (from _EmissionMinPower), use that to determine if emission is enabled
            if (materialData.EmissionStrength > 0.01f)
            {
                _logger.LogDebug(
                    "No _EmissionEnabled, but _EmissionMinPower={EmissionStrength:F2} > 0. Enabling emission",
                    materialData.EmissionStrength);
                materialData.EmissionEnabled = true;
            }
            else
            {
                _logger.LogDebug("No _EmissionEnabled and no _EmissionMinPower. Defaulting to disabled");
                materialData.EmissionEnabled = false;
            }
        }

        // Apply alpha clip settings from custom shaders
        if (alphaClipEnabled.HasValue && alphaClipEnabled.Value > 0.5f)
        {
            materialData.AlphaMode = 1; // Cutout
            if (alphaCutoff.HasValue)
                materialData.AlphaCutoff = alphaCutoff.Value;
        }
    }

    private void ProcessFloatProperty(string name, float value, MaterialData materialData, ref float? alphaClipEnabled, ref float? alphaCutoff)
    {
        switch (name)
        {
            case "_Metallic":
                materialData.Metallic = value;
                break;
            case "_Glossiness":
                materialData.Roughness = 1.0f - value; // Convert glossiness to roughness
                break;
            case "_Smoothness":
                if (materialData.Roughness >= 1.0f) // Only use if _Glossiness wasn't set
                    materialData.Roughness = 1.0f - value;
                break;
            case "_Mode":
                materialData.AlphaMode = (int)value;
                break;
            case "_Cutoff":
                materialData.AlphaCutoff = value;
                break;
            case "_AlphaClipEnabled":
                alphaClipEnabled = value;
                break;
            case "_AlphaCutoff":
                alphaCutoff = value;
                break;
            case "_EmissionEnabled":
                // Unity Standard shader: 0 = emission disabled, 1 = emission enabled
                materialData.EmissionEnabled = value > 0.5f;
                break;
            case "_EmissionMinPower":
                // Hex/Lit shader: emission intensity (0.0-1.0)
                materialData.EmissionStrength = value;
                break;
        }
    }

    private void DeriveAlphaMode(IMaterial material, MaterialData materialData)
    {
        if (materialData.AlphaMode != 0)
            return;

        // Try to get custom render queue
        int customRenderQueue = -1;
        if (material.Has_CustomRenderQueue_C21())
        {
            customRenderQueue = material.CustomRenderQueue_C21;
        }

        if (customRenderQueue >= 0)
        {
            // Unity queues: Geometry=2000, AlphaTest=2450, Transparent=3000
            if (customRenderQueue >= 2450 && customRenderQueue < 3000)
                materialData.AlphaMode = 1; // Cutout
            else if (customRenderQueue >= 3000)
                materialData.AlphaMode = 3; // Transparent
        }
    }

    // VFX materials need BLEND mode for transparency:
    // - Textured VFX: respects texture alpha
    // - Untextured VFX: fully transparent placeholder
    // - "curv" materials: soft light trails with alpha fade
    private void HandleVFXMaterial(MaterialData materialData)
    {
        if (materialData.AlphaMode == 0 &&
            materialData.Name.Contains("vfx", StringComparison.OrdinalIgnoreCase))
        {
            materialData.AlphaMode = 3;

            if (string.IsNullOrEmpty(materialData.MainTextureName))
            {
                materialData.BaseColor.W = 0.0f;
            }
        }

        if (materialData.AlphaMode == 0 &&
            materialData.Name.Contains("curv", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(materialData.MainTextureName))
        {
            materialData.AlphaMode = 3;
            _logger.LogInformation("Detected curve effect material, using BLEND mode");
        }
    }

    // Unity CullMode: Off=0 (double-sided), Back=2 (default, single-sided)
    // Default false unless shader explicitly uses Cull Off
    private void ExtractDoubleSided(IMaterial material, IUnityPropertySheet savedProps, MaterialData materialData)
    {
        materialData.IsDoubleSided = false;

        // Method 1: Check for _Cull float property in material
        // Some shaders expose Cull mode as a material property
        float? cullValue = null;
        if (savedProps.Has_Floats_AssetDictionary_FastPropertyName_Single())
        {
            foreach (var pair in savedProps.Floats_AssetDictionary_FastPropertyName_Single)
            {
                if (pair.Key.Name.String == "_Cull")
                {
                    cullValue = pair.Value;
                    break;
                }
            }
        }
        else if (savedProps.Has_Floats_AssetDictionary_Utf8String_Single())
        {
            foreach (var pair in savedProps.Floats_AssetDictionary_Utf8String_Single)
            {
                if (pair.Key.String == "_Cull")
                {
                    cullValue = pair.Value;
                    break;
                }
            }
        }

        if (cullValue.HasValue)
        {
            // CullMode.Off = 0 means double-sided
            materialData.IsDoubleSided = (int)cullValue.Value == (int)CullMode.Off;
            if (materialData.IsDoubleSided)
            {
                _logger.LogInformation(
                    "DoubleSided from _Cull property: {CullValue}",
                    cullValue.Value);
            }
            return;
        }

        // Method 2: Try to get cull mode from shader's parsed form
        var shader = material.Shader_C21P;
        if (shader != null)
        {
            try
            {
                // Check shader name for double-sided hints
                var shaderName = shader.Name?.ToString() ?? string.Empty;
                if (shaderName.Contains("TwoSide", StringComparison.OrdinalIgnoreCase) ||
                    shaderName.Contains("DoubleSide", StringComparison.OrdinalIgnoreCase) ||
                    shaderName.Contains("2Side", StringComparison.OrdinalIgnoreCase) ||
                    shaderName.Contains("Cull Off", StringComparison.OrdinalIgnoreCase) ||
                    shaderName.Contains("CullOff", StringComparison.OrdinalIgnoreCase))
                {
                    materialData.IsDoubleSided = true;
                    _logger.LogInformation(
                        "DoubleSided from shader name: {ShaderName}",
                        shaderName);
                    return;
                }

                // NOTE: We intentionally do NOT use shader.ParsedForm CullMode here.
                // Unity shaders often use Cull Off for various passes (shadows, outlines, etc.)
                // but that doesn't mean the material should be double-sided in glTF.
                // Unity handles double-sided differently (depth bias, normal flipping) which
                // glTF viewers don't replicate, causing z-fighting artifacts.
                // Only trust explicit _Cull material property or shader name hints.
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read shader cull mode");
            }
        }
    }

    // Hex/Lit shader uses HEX_EMISSION_MAP_ENABLED keyword (overrides float-based detection)
    private void CheckEmissionKeyword(IMaterial material, MaterialData materialData)
    {
        // Only check if material has an emissive texture
        if (string.IsNullOrEmpty(materialData.EmissiveTextureName))
            return;

        // Check for Hex/Lit shader's emission keyword
        if (CheckShaderKeyword(material, "HEX_EMISSION_MAP_ENABLED"))
        {
            _logger.LogInformation("Emission enabled via shader keyword HEX_EMISSION_MAP_ENABLED");
            materialData.EmissionEnabled = true;
        }
        else
        {
            // No emission keyword found - check if this is a Hex/Lit shader
            // by looking for other Hex-specific keywords
            bool isHexShader = CheckShaderKeyword(material, "HEX_ALPHA_CLIP_ENABLED") ||
                               CheckShaderKeyword(material, "HEX_DITHER_FADE_ENABLED");

            if (isHexShader)
            {
                // This is a Hex/Lit shader but emission keyword is missing
                // → Emission should be DISABLED (override float-based detection)
                _logger.LogInformation("Hex/Lit shader detected without HEX_EMISSION_MAP_ENABLED - disabling emission");
                materialData.EmissionEnabled = false;
            }
            // else: Not a Hex/Lit shader, keep the float-based detection result (for Standard shader)
        }
    }

    private bool CheckShaderKeyword(IMaterial material, string keyword)
    {
        // Unity 2017.3+ uses single string with space-separated keywords
        if (material.Has_ShaderKeywords_C21_Utf8String())
        {
            string keywords = material.ShaderKeywords_C21_Utf8String.String;
            if (!string.IsNullOrEmpty(keywords))
            {
                // Check for exact word match (keywords are space-separated)
                var keywordList = keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var kw in keywordList)
                {
                    if (kw.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Found shader keyword: {Keyword}", keyword);
                        return true;
                    }
                }
            }
        }

        // Older Unity versions use array of keywords
        if (material.Has_ShaderKeywords_C21_AssetList_Utf8String())
        {
            foreach (var kw in material.ShaderKeywords_C21_AssetList_Utf8String)
            {
                if (kw.String.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("Found shader keyword: {Keyword}", keyword);
                    return true;
                }
            }
        }

        return false;
    }

    public TextureData? GetTextureByName(string name)
    {
        return _textureCache.GetAllCached().FirstOrDefault(t => t.Name == name);
    }

    public void ClearCaches()
    {
        _textureCache.Clear();
        _materialCache.Clear();
    }

    public int TextureCacheCount => _textureCache.Count;
}
