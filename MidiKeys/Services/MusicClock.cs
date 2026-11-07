using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MidiKeys.Services;

/// <summary>Something that plays in time with the shared clock (sequencer, arpeggiator).</summary>
public interface IClockClient
{
    /// <summary>Clock thread, <see cref="MusicClock.Ppqn"/> times per quarter note.</summary>
    void Tick(long tick, double now, double tickMs);

    /// <summary>Clock thread, between ticks: send the note-offs that are due.</summary>
    void ReleaseDue(double now);

    /// <summary>Time of the next pending note-off, or +∞.</summary>
    double NextRelease { get; }

    /// <summary>The client left the clock: silence everything it still holds.</summary>
    void ReleaseAll();
}

/// <summary>
/// One tempo and one timing thread for everything that plays in time, so the sequencer and the
/// arpeggiator stay in step. The thread only runs while a client has joined; a client that joins
/// a running clock falls in on the existing grid.
/// </summary>
public sealed class MusicClock
{
    public const int Ppqn = 24;

    readonly object _lock = new();
    readonly List<IClockClient> _clients = new();
    readonly Stopwatch _time = Stopwatch.StartNew();

    Thread? _thread;
    long _tick;
    double _nextTick;
    volatile int _bpm = 120;

    public MusicClock()
    {
        var p = Preferences.Default;
        Bpm = p.Get("clock.bpm", p.Get("seq.bpm", Bpm)); // the tempo used to belong to the sequencer
    }

    /// <summary>30–300 BPM.</summary>
    public int Bpm
    {
        get => _bpm;
        set
        {
            value = Math.Clamp(value, 30, 300);
            if (_bpm == value) return;
            _bpm = value;
            TempoChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised on the thread that set <see cref="Bpm"/>.</summary>
    public event EventHandler? TempoChanged;

    public void Save() => Preferences.Default.Set("clock.bpm", Bpm);

    public void Join(IClockClient client)
    {
        lock (_lock)
        {
            if (_clients.Contains(client)) return;
            _clients.Add(client);
            if (_thread is not null) return;

            _tick = 0;
            _nextTick = Now;
            _thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "Clock" };
            _thread.Start();
        }
    }

    public void Leave(IClockClient client)
    {
        lock (_lock)
        {
            if (_clients.Remove(client))
                client.ReleaseAll();
        }
    }

    double Now => _time.Elapsed.TotalMilliseconds;

    void Run()
    {
        TimerResolution(true);
        try
        {
            while (true)
            {
                double nextEvent;
                lock (_lock)
                {
                    if (_clients.Count == 0)
                    {
                        _thread = null;
                        return;
                    }

                    double now = Now;
                    foreach (var c in _clients)
                        c.ReleaseDue(now);

                    if (now >= _nextTick)
                    {
                        double tickMs = 60000.0 / _bpm / Ppqn;
                        foreach (var c in _clients)
                            c.Tick(_tick, now, tickMs);
                        _tick++;
                        _nextTick += tickMs;
                        if (_nextTick < now) _nextTick = now + tickMs; // fell behind (debugger, sleep): don't rush to catch up
                    }

                    nextEvent = _nextTick;
                    foreach (var c in _clients)
                        nextEvent = Math.Min(nextEvent, c.NextRelease);
                }

                double wait = nextEvent - Now;
                if (wait > 3) Thread.Sleep(1);
                else if (wait > 0) Thread.Yield();
            }
        }
        finally
        {
            TimerResolution(false);
        }
    }

    // Windows sleeps in ~15 ms slices by default; ask for 1 ms while running.
    static void TimerResolution(bool fine)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (fine) TimeBeginPeriod(1); else TimeEndPeriod(1);
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    static extern uint TimeBeginPeriod(uint ms);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    static extern uint TimeEndPeriod(uint ms);
}

/// <summary>Notes that are on, with the time their note-off is due. Clock thread only.</summary>
public sealed class SoundingNotes(IMidiOutput midi)
{
    readonly record struct Sounding(int Channel, int Note, double OffAt, bool Legato);

    readonly List<Sounding> _notes = new();

    public double NextRelease => _notes.Count > 0 ? _notes.Min(s => s.OffAt) : double.PositiveInfinity;

    public void Play(int channel, int note, int velocity, double offAt, bool legato = false)
    {
        // Still on (e.g. the tempo jumped): end it first so the new note really retriggers.
        int existing = _notes.FindIndex(s => s.Channel == channel && s.Note == note);
        if (existing >= 0)
        {
            midi.NoteOff(channel, note);
            _notes.RemoveAt(existing);
        }

        midi.NoteOn(channel, note, velocity);
        _notes.Add(new Sounding(channel, note, offAt, legato));
    }

    /// <summary>The previous note was legato on this same note: tie instead of retriggering.</summary>
    public bool TryTie(int channel, int note, double offAt, bool legato)
    {
        int tied = _notes.FindIndex(s => s.Channel == channel && s.Note == note && s.Legato);
        if (tied < 0) return false;
        _notes[tied] = new Sounding(channel, note, offAt, legato);
        return true;
    }

    public void ReleaseDue(double now)
    {
        for (int i = _notes.Count - 1; i >= 0; i--)
        {
            if (_notes[i].OffAt > now) continue;
            midi.NoteOff(_notes[i].Channel, _notes[i].Note);
            _notes.RemoveAt(i);
        }
    }

    public void ReleaseAll()
    {
        foreach (var s in _notes)
            midi.NoteOff(s.Channel, s.Note);
        _notes.Clear();
    }
}
