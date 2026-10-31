using System.Globalization;
using System.Text;

namespace Sophron.Tuning;

/// <summary>
/// The tuning as Scala files, for instruments that read them: a <c>.scl</c> holding the 27 steps and a
/// <c>.kbm</c> saying which MIDI key plays which step, with A4 at 432 Hz.
/// </summary>
public static class ScalaFiles
{
    public const string ScaleFileName = "Sophron-27tet.scl";

    public const string StandardMappingFileName = "Sophron-27tet-standard.kbm";

    private const int A4MidiNote = 69;

    public static string Scale()
    {
        var text = new StringBuilder();
        text.AppendLine("! " + ScaleFileName);
        text.AppendLine("!");
        text.AppendLine("27-tone equal temperament (Sophron)");
        text.AppendLine(" " + Tet27.StepsPerOctave.ToString(CultureInfo.InvariantCulture));
        text.AppendLine("!");

        // Scala reads a value with a point as cents, and one without as a ratio.
        for (var step = 1; step < Tet27.StepsPerOctave; step++)
        {
            text.AppendLine(" " + Tet27.Cents(step).ToString("0.00000", CultureInfo.InvariantCulture));
        }

        text.AppendLine(" 2/1");
        return text.ToString();
    }

    /// <summary>A mapping that plays each standard key on its nearest step, as <paramref name="map"/> does.</summary>
    public static string StandardMapping(StandardKeyMap map)
    {
        var a4 = Tet27.Frequency(map.StepFor(A4MidiNote));
        var text = new StringBuilder();
        text.AppendLine("! " + StandardMappingFileName);
        text.AppendLine("! Each standard key plays the nearest step of 27-TET; A0 is 27 Hz.");
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
        text.AppendLine(Invariant(Tet27.StepsPerOctave));
        text.AppendLine("! Mapping, from A:");
        for (var key = 0; key < StandardKeyMap.KeysPerOctave; key++)
        {
            text.AppendLine(Invariant(map.Degree(key)));
        }

        return text.ToString();
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
