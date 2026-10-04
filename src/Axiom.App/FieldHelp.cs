using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Axiom.App;

internal static class FieldHelp
{
    internal static string Text(string field) => field switch
    {
        "Tags" => "Tags group documents, for example Project or Decision. Enter one tag per line. Apply stores metadata separately from Markdown.",
        "Type" => "Type is an optional document category, for example Note or Decision. Enter one value. It does not change the filename or Markdown.",
        "Aliases" => "Aliases are alternate document names used by Classify and wiki-link lookup. Enter one alias per line. They do not rename the file; ambiguous names require a choice.",
        "Filter tags" => "Enter one tag per line. Results must have every requested tag. Filters search saved metadata and leave the editor unchanged.",
        "Filter type" => "Enter the exact saved document type, or leave empty to include every type.",
        "Filename or alias" => "Find saved documents by filename or alternate alias. Clear resets the filters; Refresh rescans saved identities without saving your current buffer.",
        "Workspace search" => "Search text in saved Markdown files in the active workspace. Results show the filename, matching excerpt and line. Unsaved editor text is not written or searched on disk.",
        "Target document" => "Choose a uniquely identified, saved Axiom document. The relationship points from the current document to this target.",
        "Kind" => "Describe the directed relationship, for example Related or DependsOn. The kind is stored with the relationship, outside Markdown.",
        "Optional note" => "An optional explanation of this relationship. Apply stores it outside Markdown; Cancel leaves the previous relationship unchanged.",
        _ => "This field affects the current view; it does not save the editor."
    };

    internal static Flyout Show(Control target, string field)
    {
        var flyout = new Flyout
        {
            Content = new TextBlock
            {
                Width = 290
              , Text = Text(field)
              , TextWrapping = TextWrapping.Wrap
            }
        };
        flyout.ShowAt(target);
        return flyout;
    }

    internal static StackPanel Label(string field)
    {
        var info = new Button
        {
            Content = "ⓘ"
          , Tag = field
          , Padding = new Avalonia.Thickness(4, 0)
          , MinHeight = 22
        };
        Avalonia.Automation.AutomationProperties.SetName(info, "About " + field);
        ToolTip.SetTip(info, Text(field));
        info.Click += (_, _) => Show(info, field);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal
          , Spacing = 6
          , Children =
            {
                new TextBlock
                {
                    Text = field
                  , VerticalAlignment = VerticalAlignment.Center
                }
              , info
            }
        };
    }
}
