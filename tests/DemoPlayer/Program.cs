// A media player Windows reports like any other (Spotify, a browser), driven from stdin, for tests and demos:
//   track <title>|<artist>|<cover.png or empty>|<seconds>|<position>    pause    play    quit
// It plays a quiet tone in a loop, so the session is "playing" and the waveform has something to show.
// Copy the executable under another name to have a second, separate player.
using System.Text;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

var wav = Path.Combine(Path.GetTempPath(), "mora-demo-tone.wav");
if (!File.Exists(wav)) WriteTone(wav);
var player = new MediaPlayer { IsLoopingEnabled = true, Volume = 0.15 };
var smtc = player.SystemMediaTransportControls;
// The player's own command manager runs the system controls: the state Windows reports follows the real playback.
player.Source = MediaSource.CreateFromUri(new Uri(wav));
player.Play();
Console.WriteLine("ready");
string? line;
while ((line = Console.ReadLine()) != null)
{
    var p = line.Split(' ', 2);
    switch (p[0])
    {
        case "track":
            var f = p[1].Split('|');
            var u = smtc.DisplayUpdater;
            u.Type = MediaPlaybackType.Music;
            u.MusicProperties.Title = f[0];
            u.MusicProperties.Artist = f[1];
            u.Thumbnail = f[2] != "" ? RandomAccessStreamReference.CreateFromFile(await StorageFile.GetFileFromPathAsync(f[2])) : null;
            u.Update();
            player.Play();
            var length = TimeSpan.FromSeconds(double.Parse(f[3]));
            smtc.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero, EndTime = length, MinSeekTime = TimeSpan.Zero, MaxSeekTime = length, Position = TimeSpan.FromSeconds(double.Parse(f[4])),
            });
            break;
        case "pause": player.Pause(); break;
        case "play": player.Play(); break;
        case "quit": return;
    }
    Console.WriteLine("ok");
}

// Two seconds of a soft 220 Hz tone with a beat, mono 16-bit.
static void WriteTone(string path)
{
    const int rate = 22050, seconds = 2;
    var samples = new short[rate * seconds];
    for (int i = 0; i < samples.Length; i++)
    {
        double t = (double)i / rate, beat = t % 0.5;
        samples[i] = (short)(Math.Sin(2 * Math.PI * 220 * t) * 6000 * Math.Exp(-beat * 6));
    }
    using var w = new BinaryWriter(File.Create(path));
    w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + samples.Length * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
    w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
    w.Write(Encoding.ASCII.GetBytes("data")); w.Write(samples.Length * 2);
    foreach (var s in samples) w.Write(s);
}
