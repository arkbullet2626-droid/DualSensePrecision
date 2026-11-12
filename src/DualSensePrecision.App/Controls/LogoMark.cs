using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace DualSensePrecision.App.Controls;

/// <summary>
/// The DualSense Precision "DP" mark (cut-corner variation), drawn as one vector path in the theme's logo colour
/// (DsLogoBrush, set in Settings → Interface → Colours → Logo). Scales to whatever size it's given.
/// </summary>
public sealed partial class LogoMark : UserControl
{
    // 64×64 design grid: cut-corner D with a P inside its counter (even-odd fill makes the holes).
    public const string PathData =
        "F0 M8,6 H42 L58,22 V42 L42,58 H8 Z M19,17 V47 H37 L47,37 V27 L37,17 Z " +
        "M24,23 H37 L41,27 V33 L37,37 H30 V41 H24 Z M30,28.5 V31.5 H35 V28.5 Z";

    public LogoMark()
    {
        var path = (Path)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            $"<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"{PathData}\" />");
        path.Fill = (Brush)Application.Current.Resources["DsLogoBrush"];
        // Keep the full 64×64 box so the mark's padding is the same everywhere.
        var box = new Grid { Width = 64, Height = 64 };
        box.Children.Add(path);
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = box };
    }
}
