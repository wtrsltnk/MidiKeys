namespace MidiKeys;

public class App(MainPage mainPage) : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
        => new(mainPage) { Title = "MIDI Keys" };
}
