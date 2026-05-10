namespace AssetStudio
{
    internal abstract class Component : EditorExtension
    {
        public PPtr<GameObject> m_GameObject;

        public Component() { }

        protected Component(ObjectReader reader) : base(reader)
        {
            m_GameObject = new PPtr<GameObject>(reader);
        }
    }
}
