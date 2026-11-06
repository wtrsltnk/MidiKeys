using Android.Content;
using Android.Content.PM;
using Android.Media.Midi;

#pragma warning disable CA1422, CS0618 // GetDevices/RegisterDeviceCallback are deprecated on API 33+ but still work and cover API 23+

namespace MidiKeys.Services;

/// <summary>
/// Sends MIDI through android.media.midi. A class-compliant USB-C MIDI cable shows up
/// here as a device with one "input port" (input from the device's point of view = where we write).
/// </summary>
public sealed class AndroidMidiOutput : IMidiOutput, IDisposable
{
    readonly MidiManager? _manager;
    readonly Android.OS.Handler _handler = new(Android.OS.Looper.MainLooper!);
    readonly DeviceCallbackImpl? _callback;
    readonly object _lock = new();

    MidiDevice? _device;
    MidiInputPort? _port;

    public event EventHandler? DevicesChanged;
    public event EventHandler? ConnectionChanged;

    public bool IsSupported => _manager is not null;
    public bool IsConnected { get { lock (_lock) return _port is not null; } }
    public int? ConnectedDeviceId { get; private set; }
    public string? ConnectedDeviceName { get; private set; }

    public AndroidMidiOutput()
    {
        var context = Android.App.Application.Context;
        if (context.PackageManager?.HasSystemFeature(PackageManager.FeatureMidi) != true)
            return;

        _manager = context.GetSystemService(Context.MidiService) as MidiManager;
        if (_manager is null)
            return;

        _callback = new DeviceCallbackImpl(this);
        _manager.RegisterDeviceCallback(_callback, _handler);
    }

    public IReadOnlyList<MidiOutputDevice> GetDevices()
    {
        if (_manager is null)
            return [];

        return (_manager.GetDevices() ?? [])
            .Where(info => info.InputPortCount > 0)
            .Select(info => new MidiOutputDevice(info.Id, NameOf(info)))
            .ToList();
    }

    public Task<bool> ConnectAsync(int deviceId)
    {
        if (_manager is null)
            return Task.FromResult(false);

        if (ConnectedDeviceId == deviceId && IsConnected)
            return Task.FromResult(true);

        Disconnect();

        var info = _manager.GetDevices()?.FirstOrDefault(i => i.Id == deviceId && i.InputPortCount > 0);
        if (info is null)
            return Task.FromResult(false);

        var tcs = new TaskCompletionSource<bool>();
        _manager.OpenDevice(info, new OpenedListener(device =>
        {
            var port = device?.OpenInputPort(0);
            if (device is null || port is null)
            {
                device?.Close();
                tcs.TrySetResult(false);
                return;
            }

            lock (_lock)
            {
                _device = device;
                _port = port;
            }
            ConnectedDeviceId = info.Id;
            ConnectedDeviceName = NameOf(info);
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
            tcs.TrySetResult(true);
        }), _handler);

        return tcs.Task;
    }

    public void Disconnect()
    {
        bool wasConnected;
        lock (_lock)
        {
            wasConnected = _port is not null;
            try { _port?.Close(); } catch (Java.IO.IOException) { }
            try { _device?.Close(); } catch (Java.IO.IOException) { }
            _port?.Dispose();
            _device?.Dispose();
            _port = null;
            _device = null;
        }
        ConnectedDeviceId = null;
        ConnectedDeviceName = null;
        if (wasConnected)
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Send(byte[] message)
    {
        lock (_lock)
        {
            if (_port is null)
                return;
            try
            {
                _port.Send(message, 0, message.Length);
            }
            catch (Java.IO.IOException)
            {
                // Cable pulled mid-send; OnDeviceRemoved will clean up.
            }
        }
    }

    public void Dispose()
    {
        Disconnect();
        if (_callback is not null)
            _manager?.UnregisterDeviceCallback(_callback);
    }

    void OnDeviceAdded(MidiDeviceInfo? info) => DevicesChanged?.Invoke(this, EventArgs.Empty);

    void OnDeviceRemoved(MidiDeviceInfo? info)
    {
        if (info is not null && info.Id == ConnectedDeviceId)
            Disconnect();
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    static string NameOf(MidiDeviceInfo info)
    {
        var props = info.Properties;
        var name = props?.GetString(MidiDeviceInfo.PropertyName);
        if (string.IsNullOrWhiteSpace(name))
            name = props?.GetString(MidiDeviceInfo.PropertyProduct);
        return string.IsNullOrWhiteSpace(name) ? $"MIDI-apparaat {info.Id}" : name;
    }

    sealed class OpenedListener(Action<MidiDevice?> onOpened)
        : Java.Lang.Object, MidiManager.IOnDeviceOpenedListener
    {
        public void OnDeviceOpened(MidiDevice? device) => onOpened(device);
    }

    sealed class DeviceCallbackImpl(AndroidMidiOutput owner) : MidiManager.DeviceCallback
    {
        public override void OnDeviceAdded(MidiDeviceInfo? device) => owner.OnDeviceAdded(device);
        public override void OnDeviceRemoved(MidiDeviceInfo? device) => owner.OnDeviceRemoved(device);
    }
}
