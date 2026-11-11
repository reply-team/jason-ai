using System.Text.Json;
using Jason.Contracts.Discovery;
using Jason.Contracts.Update;

namespace Jason.Cli.Installation;

/// <summary>
/// A jason the desktop application carries inside its package: the file, the record beside it, and the version that
/// record says the package was prepared as. Null, where it is asked for, means this jason is not one the desktop carries.
/// </summary>
/// <remarks>
/// <para>
/// <b>The record says whose; the path says nothing.</b> The desktop's preparation puts <c>version.json</c> beside the
/// executable it carries — a JSON object whose <c>version</c> names the version the package was built at — and every
/// package since the desktop's first release holds it. That file, in that shape, is how a jason knows it is carried. A
/// folder named like a package's is not enough, and a copy of the carried folder made elsewhere, with its record, is
/// carried too.
/// </para>
/// <para>
/// Asked by the verbs that would otherwise change the file: <c>jason update apply</c> and <c>rollback</c>, which would
/// replace it, and <c>jason uninstall</c>, which would delete it. The desktop's own update replaces the application and the
/// jason inside it together; the system's way of removing the application removes both.
/// </para>
/// </remarks>
/// <param name="Executable">The carried executable, with any link resolved.</param>
/// <param name="Record">The record beside it.</param>
/// <param name="Version">The version the record names.</param>
public sealed record CarriedExecutable(string Executable, string Record, string Version)
{
    /// <summary>The record's name, in the executable's own directory.</summary>
    public const string RecordName = "version.json";

    /// <summary>
    /// Whether the installation this environment runs as is carried: the file <see cref="CliEnvironment.InstallPath"/>
    /// names, or the one this process runs as. Null where there is no single installed file, or no record beside it.
    /// </summary>
    public static CarriedExecutable? Of(CliEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        return (env.InstallPath ?? SelfExecutable.InstalledImage) is { } installed ? Of(installed) : null;
    }

    /// <summary>
    /// The rule itself: the record beside <paramref name="executable"/>, read as the desktop's preparation writes it. A
    /// file of that name that cannot be read, or that is not an object naming a version, is somebody else's, and the
    /// executable beside it is Jason's own.
    /// </summary>
    public static CarriedExecutable? Of(string executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);

        var image = Path.GetFullPath(Resolved(executable));
        if (Path.GetDirectoryName(image) is not { } directory)
        {
            return null;
        }

        var record = Path.Combine(directory, RecordName);
        string text;
        try
        {
            text = File.ReadAllText(record);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("version", out var named)
                && named.ValueKind == JsonValueKind.String
                && SemanticVersion.TryParse(named.GetString(), out var version)
                ? new CarriedExecutable(image, record, version.ToString())
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The file a path names with every link on the way resolved, or the path itself where it is no link or cannot be read.</summary>
    private static string Resolved(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }
}
