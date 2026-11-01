using Sophron.Songs;

namespace Sophron.Tests;

public class EditingTests
{
    private const long Beat = Song.TicksPerBeat;

    private static readonly List<Note> Three =
    [
        new(0, Beat, 100, 0.5f),
        new(Beat, Beat, 105, 0.6f),
        new(2 * Beat, 2 * Beat, 110, 0.7f),
    ];

    [Theory]
    [InlineData(0, 240, 0)]
    [InlineData(119, 240, 0)]
    [InlineData(120, 240, 240)]
    [InlineData(500, 240, 480)]
    [InlineData(-130, 240, -240)]
    [InlineData(77, 1, 77)]
    [InlineData(77, 0, 77)]
    public void Snap_finds_the_nearest_point_of_the_grid(long tick, long grid, long expected) =>
        Assert.Equal(expected, NoteEditing.Snap(tick, grid));

    [Fact]
    public void Snapping_down_never_goes_later() =>
        Assert.Equal([0, 240, 240, -240], new long[] { 239, 240, 479, -1 }.Select(tick => NoteEditing.SnapDown(tick, 240)));

    [Fact]
    public void Moving_keeps_lengths_and_leaves_the_rest_alone()
    {
        var moved = NoteEditing.Move(Three, [0, 2], Beat / 2, 3, 0, 300);

        Assert.Equal(new Note(Beat / 2, Beat, 103, 0.5f), moved[0]);
        Assert.Equal(Three[1], moved[1]);
        Assert.Equal(new Note(5 * Beat / 2, 2 * Beat, 113, 0.7f), moved[2]);
    }

    [Fact]
    public void Moving_does_not_change_the_list_given()
    {
        var notes = new List<Note>(Three);
        NoteEditing.Move(notes, [1], Beat, 1, 0, 300);

        Assert.Equal(Three, notes);
    }

    [Fact]
    public void A_group_pushed_against_the_start_or_the_keyboard_keeps_its_shape()
    {
        var moved = NoteEditing.Move(Three, [1, 2], -5 * Beat, 1000, 0, 112);

        Assert.Equal(0, moved[1].Start);
        Assert.Equal(Beat, moved[2].Start);
        Assert.Equal(107, moved[1].Step);
        Assert.Equal(112, moved[2].Step);
    }

    [Fact]
    public void Resizing_the_end_keeps_the_start_and_never_goes_below_the_shortest()
    {
        var longer = NoteEditing.ResizeEnd(Three, [0], Beat);
        var gone = NoteEditing.ResizeEnd(Three, [0], -10 * Beat);

        Assert.Equal(new Note(0, 2 * Beat, 100, 0.5f), longer[0]);
        Assert.Equal(NoteEditing.ShortestNote, gone[0].Length);
        Assert.Equal(0, gone[0].Start);
    }

    [Fact]
    public void Resizing_the_start_keeps_the_end()
    {
        var earlier = NoteEditing.ResizeStart(Three, [1], -Beat / 2);
        var later = NoteEditing.ResizeStart(Three, [1], Beat / 4);
        var tooFar = NoteEditing.ResizeStart(Three, [1], 5 * Beat);
        var beforeTheSong = NoteEditing.ResizeStart(Three, [1], -5 * Beat);

        Assert.Equal(new Note(Beat / 2, 3 * Beat / 2, 105, 0.6f), earlier[1]);
        Assert.Equal(new Note(5 * Beat / 4, 3 * Beat / 4, 105, 0.6f), later[1]);
        Assert.Equal(2 * Beat, tooFar[1].End);
        Assert.Equal(NoteEditing.ShortestNote, tooFar[1].Length);
        Assert.Equal(new Note(0, 2 * Beat, 105, 0.6f), beforeTheSong[1]);
    }

    [Fact]
    public void Quantising_pulls_starts_and_ends_onto_the_grid()
    {
        List<Note> played = [new(250, 700, 100, 1), new(1430, 40, 101, 1)];
        var quantised = NoteEditing.Quantise(played, [0, 1], 240);

        Assert.Equal(new Note(240, 720, 100, 1), quantised[0]);
        Assert.Equal(new Note(1440, 240, 101, 1), quantised[1]);
    }

    [Fact]
    public void Velocity_is_kept_between_0_and_1()
    {
        Assert.Equal(1f, NoteEditing.SetVelocity(Three, [0], 1.5f)[0].Velocity);
        Assert.Equal(0.25f, NoteEditing.SetVelocity(Three, [0], 0.25f)[0].Velocity);
    }

