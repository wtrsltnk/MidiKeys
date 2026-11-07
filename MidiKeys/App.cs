namespace MidiKeys;

public class App(AppShell shell) : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
        => new(shell) { Title = "MIDI Keys" };
}
