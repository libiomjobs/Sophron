using System.Globalization;
using System.Text;

namespace Sophron.Tuning;

/// <summary>
/// A temperament as Scala files, for instruments that read them: a <c>.scl</c> holding its steps and a
/// <c>.kbm</c> saying which MIDI key plays which step, with A4 where the fundamental puts it.
/// </summary>
public static class ScalaFiles
{
    private const int A4MidiNote = 69;

    public static string ScaleFileName(Temperament temperament) =>
        $"Sophron-{Invariant(temperament.StepsPerOctave)}tet.scl";

    public static string StandardMappingFileName(Temperament temperament) =>
        $"Sophron-{Invariant(temperament.StepsPerOctave)}tet-standard.kbm";

    public static string Scale(Temperament temperament)
    {
        var text = new StringBuilder();
        text.AppendLine("! " + ScaleFileName(temperament));
        text.AppendLine("!");
        text.AppendLine(Invariant(temperament.StepsPerOctave) + "-tone equal temperament (Sophron)");
        text.AppendLine(" " + Invariant(temperament.StepsPerOctave));
        text.AppendLine("!");

        // Scala reads a value with a point as cents, and one without as a ratio.
        for (var step = 1; step < temperament.StepsPerOctave; step++)
        {
            text.AppendLine(" " + temperament.Cents(step).ToString("0.00000", CultureInfo.InvariantCulture));
        }

        text.AppendLine(" 2/1");
        return text.ToString();
    }

    /// <summary>
    /// A mapping that plays each standard key on the step <paramref name="temperament"/> gives it: its
    /// own in 12-TET, the nearest by <paramref name="map"/> in 27-TET.
    /// </summary>
    public static string StandardMapping(Temperament temperament, StandardKeyMap map)
    {
        var a4 = temperament.Frequency(temperament.StepFor(A4MidiNote, map));
        var fundamental = temperament.FundamentalHz.ToString("0.###", CultureInfo.InvariantCulture);
        var text = new StringBuilder();
        text.AppendLine("! " + StandardMappingFileName(temperament));
        text.AppendLine(temperament.IsStandard
            ? $"! Each standard key plays its own step; A0 is {fundamental} Hz."
            : $"! Each standard key plays the nearest step of {Invariant(temperament.StepsPerOctave)}-TET; A0 is {fundamental} Hz.");
        text.AppendLine("! Size of map:");
        text.AppendLine(Invariant(StandardKeyMap.KeysPerOctave));
        text.AppendLine("! First and last MIDI notes to retune:");
        text.AppendLine("0");
        text.AppendLine("127");
        text.AppendLine("! Middle note, where the mapping starts:");
        text.AppendLine(Invariant(A4MidiNote));
        text.AppendLine("! Reference note and its frequency:");
        text.AppendLine(Invariant(A4MidiNote));
        text.AppendLine(a4.ToString("0.000000", CultureInfo.InvariantCulture));
        text.AppendLine("! Scale degree of the octave:");
        text.AppendLine(Invariant(temperament.StepsPerOctave));
        text.AppendLine("! Mapping, from A:");
        for (var key = 0; key < StandardKeyMap.KeysPerOctave; key++)
        {
            text.AppendLine(Invariant(temperament.Degree(temperament.StepFor(StandardKeyMap.A0MidiNote + key, map))));
        }

        return text.ToString();
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
