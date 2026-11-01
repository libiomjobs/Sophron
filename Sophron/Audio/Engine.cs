using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sophron.Audio;

/// <summary>
/// The native engine, <c>sophron_engine.dll</c>, which owns the audio thread. Every call here only
/// queues a command; the audio thread picks it up at its next buffer, so none of them waits on audio.
/// </summary>
internal sealed partial class Engine : IDisposable
{
    private const string Library = "sophron_engine";

    private nint _handle;

    private Engine(nint handle) => _handle = handle;

    public double SampleRate => sophron_sample_rate(_handle);

    /// <summary>Opens the default output device, or explains why it could not.</summary>
    public static Engine? TryStart(out string? problem)
    {
        nint handle;
        try
        {
            handle = sophron_create();
        }
        catch (DllNotFoundException)
        {
            problem = "The audio engine has not been built, so there is no sound. Run build-engine.ps1.";
            return null;
        }

        var result = sophron_start(handle);
        if (result != 0)
        {
            sophron_destroy(handle);
            problem = $"The audio device would not open (0x{result:X8}).";
            return null;
        }

        problem = null;
        return new Engine(handle);
    }

    /// <summary>Starts a note; <paramref name="noteId"/> is what <see cref="NoteOff"/> later names it by.</summary>
    public void NoteOn(int noteId, double frequencyHz, float velocity) => sophron_note_on(_handle, noteId, frequencyHz, velocity);

    public void NoteOff(int noteId) => sophron_note_off(_handle, noteId);

    public void AllNotesOff() => sophron_all_notes_off(_handle);

    public void Sustain(bool down) => sophron_sustain(_handle, down ? 1 : 0);

    public void PitchBend(double cents) => sophron_pitch_bend(_handle, cents);

    /// <summary>
    /// The sample the listener is hearing at this moment, worked out from the last sample the device
    /// reported and how long ago that was. Before the device has reported anything, 0.
    /// </summary>
    public long HeardSample()
    {
        sophron_clock(_handle, out var sample, out var heardAt);
        if (heardAt == 0)
        {
            return 0;
        }

        var now = Stopwatch.GetTimestamp() * (10_000_000.0 / Stopwatch.Frequency);
        return sample + (long)((now - heardAt) / 10_000_000.0 * SampleRate);
    }

    public void NoteOnAt(long sample, int noteId, double frequencyHz, float velocity) => sophron_note_on_at(_handle, sample, noteId, frequencyHz, velocity);

    public void NoteOffAt(long sample, int noteId) => sophron_note_off_at(_handle, sample, noteId);

    public void SustainAt(long sample, bool down) => sophron_sustain_at(_handle, sample, down ? 1 : 0);

    public void PitchBendAt(long sample, double cents) => sophron_pitch_bend_at(_handle, sample, cents);

    public void ClickAt(long sample, bool accent) => sophron_click_at(_handle, sample, accent ? 1 : 0);

    /// <summary>Forgets every event still waiting for its sample.</summary>
    public void CancelScheduled() => sophron_cancel_scheduled(_handle);

    /// <summary>Moves every sounding note by a frequency ratio, so held notes follow a change of fundamental.</summary>
    public void Retune(double ratio) => sophron_retune(_handle, ratio);

    public void Dispose()
    {
        if (_handle != 0)
        {
            sophron_destroy(_handle);
            _handle = 0;
        }
    }

    [LibraryImport(Library)]
    private static partial nint sophron_create();

    [LibraryImport(Library)]
    private static partial int sophron_start(nint engine);

    [LibraryImport(Library)]
    private static partial double sophron_sample_rate(nint engine);

    [LibraryImport(Library)]
    private static partial void sophron_note_on(nint engine, int noteId, double frequencyHz, float velocity);

    [LibraryImport(Library)]
    private static partial void sophron_note_off(nint engine, int noteId);

    [LibraryImport(Library)]
    private static partial void sophron_all_notes_off(nint engine);

    [LibraryImport(Library)]
    private static partial void sophron_sustain(nint engine, int down);

    [LibraryImport(Library)]
    private static partial void sophron_pitch_bend(nint engine, double cents);

    [LibraryImport(Library)]
    private static partial void sophron_retune(nint engine, double ratio);

    [LibraryImport(Library)]
    private static partial void sophron_clock(nint engine, out long sample, out long qpc100ns);

    [LibraryImport(Library)]
    private static partial void sophron_note_on_at(nint engine, long sample, int noteId, double frequencyHz, float velocity);

    [LibraryImport(Library)]
    private static partial void sophron_note_off_at(nint engine, long sample, int noteId);

    [LibraryImport(Library)]
    private static partial void sophron_sustain_at(nint engine, long sample, int down);

    [LibraryImport(Library)]
    private static partial void sophron_pitch_bend_at(nint engine, long sample, double cents);

    [LibraryImport(Library)]
    private static partial void sophron_click_at(nint engine, long sample, int accent);

    [LibraryImport(Library)]
    private static partial void sophron_cancel_scheduled(nint engine);

    [LibraryImport(Library)]
    private static partial void sophron_destroy(nint engine);
}
