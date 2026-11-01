using System.Globalization;
using System.IO;

namespace Sophron;

/// <summary>
/// The few things Sophron remembers between runs, each in its own small file in
/// <c>%LocalAppData%\Sophron</c>.
/// </summary>
internal static class Preferences
{
    private static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sophron");

    /// <summary>The MIDI input last chosen, by name, since device numbers change as things are plugged in.</summary>
    public static string? MidiInput
    {
        get => Read("midi-input.txt");
        set => Write("midi-input.txt", value);
    }

    /// <summary>Steps to the octave, 12 or 27.</summary>
    public static int? StepsPerOctave
    {
        get => int.TryParse(Read("temperament.txt"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var steps) ? steps : null;
        set => Write("temperament.txt", value?.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>A0, in hertz.</summary>
    public static double? FundamentalHz
    {
        get => double.TryParse(Read("fundamental.txt"), NumberStyles.Float, CultureInfo.InvariantCulture, out var hertz) ? hertz : null;
        set => Write("fundamental.txt", value?.ToString("R", CultureInfo.InvariantCulture));
    }

    private static string? Read(string name)
    {
        try
        {
            var text = File.ReadAllText(Path.Combine(Root, name)).Trim();
            return text.Length > 0 ? text : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void Write(string name, string? value)
    {
        try
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, name), value ?? "");
        }
        catch (IOException)
        {
            // Not remembering is no reason to stop playing.
        }
    }
}
