using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Harness;
using Jason.Contracts.Plugins;
using Jint;
using Jint.Native;
using Jint.Runtime;

namespace Jason.PluginHost.Sdk;

/// <summary>
/// <c>host.appendLine(path, text)</c>: the one way a plugin writes a file. It adds one line to a file directly in a
/// directory a person granted, and it creates, never truncates, renames or deletes. Whatever breaks a rule ends the
/// invocation, since no plugin code can make that right; only what the disk did is a failure the plugin may catch.
/// </summary>
/// <remarks>
/// Several hosts write one file at the same moment, so each line takes its own open, and the open keeps every other writer
/// out for as long as the line takes: <c>FileShare.Read</c> on Windows, which leaves a reader in, and <c>FileShare.None</c>
/// elsewhere, an advisory lock that only another writer takes, so a reader is not stopped there either. Four processes
/// measured it: any wider share loses lines. Elsewhere the file is opened for reading as well, because a write-only open of
/// a named pipe would wait for a reader.
/// A path never reaches a message, a detail or a diagnostic record: a directory is a person's, and one set through a
/// plugin's configuration is a known secret that would read as a mask.
/// </remarks>
public sealed class FilesService(HostServices services)
{
    public const string Function = "host.appendLine";

    /// <summary>What a line added is called on stderr.</summary>
    public const string AppendMessage = "append";

    /// <summary>What a line the disk refused is called on stderr.</summary>
    public const string FailedMessage = "append_failed";

    private const int MaxNameLength = 100;

    /// <summary>Windows' sharing violation and lock violation, the low word of an I/O error's result.</summary>
    private const int WindowsSharingViolation = 32;

    private const int WindowsLockViolation = 33;

    /// <summary>The raw error a lock held elsewhere comes back with: <c>EWOULDBLOCK</c> on macOS and on Linux.</summary>
    private const int MacWouldBlock = 35;

    private const int LinuxWouldBlock = 11;

    private static readonly TimeSpan SharingPause = TimeSpan.FromMilliseconds(2);

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>How long a file another writer holds is waited for, inside what is left of the invocation.</summary>
    internal TimeSpan SharingWait { get; init; } = TimeSpan.FromSeconds(10);

