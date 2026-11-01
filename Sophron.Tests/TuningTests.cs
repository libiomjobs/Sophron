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
        Assert.Equal(cents, PitchBend.Cents(wheel, rangeSteps, Tet27.CentsPerStep), 5);
    }

    [Fact]
    public void The_wheel_bends_halfway_at_half_throw()
    {
        Assert.Equal(-1.5 * Tet27.CentsPerStep, PitchBend.Cents(4096, 3, Tet27.CentsPerStep), 9);
    }

    [Fact]
    public void The_scale_file_holds_27_steps_ending_on_the_octave()
    {
        var pitches = ScalaFiles.Scale(Temperament.TwentySeven()).Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('!')).ToList();
        // The description, the count, then the 27 pitches themselves.
        Assert.Equal("27", pitches[1]);
        Assert.Equal(29, pitches.Count);
        Assert.Equal("44.44444", pitches[2]);
        Assert.Equal("2/1", pitches[^1]);
    }

    [Fact]
    public void The_mapping_file_puts_A4_at_432_hertz_with_the_chosen_ties()
    {
        var values = ScalaFiles.StandardMapping(Temperament.TwentySeven(), new StandardKeyMap(B: Tie.Lower)).Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('!')).ToList();
        Assert.Equal(["12", "0", "127", "69", "69", "432.000000", "27"], values.Take(7));
        Assert.Equal(["0", "2", "4", "7", "9", "11", "14", "16", "18", "20", "23", "25"], values.Skip(7));
    }

    [Theory]
    [InlineData(27, 27.0, 432.0)]
    [InlineData(27, 27.5, 440.0)]
    [InlineData(12, 27.5, 440.0)]
    [InlineData(12, 27.0, 432.0)]
    public void A4_is_sixteen_times_the_fundamental_in_either_temperament(int steps, double fundamental, double a4)
    {
        var temperament = new Temperament(steps, fundamental);
        Assert.Equal(a4, temperament.A4Hz, 9);
        Assert.Equal(a4, temperament.Frequency(temperament.StepFor(69, StandardKeyMap.Default)), 9);
    }

    [Fact]
    public void Twelve_tet_plays_every_key_on_its_own_step()
    {
        var twelve = Temperament.Twelve();
        Assert.Equal(0, twelve.StepFor(21, StandardKeyMap.Default));
        Assert.Equal(39, twelve.StepFor(60, StandardKeyMap.Default));
        Assert.Equal(261.6256, twelve.Frequency(39), 4);
        Assert.Equal(60, twelve.MidiNoteFor(39, StandardKeyMap.Default));
        Assert.Null(twelve.MidiNoteFor(-22, StandardKeyMap.Default));
    }

    [Fact]
    public void The_temperament_of_27_agrees_with_tet27()
    {
        var temperament = Temperament.TwentySeven();
        for (var step = -30; step < 220; step++)
        {
            Assert.Equal(Tet27.Frequency(step), temperament.Frequency(step), 9);
        }
    }

    [Fact]
    public void A_fundamental_can_be_found_from_A4()
    {
        Assert.Equal(27.5, Temperament.FundamentalFor(440), 12);
        Assert.Equal(25.9375, Temperament.FundamentalFor(415), 12);
    }

    [Fact]
    public void The_twelve_tet_mapping_file_maps_each_key_to_itself_at_440()
    {
        var values = ScalaFiles.StandardMapping(Temperament.Twelve(), StandardKeyMap.Default).Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('!')).ToList();
        Assert.Equal(["12", "0", "127", "69", "69", "440.000000", "12"], values.Take(7));
        Assert.Equal(Enumerable.Range(0, 12).Select(key => key.ToString(System.Globalization.CultureInfo.InvariantCulture)), values.Skip(7));
        Assert.Equal("Sophron-12tet.scl", ScalaFiles.ScaleFileName(Temperament.Twelve()));
    }
}
