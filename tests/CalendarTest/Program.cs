// Checks the .ics reading used by the Home tab's agenda: dates, time zones, repeats, exceptions, meeting links.
// Run: dotnet run --project tests/CalendarTest
using Mora;

var paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
DateTime P(int y, int mo, int d, int h, int mi) => TimeZoneInfo.ConvertTime(new DateTime(y, mo, d, h, mi, 0), paris, TimeZoneInfo.Local);

const string Ics = """
BEGIN:VCALENDAR
BEGIN:VEVENT
UID:once
DTSTART:20261109T090000Z
DTEND:20261109T093000Z
SUMMARY:Dentist\, early
END:VEVENT
BEGIN:VEVENT
UID:standup
DTSTART;TZID=Europe/Paris:20261005T093000
DTEND;TZID=Europe/Paris:20261005T094500
RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR
EXDATE;TZID=Europe/Paris:20261111T093000
SUMMARY:Standup
DESCRIPTION:Join with Google Meet: https://meet.google.com/abc-defg-hij\nOr dial in
END:VEVENT
BEGIN:VEVENT
UID:standup
RECURRENCE-ID;TZID=Europe/Paris:20261113T093000
DTSTART;TZID=Europe/Paris:20261113T110000
DTEND;TZID=Europe/Paris:20261113T111500
SUMMARY:Standup (moved)
END:VEVENT
BEGIN:VEVENT
UID:monthly
DTSTART;TZID=Romance Standard Time:20260101T140000
DURATION:PT1H
RRULE:FREQ=MONTHLY;BYDAY=2TU;COUNT=24
SUMMARY:Review
LOCATION:https://teams.microsoft.com/l/meetup-join/xyz
END:VEVENT
BEGIN:VEVENT
UID:holiday
DTSTART;VALUE=DATE:20261111
DTEND;VALUE=DATE:20261112
SUMMARY:Armistice
END:VEVENT
BEGIN:VEVENT
UID:gone
DTSTART:20261110T100000Z
STATUS:CANCELLED
SUMMARY:Cancelled
END:VEVENT
END:VCALENDAR
""";

var events = Calendar.ParseIcs(Ics, new DateTime(2026, 11, 9), new DateTime(2026, 11, 16)).OrderBy(e => e.Start).ToList();
foreach (var e in events) Console.WriteLine($"{e.Start:ddd dd HH:mm} → {e.End:HH:mm}  {e.Title}  {(e.AllDay ? "(all day) " : "")}{e.Link}");

int fails = 0;
void Check(bool ok, string what) { if (!ok) { fails++; Console.WriteLine("FAIL: " + what); } }

Check(events.Any(e => e.Title == "Dentist, early" && e.Start == new DateTime(2026, 11, 9, 9, 0, 0, DateTimeKind.Utc).ToLocalTime()), "UTC event, escaped comma");
var standups = events.Where(e => e.Title.StartsWith("Standup")).ToList();
Check(standups.Select(e => e.Start).SequenceEqual([P(2026, 11, 9, 9, 30), P(2026, 11, 13, 11, 0)]), "weekly Mon/Wed/Fri, Wed excluded, Fri moved");
Check(standups[0].Link == "https://meet.google.com/abc-defg-hij", "Meet link from the description");
Check(standups[0].End - standups[0].Start == TimeSpan.FromMinutes(15), "length from DTEND");
Check(events.Any(e => e.Title == "Review" && e.Start == P(2026, 11, 10, 14, 0) && e.Link!.StartsWith("https://teams.microsoft.com")), "2nd Tuesday, Windows zone name, Teams link");
Check(events.Any(e => e.Title == "Armistice" && e.AllDay && e.Start == new DateTime(2026, 11, 11)), "all-day event");
Check(!events.Any(e => e.Title == "Cancelled"), "cancelled event left out");
Check(Calendar.MeetingLink("call at https://us02web.zoom.us/j/123?pwd=x.") == "https://us02web.zoom.us/j/123?pwd=x", "Zoom link, trailing dot trimmed");

Console.WriteLine(fails == 0 ? "All calendar checks passed" : $"{fails} check(s) failed");
return fails == 0 ? 0 : 1;
