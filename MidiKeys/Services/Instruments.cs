namespace MidiKeys.Services;

/// <summary>A view the user added to a MIDI channel: a 1601 sequencer, a BQ-10, a Cirklon2, a DUO or an arp.</summary>
public interface IInstrument
{
    /// <summary>MIDI channel (0–15) it plays on.</summary>
    int Channel { get; }

    /// <summary>Number its settings are stored under; unique per kind of instrument.</summary>
    int Slot { get; }
}

/// <summary>
/// The views the user added, grouped per MIDI channel: a channel can have several sequencers,
/// BQ-10s, Cirklon2s, DUOs and arps. The list is stored, so they come back on the next start.
/// </summary>
public sealed class Instruments
{
    readonly IMidiOutput _midi;
    readonly MusicClock _clock;
    readonly List<IInstrument> _all = new();

    public Instruments(IMidiOutput midi, MusicClock clock)
    {
        _midi = midi;
        _clock = clock;

        // "s0@0,a2@0,b1@3": sequencer in slot 0 and arp in slot 2 on channel 1, BQ-10 in slot 1 on channel 4.
        // Older lists have no "@": the slot was the channel.
        foreach (var entry in Preferences.Default.Get("instruments", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry[1..].Split('@');
            if (!int.TryParse(parts[0], out int slot) || slot < 0)
                continue;
            int channel = slot;
            if (parts.Length > 1 && !int.TryParse(parts[1], out channel))
                continue;
            if (channel is < 0 or > 15)
                continue;

            if (entry[0] == 's' && !_all.OfType<StepSequencer>().Any(s => s.Slot == slot))
                _all.Add(new StepSequencer(midi, clock, channel, slot));
            else if (entry[0] == 'a' && !_all.OfType<Arpeggiator>().Any(a => a.Slot == slot))
                _all.Add(new Arpeggiator(midi, clock, channel, slot));
            else if (entry[0] == 'b' && !_all.OfType<BqSequencer>().Any(b => b.Slot == slot))
                _all.Add(new BqSequencer(midi, clock, channel, slot));
            else if (entry[0] == 'c' && !_all.OfType<CirklonSequencer>().Any(c => c.Slot == slot))
                _all.Add(new CirklonSequencer(midi, clock, channel, slot));
            else if (entry[0] == 'd' && !_all.OfType<DuoSequencer>().Any(d => d.Slot == slot))
                _all.Add(new DuoSequencer(midi, clock, channel, slot));
        }
        Sort();
    }

    /// <summary>Sorted by channel; within a channel in the order they were added.</summary>
    public IReadOnlyList<IInstrument> All => _all;

    /// <summary>Channels (0–15) that have at least one view.</summary>
    public IReadOnlyList<int> UsedChannels => _all.Select(i => i.Channel).Distinct().ToList();

    /// <summary>Channels (0–15) without a view yet.</summary>
    public IReadOnlyList<int> FreeChannels => Enumerable.Range(0, 16).Except(UsedChannels).ToList();

    public StepSequencer AddSequencer(int channel) =>
        Add(new StepSequencer(_midi, _clock, channel, FreeSlot<StepSequencer>()));

    public Arpeggiator AddArp(int channel) =>
        Add(new Arpeggiator(_midi, _clock, channel, FreeSlot<Arpeggiator>()));

    public BqSequencer AddBq(int channel) =>
        Add(new BqSequencer(_midi, _clock, channel, FreeSlot<BqSequencer>()));

    public CirklonSequencer AddCirklon(int channel) =>
        Add(new CirklonSequencer(_midi, _clock, channel, FreeSlot<CirklonSequencer>()));

    public DuoSequencer AddDuo(int channel) =>
        Add(new DuoSequencer(_midi, _clock, channel, FreeSlot<DuoSequencer>()));

    public void Remove(IInstrument instrument)
    {
        switch (instrument)
        {
            case StepSequencer sequencer:
                sequencer.Stop();
                sequencer.Forget();
                break;
            case BqSequencer bq:
                bq.Stop();
                bq.Forget();
                break;
            case CirklonSequencer ck:
                ck.Stop();
                ck.Forget();
                break;
            case DuoSequencer duo:
                duo.Stop();
                duo.Forget();
                break;
            case Arpeggiator arp:
                arp.Stop();
                arp.ReleaseKeys();
                arp.Forget();
                break;
        }
        _all.Remove(instrument);
        Save();
    }

    T Add<T>(T instrument) where T : IInstrument
    {
        _all.Add(instrument);
        Sort();
        Save();
        return instrument;
    }

    int FreeSlot<T>() where T : IInstrument
    {
        var used = _all.OfType<T>().Select(i => i.Slot).ToHashSet();
        int slot = 0;
        while (used.Contains(slot)) slot++;
        return slot;
    }

    void Sort()
    {
        var sorted = _all.OrderBy(i => i.Channel).ToList(); // stable: keeps the order within a channel
        _all.Clear();
        _all.AddRange(sorted);
    }

    void Save() => Preferences.Default.Set("instruments",
        string.Join(',', _all.Select(i => $"{Kind(i)}{i.Slot}@{i.Channel}")));

    static char Kind(IInstrument instrument) => instrument switch
    {
        StepSequencer => 's',
        BqSequencer => 'b',
        CirklonSequencer => 'c',
        DuoSequencer => 'd',
        _ => 'a',
    };
}
