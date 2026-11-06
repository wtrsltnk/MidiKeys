# MIDI Keys

A .NET MAUI app (Android) with a multitouch piano keyboard that sends MIDI over a
class-compliant USB-C MIDI cable (e.g. CME C2MIDI Pro) to a synth with 5-pin DIN.

## Features
- 2 octaves + top C, multitouch (chords, sliding across keys), haptic feedback
- Oct − / Oct + (C-1 … C9)
- Choose the MIDI channel (1–16)
- Velocity sensitivity (**Aanslag** button): fixed, position on the key, contact area,
  accelerometer, or automatic. Calibrate with 6 soft and 6 hard taps; per source the app
  decides whether your device can tell soft from hard.
- Connects automatically as soon as you plug in the cable; disconnects cleanly when you unplug it
- Sends note-off / All Notes Off when the app goes to the background (no hanging notes)

## Build & run
```bash
# Android
dotnet workload install maui-android
dotnet build -t:Run -f net10.0-android

# Windows (unpackaged, mouse / pen / touchscreen)
dotnet workload install maui-windows
dotnet run -f net10.0-windows10.0.19041.0
```
In Visual Studio choose **Windows Machine** or an Android device/emulator as the target.
Using .NET 9? Change `net10.0` to `net9.0` in `TargetFrameworks` in `MidiKeys.csproj`.

On Windows the built-in *Microsoft GS Wavetable Synth* is listed last, so you can test
without a cable (it has noticeable latency). There is no accelerometer on Windows; contact
area only works on touchscreens that report it.

On Windows you can also play with the PC keyboard, starting at the leftmost C on screen:
white keys `A S D F G H J K L ; '`, black keys `W E T Y U O P`. Keys play at the fixed velocity.

## Connecting
Cable **OUT** → synth **MIDI IN**. Set the synth to the same channel as the app (default 1).

## Structure
| File | Role |
|---|---|
| `Services/IMidiOutput.cs` | Interface + NoteOn/NoteOff/AllNotesOff helpers |
| `Platforms/Android/AndroidMidiOutput.cs` | `android.media.midi`: device list, hot-plug, sending |
| `Platforms/Windows/WindowsMidiOutput.cs` | `Windows.Devices.Midi`: device watcher, hot-plug, sending |
| `Controls/PianoKeyboard.cs`, `Controls/PianoKeyboardHandler.cs` | MAUI control (BaseNote, events) + shared handler |
| `Platforms/Android/PianoKeyboardView.cs` | Native view: drawing + multitouch per pointer |
| `Platforms/Windows/PianoKeyboardView.cs` | WinUI view: same behaviour for mouse, pen and touch |
| `Platforms/Android/AccelSampler.cs` | Accelerometer ring buffer, gravity filtered out, peak lookup |
| `Services/VelocitySettings.cs` | Velocity config, velocity + calibration math, Preferences |
| `Pages/SettingsPage.cs`, `Pages/CalibrationPage.cs` | Velocity settings and calibration |
| `MainPage.xaml(.cs)` | Toolbar and wiring |

## How velocity measurement works
With contact area or accelerometer the note-on is delayed by a measurement window
(default 20 ms). Within it the view collects the largest contact diameter (a finger keeps
flattening for a few ms after touch-down) and the impact peak of the gravity-filtered
accelerometer. Sliding to another key inherits the velocity, without extra delay.
Haptic feedback is switched off while the accelerometer is measuring, because the
vibration would otherwise show up in the measurement.
