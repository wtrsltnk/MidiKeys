using Microsoft.UI.Dispatching;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Enumeration;
using Windows.Devices.Midi;

namespace MidiKeys.Services;

/// <summary>
/// Sends MIDI through Windows.Devices.Midi. A class-compliant USB MIDI cable shows up as an
/// output port. Windows device ids are strings; we hand out a stable int per id so the
/// shared <see cref="IMidiOutput"/> interface stays the same.
/// </summary>
public sealed class WindowsMidiOutput : IMidiOutput, IDisposable
{
    // Built-in software synth: handy for testing without a cable, so it is listed last.
    const string SoftwareSynthName = "Microsoft GS Wavetable Synth";

    readonly DeviceWatcher _watcher;
    readonly DispatcherQueue _ui;
    readonly object _lock = new();
    readonly Dictionary<string, int> _ids = new();               // Windows id -> our id
    readonly Dictionary<int, DeviceInformation> _devices = new(); // UI thread only

    IMidiOutPort? _port;
    int _nextId = 1;
    int _connectAttempt;
    bool _enumerated;

    public event EventHandler? DevicesChanged;
    public event EventHandler? ConnectionChanged;

    public bool IsSupported => true;
    public bool IsConnected { get { lock (_lock) return _port is not null; } }
    public int? ConnectedDeviceId { get; private set; }
    public string? ConnectedDeviceName { get; private set; }

    public WindowsMidiOutput()
    {
        // Watcher events arrive on thread-pool threads, and MainThread.BeginInvokeOnMainThread did
        // not deliver them from there. Hold on to the UI thread's queue instead (we're created on it).
        _ui = DispatcherQueue.GetForCurrentThread()
              ?? throw new InvalidOperationException($"{nameof(WindowsMidiOutput)} must be created on the UI thread.");

        _watcher = DeviceInformation.CreateWatcher(MidiOutPort.GetDeviceSelector());
        _watcher.Added += (_, info) => OnMain(() =>
        {
            _devices[IdOf(info.Id)] = info;
            // During the initial enumeration we report once, at EnumerationCompleted.
            if (_enumerated) DevicesChanged?.Invoke(this, EventArgs.Empty);
        });
        _watcher.Removed += (_, update) => OnMain(() =>
        {
            int id = IdOf(update.Id);
            _devices.Remove(id);
            if (id == ConnectedDeviceId)
                Disconnect();
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        });
        _watcher.Updated += (_, update) => OnMain(() =>
        {
            if (_devices.TryGetValue(IdOf(update.Id), out var info))
                info.Update(update);
        });
        _watcher.EnumerationCompleted += (_, _) => OnMain(() =>
        {
            _enumerated = true;
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        });
        _watcher.Start();
    }

    public IReadOnlyList<MidiOutputDevice> GetDevices() =>
        _devices
            .Select(kv => new MidiOutputDevice(kv.Key, kv.Value.Name))
            .OrderBy(d => d.Name == SoftwareSynthName)
            .ThenBy(d => d.Id)
            .ToList();

    public async Task<bool> ConnectAsync(int deviceId)
    {
        if (ConnectedDeviceId == deviceId && IsConnected)
            return true;

        Disconnect();

        if (!_devices.TryGetValue(deviceId, out var info))
            return false;

        int attempt = ++_connectAttempt;
        IMidiOutPort? port;
        try
        {
            port = await MidiOutPort.FromIdAsync(info.Id); // null when the port is in use or gone
        }
        catch (COMException)
        {
            port = null;
        }

        if (port is null)
            return false;
        if (attempt != _connectAttempt) // a newer connect started meanwhile
        {
            port.Dispose();
            return false;
        }

        lock (_lock)
            _port = port;
        ConnectedDeviceId = deviceId;
        ConnectedDeviceName = info.Name;
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Disconnect()
    {
        bool wasConnected;
        lock (_lock)
        {
            wasConnected = _port is not null;
            _port?.Dispose();
            _port = null;
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
                _port.SendBuffer(message.AsBuffer());
            }
            catch (COMException)
            {
                // Cable pulled mid-send; the watcher's Removed event will clean up.
            }
        }
    }

    public void Dispose()
    {
        _watcher.Stop();
        Disconnect();
    }

    int IdOf(string windowsId)
    {
        if (!_ids.TryGetValue(windowsId, out int id))
            _ids[windowsId] = id = _nextId++;
        return id;
    }

    void OnMain(Action action) => _ui.TryEnqueue(() => action());
}
