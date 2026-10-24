using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Atoll;

// Clipboard tab: the last few text copies, each with a Paste button that types into the app the user is in.
public sealed partial class IslandWindow
{
    ClipboardHistory? _clipboard;

    void OnClipboardUpdate()
    {
        var text = _clipboard!.OnUpdate();
        FillClipboard();
        if (_mode == Mode.Expanded && _tab == Tab.Clipboard) Retarget(GeoOf(Mode.Expanded), OpenSpring); // one more row
        if (text != null) Notify(new Alert(Pop.Clipboard, "", "Copied", OneLine(text)));
    }

    void FillClipboard()
    {
        var res = ((FrameworkElement)Content).Resources;
        ClipboardTab.Children.Clear();
        if (_clipboard is not { Items.Count: > 0 } c)
        {
            ClipboardTab.Children.Add(new TextBlock { Text = "Copy some text and it shows up here.", FontSize = 11, Foreground = (Brush)res["Muted"] });
            return;
        }
        foreach (var item in c.Items.Take(4))
        {
            var paste = new Border { Style = (Style)res["Chip"], Child = new TextBlock { Text = "Paste", Style = (Style)res["ChipText"] } };
            paste.Tapped += (_, _) => c.Paste(item);
            var row = new Grid { Background = (Brush)res["Raised"], CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 3, 3, 3), ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = OneLine(item),
                FontSize = 11,
                Foreground = (Brush)res["Fg"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(paste, 1);
            row.Children.Add(paste);
            ClipboardTab.Children.Add(row);
        }
    }

    static string OneLine(string text) => string.Join(' ', text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
}
