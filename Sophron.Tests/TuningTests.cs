using Sophron.Tuning;

namespace Sophron.Tests;

public class TuningTests
{
    [Theory]
    [InlineData(0, 27.0)]
    [InlineData(27, 54.0)]
    [InlineData(108, 432.0)]
    [InlineData(-27, 13.5)]
    public void Octaves_double_from_27_hertz(int step, double hertz)
    {
        Assert.Equal(hertz, Tet27.Frequency(step), 9);
    }

    [Fact]
    public void A_step_is_a_27th_of_an_octave()
    {
        Assert.Equal(Math.Pow(2, 1.0 / 27), Tet27.Frequency(1) / Tet27.Frequency(0), 12);
        Assert.Equal(1200.0, Tet27.Cents(27), 9);
    }

    [Fact]
    public void Shades_go_white_light_dark_from_A0()
    {
        KeyShade[] expected = [KeyShade.White, KeyShade.Light, KeyShade.Dark];
        for (var step = -6; step < 60; step++)
        {
            Assert.Equal(expected[Tet27.Mod(step, 3)], Tet27.Shade(step));
        }

        Assert.Equal(KeyShade.White, Tet27.Shade(108));
        Assert.Equal(KeyShade.Dark, Tet27.Shade(-1));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(26, 0, 26)]
    [InlineData(27, 1, 0)]
    [InlineData(-1, -1, 26)]
    public void Steps_split_into_octave_and_degree(int step, int octave, int degree)
    {
        Assert.Equal(octave, Tet27.Octave(step));
        Assert.Equal(degree, Tet27.Degree(step));
    }

    [Fact]
    public void Standard_keys_land_on_their_nearest_step()
    {
        var map = StandardKeyMap.Default;
        for (var key = 0; key < 12; key++)
        {
            var error = Math.Abs(Tet27.Cents(map.Degree(key)) - (key * 100));
            Assert.True(error <= 22.23, $"{StandardKeyMap.KeyNames[key]} is {error:F1} cents out.");
        }

        Assert.Equal([0, 2, 5, 7, 9, 11, 14, 16, 18, 20, 23, 25], Enumerable.Range(0, 12).Select(map.Degree));
    }

    [Fact]
    public void Only_B_D_sharp_and_G_are_ties_and_each_follows_its_own_choice()
    {
        var lower = new StandardKeyMap(Tie.Lower, Tie.Lower, Tie.Lower);
        Assert.Equal([0, 2, 4, 7, 9, 11, 13, 16, 18, 20, 22, 25], Enumerable.Range(0, 12).Select(lower.Degree));

        var mixed = new StandardKeyMap(B: Tie.Upper, DSharp: Tie.Lower, G: Tie.Lower);
        Assert.Equal(5, mixed.Degree(2));
        Assert.Equal(13, mixed.Degree(6));
        Assert.Equal(22, mixed.Degree(10));
    }

    [Theory]
    [InlineData(21, 0)]
    [InlineData(33, 27)]
    [InlineData(69, 108)]
    [InlineData(72, 115)]
    [InlineData(20, -2)]
    public void MIDI_notes_count_octaves_from_A0(int midiNote, int step)
    {
        Assert.Equal(step, StandardKeyMap.Default.StepFor(midiNote));
    }

    [Fact]
    public void A4_plays_432_hertz()
    {
        Assert.Equal(432.0, Tet27.Frequency(StandardKeyMap.Default.StepFor(69)), 9);
    }

    [Fact]
    public void Steps_lead_back_to_the_keys_that_play_them()
    {
        var map = StandardKeyMap.Default;
        for (var note = 0; note < 128; note++)
        {
            Assert.Equal(note, map.MidiNoteFor(map.StepFor(note)));
        }

        Assert.Null(map.MidiNoteFor(1));
        Assert.Null(map.MidiNoteFor(3));
    }

    [Theory]
    [InlineData(21, "A0")]
    [InlineData(60, "C4")]
    [InlineData(69, "A4")]
    [InlineData(70, "A♯4")]
    public void Notes_keep_their_usual_names(int midiNote, string name)
    {
        Assert.Equal(name, StandardKeyMap.NoteName(midiNote));
    }

    [Theory]
    [InlineData(PitchBend.Centre, 3, 0.0)]
    [InlineData(16383, 3, 133.333333)]
    [InlineData(0, 3, -133.333333)]
    [InlineData(16383, 27, 1200.0)]
    [InlineData(0, 1, -44.444444)]
    public void The_wheel_reaches_exactly_its_range_in_steps(int wheel, int rangeSteps, double cents)
    {
        Assert.Equal(cents, PitchBend.Cents(wheel, rangeSteps), 5);
    }

    [Fact]
    public void The_wheel_bends_halfway_at_half_throw()
    {
        Assert.Equal(-1.5 * Tet27.CentsPerStep, PitchBend.Cents(4096, 3), 9);
    }

    [Fact]
    public void The_scale_file_holds_27_steps_ending_on_the_octave()
    {
        var pitches = ScalaFiles.Scale().Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('!')).ToList();
        // The description, the count, then the 27 pitches themselves.
        Assert.Equal("27", pitches[1]);
        Assert.Equal(29, pitches.Count);
        Assert.Equal("44.44444", pitches[2]);
        Assert.Equal("2/1", pitches[^1]);
    }

    [Fact]
    public void The_mapping_file_puts_A4_at_432_hertz_with_the_chosen_ties()
    {
        var values = ScalaFiles.StandardMapping(new StandardKeyMap(B: Tie.Lower)).Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('!')).ToList();
        Assert.Equal(["12", "0", "127", "69", "69", "432.000000", "27"], values.Take(7));
        Assert.Equal(["0", "2", "4", "7", "9", "11", "14", "16", "18", "20", "23", "25"], values.Skip(7));
    }
}
