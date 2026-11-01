using Sophron.Songs;

namespace Sophron.Tests;

public class VelocityTests
{
    private const long Beat = Song.TicksPerBeat;

    private static readonly List<Note> Four =
    [
        new(0, Beat, 100, 0.5f),
        new(Beat, Beat, 101, 0.5f),
        new(2 * Beat, Beat, 102, 0.5f),
        new(3 * Beat, Beat, 103, 0.5f),
    ];

    [Fact]
    public void A_spline_passes_through_its_points()
    {
        var spline = new Spline([(0, 0.2), (100, 0.9), (50, 0.4)]);

        Assert.Equal(0.2, spline.At(0)!.Value, 9);
        Assert.Equal(0.4, spline.At(50)!.Value, 9);
        Assert.Equal(0.9, spline.At(100)!.Value, 9);
    }

    [Fact]
    public void Two_points_make_a_straight_line()
    {
        var spline = new Spline([(0, 0.2), (100, 0.6)]);

        Assert.Equal(0.3, spline.At(25)!.Value, 9);
        Assert.Equal(0.4, spline.At(50)!.Value, 9);
    }

    [Fact]
    public void A_spline_says_nothing_outside_its_points_or_with_one_point()
    {
        Assert.Null(new Spline([(10, 0.2), (20, 0.6)]).At(5));
        Assert.Null(new Spline([(10, 0.2), (20, 0.6)]).At(21));
        Assert.Null(new Spline([(10, 0.2)]).At(10));
        Assert.Null(new Spline([]).At(0));
    }

    [Fact]
    public void A_rising_spline_never_dips_or_overshoots()
    {
        var spline = new Spline([(0, 0.1), (10, 0.15), (20, 0.9), (30, 1.0)]);
        var previous = 0.0;
        for (var x = 0.0; x <= 30; x += 0.25)
        {
            var y = spline.At(x)!.Value;
            Assert.InRange(y, previous - 1e-12, 1.0);
            previous = y;
        }
    }

    [Fact]
    public void A_peak_stays_at_the_point()
    {
        var spline = new Spline([(0, 0.2), (10, 0.8), (20, 0.2)]);

        for (var x = 0.0; x <= 20; x += 0.5)
        {
            Assert.InRange(spline.At(x)!.Value, 0.2 - 1e-12, 0.8 + 1e-12);
        }
    }

    [Fact]
    public void Of_two_points_at_one_place_the_later_wins() =>
        Assert.Equal(0.7, new Spline([(0, 0.1), (10, 0.3), (10, 0.7)]).At(10)!.Value, 9);

    [Fact]
    public void A_shape_sets_only_the_chosen_notes_it_reaches()
    {
        var spline = new Spline([(Beat, 0.2), (3 * Beat, 1.0)]);
        var shaped = NoteEditing.ShapeVelocity(Four, [0, 1, 2], tick => spline.At(tick));

        Assert.Equal(0.5f, shaped[0].Velocity);
        Assert.Equal(0.2f, shaped[1].Velocity, 5);
        Assert.Equal(0.6f, shaped[2].Velocity, 5);
        Assert.Equal(0.5f, shaped[3].Velocity);
    }

    [Fact]
    public void A_stroke_paints_a_line_and_catches_stems_within_reach()
    {
        var painted = NoteEditing.VelocityLine(Four, [0, 1, 2, 3], 3 * Beat - 10, 1.0, Beat, 0.2, 20);

        Assert.Equal(0.5f, painted[0].Velocity);
        Assert.Equal(0.2f, painted[1].Velocity, 5);
        Assert.InRange(painted[2].Velocity, 0.59f, 0.61f);
        Assert.Equal(1.0f, painted[3].Velocity);
    }

    [Fact]
    public void A_click_paints_the_notes_at_one_place()
    {
        var painted = NoteEditing.VelocityLine(Four, [0, 1, 2, 3], Beat + 5, 0.9, Beat + 5, 0.9, 10);

        Assert.Equal([0.5f, 0.9f, 0.5f, 0.5f], painted.Select(note => note.Velocity));
    }
}
