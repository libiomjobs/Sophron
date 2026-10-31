# Σώφρων

*Sophron* — a studio in 27-tone equal temperament, in the manner of a rack of instruments. **Sophron**
is what the code is called.

## The tuning

Step 0 is A0 at 27 Hz, and each step is a 27th of an octave (44.44 cents), so A4 is 432 Hz. Every third
step is a tone of 9-TET: those are the white keys, the step just above each is light gray, and the
step just below the next white is dark gray.

For now a standard keyboard plays the nearest step to each key. A, C♯ and F land exactly; B, D♯ and G
lie halfway between two steps, and which way each goes is a setting.

## Building

.NET 10 and WPF for the program, C++ for the audio engine, which needs Visual Studio's
"Desktop development with C++" workload.

```
dotnet build Sophron.slnx
dotnet test Sophron.slnx
```

Building Sophron builds the engine too, through `build-engine.ps1`, which also runs the engine's own
tests. Without the C++ tools the program still builds, and runs without sound.

- `Sophron.Core` — the tuning, and in time the model of a song.
- `Sophron` — the window: the 27-TET keyboard, MIDI input and Scala export.
- `Engine` — `sophron_engine.dll`: the audio thread, WASAPI output, and a plain synth.
