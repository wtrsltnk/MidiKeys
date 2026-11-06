namespace MidiKeys.Controls;

/// <summary>Pressed-key colours: pale for a soft touch, saturated for a hard one.</summary>
static class KeyColors
{
    static readonly (byte R, byte G, byte B) WhiteSoft = (205, 222, 250), WhiteHard = (35, 95, 235);
    static readonly (byte R, byte G, byte B) BlackSoft = (70, 80, 110), BlackHard = (125, 180, 255);

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
