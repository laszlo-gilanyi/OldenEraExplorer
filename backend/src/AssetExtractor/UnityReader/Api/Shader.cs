namespace UnityReader;

// Metadata only; shader bytecode is out of scope. The reference is used only to key
// material-property layout.
public sealed class Shader
{
    private readonly AssetStudio.Shader _vendor;

    internal Shader(AssetStudio.Shader vendor) => _vendor = vendor;

    public string Name => _vendor.m_Name ?? string.Empty;
    public long PathId => _vendor.m_PathID;
}
