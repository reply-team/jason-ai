using Jason.Cli.Installation;

namespace Jason.Cli.Tests.Installation;

/// <summary>The record a desktop package holds beside the jason it carries, planted as the desktop's preparation writes it.</summary>
public static class CarriedRecord
{
    /// <summary>The record exactly as the preparation writes it: the version, pretty-printed, and a newline.</summary>
    public const string AsPrepared = "{\n  \"version\": \"0.34.0\"\n}\n";

    /// <summary>Puts <paramref name="record"/> beside <paramref name="executable"/>, as a desktop package holds it.</summary>
    public static void Beside(string executable, string record = AsPrepared) =>
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(executable)!, CarriedExecutable.RecordName), record);
}
