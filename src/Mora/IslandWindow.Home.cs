using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Devices.Geolocation;

namespace Mora;

// Today: the agenda (with a Join button for the next call), a focus timer and the
// weather. The agenda and the weather refresh when the tab shows, at most every few minutes.
public sealed partial class IslandWindow
{
    List<Calendar.Event> _events = [];
    DateTime _eventsAt, _weatherAt;
    (int Temp, int High, int Low, int Code, bool Day)? _weather;
    string _weatherNote = "";

    static readonly SolidColorBrush AppleBlue = new(Windows.UI.Color.FromArgb(255, 0x0A, 0x84, 0xFF)); // links and actions, as Apple tints them
    static readonly SolidColorBrush Hairline = new(Windows.UI.Color.FromArgb(255, 0x2C, 0x2C, 0x2E));

    void FillHome()
    {
        if (_media is null) return; // the theme shows the first tab before the media session exists
        var res = ((FrameworkElement)Content).Resources;
        Brush Res(string key) => (Brush)res[key];

        // Agenda: the next two events, the one starting soon with its Join button.
        var now = DateTime.Now;
        HomeDay.Text = now.Day.ToString();
        HomeWeekday.Text = now.ToString("ddd").TrimEnd('.').ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        HomeEvents.Children.Clear();
        var next = _events.Where(e => e.End > now).Take(2).ToList();
        if (next.Count == 0)
            HomeEvents.Children.Add(new TextBlock { Text = "No events", FontSize = 13, Foreground = Res("Muted") });
        for (int i = 0; i < next.Count; i++)
        {
            var e = next[i];
            var row = new Grid { ColumnSpacing = 7 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(i == 0 ? Windows.UI.Color.FromArgb(255, 0x0A, 0x84, 0xFF) : Windows.UI.Color.FromArgb(255, 0xBF, 0x5A, 0xF2)) });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = e.Title, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Res("Fg"), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
            text.Children.Add(new TextBlock { Text = When(e), FontSize = 12, Foreground = Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            if (e.Link is { } link && e.Start - now < TimeSpan.FromMinutes(15)) // about to start, or under way
            {
                var join = new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Padding = new Thickness(6, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "Join", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = AppleBlue } };
                join.Tapped += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link) { UseShellExecute = true });
                Grid.SetColumn(join, 2);
                row.Children.Add(join);
            }
            HomeEvents.Children.Add(row);
        }

        // Focus: idle, the ring is empty and offers a 25 min round; running, it empties as time goes, orange (green on
        // a break), and a tap stops it.
        var running = _timer;
        var left = running is null ? TimeSpan.FromSeconds(_focusWork) : TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling((running.End - DateTime.UtcNow).TotalSeconds)));
        HomeTimerLabel.Text = (running is null ? "Focus" : running.Round > 0 ? (running.Break ? "Break" : $"Focus {running.Round}") : "Timer").ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        HomeTimerLeft.Text = Format(left);
        HomeTimerLeft.Foreground = Res("Fg");
        var tint = new SolidColorBrush(running is { Break: true } ? _theme.Calm : _theme.Warm);
        double share = running is { Seconds: > 0 } ? Math.Clamp(left.TotalSeconds / running.Seconds, 0, 1) : 0, units = Math.PI * (66 - 5) / 5;
        HomeTimerArc.Stroke = tint;
        HomeTimerArc.Visibility = share > 0 ? Visibility.Visible : Visibility.Collapsed;
        HomeTimerArc.StrokeDashArray = share >= 1 ? null : [Math.Max(0.01, share * units - 1), units * 2];
        HomeTimerIcon.Glyph = running is null ? "\uE768" : "\uE71A"; // play, stop
        HomeTimerIcon.Foreground = running is null ? Res("Muted") : tint;

        // Weather
        HomeWeather.Visibility = _weather is null ? Visibility.Collapsed : Visibility.Visible;
        HomeWeatherNone.Visibility = _weather is null ? Visibility.Visible : Visibility.Collapsed;
        HomeWeatherNone.Text = _settings.ShowWeather ? _weatherNote : "Weather is off in Settings";
        if (_weather is { } w)
        {
            var (glyph, sky) = Sky(w.Code, w.Day);
            HomeTemp.Text = $"{w.Temp}°";
            HomeSky.Glyph = glyph;
            HomeSkyText.Text = sky;
            HomeHighLow.Text = $"H {w.High}°  L {w.Low}°";
        }
        _ = RefreshHome();
    }

    // "In 12 min", "10:00 – 10:30", "Tomorrow 9:30", "All day"
    static string When(Calendar.Event e)
    {
        var now = DateTime.Now;
        if (e.AllDay) return e.Start.Date > DateTime.Today ? "Tomorrow, all day" : "All day";
        if (e.Start <= now) return $"Now, until {e.End:t}";
        if (e.Start - now < TimeSpan.FromMinutes(60)) return $"In {(int)Math.Ceiling((e.Start - now).TotalMinutes)} min";
        return (e.Start.Date > DateTime.Today ? "Tomorrow " : "") + $"{e.Start:t} – {e.End:t}";
    }

    void HomeTimer_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (_timer is null) StartFocus(1, rest: false); else StopTimer();
        FillHome();
    }

    bool _refreshing;

    async Task RefreshHome()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            if (DateTime.Now - _eventsAt > TimeSpan.FromMinutes(5))
            {
                _eventsAt = DateTime.Now;
                _events = await Calendar.Upcoming(Http, _settings.CalendarLink);
                if (_tab == Tab.Home) FillHome();
            }
            if (_settings.ShowWeather && DateTime.Now - _weatherAt > TimeSpan.FromMinutes(30))
            {
                _weatherAt = DateTime.Now;
                await LoadWeather();
                if (_tab == Tab.Home) FillHome();
            }
        }
        finally { _refreshing = false; }
    }

    // Open-Meteo (free, no key) for where Windows' location service puts the PC, rounded to about 10 km before
    // it leaves the PC. Fahrenheit where the region uses it.
    async Task LoadWeather()
    {
        _weatherNote = "Locating…";
        try
        {
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed)
            {
                (_weather, _weatherNote) = (null, "Turn on location in Windows Settings for the weather");
                return;
            }
            var p = (await new Geolocator { DesiredAccuracy = PositionAccuracy.Default }.GetGeopositionAsync()).Coordinate.Point.Position;
            var unit = System.Globalization.RegionInfo.CurrentRegion.IsMetric ? "" : "&temperature_unit=fahrenheit";
            var url = FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={Math.Round(p.Latitude, 1)}&longitude={Math.Round(p.Longitude, 1)}&current=temperature_2m,weather_code,is_day&daily=temperature_2m_max,temperature_2m_min&forecast_days=1&timezone=auto{unit}");
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var cur = doc.RootElement.GetProperty("current");
            var daily = doc.RootElement.GetProperty("daily");
            _weather = ((int)Math.Round(cur.GetProperty("temperature_2m").GetDouble()),
                (int)Math.Round(daily.GetProperty("temperature_2m_max")[0].GetDouble()),
                (int)Math.Round(daily.GetProperty("temperature_2m_min")[0].GetDouble()),
                cur.GetProperty("weather_code").GetInt32(), cur.GetProperty("is_day").GetInt32() == 1);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException
                                      or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _weatherNote = "Weather unavailable right now";
            _weatherAt = DateTime.Now.AddMinutes(-25); // try again in 5 minutes
        }
    }

    // WMO weather codes, as Open-Meteo reports them.
    static (string Glyph, string Text) Sky(int code, bool day) => code switch
    {
        0 => (day ? "" : "", day ? "Sunny" : "Clear"),
        1 or 2 => (day ? "" : "", "Partly cloudy"),
        3 => ("", "Cloudy"),
        45 or 48 => ("", "Fog"),
        >= 51 and <= 57 => ("", "Drizzle"),
        >= 61 and <= 67 or >= 80 and <= 82 => ("", "Rain"),
        >= 71 and <= 77 or 85 or 86 => ("", "Snow"),
        >= 95 => ("", "Storm"),
        _ => ("", ""),
    };
}
