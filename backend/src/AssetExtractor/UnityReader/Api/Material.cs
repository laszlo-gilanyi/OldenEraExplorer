using System.Collections.Generic;
using System.Numerics;
using UnityReader.Internal;

namespace UnityReader;

public sealed class Material
{
    private readonly UnityScene _scene;
    private readonly AssetStudio.Material _vendor;

    private Dictionary<string, float>? _floats;
    private Dictionary<string, Vector4>? _colors;
    private Dictionary<string, MaterialTextureRef>? _textures;
    private Shader? _shaderCache;
    private bool _shaderResolved;

    internal Material(UnityScene scene, AssetStudio.Material vendor)
    {
        _scene = scene;
        _vendor = vendor;
    }

    public string Name => _vendor.m_Name ?? string.Empty;
    public long PathId => _vendor.m_PathID;

    public string SourceFile =>
        System.IO.Path.GetFileName(_vendor.assetsFile?.fileName ?? string.Empty);

    // Empty on Unity 5.0 - 2021.2.17, which use the space-separated LegacyShaderKeywords instead.
    public IReadOnlyList<string> ValidKeywords => _vendor.m_ValidKeywords;

    public IReadOnlyList<string> InvalidKeywords => _vendor.m_InvalidKeywords;

    public string LegacyShaderKeywords => _vendor.m_LegacyShaderKeywords ?? string.Empty;

    // -1 means inherit from shader; otherwise Unity convention (Geometry=2000,
    // AlphaTest=2450, Transparent=3000).
    public int CustomRenderQueue => _vendor.m_CustomRenderQueue;

    public Shader? Shader
    {
        get
        {
            if (!_shaderResolved)
            {
                _shaderCache = _vendor.m_Shader != null && _vendor.m_Shader.TryGet(out var s)
                    ? _scene.GetOrCreateShader(s)
                    : null;
                _shaderResolved = true;
            }
            return _shaderCache;
        }
    }

    public IReadOnlyDictionary<string, float> Floats
    {
        get
        {
            if (_floats == null)
            {
                _floats = new Dictionary<string, float>();
                foreach (var kv in _vendor.m_SavedProperties?.m_Floats
                                   ?? new List<KeyValuePair<string, float>>())
                    _floats[kv.Key] = kv.Value;
            }
            return _floats;
        }
    }

    public IReadOnlyDictionary<string, Vector4> Colors
    {
        get
        {
            if (_colors == null)
            {
                _colors = new Dictionary<string, Vector4>();
                foreach (var kv in _vendor.m_SavedProperties?.m_Colors
                                   ?? new List<KeyValuePair<string, AssetStudio.Color>>())
                    _colors[kv.Key] = new Vector4(kv.Value.R, kv.Value.G, kv.Value.B, kv.Value.A);
            }
            return _colors;
        }
    }

    public IReadOnlyDictionary<string, MaterialTextureRef> TextureProperties
    {
        get
        {
            if (_textures == null)
            {
                _textures = new Dictionary<string, MaterialTextureRef>();
                foreach (var kv in _vendor.m_SavedProperties?.m_TexEnvs
                                   ?? new List<KeyValuePair<string, AssetStudio.UnityTexEnv>>())
                {
                    UnityTexture? tex = null;
                    if (kv.Value.m_Texture != null && kv.Value.m_Texture.TryGet(out var vendorTex)
                        && vendorTex is AssetStudio.Texture2D t2d)
                        tex = _scene.GetOrCreateTexture(t2d);

                    _textures[kv.Key] = new MaterialTextureRef(
                        tex,
                        kv.Value.m_Scale.ToNumerics(),
                        kv.Value.m_Offset.ToNumerics());
                }
            }
            return _textures;
        }
    }
}

// Texture is null when the slot is unbound or when the reference is unresolvable
// (e.g. a Cubemap or other non-Texture2D target).
public readonly record struct MaterialTextureRef(
    UnityTexture? Texture,
    Vector2 Scale,
    Vector2 Offset);
