using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Mora;

// Clipboard tab: the last few copies of text and images, each with a Paste button that types into the app the user is in.
public sealed partial class IslandWindow
{
    ClipboardHistory? _clipboard;

    async void OnClipboardUpdate()
    {
        var clip = _clipboard!.OnUpdate();
        if (clip is { Dib: not null }) clip.Thumb = await Thumbnail(clip);
        FillClipboard();
        if (_mode == Mode.Expanded && _tab == Tab.Clipboard) Retarget(GeoOf(Mode.Expanded), OpenSpring); // one more row
        if (clip?.Text is { } text) Notify(new Alert(Pop.Clipboard, "", "Copied", OneLine(text)));
        else if (clip != null) Notify(new Alert(Pop.Clipboard, "", "Copied", ImageName(clip), Art: clip.Thumb));
    }

    static async Task<ImageSource?> Thumbnail(Clip clip)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(clip.Bmp().AsBuffer());
            stream.Seek(0);
            var image = new BitmapImage { DecodePixelHeight = 64 };
            await image.SetSourceAsync(stream);
            return image;
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or ArgumentException) { return null; } // a DIB the decoder does not read: the row shows an icon
    }

    static string ImageName(Clip clip) => $"Image · {clip.Width} × {clip.Height}";

    void FillClipboard()
    {
        var res = ((FrameworkElement)Content).Resources;
        ClipboardTab.Children.Clear();
        if (_clipboard is not { Items.Count: > 0 } c)
        {
            ClipboardTab.Children.Add(new TextBlock
            {
                Text = "Copy some text or an image and it shows up here", FontSize = 13, Foreground = (Brush)res["Muted"],
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 52, 0, 0), // centered, like "Not playing"
            });
            return;
        }
        foreach (var item in c.Items.Take(3)) // what fits the panel's fixed height
        {
            var paste = new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Padding = new Thickness(10, 6, 2, 6), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "Paste", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = AppleBlue } };
            paste.Tapped += (_, _) => c.Paste(item);
            var row = new Grid { BorderBrush = Hairline, BorderThickness = new Thickness(0, 0, 0, item == c.Items.Take(3).Last() ? 0 : 1), Padding = new Thickness(2, 0, 0, 0), Height = RowHeight, ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (item.Dib != null)
            {
                row.Padding = new Thickness(4, 0, 5, 0);
                row.Children.Add(new Border
                {
                    Width = 46, Height = 26, CornerRadius = new CornerRadius(7), Background = (Brush)res["Line"],
                    Child = item.Thumb is { } thumb
                        ? new Image { Source = thumb, Stretch = Stretch.UniformToFill }
                        : new FontIcon { Glyph = "", FontSize = 12, FontFamily = (FontFamily)res["Icons"], Foreground = (Brush)res["Fg"] },
                });
            }
            var label = new TextBlock
            {
                Text = item.Text is { } text ? OneLine(text) : ImageName(item),
                FontSize = 13,
                Foreground = (Brush)res["Fg"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            Grid.SetColumn(paste, 2);
            row.Children.Add(paste);
            ClipboardTab.Children.Add(row);
        }
    }

    // On one line, and no longer than a row can show: a 1 MB copy must not reach the layout whole.
    static string OneLine(string text) =>
        string.Join(' ', (text.Length > 400 ? text[..400] : text).Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
}
