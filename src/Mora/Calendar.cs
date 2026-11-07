using System.Globalization;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.Appointments;

namespace Mora;

// The next events, from Windows' own calendars and from a calendar link (.ics: the private "iCal address" that
// Google Calendar and Outlook give out). Recurring events are expanded for daily, weekly, monthly and yearly rules.
static partial class Calendar
{
    public record Event(DateTime Start, DateTime End, string Title, string? Link, bool AllDay);

    // What is left of today and tomorrow, soonest first.
    public static async Task<List<Event>> Upcoming(HttpClient http, string icsUrl)
    {
        DateTime now = DateTime.Now, to = DateTime.Today.AddDays(2);
        var events = new List<Event>(await FromWindows(now, to));
        if (icsUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) || icsUrl.StartsWith("webcal", StringComparison.OrdinalIgnoreCase))
        {
            try { events.AddRange(ParseIcs(await http.GetStringAsync(Regex.Replace(icsUrl, "^webcal", "https", RegexOptions.IgnoreCase)), now.AddDays(-1), to)); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or UriFormatException) { } // offline, or a wrong link
        }
        return [.. events.Where(e => e.End > now && e.Start < to).OrderBy(e => e.AllDay).ThenBy(e => e.Start)];
    }

    static async Task<IEnumerable<Event>> FromWindows(DateTime from, DateTime to)
    {
        try
        {
            var store = await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly);
            var options = new FindAppointmentsOptions();
            foreach (var p in new[] { AppointmentProperties.Subject, AppointmentProperties.StartTime, AppointmentProperties.Duration,
                         AppointmentProperties.AllDay, AppointmentProperties.Location, AppointmentProperties.OnlineMeetingLink })
                options.FetchProperties.Add(p);
            var found = await store.FindAppointmentsAsync(from, to - from, options);
            return found.Select(a => new Event(a.StartTime.LocalDateTime, (a.StartTime + a.Duration).LocalDateTime, a.Subject,
                MeetingLink(a.OnlineMeetingLink + " " + a.Location), a.AllDay));
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { return []; } // no calendar access
    }

    // Meet, Teams, Zoom, Webex or Discord, wherever it appears in the event.
    public static string? MeetingLink(string text) =>
        MeetingRegex().Match(text) is { Success: true } m ? m.Value.TrimEnd('.', ',', ')', '>', '"') : null;

    [GeneratedRegex(@"https://(meet\.google\.com|teams\.microsoft\.com|teams\.live\.com|[\w.-]*zoom\.us|[\w.-]*webex\.com|discord\.gg|discord\.com)/[^\s\\<""]+", RegexOptions.IgnoreCase)]
    private static partial Regex MeetingRegex();

    // ---------- .ics ----------

    public static List<Event> ParseIcs(string ics, DateTime from, DateTime to)
    {
        var lines = ics.Replace("\r\n", "\n").Replace("\n ", "").Replace("\n\t", "").Split('\n');
        var blocks = new List<List<(string Name, Dictionary<string, string> Params, string Value)>>();
        List<(string, Dictionary<string, string>, string)>? current = null;
        foreach (var line in lines)
        {
            if (line == "BEGIN:VEVENT") current = [];
            else if (line == "END:VEVENT" && current != null) { blocks.Add(current); current = null; }
            else if (current != null && line.IndexOf(':') is var colon and > 0)
            {
                var head = line[..colon].Split(';');
                var ps = head.Skip(1).Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].ToUpperInvariant(), p => p[1].Trim('"'));
                current.Add((head[0].ToUpperInvariant(), ps, line[(colon + 1)..]));
            }
        }

        var events = new List<Event>();
        var moved = new HashSet<(string, DateTime)>(); // occurrences of a series that were edited: their own event replaces them
        foreach (var b in blocks)
            if (Prop(b, "RECURRENCE-ID") is { } rid && Prop(b, "UID") is { } uid) moved.Add((uid.Value, When(rid).At));

        foreach (var b in blocks)
        {
            if (Prop(b, "DTSTART") is not { } dtStart || Prop(b, "STATUS")?.Value == "CANCELLED") continue;
            var (start, allDay, zone) = When(dtStart);
            var length = Prop(b, "DTEND") is { } dtEnd ? When(dtEnd).At - start
                : Prop(b, "DURATION") is { } dur ? Duration(dur.Value) : allDay ? TimeSpan.FromDays(1) : TimeSpan.Zero;
            var title = Unescape(Prop(b, "SUMMARY")?.Value ?? "");
            var link = MeetingLink(string.Join(" ", b.Where(p => p.Name is "LOCATION" or "DESCRIPTION" or "URL" or "X-GOOGLE-CONFERENCE").Select(p => Unescape(p.Value))));
            var uidValue = Prop(b, "UID")?.Value ?? "";
            var skip = b.Where(p => p.Name == "EXDATE").SelectMany(p => p.Value.Split(',').Select(v => When((p.Name, p.Params, v)).At)).ToHashSet();

            IEnumerable<DateTime> starts = Prop(b, "RRULE") is { } rule && Prop(b, "RECURRENCE-ID") is null
                ? Repeat(start, rule.Value, to, zone, allDay)
                : [start];
            foreach (var s in starts)
            {
                if (s + length <= from) continue;
                if (s >= to) break;
                if (skip.Contains(s) || moved.Contains((uidValue, s)) && Prop(b, "RECURRENCE-ID") is null) continue;
                events.Add(new Event(s, s + length, title, link, allDay));
            }
        }
        return events;
    }

    static (string Name, Dictionary<string, string> Params, string Value)? Prop(List<(string Name, Dictionary<string, string> Params, string Value)> b, string name) =>
        b.FirstOrDefault(p => p.Name == name) is { Name: not null } p ? p : null;

    static string Unescape(string s) => s.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");

    // A date or a date-time, in local time; and the time zone the event repeats in (its wall clock time stays put across DST).
    static (DateTime At, bool AllDay, TimeZoneInfo? Zone) When((string Name, Dictionary<string, string> Params, string Value) p)
    {
        var v = p.Value.Trim();
        if (v.Length == 8 && DateTime.TryParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return (day, true, null);
        if (!DateTime.TryParseExact(v.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            return (DateTime.MinValue, false, null);
        if (v.EndsWith('Z')) return (DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime(), false, TimeZoneInfo.Utc);
        var zone = p.Params.TryGetValue("TZID", out var id) ? FindZone(id) : null;
        return (zone is null ? t : Local(t, zone), false, zone);
    }

    static TimeZoneInfo? FindZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); } // IANA (Google) and Windows (Outlook) names both work
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { return null; }
    }

    static DateTime Local(DateTime wall, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(wall, DateTimeKind.Unspecified), zone, TimeZoneInfo.Local);

    static TimeSpan Duration(string v)
    {
        var m = Regex.Match(v, @"P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?");
        int N(int g) => m.Groups[g].Success ? int.Parse(m.Groups[g].Value) : 0;
        return new TimeSpan(N(1) * 7 + N(2), N(3), N(4), N(5));
    }

    static readonly string[] Days = ["SU", "MO", "TU", "WE", "TH", "FR", "SA"];

    // Occurrence starts of a repeating event, in local time, up to "to".
    // ponytail: FREQ with INTERVAL, COUNT, UNTIL, BYDAY (weekly days, or the nth weekday of a month) and BYMONTHDAY;
    // the rarer parts of RFC 5545 (BYSETPOS, BYWEEKNO, hourly rules) are ignored.
    static IEnumerable<DateTime> Repeat(DateTime first, string rule, DateTime to, TimeZoneInfo? zone, bool allDay)
    {
        var r = rule.Split(';').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].ToUpperInvariant(), p => p[1]);
        var freq = r.GetValueOrDefault("FREQ", "");
        int interval = int.TryParse(r.GetValueOrDefault("INTERVAL"), out var i) && i > 0 ? i : 1;
        int count = int.TryParse(r.GetValueOrDefault("COUNT"), out var c) ? c : int.MaxValue;
        var until = r.TryGetValue("UNTIL", out var u) ? When(("UNTIL", [], u)).At : DateTime.MaxValue;
        if (until.Date == until && u?.Length == 8) until = until.AddDays(1); // a date: the whole day counts
        var byDay = r.TryGetValue("BYDAY", out var bd) ? bd.Split(',') : [];
        var byMonthDay = r.TryGetValue("BYMONTHDAY", out var bm) ? bm.Split(',').Select(int.Parse).ToArray() : [];

        // The rule runs on the event's own wall clock, then each start is turned into local time.
        var wall = zone is null || allDay ? first : TimeZoneInfo.ConvertTime(first, TimeZoneInfo.Local, zone);
        DateTime ToLocal(DateTime w) => zone is null || allDay ? w : Local(w, zone);

        int emitted = 0;
        for (int n = 0; n < 20000; n++)
        {
            var batch = freq switch
            {
                "DAILY" => [wall.AddDays(n * interval)],
                "WEEKLY" => WeekDays(wall.AddDays(n * 7 * interval)),
                "MONTHLY" => MonthDays(wall.AddMonths(n * interval)),
                "YEARLY" => [wall.AddYears(n * interval)],
                _ => n == 0 ? [wall] : [],
            };
            if (batch.Count == 0) yield break;
            foreach (var w in batch.Where(w => w >= wall).Order())
            {
                var local = ToLocal(w);
                if (local > until || emitted >= count || local >= to) yield break;
                emitted++;
                yield return local;
            }
        }

        List<DateTime> WeekDays(DateTime anyDayOfWeek)
        {
            if (byDay.Length == 0) return [anyDayOfWeek];
            var monday = anyDayOfWeek.AddDays(-(((int)anyDayOfWeek.DayOfWeek + 6) % 7));
            return [.. byDay.Select(d => Array.IndexOf(Days, d[^2..])).Where(d => d >= 0).Select(d => monday.AddDays((d + 6) % 7))];
        }

        List<DateTime> MonthDays(DateTime month)
        {
            var firstDay = new DateTime(month.Year, month.Month, 1, wall.Hour, wall.Minute, wall.Second);
            int length = DateTime.DaysInMonth(month.Year, month.Month);
            if (byDay.Length > 0)
                return [.. byDay.Select(d =>
                {
                    int wd = Array.IndexOf(Days, d[^2..]);
                    int nth = d.Length > 2 && int.TryParse(d[..^2], out var k) ? k : 1;
                    if (wd < 0) return (DateTime?)null;
                    var day = nth > 0
                        ? firstDay.AddDays((wd - (int)firstDay.DayOfWeek + 7) % 7 + (nth - 1) * 7)
                        : firstDay.AddDays(length - 1).AddDays(-(((int)firstDay.AddDays(length - 1).DayOfWeek - wd + 7) % 7) + (nth + 1) * 7);
                    return day.Month == month.Month ? day : null;
                }).OfType<DateTime>()];
            var days = byMonthDay.Length > 0 ? byMonthDay : [wall.Day];
            return [.. days.Select(d => d < 0 ? length + d + 1 : d).Where(d => d >= 1 && d <= length).Select(d => firstDay.AddDays(d - 1))];
        }
    }
}
