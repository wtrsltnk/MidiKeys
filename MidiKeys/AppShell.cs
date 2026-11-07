using MidiKeys.Pages;

namespace MidiKeys;

/// <summary>Hamburger menu (top left) with the app's views.</summary>
public class AppShell : Shell
{
    public AppShell(MainPage piano, SequencerPage sequencer, ArpPage arp)
    {
        FlyoutBehavior = FlyoutBehavior.Flyout;

        SetBackgroundColor(this, Color.FromArgb("#181825"));
        SetForegroundColor(this, Colors.White);
        SetTitleColor(this, Colors.White);

        Items.Add(new ShellContent { Title = "Piano", Route = "piano", Content = piano });
        Items.Add(new ShellContent { Title = "Sequencer", Route = "sequencer", Content = sequencer });
        Items.Add(new ShellContent { Title = "Arp", Route = "arp", Content = arp });
    }
}
