using NAudio.Midi;

namespace Sophron.Audio;

/// <summary>
/// A MIDI input, reporting the notes played on it. Events arrive on the driver's own thread, not the
/// window's.
/// </summary>
internal sealed class MidiKeyboard : IDisposable
{
    private readonly MidiIn _input;

    private MidiKeyboard(int device)
    {
        _input = new MidiIn(device);
        _input.MessageReceived += OnMessage;
        _input.Start();
    }

    /// <summary>A key went down: the note and its velocity, from 0 to 1.</summary>
    public event Action<int, float>? NoteDown;

    public event Action<int>? NoteUp;

    /// <summary>The sustain pedal went down (true) or up.</summary>
    public event Action<bool>? Sustain;

    /// <summary>The pitch wheel moved, from 0 to 16383 with 8192 at rest.</summary>
    public event Action<int>? PitchWheel;

    public static IReadOnlyList<string> DeviceNames() =>
        [.. Enumerable.Range(0, MidiIn.NumberOfDevices).Select(device => MidiIn.DeviceInfo(device).ProductName)];

    public static MidiKeyboard Open(int device) => new(device);

    /// <summary>A device unplugged while open refuses to be stopped; it is let go of all the same.</summary>
    public void Dispose()
    {
        _input.MessageReceived -= OnMessage;
        try
        {
            _input.Stop();
        }
        catch (NAudio.MmException)
        {
        }

        try
        {
            _input.Dispose();
        }
        catch (NAudio.MmException)
        {
        }
    }

    private void OnMessage(object? sender, MidiInMessageEventArgs e)
    {
        switch (e.MidiEvent)
        {
            // A note-on with no velocity is how many keyboards say note-off.
            case NoteOnEvent { Velocity: > 0 } on:
                NoteDown?.Invoke(on.NoteNumber, on.Velocity / 127f);
                break;
            case NoteEvent note when note.CommandCode is MidiCommandCode.NoteOff or MidiCommandCode.NoteOn:
                NoteUp?.Invoke(note.NoteNumber);
                break;

            // A pedal sends 0 or 127; a continuous one anything between, and half down counts as down.
            case ControlChangeEvent { Controller: MidiController.Sustain } pedal:
                Sustain?.Invoke(pedal.ControllerValue >= 64);
                break;
            case PitchWheelChangeEvent wheel:
                PitchWheel?.Invoke(wheel.Pitch);
                break;
        }
    }
}
