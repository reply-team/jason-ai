using System.Text.RegularExpressions;

namespace Jason.Cli.Installation;

/// <summary>
/// The files Jason Desktop writes so that a terminal finds <c>jason</c>, read and written by the same shapes the
/// application's own <c>desktop/src/command-line.ts</c> spells: on macOS and Linux a <c>#!/bin/sh</c> wrapper with the
/// application's marker, on Windows a <c>jason.cmd</c> shim and a wrapper beside it for Git Bash.
/// </summary>
/// <remarks>
/// <para>
/// <c>jason install</c> writes the wrapper on macOS in exactly the application's shape, so the application's Command
/// Line item reads it back as its own and the two never take each other's file for another program's; and it takes the
/// Windows files back by the shapes alone, a file that is not one of them being somebody else's. A test holds the marker
/// and the wrapper's lines to the application's source.
/// </para>
/// </remarks>
public static partial class CommandFiles
{
    /// <summary>The wrapper's second line, by which a wrapper of Jason's is told from any other file.</summary>
    public const string WrapperMarker = "# Written by the Jason desktop application.";

    /// <summary>The names the application gives a command: <c>jason</c>, or <c>jason-desktop</c> beside another <c>jason</c>.</summary>
    public static IReadOnlyList<string> Names { get; } = ["jason", "jason-desktop"];

    /// <summary>
    /// The wrapper: the executable with the arguments it was given and nothing else, and the data directory only where one
    /// is set for this installation (<c>JASON_DATA_DIR</c>); a terminal and the application then use the same one.
    /// </summary>
    public static string Wrapper(string executable, string? dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);

        var environment = dataDirectory is null ? string.Empty : $"JASON_DATA_DIR={ShellQuote(dataDirectory)} ";
        return string.Join('\n', "#!/bin/sh", WrapperMarker, $"{environment}exec {ShellQuote(executable)} \"$@\"", string.Empty);
    }

    /// <summary>The executable and data directory a wrapper of Jason's runs, or null for a file that is not one.</summary>
    public static (string Executable, string? DataDirectory)? WrapperTarget(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var match = WrapperShape().Match(text);
        if (!match.Success)
        {
            return null;
        }

        return (Unquote(match.Groups["exe"].Value), match.Groups["data"].Success ? Unquote(match.Groups["data"].Value) : null);
    }

    /// <summary>The executable a Windows shim of Jason's runs, or null for a file that is not one.</summary>
    public static string? ShimTarget(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var match = ShimShape().Match(text);
        return match.Success ? match.Groups["exe"].Value : null;
    }

    /// <summary>Whether a file's text is one of the application's command files, of either shape.</summary>
    public static bool IsOurs(string text) => WrapperTarget(text) is not null || ShimTarget(text) is not null;

    /// <summary>Whether a path is a regular file, no link: what a command file of the application's is.</summary>
    public static bool IsRegularFile(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.LinkTarget is null;
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The text of a small file, or null for one that is larger than a command file ever is, or cannot be read.</summary>
    public static string? ReadSmall(string path)
    {
        try
        {
            return new FileInfo(path).Length > 64 * 1024 ? null : File.ReadAllText(path);
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Whether a path is a regular file holding the application's wrapper.</summary>
    public static bool IsWrapperFile(string path) => IsRegularFile(path) && ReadSmall(path) is { } text && WrapperTarget(text) is not null;

    private static string ShellQuote(string value) => "'" + value.Replace("'", @"'\''", StringComparison.Ordinal) + "'";

    private static string Unquote(string value) => value.Replace(@"'\''", "'", StringComparison.Ordinal);

    [GeneratedRegex("""^#!/bin/sh\n# Written by the Jason desktop application\.\n(?:JASON_DATA_DIR='(?<data>(?:[^']|'\\'')*)' )?exec '(?<exe>(?:[^']|'\\'')*)' "\$@"\n\z""", RegexOptions.CultureInvariant)]
    private static partial Regex WrapperShape();

    [GeneratedRegex("""^@echo off\r\n(?:setlocal\r\nset "JASON_DATA_DIR=[^"%\r\n]*"\r\n)?"(?<exe>[^"%\r\n]+)" %\*\r\n\z""", RegexOptions.CultureInvariant)]
    private static partial Regex ShimShape();
}
