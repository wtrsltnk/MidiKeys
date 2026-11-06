namespace MidiKeys.Services;

public sealed record MidiOutputDevice(int Id, string Name);

/// <summary>A MIDI destination (e.g. a USB-to-DIN cable) we can send bytes to.</summary>
public interface IMidiOutput
{
    bool IsSupported { get; }
    bool IsConnected { get; }
    int? ConnectedDeviceId { get; }
    string? ConnectedDeviceName { get; }

    /// <summary>Raised on the UI thread when a device is plugged in or removed.</summary>
    event EventHandler? DevicesChanged;

    /// <summary>Raised on the UI thread when we connect or lose the connection.</summary>
    event EventHandler? ConnectionChanged;

    IReadOnlyList<MidiOutputDevice> GetDevices();
    Task<bool> ConnectAsync(int deviceId);
    void Disconnect();
    void Send(byte[] message);
}

public static class MidiOutputExtensions
{
    public static void NoteOn(this IMidiOutput midi, int channel, int note, int velocity) =>
        midi.Send([(byte)(0x90 | (channel & 0x0F)), (byte)(note & 0x7F), (byte)Math.Clamp(velocity, 1, 127)]);

    public static void NoteOff(this IMidiOutput midi, int channel, int note) =>
        midi.Send([(byte)(0x80 | (channel & 0x0F)), (byte)(note & 0x7F), 0]);

    /// <summary>CC 123 – All Notes Off.</summary>
    public static void AllNotesOff(this IMidiOutput midi, int channel) =>
        midi.Send([(byte)(0xB0 | (channel & 0x0F)), 123, 0]);
}
