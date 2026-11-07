namespace MidiKeys.Services;

/// <summary>
/// The sequencers and arps the user added, each on its own MIDI channel: a channel belongs to at
/// most one of them. The list is stored, so they come back on the next start.
/// </summary>
public sealed class Instruments
{
    readonly IMidiOutput _midi;
    readonly MusicClock _clock;
    readonly List<StepSequencer> _sequencers = new();
    readonly List<Arpeggiator> _arps = new();

    public Instruments(IMidiOutput midi, MusicClock clock)
    {
        _midi = midi;
        _clock = clock;

        // "s0,a2": sequencer on channel 1, arp on channel 3.
        foreach (var entry in Preferences.Default.Get("instruments", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (entry.Length < 2 || !int.TryParse(entry[1..], out int channel) || channel is < 0 or > 15 || !FreeChannels.Contains(channel))
                continue;
            if (entry[0] == 's') _sequencers.Add(new StepSequencer(midi, clock, channel));
            else if (entry[0] == 'a') _arps.Add(new Arpeggiator(midi, clock, channel));
        }
        Sort();
    }

    /// <summary>Sorted by channel.</summary>
    public IReadOnlyList<StepSequencer> Sequencers => _sequencers;

    /// <summary>Sorted by channel.</summary>
    public IReadOnlyList<Arpeggiator> Arps => _arps;

    /// <summary>Channels (0–15) not used by a sequencer or an arp.</summary>
    public IReadOnlyList<int> FreeChannels =>
        Enumerable.Range(0, 16).Except(_sequencers.Select(s => s.Channel)).Except(_arps.Select(a => a.Channel)).ToList();

    public StepSequencer AddSequencer(int channel)
    {
        var sequencer = new StepSequencer(_midi, _clock, channel);
        _sequencers.Add(sequencer);
        Sort();
        Save();
        return sequencer;
    }

    public Arpeggiator AddArp(int channel)
    {
        var arp = new Arpeggiator(_midi, _clock, channel);
        _arps.Add(arp);
        Sort();
        Save();
        return arp;
    }

    public void Remove(StepSequencer sequencer)
    {
        sequencer.Stop();
        sequencer.Forget();
        _sequencers.Remove(sequencer);
        Save();
    }

    public void Remove(Arpeggiator arp)
    {
        arp.Stop();
        arp.ReleaseKeys();
        arp.Forget();
        _arps.Remove(arp);
        Save();
    }

    void Sort()
    {
        _sequencers.Sort((a, b) => a.Channel.CompareTo(b.Channel));
        _arps.Sort((a, b) => a.Channel.CompareTo(b.Channel));
    }

    void Save() => Preferences.Default.Set("instruments",
        string.Join(',', _sequencers.Select(s => $"s{s.Channel}").Concat(_arps.Select(a => $"a{a.Channel}"))));
}
