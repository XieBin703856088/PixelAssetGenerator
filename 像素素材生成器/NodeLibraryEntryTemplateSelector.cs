using System.Windows;
using System.Windows.Controls;

namespace PixelAssetGenerator;

/// <summary>
/// Selects between subcategory header template and node library item template.
/// </summary>
public sealed class NodeLibraryEntryTemplateSelector : DataTemplateSelector
{
    public DataTemplate? HeaderTemplate { get; set; }
    public DataTemplate? ItemTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is NodeLibraryEntry entry)
        {
            return entry.IsHeader ? HeaderTemplate : ItemTemplate;
        }
        return base.SelectTemplate(item, container);
    }
}
