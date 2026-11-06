using MidiKeys.Controls;
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
        builder.Services.AddSingleton<MainPage>();

        return builder.Build();
    }
}
