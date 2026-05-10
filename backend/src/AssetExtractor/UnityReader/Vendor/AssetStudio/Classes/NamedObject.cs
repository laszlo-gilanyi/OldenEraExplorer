namespace AssetStudio
{
    internal class NamedObject : EditorExtension
    {
        public string m_Name;

        protected NamedObject() { }

        protected NamedObject(ObjectReader reader) : base(reader)
        {
            m_Name = reader.ReadAlignedString();
        }
    }
}
