# MIDI Keys

A .NET MAUI app (Android and Windows) with a multitouch piano keyboard, step sequencers and
arpeggiators that send MIDI over a class-compliant USB MIDI cable (e.g. CME C2MIDI Pro)
to a synth with 5-pin DIN. The look follows the Roland TR-808: charcoal panel, off-white
print, orange accents and step buttons in red / orange / yellow / cream groups.

## Side menu
The hamburger menu (top left) holds:
- **Piano**
- **SEQUENCER** and **ARP** sections. The **+** next to a section title adds one on a MIDI
  channel that no other sequencer or arp uses yet; it appears as e.g. *Sequencer (ch1)*.
  The 🗑 behind an entry removes it (after asking) together with its stored settings.
  You can have several sequencers and arps, each on its own channel.
- **TEMPO** at the bottom: one tempo (30–300 BPM) for all sequencers and arps, which run on
  a shared clock and stay in step.
- **MIDI device** right at the bottom: choose the output, with the connection status below.
  Connects automatically as soon as you plug in the cable; disconnects cleanly when you unplug it.

Sequencers and arps, their channels, patterns and settings are saved and come back on the
next start. They keep playing when you switch pages, so you can play the piano over them.

On Android the app runs full screen (swipe from the edge to show the system bars briefly).

## Features
### Piano
- 2 octaves + top C, multitouch (chords, sliding across keys), haptic feedback
- Pressed keys light up from pale (soft) to saturated orange (hard) according to the velocity
- ▼ / ▲ octave (C-1 … C9), MIDI channel 1–16
- Velocity sensitivity (⚙): fixed, position on the key, contact area, accelerometer, or
  automatic. Calibrate with 6 soft and 6 hard taps; per source the app decides whether your
  device can tell soft from hard.
- Sends note-off / All Notes Off when the app goes to the background (no hanging notes)

### Sequencer
A 16-step sequencer after the ARP / Behringer 1601 on the channel it was added on. Each step
is a sixteenth note.
- Per step: a LED for the current step, a pitch slider (2 octaves in semitones, note name
  below), and switches for three gate buses plus a step mode:
  - **GATE**: the step plays a note
  - **ACCENT**: velocity 127 instead of 96
  - **LEGATO**: the note is held into the next step; the same pitch becomes one tied note
  - **MODE**: ▶ play, SKIP (pass over) or RST (jump straight back to step 1)
- In the title bar: ▶/■ start/stop, ⏮ back to step 1, gate length 5–95 % of a step
- Below it: ▼ / ▲ octave for the slider range, **1×16 / 2×8** (one 16-step sequence, or two
  independent 8-step sequences A1–A8 and B1–B8 as two voices on the same channel),
  ➡ / 🔀 forward or random order

The three gate buses are separate gate outputs on the 1601; here they are mapped to
gate, accent and legato because that is what is useful over MIDI.

### Arp
An arpeggiator with its own keyboard, built so every setting is visible and takes one tap.
- In the title bar: ▶/■ start/stop and gate length. Stopped, the keyboard plays notes directly.
- **MODUS**: ↑ up, ↓ down, ↕ up and down (no double top or bottom note), ✋ as played, 🎲 random
- **OCTAVEN**: 1–4, **SNELHEID**: 1/4, 1/8, 1/16, 1/8T
- **RITME**: 8 steps that play or rest; a LED above each step shows the one that is playing
- **HOLD** (on by default): the chord keeps playing after you let go; the first key of a new
  chord replaces it. Without Hold the arp plays while keys are down.
- ▼ / ▲ octave for the keyboard; the line next to it shows the notes being played
- Notes play at the velocity they were played with. When a sequencer is already running, the
  arp falls in on the next step of its rate.

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
Using .NET 9? Change `net10.0` to `net9.0` in `TargetFrameworks` in `MidiKeys/MidiKeys.csproj`.

On Windows the built-in *Microsoft GS Wavetable Synth* is listed last, so you can test
without a cable (it has noticeable latency). There is no accelerometer on Windows; contact
area only works on touchscreens that report it. Hover over an icon button for its tooltip
(long-press on Android).

