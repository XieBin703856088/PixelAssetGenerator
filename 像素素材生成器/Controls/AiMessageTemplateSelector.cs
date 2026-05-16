using System.Windows;
using System.Windows.Controls;

namespace PixelAssetGenerator.Controls;

/// <summary>
/// Picks the correct DataTemplate for each chat message so that only the
/// controls needed for that role are instantiated — prevents MarkdownViewer
/// from being created for every user bubble during streaming.
///
/// Supports: User, Assistant, System, Error, and ToolCallGroup messages.
/// </summary>
public sealed class AiMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? UserTemplate { get; set; }
    public DataTemplate? AssistantTemplate { get; set; }
    public DataTemplate? SystemTemplate { get; set; }
    public DataTemplate? ErrorTemplate { get; set; }
    public DataTemplate? ToolCallGroupTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is PixelAssetGenerator.ToolCallGroupViewModel)
            return ToolCallGroupTemplate;

        if (item is PixelAssetGenerator.ChatMessageViewModel msg)
        {
            return msg.Role switch
            {
                "user" => UserTemplate,
                "system" => SystemTemplate ?? AssistantTemplate,
                "error" => ErrorTemplate ?? AssistantTemplate,
                _     => AssistantTemplate,
            };
        }
        return base.SelectTemplate(item, container);
    }
}
