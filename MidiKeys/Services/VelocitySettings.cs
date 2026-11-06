namespace MidiKeys.Services;

public enum VelocityMode
{
    Fixed,          // value from the slider
    Position,       // lower on the key = louder
    ContactSize,    // larger contact area = louder (finger flattens when pressed harder)
    Accelerometer,  // tap impact on the device
    Auto,           // average of the calibrated, usable sources; otherwise Position
    Calibrate,      // internal: measure everything, don't map
}

/// <summary>Medians of the soft and hard calibration taps per measurement source.</summary>
public sealed record VelocityCalibration(
    double SoftSize, double HardSize, bool SizeUsable,
    double SoftAccel, double HardAccel, bool AccelUsable,
    bool IsCalibrated)
{
    public static readonly VelocityCalibration Default = new(8, 14, false, 0.5, 4, false, false);
}

public sealed record VelocityConfig(
    VelocityMode Mode,
    int FixedVelocity,
    int MinVelocity,
    int MaxVelocity,
    int WindowMs,
    VelocityCalibration Calibration)
{
    public static readonly VelocityConfig Default = new(VelocityMode.Auto, 100, 20, 127, 20, VelocityCalibration.Default);
}

/// <summary>Persists the velocity settings in Preferences.</summary>
public sealed class VelocitySettings
{
    public VelocityConfig Config { get; private set; } = Load();

    public event EventHandler? Changed;

    public void Update(VelocityConfig config)
    {
        Config = config;
        Save(config);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    static VelocityConfig Load()
    {
        var p = Preferences.Default;
        var d = VelocityConfig.Default;
        var dc = d.Calibration;

        var cal = new VelocityCalibration(
            p.Get("cal.softSize", dc.SoftSize), p.Get("cal.hardSize", dc.HardSize), p.Get("cal.sizeUsable", dc.SizeUsable),
            p.Get("cal.softAccel", dc.SoftAccel), p.Get("cal.hardAccel", dc.HardAccel), p.Get("cal.accelUsable", dc.AccelUsable),
            p.Get("cal.done", dc.IsCalibrated));

        var mode = (VelocityMode)p.Get("vel.mode", (int)d.Mode);
        if (mode == VelocityMode.Calibrate) mode = d.Mode;

        return new VelocityConfig(
            mode,
            p.Get("vel.fixed", d.FixedVelocity),
            p.Get("vel.min", d.MinVelocity),
            p.Get("vel.max", d.MaxVelocity),
            p.Get("vel.window", d.WindowMs),
            cal);
    }

    static void Save(VelocityConfig c)
    {
        var p = Preferences.Default;
        p.Set("vel.mode", (int)c.Mode);
        p.Set("vel.fixed", c.FixedVelocity);
        p.Set("vel.min", c.MinVelocity);
        p.Set("vel.max", c.MaxVelocity);
        p.Set("vel.window", c.WindowMs);

        var cal = c.Calibration;
        p.Set("cal.softSize", cal.SoftSize);
        p.Set("cal.hardSize", cal.HardSize);
        p.Set("cal.sizeUsable", cal.SizeUsable);
        p.Set("cal.softAccel", cal.SoftAccel);
        p.Set("cal.hardAccel", cal.HardAccel);
        p.Set("cal.accelUsable", cal.AccelUsable);
        p.Set("cal.done", cal.IsCalibrated);
    }
}

/// <summary>Maps the measured values of one touch to a MIDI velocity according to the config.</summary>
public static class VelocityMath
{
    /// <param name="size">Largest contact diameter in the measurement window.</param>
    /// <param name="accel">Accelerometer peak (0 where there is no accelerometer).</param>
    /// <param name="position">0 = top of the key, 1 = bottom.</param>
    public static int Compute(VelocityConfig c, double size, double accel, double position)
    {
        var cal = c.Calibration;

        double? amount = c.Mode switch
        {
            VelocityMode.Fixed or VelocityMode.Calibrate => null,
            VelocityMode.Position => position,
            VelocityMode.ContactSize => Normalize(size, cal.SoftSize, cal.HardSize),
            VelocityMode.Accelerometer => Normalize(accel, cal.SoftAccel, cal.HardAccel),
            _ => AutoAmount(cal, size, accel, position),
        };

        if (amount is not double a)
            return Math.Clamp(c.FixedVelocity, 1, 127);

        int min = Math.Clamp(Math.Min(c.MinVelocity, c.MaxVelocity), 1, 127);
        int max = Math.Clamp(Math.Max(c.MinVelocity, c.MaxVelocity), 1, 127);
        return (int)Math.Round(min + (max - min) * a);
    }

    static double AutoAmount(VelocityCalibration cal, double size, double accel, double position)
    {
        double sum = 0;
        int n = 0;
        if (cal.SizeUsable) { sum += Normalize(size, cal.SoftSize, cal.HardSize); n++; }
        if (cal.AccelUsable) { sum += Normalize(accel, cal.SoftAccel, cal.HardAccel); n++; }
        return n > 0 ? sum / n : position;
    }

    static double Normalize(double value, double soft, double hard) =>
        Math.Abs(hard - soft) < 1e-6 ? 0.5 : Math.Clamp((value - soft) / (hard - soft), 0, 1);
}

public static class CalibrationMath
{
    /// <summary>
    /// A source is usable when the hard taps are clearly larger than the soft ones:
    /// the median difference must exceed the spread within the groups.
    /// </summary>
    public static (double Soft, double Hard, bool Usable) Evaluate(IEnumerable<double> soft, IEnumerable<double> hard)
    {
        var s = soft.ToArray();
        var h = hard.ToArray();
        if (s.Length == 0 || h.Length == 0)
            return (0, 0, false);

        double ms = Median(s), mh = Median(h);
        double spread = Math.Sqrt((Variance(s) + Variance(h)) / 2);
        double diff = mh - ms;
        bool usable = diff > 0 && diff > 1.2 * spread && diff > 1e-3 * Math.Max(Math.Abs(mh), 1);
        return (ms, mh, usable);
    }

    static double Median(double[] values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    static double Variance(double[] values)
    {
        double mean = values.Average();
        return values.Sum(v => (v - mean) * (v - mean)) / values.Length;
    }
}
