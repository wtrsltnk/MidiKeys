using MidiKeys.Controls;
using MidiKeys.Pages;
using MidiKeys.Services;

namespace MidiKeys;

/// <summary>
/// Hamburger menu (top left): Piano, then the list of devices the user added (1601 sequencers,
/// BQ-10s, Cirklon2s, DUOs, BeatStep Pro sequences and arps), each with the MIDI channel it plays on; + adds one.
/// </summary>
public class AppShell : Shell
{
    const string SequencerName = "1601 SEQUENCER";
    const string BqName = "BQ-10";
    const string CirklonName = "CIRKLON2";
    const string DuoName = "DUO";
    const string BspSeqName = "BSP SEQ";
    const string BspDrumName = "BSP DRUM";
    const string ArpName = "ARP";

    static readonly Color MenuBackground = Theme.PanelDark;
    static readonly Color Selected = Theme.Key;

    readonly Instruments _instruments;
    readonly MusicClock _clock;
    readonly IMidiOutput _midi;
    readonly VelocitySettings _velocity;
    readonly ShellItem _piano;
    readonly Dictionary<IInstrument, (ShellItem Item, Page Page)> _views = new();
    readonly VerticalStackLayout _menu = new() { Padding = new Thickness(0, 12) };

    public AppShell(MainPage piano, Instruments instruments, MusicClock clock, IMidiOutput midi, VelocitySettings velocity)
    {
        _instruments = instruments;
        _clock = clock;
        _midi = midi;
        _velocity = velocity;

        FlyoutBehavior = FlyoutBehavior.Flyout;
        FlyoutBackgroundColor = MenuBackground;
        // Menu on top; the MIDI device choice right at the bottom.
        var flyout = new Grid { RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        flyout.Add(new ScrollView { Content = _menu }, 0, 0);
        flyout.Add(new VerticalStackLayout
        {
            Children = { Line(), new MidiDevicePicker(midi) { Padding = new Thickness(20, 12, 12, 12) } },
        }, 0, 1);
        FlyoutContent = flyout;

        SetBackgroundColor(this, MenuBackground);
        SetForegroundColor(this, Theme.Silk);
        SetTitleColor(this, Theme.Silk);

        _piano = new ShellContent { Title = "Piano", Route = "piano", Content = piano };
        Items.Add(_piano);
        foreach (var instrument in instruments.All)
            AddPage(instrument);

        Navigated += (_, _) => BuildMenu();
        BuildMenu();
    }

    // ---------------------------------------------------------------- pages

    ShellItem AddPage(IInstrument instrument)
    {
        Page page = instrument switch
        {
            StepSequencer sequencer => new SequencerPage(sequencer, _clock),
            BqSequencer bq => new BqPage(bq, _clock),
            CirklonSequencer ck => new CirklonPage(ck, _clock),
            DuoSequencer duo => new DuoPage(duo, _clock),
            BeatStepSequence bsp => new BeatStepPage(bsp, _clock),
            Arpeggiator arp => new ArpPage(arp, _velocity, _clock),
            _ => throw new ArgumentException("Unknown instrument", nameof(instrument)),
        };
        ShellItem item = new ShellContent { Content = page };
        Items.Add(item);
        _views[instrument] = (item, page);
        UpdateTitles();
        return item;
    }

    void Remove(IInstrument instrument)
    {
        // Gone from the instruments first: leaving the page rebuilds the menu from that list.
        if (!_views.Remove(instrument, out var entry)) return;
        _instruments.Remove(instrument);
        if (CurrentItem == entry.Item)
            CurrentItem = _piano;
        Items.Remove(entry.Item);
        if (entry.Page is ArpPage arpPage)
            arpPage.Close();
        UpdateTitles();
        BuildMenu();
    }

    void Go(ShellItem item)
    {
        CurrentItem = item;
        FlyoutIsPresented = false;
    }

    /// <summary>Name in the menu: the kind, numbered when a channel has more than one of it.</summary>
    string Name(IInstrument instrument)
    {
        string kind = instrument switch { StepSequencer => SequencerName, BqSequencer => BqName, CirklonSequencer => CirklonName, DuoSequencer => DuoName, BeatStepSeq => BspSeqName, BeatStepDrum => BspDrumName, _ => ArpName };
        var same = _instruments.All.Where(i => i.Channel == instrument.Channel && i.GetType() == instrument.GetType()).ToList();
        return same.Count > 1 ? $"{kind} {same.IndexOf(instrument) + 1}" : kind;
    }

    void UpdateTitles()
    {
        foreach (var (instrument, (item, page)) in _views)
            item.Title = page.Title = $"{Name(instrument)} (ch{instrument.Channel + 1})";
    }

    /// <summary>+ in the menu: choose the device, then the channel it plays on.</summary>
    async Task AddDevice()
    {
        string? choice = await DisplayActionSheetAsync("Apparaat toevoegen", "Annuleren", null, SequencerName, BqName, CirklonName, DuoName, BspSeqName, BspDrumName, ArpName);
        if (choice is null or "Annuleren") return;

        // Every channel can be chosen; the devices already on it are shown behind it.
        var options = Enumerable.Range(0, 16).Select(ch =>
        {
            var on = _instruments.All.Where(i => i.Channel == ch).Select(Name).ToList();
            return on.Count == 0 ? $"Kanaal {ch + 1}" : $"Kanaal {ch + 1} ({string.Join(", ", on)})";
        }).ToArray();
        int channel = Array.IndexOf(options, await DisplayActionSheetAsync($"{choice}: MIDI-kanaal", "Annuleren", null, options));
        if (channel < 0) return;

        IInstrument? added = choice switch
        {
            SequencerName => _instruments.AddSequencer(channel),
            BqName => _instruments.AddBq(channel),
            CirklonName => _instruments.AddCirklon(channel),
            DuoName => _instruments.AddDuo(channel),
            BspSeqName => _instruments.AddBeatStepSeq(channel),
            BspDrumName => _instruments.AddBeatStepDrum(channel),
            ArpName => _instruments.AddArp(channel),
            _ => null,
        };
        if (added is not null)
            Go(AddPage(added));
        BuildMenu();
    }

    // ---------------------------------------------------------------- menu

    static BoxView Line() => new() { HeightRequest = 1, Color = Theme.Line, Margin = new Thickness(12, 0) };

    void BuildMenu()
    {
        _menu.Children.Clear();
        _menu.Children.Add(MenuEntry("Piano", _piano));

        _menu.Children.Add(Header("Apparaten", AddDevice));
        foreach (var instrument in _instruments.All)
            _menu.Children.Add(MenuEntry(Name(instrument), _views[instrument].Item, () => Remove(instrument), $"ch {instrument.Channel + 1}"));
    }

    /// <param name="remove">Shows a trash can that removes the entry (after asking).</param>
    /// <param name="detail">Shown dimmer on the right, e.g. the MIDI channel.</param>
    View MenuEntry(string text, ShellItem target, Action? remove = null, string? detail = null)
    {
        var label = new Label { Text = text, FontSize = 16, TextColor = Theme.Silk, Padding = new Thickness(20, 12) };
        var row = new Grid
        {
            BackgroundColor = CurrentItem == target ? Selected : Colors.Transparent,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) },
        };
        row.Add(label, 0, 0);
        var labels = new List<View> { label };
        if (detail is not null)
        {
            var channel = new Label
            {
                Text = detail,
                FontSize = 15,
                TextColor = Theme.SilkMid,
                VerticalOptions = LayoutOptions.Center,
                Padding = new Thickness(8, 12),
            };
            row.Add(channel, 1, 0);
            labels.Add(channel);
        }
        foreach (var view in labels)
        {
            // On the labels only, so a tap on the trash can doesn't navigate
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => Go(target);
            view.GestureRecognizers.Add(tap);
        }

