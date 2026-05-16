namespace PixelAssetGenerator
{
    public sealed class NodeLibraryCategory
    {
        public NodeLibraryCategory(string name, int count = 0, string? key = null)
        {
            Name = name;
            Count = count;
            Key = key ?? name;
        }

        /// <summary>Localized display name shown in the UI.</summary>
        public string Name { get; }
        /// <summary>Node count in this category.</summary>
        public int Count { get; set; }
        /// <summary>Canonical category key (directory name), used for filtering.</summary>
        public string Key { get; }
    }
}