    public JsValue Append(Engine engine, JsValue[] args)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || !args[0].IsString())
        {
            throw ArgumentReader.TypeError(engine, $"{Function}: the path must be a string.");
        }

        if (args.Length < 2 || !args[1].IsString())
        {
            throw ArgumentReader.TypeError(engine, $"{Function}: the text must be a string.");
        }

        var grants = services.Grants.Files
            ?? throw new HostRuleException(
                HostOutcomeCodes.CapabilityNotGranted,
                "This plugin was not granted the files capability.",
                new JsonObject { ["capability"] = "files" });

        var (directory, name) = Place(args[0].AsString(), grants);
        Write(engine, directory, name, Line(args[1].AsString()));
        return JsValue.Undefined;
    }

    /// <summary>The granted directory a path stands directly in and the file it names there, or the rule's refusal.</summary>
    private static (FileDirectoryGrant Directory, string Name) Place(string path, FilesGrants grants)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw PathRefused();
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception unreadable) when (unreadable is ArgumentException or PathTooLongException or NotSupportedException)
        {
            throw PathRefused();
        }

        // Windows takes dots and spaces off the end of a path as it makes it full, so a name is also held to what was given.
        var name = Path.GetFileName(full);
        if (!IsFileName(name) || !path.EndsWith(name, StringComparison.Ordinal))
        {
            throw PathRefused();
        }

        var folder = Path.GetDirectoryName(full);
        foreach (var granted in grants.Directories)
        {
            if (folder is not null && (FolderPaths.Same(folder, FolderPaths.Normal(granted.Written)) || FolderPaths.Same(folder, FolderPaths.Normal(granted.Real))))
            {
                return (granted, name);
            }
        }

        throw PathRefused();
    }

    /// <summary>
    /// A letter or digit first, then letters, digits, dots, dashes and underscores, and never a device Windows reserves in
    /// any folder, so a package behaves the same everywhere. A last dot is refused for the same reason: Windows would drop it.
    /// </summary>
    private static bool IsFileName(string name)
    {
        if (name.Length is 0 or > MaxNameLength || !char.IsAsciiLetterOrDigit(name[0]) || name[^1] == '.')
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_'))
            {
                return false;
            }
        }

        var dot = name.IndexOf('.', StringComparison.Ordinal);
        return !IsDeviceName(dot < 0 ? name : name[..dot]);
    }

    private static bool IsDeviceName(string stem) =>
        stem.ToUpperInvariant() switch
        {
            "CON" or "NUL" or "PRN" or "AUX" => true,
            [.. "COM" or "LPT", >= '1' and <= '9'] => true,
            _ => false,
        };

    /// <summary>The bytes of one line with its line feed, or the rule's refusal. A line is data, as a request's body is: nothing masks it.</summary>
    private byte[] Line(string text)
    {
        if (text.AsSpan().IndexOfAny('\n', '\r') >= 0)
        {
            throw new HostRuleException(
                HostOutcomeCodes.FileLineMultiline,
                $"{Function} takes one line, and this text holds a line break.");
        }

        var max = services.Limits.Files.LineBytes;
        var count = Utf8.GetByteCount(text);
        if (count >= max)
        {
            throw new HostRuleException(
                HostOutcomeCodes.FileLineTooLarge,
                FormattableString.Invariant($"A line may be at most {max} bytes with its line feed."),
                new JsonObject { ["limit"] = "files.line_bytes", ["max"] = max });
        }

        var line = new byte[count + 1];
        Utf8.GetBytes(text, line);
        line[count] = (byte)'\n';
        return line;
    }

    /// <summary>
    /// The line, on the disk before the call returns. Everything that touches the disk gets the one answer the disk may be
    /// given, a failure the plugin can catch; what is refused by rule before the call is counted spends none. The directory
    /// is checked again once it is there, since that is when a part of it missing at the load first has an answer. Between
    /// the last check and the open a program under the same account could put a link there; a plugin cannot make one.
    /// </summary>
    private void Write(Engine engine, FileDirectoryGrant directory, string name, byte[] line)
    {
        var file = Path.Combine(directory.Real, name);
        var record = new JsonObject { ["file"] = name, ["bytes"] = line.Length };
        try
        {
            RefuseAMove(directory);
            if (Directory.Exists(file) || new FileInfo(file).LinkTarget is not null)
            {
                throw PathRefused();
            }

            services.Budget.Files();
            Directory.CreateDirectory(directory.Real);
            RefuseAMove(directory);

            using var stream = Open(file);
            if (!stream.CanSeek)
            {
                throw PathRefused();
            }

            stream.Seek(0, SeekOrigin.End);
            stream.Write(line);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            services.Diagnostics.Host("warn", FailedMessage, record);
            throw new JavaScriptException(HostFailure.Build(
                engine,
                "transient",
                HostOutcomeCodes.FileWriteFailed,
                $"The line could not be appended to {name}.",
                new JsonObject { ["file"] = name }));
        }

        services.Diagnostics.Host("info", AppendMessage, record);
    }

    /// <summary>Whether the directory still leads where it did at the load: a place nobody granted is not reached through one somebody did.</summary>
    private static void RefuseAMove(FileDirectoryGrant directory)
    {
        if (!FolderPaths.Same(FolderPaths.Real(directory.Written), directory.Real))
        {
            throw PathRefused();
        }
    }

    /// <summary>The file to this writer alone, waiting for another that holds it for as long as a line takes, within the wait and what is left of the invocation.</summary>
    private FileStream Open(string file)
    {
        var wait = services.Remaining();
        wait = wait < SharingWait ? wait : SharingWait;
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            try
            {
                return OperatingSystem.IsWindows()
                    ? new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read, 1, FileOptions.WriteThrough)
                    : new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.WriteThrough);
            }
            catch (IOException held) when (IsHeld(held) && Stopwatch.GetElapsedTime(started) < wait)
            {
                services.Deadline.WaitHandle.WaitOne(SharingPause);
            }
        }
    }

    private static bool IsHeld(IOException exception)
    {
        if (OperatingSystem.IsWindows())
        {
            return (exception.HResult & 0xFFFF) is WindowsSharingViolation or WindowsLockViolation;
        }

        return exception.HResult == (OperatingSystem.IsMacOS() ? MacWouldBlock : LinuxWouldBlock);
    }

    /// <summary>One sentence for every path the door will not take: it says nothing of the path, which is a person's.</summary>
    private static HostRuleException PathRefused() =>
        new(
            HostOutcomeCodes.FilePathNotAllowed,
            $"{Function} writes a plain file, under a name of letters, digits, dots, dashes and underscores, directly in a granted directory.");
}
