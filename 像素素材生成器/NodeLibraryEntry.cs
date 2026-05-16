namespace PixelAssetGenerator;

/// <summary>
/// Wraps either a subcategory header or a node library item for display
/// in the node library list with interleaved subcategory headers.
/// </summary>
public sealed class NodeLibraryEntry
{
    private NodeLibraryEntry(bool isHeader, string headerText, NodeLibraryItem? item)
    {
        IsHeader = isHeader;
        HeaderText = headerText;
        Item = item;
    }

    public bool IsHeader { get; }
    public string HeaderText { get; }
    public NodeLibraryItem? Item { get; }

    public static NodeLibraryEntry CreateHeader(string text) => new(true, text, null);
    public static NodeLibraryEntry CreateItem(NodeLibraryItem item) => new(false, "", item);
}
