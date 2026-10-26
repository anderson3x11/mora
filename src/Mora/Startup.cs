using Microsoft.Win32;

namespace Mora;

// Start with Windows: a per-user Run entry pointing at the installed executable. No admin rights
// needed, and unchecking the option removes it. Only the installed copy touches the entry, so a
// development build never points Windows at a build folder.
static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static readonly string InstallFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Mora");

    public static void Apply(bool on)
    {
        var exe = Environment.ProcessPath!;
        if (!exe.StartsWith(InstallFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        run.DeleteValue("Atoll", throwOnMissingValue: false); // the app's former name
        if (on) run.SetValue("Mora", $"\"{exe}\"");
        else run.DeleteValue("Mora", throwOnMissingValue: false);
    }
}