    [Fact]
    public void Deleting_removes_only_the_chosen()
    {
        Assert.Equal([Three[1]], NoteEditing.Delete(Three, [0, 2]));
    }

    [Fact]
    public void Pasting_puts_the_earliest_note_where_asked_and_says_which_are_new()
    {
        var copied = NoteEditing.Copy(Three, [2, 1]);
        var (notes, added) = NoteEditing.Paste(Three, copied, 8 * Beat);

        Assert.Equal([3, 4], added);
        Assert.Equal(new Note(8 * Beat, Beat, 105, 0.6f), notes[3]);
        Assert.Equal(new Note(9 * Beat, 2 * Beat, 110, 0.7f), notes[4]);
    }

    [Fact]
    public void A_duplicate_follows_the_original_on_the_grid()
    {
        // Notes from beat 1 to beat 4 repeat from beat 4; a ragged end is rounded up to the grid.
        Assert.Equal(4 * Beat, NoteEditing.DuplicateAt(Three, [1, 2], Beat));
        List<Note> ragged = [new(0, Beat + 10, 100, 1)];
        Assert.Equal(2 * Beat, NoteEditing.DuplicateAt(ragged, [0], Beat));
    }

    [Fact]
    public void A_rectangle_catches_the_notes_it_touches()
    {
        Assert.Equal([1, 2], NoteEditing.Within(Three, Beat + 10, 3 * Beat, 104, 120));
        Assert.Empty(NoteEditing.Within(Three, 0, 10 * Beat, 101, 104));
    }

    [Fact]
    public void Undo_and_redo_go_back_and_forth()
    {
        var track = new Track { Notes = [.. Three] };
        var history = new EditHistory();

        history.Before(track, "Delete");
        track.Notes = NoteEditing.Delete(track.Notes, [0]);
        history.Before(track, "Move");
        track.Notes = NoteEditing.Move(track.Notes, [0], Beat, 0, 0, 300);

        Assert.Equal("Move", history.Undo(track));
        Assert.Equal([Three[1], Three[2]], track.Notes);
        Assert.Equal("Delete", history.Undo(track));
        Assert.Equal(Three, track.Notes);
        Assert.Null(history.Undo(track));

        Assert.Equal("Delete", history.Redo(track));
        Assert.Equal("Move", history.Redo(track));
        Assert.Equal(2 * Beat, track.Notes[0].Start);
        Assert.Null(history.Redo(track));
    }

    [Fact]
    public void A_new_edit_forgets_what_was_undone()
    {
        var track = new Track { Notes = [.. Three] };
        var history = new EditHistory();
        history.Before(track, "Delete");
        track.Notes = NoteEditing.Delete(track.Notes, [0]);
        history.Undo(track);

        history.Before(track, "Quantise");

        Assert.False(history.CanRedo);
        Assert.Equal("Quantise", history.UndoName);
    }

    [Fact]
    public void A_take_can_be_undone()
    {
        var track = new Track { Notes = [.. Three] };
        var history = new EditHistory();
        var recorder = new Recorder();
        recorder.NoteDown(120, 1, 8 * Beat);
        recorder.NoteUp(120, 9 * Beat);

        history.Before(track, "Take");
        recorder.FinishInto(track, 10 * Beat);
        Assert.Equal(4, track.Notes.Count);

        history.Undo(track);
        Assert.Equal(Three, track.Notes);
    }

    [Fact]
    public void Overlapping_notes_on_one_step_sound_until_the_later_ends()
    {
        var track = new Track { Notes = [new(0, 2 * Beat, 100, 1), new(Beat, 2 * Beat, 100, 1), new(0, Beat, 101, 1)] };
        var offs = Schedule.Events(track, 0, 10 * Beat).Where(e => e.Kind == SongEventKind.NoteOff).ToList();

        Assert.Equal([(Beat, 101), (3 * Beat, 100)], offs.Select(e => (e.Tick, e.Step)));
    }

    [Fact]
    public void Two_identical_notes_still_end()
    {
        var track = new Track { Notes = [new(0, Beat, 100, 1), new(0, Beat, 100, 1)] };

        Assert.Equal(2, Schedule.Events(track, 0, 2 * Beat).Count(e => e.Kind == SongEventKind.NoteOff));
    }
}
