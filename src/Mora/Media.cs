using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Control;

namespace Mora;

// Whatever Windows reports as the current media session (Spotify, browsers, local players),
// so no per-app integration. Session events arrive on worker threads; all state lives on the UI thread.
sealed class Media(DispatcherQueue ui)
{
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    GlobalSystemMediaTransportControlsSession? _session;
    TimeSpan _start, _position;
    DateTimeOffset _positionAt;
    string _artKey = "", _realKey = ""; // the track shown; the last one that was really played

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
        _manager.SessionsChanged += (_, _) => ui.TryEnqueue(Attach); // an ignored app's clip ending can leave the current session unchanged
        Attach();
    }

    // Game launchers play promo clips (the Riot client), which are not the user's media.
    // ponytail: matched on the app id; turn it into a setting if more launchers turn up.
    static readonly string[] IgnoredApps = ["Riot", "League"];
    static bool Ignored(GlobalSystemMediaTransportControlsSession s) =>
        IgnoredApps.Any(a => s.SourceAppUserModelId.Contains(a, StringComparison.OrdinalIgnoreCase));

    // Which player the island follows: the one picked from the panel while it is open; else Windows' current one if it
    // plays; else one that plays (Spotify playing while a paused browser tab is still "current"); else the current one.
    // Ignored apps never.
    GlobalSystemMediaTransportControlsSession? Pick()
    {
        var sessions = _manager!.GetSessions().Where(s => !Ignored(s)).ToList();
        if (sessions.FirstOrDefault(s => s.SourceAppUserModelId == _pinned) is { } pinned) return pinned;
        _pinned = null; // closed since
        var current = _manager.GetCurrentSession();
        if (current != null && Ignored(current)) current = null;
        if (current != null && IsPlaying(current)) return current;
        return sessions.FirstOrDefault(IsPlaying) ?? current ?? sessions.FirstOrDefault();
    }

    static bool IsPlaying(GlobalSystemMediaTransportControlsSession s) =>
        s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

    string? _pinned;  // app id of the player picked from the panel
    bool _switching;  // the next track is a switch of player by the user, not news
    readonly List<GlobalSystemMediaTransportControlsSession> _watched = [];

    public int Players => _manager?.GetSessions().Count(s => !Ignored(s)) ?? 0;

    // The next open player, in Windows' order: the source badge in the panel cycles through them.
    public void NextPlayer()
    {
        var sessions = _manager?.GetSessions().Where(s => !Ignored(s)).ToList() ?? [];
        if (sessions.Count < 2) return;
        int at = sessions.FindIndex(s => s.SourceAppUserModelId == _session?.SourceAppUserModelId);
        _pinned = sessions[(at + 1) % sessions.Count].SourceAppUserModelId;
        _switching = true;
        Attach();
    }

    // Any player starting or stopping can change which one to follow.
    void Reconsider()
    {
        if (_manager != null && Pick() is var next && next?.SourceAppUserModelId != _session?.SourceAppUserModelId) Attach();
    }

    public void TogglePlay() => _ = _session?.TryTogglePlayPauseAsync();
    public void Next() => _ = _session?.TrySkipNextAsync();
    public void Previous() => _ = _session?.TrySkipPreviousAsync();
    public void Seek(double fraction) => _ = _session?.TryChangePlaybackPositionAsync((_start + Duration * fraction).Ticks);

    void Attach()
    {
        foreach (var w in _watched) w.PlaybackInfoChanged -= OnAnyPlayback;
        _watched.Clear();
        foreach (var w in _manager!.GetSessions())
        {
            w.PlaybackInfoChanged += OnAnyPlayback;
            _watched.Add(w);
        }
        var next = Pick();
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnPropertiesChanged;
            _session.PlaybackInfoChanged -= OnSessionChanged;
            _session.TimelinePropertiesChanged -= OnSessionChanged;
        }
        _session = next;
        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnPropertiesChanged;
            _session.PlaybackInfoChanged += OnSessionChanged;
            _session.TimelinePropertiesChanged += OnSessionChanged;
        }
        _ = Refresh(properties: true);
    }

    void OnSessionChanged(GlobalSystemMediaTransportControlsSession s, object e) => ui.TryEnqueue(() => _ = Refresh(properties: false));
    void OnAnyPlayback(GlobalSystemMediaTransportControlsSession s, object e) => ui.TryEnqueue(Reconsider);
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
        // Announced only for a real new track: not a hover preview, and not the track that was already
        // playing coming back once a preview ends.
        if (newTrack && !MediaSource.IsPreview(s.SourceAppUserModelId, Title, Artist))
        {
            var key = Title + "\n" + Artist;
            if (!_primed || _switching) _realKey = key; // the user picked this player: no "now playing"
            else if (key != _realKey) _ = AnnounceLater(s, key);
            _switching = false;
        }
        _primed = true;
    }

    // Clips that follow each other (scrolling a feed, skipping fast) are announced once, for the one that stays.
    async Task AnnounceLater(GlobalSystemMediaTransportControlsSession s, string key)
    {
        await Task.Delay(1200);
        if (s != _session || key != _artKey || key == _realKey) return;
        _realKey = key;
        TrackChanged?.Invoke();
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
