using Windows.UI;
using Windows.UI.ViewManagement;

namespace Atoll;

// A theme is a whole look, not a palette: the island's shape (floating, or grown out of the top edge of the
// screen), its corners, surface and border, how it moves, and parts of the layout.
sealed record Theme(
    string Name,
    string Description,
    bool Attached,                // grows out of the top edge: no gap, square top, curved shoulders into the edge
    double Top,                   // gap above the island when it floats
    Func<double, double> Radius,  // corner radius from the island's natural one (round ends, at most 26)
    (double K, double C) Open,    // springs: stiffness and damping
    (double K, double C) Close,
    Color Surface,
    Color? Border,
    Color Fg, Color Muted, Color Line, Color Raised,
    Color Ink, Color InkText,     // waveform, progress and the main button; text on that button
    Color Warm, Color Calm,       // a running timer; a break
    bool IconTabs,                // tabs show their icon only
    double Track)                 // thickness of progress and level bars
{
    static Color Hex(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    // Atoll's own: a black pill floating just below the edge, round ends, a springy entrance.
    public static readonly Theme Island = new("Island", "A black pill floating just below the top of the screen.",
        Attached: false, Top: 8, Radius: r => r, Open: (340, 27), Close: (520, 46),
        Surface: Hex(0xFF08080A), Border: null,
        Fg: Hex(0xFFF4F4F6), Muted: Hex(0xFF9B9BA5), Line: Hex(0xFF2C2C33), Raised: Hex(0xFF1C1C21),
        Ink: Hex(0xFFFFFFFF), InkText: Hex(0xFF08080A), Warm: Hex(0xFFFFB454), Calm: Hex(0xFF5BD68A),
        IconTabs: false, Track: 5);

    // The MacBook notch: pure black, grown out of the top edge with curved shoulders, tighter corners,
    // Apple's system colors, a livelier spring, icon tabs and hairline progress.
    public static readonly Theme Notch = new("Notch", "Grows out of the top edge like a MacBook notch, with Apple's colors and motion.",
        Attached: true, Top: 0, Radius: r => Math.Round(r * 0.62), Open: (380, 24), Close: (560, 44),
        Surface: Hex(0xFF000000), Border: null,
        Fg: Hex(0xFFFFFFFF), Muted: Hex(0xFF98989D), Line: Hex(0xFF38383A), Raised: Hex(0xFF1C1C1E),
        Ink: Hex(0xFFFFFFFF), InkText: Hex(0xFF000000), Warm: Hex(0xFFFF9F0A), Calm: Hex(0xFF30D158),
        IconTabs: true, Track: 3);

    // Windows 11: a flyout-like card with small corners and a hairline border, light or dark like Windows,
    // the Windows accent color, and motion that settles without bouncing.
    public static Theme Fluent()
    {
        var ui = new UISettings();
        bool light = ui.GetColorValue(UIColorType.Background).R > 128;
        var accent = ui.GetColorValue(light ? UIColorType.AccentDark1 : UIColorType.AccentLight2);
        return light
            ? new("Fluent", "A Windows 11 card: follows light or dark mode and your accent color.",
                Attached: false, Top: 10, Radius: _ => 8, Open: (600, 49), Close: (800, 57),
                Surface: Hex(0xFFF9F9F9), Border: Hex(0x14000000),
                Fg: Hex(0xFF1B1B1B), Muted: Hex(0xFF5F5F5F), Line: Hex(0xFFE0E0E0), Raised: Hex(0xFFEDEDED),
                Ink: accent, InkText: Hex(0xFFFFFFFF), Warm: Hex(0xFF9D5D00), Calm: Hex(0xFF0F7B0F),
                IconTabs: false, Track: 4)
            : new("Fluent", "A Windows 11 card: follows light or dark mode and your accent color.",
                Attached: false, Top: 10, Radius: _ => 8, Open: (600, 49), Close: (800, 57),
                Surface: Hex(0xFF2C2C2C), Border: Hex(0x18FFFFFF),
                Fg: Hex(0xFFFFFFFF), Muted: Hex(0xFFC5C5C5), Line: Hex(0xFF454545), Raised: Hex(0xFF383838),
                Ink: accent, InkText: Hex(0xFF000000), Warm: Hex(0xFFFCE100), Calm: Hex(0xFF6CCB5F),
                IconTabs: false, Track: 4);
    }

    public static IEnumerable<Theme> All => [Island, Notch, Fluent()];

    public static Theme Named(string name) => All.FirstOrDefault(t => t.Name == name) ?? Island;
}
