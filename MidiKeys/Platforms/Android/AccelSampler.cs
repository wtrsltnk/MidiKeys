using Android.Content;
using Android.Hardware;
using Android.OS;

namespace MidiKeys.Controls;

/// <summary>
/// Samples the accelerometer as fast as possible, filters out gravity (high-pass)
/// and keeps the last ~0.5 s so we can look up the impact peak of a tap afterwards.
/// Callbacks arrive on the main thread, the same thread as the touch events.
/// </summary>
sealed class AccelSampler : Java.Lang.Object, ISensorEventListener
{
    const int Capacity = 256;
    const float GravityAlpha = 0.9f;

    readonly SensorManager? _manager;
    readonly Sensor? _sensor;
    readonly long[] _times = new long[Capacity];
    readonly float[] _magnitudes = new float[Capacity];
    int _head, _count;
    float _gx, _gy, _gz;
    bool _hasGravity, _running;

    public AccelSampler(Context context)
    {
        _manager = context.GetSystemService(Context.SensorService) as SensorManager;
        _sensor = _manager?.GetDefaultSensor(SensorType.Accelerometer);
    }

    public bool IsAvailable => _sensor is not null;

    public void Start()
    {
        if (_running || _manager is null || _sensor is null) return;
        _running = _manager.RegisterListener(this, _sensor, SensorDelay.Fastest);
    }

    public void Stop()
    {
        if (!_running) return;
        _manager?.UnregisterListener(this);
        _running = false;
        _count = 0;
        _hasGravity = false;
    }

    /// <summary>Largest gravity-free acceleration between two uptime timestamps (ms).</summary>
    public float Peak(long fromMs, long toMs)
    {
        float peak = 0;
        for (int i = 0; i < _count; i++)
        {
            int idx = (_head - 1 - i + Capacity) % Capacity;
            long t = _times[idx];
            if (t < fromMs) break;           // older than the window: stop
            if (t <= toMs && _magnitudes[idx] > peak)
                peak = _magnitudes[idx];
        }
        return peak;
    }

    public void OnSensorChanged(SensorEvent? e)
    {
        if (e?.Values is not { Count: >= 3 } v) return;
        float x = v[0], y = v[1], z = v[2];

        if (!_hasGravity)
        {
            (_gx, _gy, _gz) = (x, y, z);
            _hasGravity = true;
        }
        _gx = GravityAlpha * _gx + (1 - GravityAlpha) * x;
        _gy = GravityAlpha * _gy + (1 - GravityAlpha) * y;
        _gz = GravityAlpha * _gz + (1 - GravityAlpha) * z;

        float hx = x - _gx, hy = y - _gy, hz = z - _gz;

        // Uptime ms on arrival: same time base as MotionEvent.EventTime.
        _times[_head] = SystemClock.UptimeMillis();
        _magnitudes[_head] = MathF.Sqrt(hx * hx + hy * hy + hz * hz);
        _head = (_head + 1) % Capacity;
        _count = Math.Min(_count + 1, Capacity);
    }

    public void OnAccuracyChanged(Sensor? sensor, SensorStatus accuracy) { }
}
