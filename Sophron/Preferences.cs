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
