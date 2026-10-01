using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Axiom.App;

internal sealed class DocumentCreationPreviewWindow : Window
{
    public DocumentCreationPreviewWindow(string destination, string text)
    {
        Title = "Review new document";
        Width = 760;
        Height = 580;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var create = new Button
        {
            Content   = "Create document"
          , IsDefault = true
        };
        var cancel = new Button
        {
            Content  = "Cancel"
          , IsCancel = true
        };
        create.Click += (_, _) => Close(true);
        cancel.Click += (_, _) => Close(false);
        var preview = new TextBox
        {
            Text          = text
          , IsReadOnly    = true
          , AcceptsReturn = true
          , TextWrapping  = Avalonia.Media.TextWrapping.Wrap
        };
        Avalonia.Automation.AutomationProperties.SetName(preview, "Expanded template preview");
        var heading = new TextBlock
        {
            Text         = $"{destination}\nReview the expanded Markdown below. This creates an ordinary document; enrollment is separate."
          , TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal
          , Spacing     = 10
          , Children    = { create, cancel }
        };
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
          , Margin         = new Thickness(20)
          , RowSpacing     = 12
          , Children       = { heading, preview, buttons }
        };
        Grid.SetRow(preview, 1);
        Grid.SetRow(buttons, 2);
        Content = grid;
    }
}
