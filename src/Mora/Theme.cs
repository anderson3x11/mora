using Windows.UI;

namespace Mora;

// Mora's look, the MacBook notch: pure black, grown out of the top edge with curved shoulders, tight corners,
// Apple's system colors, a lively spring and hairline progress.
sealed record Theme(
    Func<double, double> Radius,  // corner radius from the island's natural one (round ends, at most 26)
    (double K, double C) Open,    // springs: stiffness and damping
    (double K, double C) Close,
    Color Surface,
    Color Fg, Color Muted, Color Line, Color Raised,
    Color Ink, Color InkText,     // waveform, progress and the main button; text on that button
    Color Warm, Color Calm,       // a running timer; a break
    double Track)                 // thickness of progress and level bars
{
    static Color Hex(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    public static readonly Theme Notch = new(Radius: r => Math.Round(r * 0.62), Open: (380, 24), Close: (560, 44),
        Surface: Hex(0xFF000000),
        Fg: Hex(0xFFFFFFFF), Muted: Hex(0xFF98989D), Line: Hex(0xFF38383A), Raised: Hex(0xFF1C1C1E),
        Ink: Hex(0xFFFFFFFF), InkText: Hex(0xFF000000), Warm: Hex(0xFFFF9F0A), Calm: Hex(0xFF30D158),
        Track: 3);
}
