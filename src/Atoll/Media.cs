using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Control;

namespace Atoll;

// Whatever Windows reports as the current media session (Spotify, browsers, local players),
// so no per-app integration. Session events arrive on worker threads; all state lives on the UI thread.
sealed class Media(DispatcherQueue ui)
{
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    GlobalSystemMediaTransportControlsSession? _session;
    TimeSpan _start, _position;
    DateTimeOffset _positionAt;
    string _artKey = "";

    public event Action? Changed;
    public event Action? TrackChanged; // not raised for what was already playing at startup
    bool _primed;
    int _propertiesSeq;
    public bool HasSession => _session != null;
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public BitmapImage? Art { get; private set; }
    public string? Site { get; private set; }   // YouTube, Twitch... when it can be told
    public string Source { get; private set; } = ""; // badge: the site, else the app
    public bool Playing { get; private set; }
    public TimeSpan Duration { get; private set; }

    // Players report position rarely, so extrapolate while playing.
    public TimeSpan Position
    {
        get
        {
            var p = Playing ? _position + (DateTimeOffset.Now - _positionAt) : _position;
            return p < TimeSpan.Zero ? TimeSpan.Zero : p > Duration ? Duration : p;
        }
    }

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += (_, _) => ui.TryEnqueue(Attach);
        Attach();
    }

    public void TogglePlay() => _ = _session?.TryTogglePlayPauseAsync();
    public void Next() => _ = _session?.TrySkipNextAsync();
    public void Previous() => _ = _session?.TrySkipPreviousAsync();
    public void Seek(double fraction) => _ = _session?.TryChangePlaybackPositionAsync((_start + Duration * fraction).Ticks);

    void Attach()
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnPropertiesChanged;
            _session.PlaybackInfoChanged -= OnSessionChanged;
            _session.TimelinePropertiesChanged -= OnSessionChanged;
        }
        _session = _manager!.GetCurrentSession();
        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnPropertiesChanged;
            _session.PlaybackInfoChanged += OnSessionChanged;
            _session.TimelinePropertiesChanged += OnSessionChanged;
        }
        _ = Refresh(properties: true);
    }

    void OnSessionChanged(GlobalSystemMediaTransportControlsSession s, object e) => ui.TryEnqueue(() => _ = Refresh(properties: false));
    void OnPropertiesChanged(GlobalSystemMediaTransportControlsSession s, object e) => ui.TryEnqueue(() => _ = Refresh(properties: true));

    // properties: the title, artist or artwork changed. Browsers send these in steps when switching
    // tabs (title first, artwork later), so the artwork and source are re-read every time, not only
    // when the title changes.
    async Task Refresh(bool properties)
    {
        var s = _session;
        if (s == null)
        {
            (Title, Artist, Art, Playing, _artKey, Site, Source) = ("", "", null, false, "", null, "");
            _primed = true;
            Changed?.Invoke();
            return;
        }

        Playing = s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        var t = s.GetTimelineProperties();
        _start = t.StartTime;
        Duration = t.EndTime - t.StartTime;
        _position = t.Position - t.StartTime;
        _positionAt = t.LastUpdatedTime.Year < 2000 ? DateTimeOffset.Now : t.LastUpdatedTime;

        bool newTrack = false;
        var seq = properties ? ++_propertiesSeq : _propertiesSeq;
        try
        {
            var props = await s.TryGetMediaPropertiesAsync();
            if (s != _session || seq != _propertiesSeq) return; // a newer read is under way
            (Title, Artist) = (props.Title, props.Artist);
            var key = Title + "\n" + Artist;
            newTrack = key != _artKey && Title != "";
            if (properties || newTrack)
            {
                _artKey = key;
                (Site, Source) = MediaSource.Identify(s.SourceAppUserModelId, Title, Artist);
                BitmapImage? art = null; // the old artwork stays up until the new one is decoded: no blank flash
                if (props.Thumbnail != null)
                {
                    art = new BitmapImage { DecodePixelHeight = 128 }; // by height: video thumbnails are wide
                    await art.SetSourceAsync(await props.Thumbnail.OpenReadAsync());
                    if (s != _session || seq != _propertiesSeq) return;
                }
                Art = art;
                if (Source == "" || Site is null) _ = IdentifyLater(s, key);
            }
        }
        catch (COMException) { } // the player closed while we were reading it

        Changed?.Invoke();
        if (newTrack && _primed) TrackChanged?.Invoke();
        _primed = true;
    }

    // The browser window title can lag the media title by a moment after a tab switch: look once more.
    async Task IdentifyLater(GlobalSystemMediaTransportControlsSession s, string key)
    {
        await Task.Delay(1500);
        if (s != _session || key != _artKey) return;
        var found = MediaSource.Identify(s.SourceAppUserModelId, Title, Artist);
        if (found == (Site, Source)) return;
        (Site, Source) = found;
        Changed?.Invoke();
    }
}
