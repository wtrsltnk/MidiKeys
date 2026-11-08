namespace MidiKeys.Controls;

/// <summary>
/// Key colours in the TR-808 look; pressed keys turn orange: pale for a soft touch, saturated for a hard one.
/// Marked keys (e.g. the notes an arp plays) are light green.
/// </summary>
static class KeyColors
{
    public static readonly (byte R, byte G, byte B) White = (239, 233, 214), Black = (30, 30, 30);
    public static readonly (byte R, byte G, byte B) Border = (60, 60, 60), Text = (125, 121, 111);

    public static readonly (byte R, byte G, byte B) MarkedWhite = (190, 236, 168), MarkedBlack = (96, 168, 78);

    static readonly (byte R, byte G, byte B) WhiteSoft = (250, 218, 185), WhiteHard = (240, 125, 25);
    static readonly (byte R, byte G, byte B) BlackSoft = (95, 60, 30), BlackHard = (255, 150, 60);

    /// <param name="velocity">1–127, or null while the velocity is still being measured.</param>
    public static (byte R, byte G, byte B) Pressed(bool black, int? velocity)
    {
        double t = velocity is int v ? Math.Clamp((v - 1) / 126.0, 0, 1) : 0;
        var (soft, hard) = black ? (BlackSoft, BlackHard) : (WhiteSoft, WhiteHard);
        return (Lerp(soft.R, hard.R, t), Lerp(soft.G, hard.G, t), Lerp(soft.B, hard.B, t));
    }

    /// <summary>Strongest velocity of several touches on the same key (null = not measured yet).</summary>
    public static int? Strongest(int? a, int? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

    static byte Lerp(byte from, byte to, double t) => (byte)Math.Round(from + (to - from) * t);
}
