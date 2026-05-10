using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AssetStudio
{
    internal sealed class GameObject : EditorExtension
    {
        public List<PPtr<Component>> m_Components;
        public string m_Name;

        // MODIFIED FOR UnityAssetReader: m_Tag and m_IsActive are read past m_Name
        // so OEE's hierarchy filter (skip-inactive children) has the same semantics
        // as AssetRipper's IGameObject.GetIsActive(). Upstream AssetStudioMod stops
        // at m_Name. Layout below mirrors Unity 5.5+ runtime: 2-byte tag + 1-byte
        // bool + 4-byte alignment - the only branch produced by the EA build's
        // Tuanjie 2022.3+ runtime data.
        public ushort m_Tag;
        public bool m_IsActive = true;

        public Transform m_Transform;
        public MeshRenderer m_MeshRenderer;
        public MeshFilter m_MeshFilter;
        public SkinnedMeshRenderer m_SkinnedMeshRenderer;
        public Animator m_Animator;
        public Animation m_Animation;
        [JsonIgnore]
        public CubismModel CubismModel;

        public GameObject(ObjectReader reader) : base(reader)
        {
            var m_ComponentSize = reader.ReadInt32();
            m_Components = new List<PPtr<Component>>();
            for (var i = 0; i < m_ComponentSize; i++)
            {
                if (version < (5, 5)) //5.5 down
                {
                    var first = reader.ReadInt32();
                }
                m_Components.Add(new PPtr<Component>(reader));
            }

            var m_Layer = reader.ReadInt32();
            if (version.IsTuanjie && (version > (2022, 3, 2) || (version == (2022, 3, 2) && version.Build >= 11))) //2022.3.2t11(1.1.3) and up
            {
                var m_HasEditorInfo = reader.ReadBoolean();
                reader.AlignStream();
            }
            m_Name = reader.ReadAlignedString();

            // MODIFIED FOR UnityAssetReader: continue past m_Name to capture
            // m_IsActive. <5.5 had a string m_TagString here instead of a UInt16
            // tag, so we skip in that case (the EA build does not reach that
            // branch but we keep the parser safe against unexpected data).
            if (version >= (5, 5))
            {
                m_Tag = reader.ReadUInt16();
                m_IsActive = reader.ReadBoolean();
                reader.AlignStream();
            }
        }
    }
}
