using MidiKeys.Controls;
using MidiKeys.Pages;
using MidiKeys.Services;

namespace MidiKeys;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureMauiHandlers(handlers =>
                handlers.AddHandler<PianoKeyboard, PianoKeyboardHandler>());

#if ANDROID
        builder.Services.AddSingleton<IMidiOutput, AndroidMidiOutput>();
#elif WINDOWS
        builder.Services.AddSingleton<IMidiOutput, WindowsMidiOutput>();
#endif
        builder.Services.AddSingleton<VelocitySettings>();
        builder.Services.AddSingleton<StepSequencer>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<SequencerPage>();
        builder.Services.AddSingleton<ArpPage>();
        builder.Services.AddSingleton<AppShell>();

        return builder.Build();
    }
}