On Windows you can also play the Piano and Arp keyboards with the PC keyboard, starting at
the leftmost C on screen: white keys `A S D F G H J K L ; '`, black keys `W E T Y U O P`.
Keys play at the fixed velocity. Page Up / Page Down change the octave.

## Connecting
Cable **OUT** → synth **MIDI IN**. Set the synth (or each part of a multitimbral synth) to
the channel of the piano, sequencer or arp that should play it.

## Structure
| File | Role |
|---|---|
| `Services/IMidiOutput.cs` | Interface + NoteOn/NoteOff/AllNotesOff helpers |
| `Platforms/Android/AndroidMidiOutput.cs` | `android.media.midi`: device list, hot-plug, sending |
| `Platforms/Windows/WindowsMidiOutput.cs` | `Windows.Devices.Midi`: device watcher, hot-plug, sending |
| `Platforms/Android/MainActivity.cs` | Landscape, screen kept on, full screen |
| `Controls/PianoKeyboard.cs`, `Controls/PianoKeyboardHandler.cs` | MAUI control (BaseNote, events) + shared handler |
| `Platforms/Android/PianoKeyboardView.cs` | Native view: drawing + multitouch per pointer |
| `Platforms/Windows/PianoKeyboardView.cs` | WinUI view: same behaviour for mouse, pen, touch and PC keyboard |
| `Platforms/Android/AccelSampler.cs` | Accelerometer ring buffer, gravity filtered out, peak lookup |
| `Services/VelocitySettings.cs` | Velocity config, velocity + calibration math, Preferences |
| `Controls/Theme.cs` | TR-808 colours, default control styles, step colours |
| `Controls/KeyColors.cs` | Key colours and pressed-key colour from the velocity |
| `Controls/PageHeader.cs` | Title bar with the page title left and its main controls right |
| `Controls/MidiDevicePicker.cs` | MIDI device choice + connection status (side menu) |
| `Pages/SettingsPage.cs`, `Pages/CalibrationPage.cs` | Velocity settings and calibration |
| `MainPage.xaml(.cs)` | Piano page: toolbar and wiring |
| `AppShell.cs` | Side menu: pages, + / 🗑 per section, tempo, MIDI device |
| `Services/Instruments.cs` | The added sequencers and arps, one per channel, stored in Preferences |
| `Services/MusicClock.cs` | Shared tempo and timing thread (24 PPQN), note-off bookkeeping |
| `Services/StepSequencer.cs` | Sequencer engine: steps, gate buses, Preferences per channel |
| `Services/Arpeggiator.cs` | Arp engine: held/latched notes, modes, rhythm, Preferences per channel |
| `Controls/SequencerPanel.cs` | Drawn step panel: LEDs, pitch sliders, switches |
| `Pages/SequencerPage.cs` | Sequencer page |
| `Pages/ArpPage.cs` | Arp page with its own keyboard |

## How velocity measurement works
With contact area or accelerometer the note-on is delayed by a measurement window
(default 20 ms). Within it the view collects the largest contact diameter (a finger keeps
flattening for a few ms after touch-down) and the impact peak of the gravity-filtered
accelerometer. Sliding to another key inherits the velocity, without extra delay.
Haptic feedback is switched off while the accelerometer is measuring, because the
vibration would otherwise show up in the measurement.

## How timing works
All sequencers and arps run on one clock thread (`MusicClock`) with a `Stopwatch`,
independent of the UI, at 24 ticks per quarter note: a sequencer step is 6 ticks, an arp
step 24 / 12 / 8 / 6 ticks depending on its rate. The thread only runs while something
plays; whatever joins a running clock falls in on its grid. It sleeps in 1 ms slices and
yields for the last few milliseconds before a tick or note-off. On Windows it asks for a
1 ms timer resolution while running (`timeBeginPeriod`), because the default is about
15 ms. If it falls behind (debugger, device sleep), it continues from now instead of
rushing to catch up.
