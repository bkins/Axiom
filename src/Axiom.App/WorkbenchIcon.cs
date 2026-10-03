using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using IconPath = Avalonia.Controls.Shapes.Path;

namespace Axiom.App;

internal sealed class WorkbenchIcon : Viewbox
{
    public WorkbenchIcon(string geometry)
    {
        Width = Height = 17;
        var path = new IconPath
        {
            Data = Geometry.Parse(geometry)
          , StrokeThickness = 1.6
          , StrokeLineCap = PenLineCap.Round
          , StrokeJoin = PenLineJoin.Round
          , Width = 24
          , Height = 24
        };
        path.Bind(Shape.StrokeProperty, new Binding("Foreground")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(Button) }
        });
        Child = path;
    }
}
