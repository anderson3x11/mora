using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using static Atoll.Native;

namespace Atoll;

// Shelf: files dropped on the island stay listed (the files themselves are not moved or copied)
// and can be dragged out into any app. A file dragged over the island opens it on this tab.
// The list of paths is kept in shelf.json next to the settings.
// ponytail: dragging toward the top of the screen while the island is hidden does not open it yet
// (that needs a global drag watcher that ignores browser tab and window drags).
public sealed partial class IslandWindow
{
    static readonly string ShelfFile = Path.Combine(Path.GetDirectoryName(Settings.FilePath)!, "shelf.json");
    readonly List<string> _shelf = [];
    int _dragToken;

    void LoadShelf()
    {
        try
        {
            var saved = System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(ShelfFile)) ?? [];
            _shelf.AddRange(saved.Where(p => File.Exists(p) || Directory.Exists(p)));
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { }
    }

    void SaveShelf()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ShelfFile)!);
            File.WriteAllText(ShelfFile, System.Text.Json.JsonSerializer.Serialize(_shelf));
        }
        catch (IOException) { } // keeps working from memory
    }

    void Pill_DragEnter(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems) || _game != null || _setup != null) return;
        _dragToken++;
        if (_mode == Mode.Expanded && _tab == Tab.Shelf) return;
        _tab = Tab.Shelf;
        ShowTab();
        if (_mode == Mode.Expanded) Retarget(GeoOf(Mode.Expanded), OpenSpring);
        else SetMode(Mode.Expanded, Via.Drag);
    }

    void Pill_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Copy; // the source keeps its file: nothing is moved
        e.DragUIOverride.Caption = "Keep on shelf";
    }

    async void Pill_DragLeave(object sender, DragEventArgs e) => await SettleAfterDrag(450);

    async void Pill_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var deferral = e.GetDeferral();
        IReadOnlyList<IStorageItem> items;
        try { items = await e.DataView.GetStorageItemsAsync(); }
        catch (System.Runtime.InteropServices.COMException) { return; } // virtual files (zip contents, phones) have no path
        finally { deferral.Complete(); }
        foreach (var item in items)
            if (!string.IsNullOrEmpty(item.Path) && !_shelf.Contains(item.Path)) _shelf.Insert(0, item.Path);
        SaveShelf();
        FillShelf();
        if (_mode == Mode.Expanded) Retarget(GeoOf(Mode.Expanded), OpenSpring);
        await SettleAfterDrag(900);
    }

    // A panel the drag opened goes away once the drag is over, unless the pointer stays on it.
    async Task SettleAfterDrag(int ms)
    {
        var t = ++_dragToken;
        await Task.Delay(ms);
        if (t == _dragToken && _mode == Mode.Expanded && _via == Via.Drag && !(GetCursorPos(out var pt) != 0 && OnPill(pt)))
            Settle();
    }

    void FillShelf()
    {
        var res = ((FrameworkElement)Content).Resources;
        ShelfTabText.Text = _shelf.Count == 0 ? "Shelf" : $"Shelf ({_shelf.Count})";
        ShelfTab.Children.Clear();
        if (_shelf.Count == 0)
        {
            var zone = new Grid { Height = 57 };
            zone.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Stroke = (Brush)res["Line"], StrokeThickness = 1.5, StrokeDashArray = [4, 3], RadiusX = 12, RadiusY = 12,
            });
            var text = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = "Drop files here to keep them for later", FontSize = 11, Foreground = (Brush)res["Fg"], HorizontalAlignment = HorizontalAlignment.Center });
            text.Children.Add(new TextBlock { Text = "Then drag them out into any app", FontSize = 10.5, Foreground = (Brush)res["Muted"], HorizontalAlignment = HorizontalAlignment.Center });
            zone.Children.Add(text);
            ShelfTab.Children.Add(zone);
            return;
        }
        foreach (var path in _shelf.Take(4))
        {
            var row = new Grid { Background = (Brush)res["Raised"], CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 3, 3, 3), ColumnSpacing = 8, CanDrag = true };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new FontIcon
            {
                Glyph = Directory.Exists(path) ? "" : "", FontSize = 13, FontFamily = (FontFamily)res["Icons"], Foreground = (Brush)res["Fg"],
            });
            var name = new TextBlock
            {
                Text = Path.GetFileName(path.TrimEnd('\\')), FontSize = 11, Foreground = (Brush)res["Fg"],
                TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var remove = new Border
            {
                Style = (Style)res["Chip"], Padding = new Thickness(8, 5, 8, 5),
                Child = new FontIcon { Glyph = "", FontSize = 9, FontFamily = (FontFamily)res["Icons"], Foreground = (Brush)res["Fg"] },
            };
            remove.Tapped += (_, e) =>
            {
                e.Handled = true;
                _shelf.Remove(path);
                SaveShelf();
                FillShelf();
                Retarget(GeoOf(Mode.Expanded), CloseSpring);
            };
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            row.DragStarting += async (_, e) =>
            {
                var deferral = e.GetDeferral();
                try
                {
                    IStorageItem item = Directory.Exists(path)
                        ? await StorageFolder.GetFolderFromPathAsync(path)
                        : await StorageFile.GetFileFromPathAsync(path);
                    e.Data.SetStorageItems([item]);
                    e.Data.RequestedOperation = DataPackageOperation.Copy;
                }
                catch (Exception x) when (x is FileNotFoundException or UnauthorizedAccessException)
                {
                    e.Cancel = true; // moved or deleted since it was dropped
                    _shelf.Remove(path);
                    SaveShelf();
                    FillShelf();
                }
                finally { deferral.Complete(); }
            };
            ShelfTab.Children.Add(row);
        }
    }
}