        if (remove is not null)
        {
            var trash = new Button
            {
                Text = "🗑",
                FontSize = 16,
                WidthRequest = 44,
                HeightRequest = 36,
                Padding = 0,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalOptions = LayoutOptions.Center,
                BackgroundColor = Colors.Transparent,
                TextColor = Theme.SilkMid,
            };
            trash.Clicked += async (_, _) =>
            {
                if (await DisplayAlertAsync("Verwijderen", $"{target.Title} en de instellingen verwijderen?", "Verwijderen", "Annuleren"))
                    remove();
            };
            row.Add(trash, 2, 0);
        }
        return row;
    }

    /// <summary>Section title with a line above it and its + button on the right.</summary>
    static View Header(string text, Func<Task> add)
    {
        var header = new Grid
        {
            Margin = new Thickness(20, 8, 12, 4),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        header.Add(new Label
        {
            Text = text.ToUpperInvariant(),
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Theme.Orange,
            VerticalOptions = LayoutOptions.Center,
        }, 0, 0);
        header.Add(PlusButton(add), 1, 0);

        var line = Line();
        return new VerticalStackLayout { Margin = new Thickness(0, 12, 0, 0), Children = { line, header } };
    }

    static View PlusButton(Func<Task> add)
    {
        var button = new Button
        {
            Text = "+",
            FontSize = 18,
            WidthRequest = 44,
            HeightRequest = 36,
            Padding = 0,
            BackgroundColor = Selected,
            TextColor = Theme.Silk,
        };
        button.Clicked += async (_, _) => await add();
        return button;
    }
}
