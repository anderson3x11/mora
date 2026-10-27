using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Mora;

// Other apps' notifications (Discord, Claude, Windows...), read from the notification center. The apps the user
// picks show in the island instead, and Windows' own banner is turned off for them, the same switch as in
// Windows Settings; they still land in the notification center.
// ponytail: polled every second, since the NotificationChanged event needs a packaged app.
sealed class Notifications
{
    public record Toast(string App, string AppId, string Title, string Body, BitmapImage? Logo);

    public static readonly Dictionary<string, string> Seen = []; // app id → name, for the settings list
    public event Action<Toast>? Arrived;
    public static bool Allowed { get; private set; }

    readonly HashSet<uint> _known = [];
    readonly DispatcherQueueTimer _poll;
    bool _primed, _busy;

    readonly Func<string, bool> _wanted; // by app id: the others are only listed

    public Notifications(DispatcherQueue ui, Func<string, bool> wanted)
    {
        _wanted = wanted;
        _poll = ui.CreateTimer();
        _poll.Interval = TimeSpan.FromSeconds(1);
        _poll.Tick += async (_, _) => await Poll();
    }

    public async Task StartAsync()
    {
        try { Allowed = await UserNotificationListener.Current.RequestAccessAsync() == UserNotificationListenerAccessStatus.Allowed; }
        catch (Exception) { Allowed = false; } // no notification platform (Windows N, policies)
        if (Allowed) _poll.Start();
    }

    async Task Poll()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var list = await UserNotificationListener.Current.GetNotificationsAsync(NotificationKinds.Toast);
            var ids = new HashSet<uint>();
            foreach (var n in list)
            {
                ids.Add(n.Id);
                Seen.TryAdd(n.AppInfo.AppUserModelId, n.AppInfo.DisplayInfo.DisplayName);
                if (!_primed || _known.Contains(n.Id) || !_wanted(n.AppInfo.AppUserModelId)) continue;
                var texts = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements().Select(t => t.Text).ToList() ?? [];
                BitmapImage? logo = null;
                try
                {
                    logo = new BitmapImage { DecodePixelHeight = 64 };
                    await logo.SetSourceAsync(await n.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(64, 64)).OpenReadAsync());
                }
                catch (Exception) { logo = null; } // no logo: the bell
                Arrived?.Invoke(new Toast(n.AppInfo.DisplayInfo.DisplayName, n.AppInfo.AppUserModelId,
                    texts.FirstOrDefault() ?? "", string.Join(" · ", texts.Skip(1)), logo));
            }
            _known.Clear();
            _known.UnionWith(ids); // what was there at startup is old news
            _primed = true;
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { }
        finally { _busy = false; }
    }

    // Windows' banner for an app: off while it goes to the island, back to Windows' default otherwise.
    public static void SetBanner(string appId, bool show)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\{appId}");
        if (show) key.DeleteValue("ShowBanner", throwOnMissingValue: false);
        else key.SetValue("ShowBanner", 0, RegistryValueKind.DWord);
    }
}
