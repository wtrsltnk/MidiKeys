using MidiKeys.Controls;

namespace MidiKeys;

public class App : Application
{
    readonly AppShell _shell;

    public App(AppShell shell)
    {
        _shell = shell;
        Theme.Register(Resources);
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new(_shell) { Title = "MIDI Keys" };
}
