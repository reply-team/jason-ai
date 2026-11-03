namespace Jason.Cli.Process;

/// <summary>
/// How text is put on the person's clipboard — a seam whose default is the refusal, for the reason
/// <see cref="IUrlOpener"/> gives: a test that let the real one run would replace whatever the person running the suite
/// had copied. Null refuses; <see cref="CliEnvironment.Default()"/> names this platform's, which is Windows' alone.
/// </summary>
public interface IClipboard
{
    /// <summary>True when the clipboard holds <paramref name="text"/>; false with a problem code when it does not.</summary>
    bool TrySetText(string text, out string? problem);
}
