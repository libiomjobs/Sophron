using Sophron.Songs;

namespace Sophron.Tests;

public class SongTests
{
    private const long Beat = Song.TicksPerBeat;

    [Fact]
    public void A_recorded_note_runs_from_key_down_to_key_up()
    {
        var recorder = new Recorder();
        recorder.NoteDown(160, 0.5f, Beat);
        recorder.NoteUp(160, 3 * Beat);
        var track = new Track();
        recorder.FinishInto(track, 4 * Beat);

        Assert.Equal([new Note(Beat, 2 * Beat, 160, 0.5f)], track.Notes);
    }

    [Fact]
    public void Steps_beyond_midi_are_kept_as_they_are()
    {
        var recorder = new Recorder();
        recorder.NoteDown(-5, 1, 0);
        recorder.NoteDown(250, 1, 0);
        var track = new Track();
        recorder.FinishInto(track, Beat);

        Assert.Equal([-5, 250], track.Notes.Select(note => note.Step));
        Assert.All(track.Notes, note => Assert.Equal(Beat, note.Length));
    }

    [Fact]
    public void Notes_still_held_when_recording_stops_end_there()
    {
        var recorder = new Recorder();
        recorder.NoteDown(108, 1, 0);
        Assert.Equal(2 * Beat, recorder.NotesSoFar(2 * Beat).Single().Length);

        var track = new Track();
        recorder.FinishInto(track, 5 * Beat);
        Assert.Equal(5 * Beat, track.Notes.Single().Length);
    }

    [Fact]
    public void The_count_in_keeps_early_notes_on_the_first_beat_and_drops_the_rest()
    {
        var recorder = new Recorder();
        recorder.NoteDown(100, 1, -2 * Beat);
        recorder.NoteUp(100, -Beat);
        recorder.NoteDown(108, 1, -20);
        recorder.NoteUp(108, Beat);
        var track = new Track();
        recorder.FinishInto(track, 2 * Beat);

        Assert.Equal([new Note(0, Beat, 108, 1)], track.Notes);
    }

    [Fact]
    public void A_tap_leaves_a_note_too_short_to_vanish()
    {
        var recorder = new Recorder();
        recorder.NoteDown(108, 1, Beat);
        recorder.NoteUp(108, Beat);
        var track = new Track();
        recorder.FinishInto(track, 2 * Beat);

        Assert.Equal(Recorder.ShortestNote, track.Notes.Single().Length);
    }

    [Fact]
    public void Recording_again_adds_to_what_is_there_in_order()
    {
        var track = new Track { Notes = [new Note(2 * Beat, Beat, 108, 1)] };
        var recorder = new Recorder();
        recorder.NoteDown(111, 1, 0);
        recorder.NoteUp(111, Beat);
        recorder.FinishInto(track, 3 * Beat);

        Assert.Equal([0, 2 * Beat], track.Notes.Select(note => note.Start));
    }

    [Fact]
    public void A_stretch_of_the_song_schedules_what_starts_and_ends_in_it()
    {
        var track = new Track
        {
            Notes = [new Note(0, Beat, 108, 1), new Note(Beat, Beat, 108, 0.5f), new Note(3 * Beat, Beat, 111, 1)],
            Bends = [new BendPoint(Beat + 10, 44.4)],
            Sustain = [new PedalChange(Beat, true)],
        };

        var events = Schedule.Events(track, Beat, 2 * Beat);

        // The first note's end and the second's start fall together; the end must come first.
        Assert.Equal(
            [
                new SongEvent(Beat, SongEventKind.NoteOff, 108, 0),
                new SongEvent(Beat, SongEventKind.NoteOn, 108, 0.5f),
                new SongEvent(Beat, SongEventKind.Sustain, 0, 1),
                new SongEvent(Beat + 10, SongEventKind.Bend, 0, 44.4),
            ],
            events);
    }

    [Fact]
    public void Stretches_end_to_end_schedule_each_event_once()
    {
        var track = new Track { Notes = [.. Enumerable.Range(0, 20).Select(i => new Note(i * 300, 450, 100 + i, 1))] };
        var all = new List<SongEvent>();
        for (var from = 0L; from < 10_000; from += 777)
        {
            all.AddRange(Schedule.Events(track, from, from + 777));
        }

        Assert.Equal(40, all.Count);
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void The_metronome_marks_the_first_beat_of_each_bar_count_in_included()
    {
        var song = new Song { BeatsPerBar = 3 };
        var clicks = Schedule.Clicks(song, -3 * Beat, 3 * Beat + 1);

        Assert.Equal([-3 * Beat, -2 * Beat, -Beat, 0, Beat, 2 * Beat, 3 * Beat], clicks.Select(click => click.Tick));
        Assert.Equal([1.0, 0, 0, 1, 0, 0, 1], clicks.Select(click => click.Value));
        Assert.Empty(Schedule.Clicks(song, 1, Beat));
    }

    [Fact]
    public void A_song_survives_being_saved_and_read_back()
    {
        var song = new Song { StepsPerOctave = 12, FundamentalHz = 27.5, Tempo = 96.5, BeatsPerBar = 7 };
        song.Tracks[0].Notes.Add(new Note(10, 20, -3, 0.25f));
        song.Tracks[0].Bends.Add(new BendPoint(5, -133.3));
        song.Tracks[0].Sustain.Add(new PedalChange(7, true));

        var json = SongFile.Write(song);
        var read = SongFile.Read(json);

        Assert.Contains("\"format\": 1", json, StringComparison.Ordinal);
        Assert.Equal(json, SongFile.Write(read));
        Assert.Equal(song.Tracks[0].Notes, read.Tracks[0].Notes);
        Assert.Equal(27.5 * 16, read.Temperament.A4Hz, 9);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"format\": 99, \"song\": {}}")]
    [InlineData("{\"format\": 1, \"song\": {\"stepsPerOctave\": 0}}")]
    public void Anything_but_a_song_is_refused(string json)
    {
        Assert.Throws<InvalidDataException>(() => SongFile.Read(json));
    }

    [Fact]
    public void Ticks_are_timed_by_the_tempo()
    {
        var song = new Song { Tempo = 120 };
        Assert.Equal(0.5, song.SecondsPerTick * Beat, 12);
        Assert.Equal(4 * Beat, song.TicksPerBar);
    }
}
