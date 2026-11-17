using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Mora;

// Synced lyrics from LRCLIB (free, no key), asked for only while the lyrics button is on. A card beside the
// player shows the line being sung, and the next one under it.
public sealed partial class IslandWindow
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8), DefaultRequestHeaders = { { "User-Agent", "Mora (https://github.com/anderson3x11/mora)" } } };

    List<(TimeSpan At, string Text)>? _lyrics;
    string _lyricsKey = "";
    string? _lyricsTip;

    void LoadLyrics()
    {
        var key = _media.Title + "\n" + _media.Artist;
        if (!_settings.ShowLyrics || key == _lyricsKey) return;
        _lyricsKey = key;
        _lyrics = null;
        if (_media.Title != "" && _media.Artist != "") _ = FetchLyrics(key, _media.Title, _media.Artist, _media.Duration);
    }

    async Task FetchLyrics(string key, string title, string artist, TimeSpan duration)
    {
        List<(TimeSpan, string)>? lines = null;
        try
        {
            var url = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var synced = doc.RootElement.EnumerateArray()
                .Where(r => r.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String)
                .OrderBy(r => r.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? Math.Abs(d.GetDouble() - duration.TotalSeconds) : 999)
                .Select(r => r.GetProperty("syncedLyrics").GetString()!)
                .FirstOrDefault();
            if (synced != null) lines = Parse(synced);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException) { } // offline: no lyrics
        if (key != _lyricsKey) return; // the track changed meanwhile
        _lyrics = lines is { Count: > 0 } ? lines : null;
        FillLyrics();
        UpdateProgressTimer();
    }

    static List<(TimeSpan, string)> Parse(string lrc) =>
    [
        .. lrc.Split('\n').Select(l => LrcLine().Match(l.Trim())).Where(m => m.Success).Select(m =>
            (TimeSpan.FromMinutes(int.Parse(m.Groups[1].Value)) + TimeSpan.FromSeconds(double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)), m.Groups[3].Value)),
    ];

    [GeneratedRegex(@"^\[(\d+):(\d+(?:\.\d+)?)\]\s*(.*)$")] private static partial Regex LrcLine();

    void FillLyrics()
    {
        var lines = _settings.ShowLyrics ? _lyrics : null;
        var p = _media.Position;
        int i = lines?.FindLastIndex(l => l.At <= p) ?? -1;
        TitleText.Text = _media.Title;
        ArtistText.Text = _media.Artist;
        LyricsCard.Visibility = lines is null ? Visibility.Collapsed : Visibility.Visible;
        if (lines != null)
        {
            // Before the first line, the first one waits as the next.
            LyricNow.Text = i < 0 ? "♪" : lines[i].Text is { Length: > 0 } t ? t : "♪";
            LyricNext.Text = i + 1 < lines.Count ? lines[i + 1].Text : "";
        }
        // The button: for music, not videos (no lyrics on YouTube or Twitch); lit while the lyrics show.
        LyricsButton.Visibility = _media.Site is "YouTube" or "Twitch" or "Kick" or "Netflix" ? Visibility.Collapsed : Visibility.Visible;
        LyricsIcon.Foreground = (Brush)((FrameworkElement)Content).Resources[_settings.ShowLyrics ? "Fg" : "Muted"];
        LyricsButton.Background = _settings.ShowLyrics ? PageOn : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var tip = _settings.ShowLyrics ? (lines is null ? "No synced lyrics for this song" : "Hide the lyrics") : "Show the lyrics";
        if (tip != _lyricsTip) Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(LyricsButton, _lyricsTip = tip); // this runs 4 times a second
    }

    void Lyrics_Tapped(object sender, TappedRoutedEventArgs e)
    {
        _settings.ShowLyrics = !_settings.ShowLyrics;
        _settings.Save();
        if (_lyrics is null) _lyricsKey = ""; // off and on again asks once more (LRCLIB was busy, or offline)
        LoadLyrics();
        FillLyrics();
        UpdateProgressTimer();
    }
}
