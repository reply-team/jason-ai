using Jason.Contracts.Configuration;
using Jason.Contracts.Discovery;
using Jason.Runtime.Domain;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Configuration;

/// <summary>What became of the settings file when what the write was for refused after it.</summary>
public enum SettingsPutBack
{
    /// <summary>The file holds again, byte for byte, what it held before the write.</summary>
    PutBack,

    /// <summary>Another writer changed the file after this one wrote it, so it was left as that writer left it.</summary>
    ChangedSince,

    /// <summary>Another program held the file, so it could not be put back: it still holds what this wrote.</summary>
    Held,

    /// <summary>The file could not be written again — it was made read-only, or the disk filled — so it still holds what this wrote.</summary>
    NotWritable,
}

/// <summary>
/// The runtime's one writer of the user's settings file. It reads the file, edits the paths its caller owns and keeps
/// every other byte (<see cref="SettingsDocument"/>), writes the result by a temporary file and a rename only if the file
/// still holds what was read, and then runs what makes the edit true — the plugin reload, for every caller there is. When
/// that refuses, the file is put back whole, and the caller is told whether it was.
/// </summary>
/// <remarks>
/// <para>
/// <b>One at a time.</b> The caller holds the reload gate (<see cref="Plugins.Registry.ReloadGate"/>) through the whole
/// act, so no other write and no reload of this runtime runs in between; another program's write is what the compare
/// guards against (<see cref="JasonConfiguration.CommitSettingsFile"/>).
/// </para>
/// <para>
/// <b>What it says.</b> Its log lines name the section and a code, and nothing else: never a value of the file, a path
/// a person chose, or what the edit wrote. Its refusals name the setting by its path, each key written as
/// <see cref="SettingKey.ShownPath"/> writes it, and the file's place for one the parser stopped in. A secret sits
/// beside every route in this file.
/// </para>
/// <para>
/// <b>A live edit never stops the runtime.</b> A refused write leaves the file and the settings in force as they were.
/// </para>
/// </remarks>
public sealed class SettingsWriter(SettingsFile file, JasonPaths paths, ILogger<SettingsWriter> logger, Action<int>? beforeCommit = null)
{
    /// <summary>How many times the act is made again from a fresh read when another writer changed the file meanwhile.</summary>
    public const int Attempts = 3;

    private static readonly Action<ILogger, string, Exception?> Wrote = LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(1, nameof(Wrote)),
        "The settings writer wrote the {Section} section of the settings file");

    private static readonly Action<ILogger, string, string, Exception?> Refused = LoggerMessage.Define<string, string>(
        LogLevel.Information,
        new EventId(2, nameof(Refused)),
        "The settings writer wrote nothing to the {Section} section of the settings file: {Code}");

    private static readonly Action<ILogger, string, string, Exception?> Restored = LoggerMessage.Define<string, string>(
        LogLevel.Information,
        new EventId(3, nameof(Restored)),
        "The settings writer's edit of the {Section} section was refused after it was written: {Code}");

    /// <summary>
    /// Edits the file and runs <paramref name="inForce"/>, which is told whether anything was written; when it throws
    /// after a write, the file is put back and the exception rethrown — a <see cref="DomainException"/> as
    /// <paramref name="refused"/> rewrites it with the put-back's outcome, anything else as it was.
    /// </summary>
    /// <param name="edits">Every path the caller owns, in order; nothing else in the file is touched.</param>
    /// <param name="section">The section the edits are in, for the log.</param>
    /// <exception cref="DomainException">
    /// <c>setting_shadowed</c>, <c>settings_file_unreadable</c>, <c>settings_file_not_editable</c>, <c>settings_file_changed</c>,
    /// <c>settings_file_busy</c> or <c>settings_file_not_writable</c>, each with nothing written.
    /// </exception>
    public async Task<T> WriteAsync<T>(
        IReadOnlyList<SettingsEdit> edits,
        string section,
        Func<bool, CancellationToken, Task<T>> inForce,
        Func<DomainException, SettingsPutBack, DomainException> refused,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(inForce);
        ArgumentNullException.ThrowIfNull(refused);

        if (edits.FirstOrDefault(edit => edit is not RemoveIfEmptyEdit && file.Shadowed(edit.Path)) is { } shadowed)
        {
            throw Refuse(section, DomainErrors.SettingShadowed(Shown(shadowed.Path)));
        }

        var (before, after) = Commit(edits, section);
        try
        {
            return await inForce(after is not null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (after is not null)
        {
            // Put back on every failure after the write — a refusal, a database that could not be written, a request that
            // was cancelled — since the edit is in force only once what makes it true has succeeded.
            var outcome = file.Commit(expected: after, next: before) switch
            {
                SettingsCommit.Committed => SettingsPutBack.PutBack,
                SettingsCommit.Changed => SettingsPutBack.ChangedSince,
                SettingsCommit.Busy => SettingsPutBack.Held,
                _ => SettingsPutBack.NotWritable,
            };
            Restored(logger, section, Code(outcome), null);
            if (failure is DomainException domain)
            {
                throw refused(domain, outcome);
            }

            throw;
        }
    }

    /// <summary>The put-back's outcome as the code its detail carries.</summary>
    public static string Code(SettingsPutBack outcome) => outcome == SettingsPutBack.PutBack ? "settings_file_put_back" : "settings_file_not_put_back";

    /// <summary>
    /// Reads, edits and commits, again from a fresh read while another writer changes the file in between, up to
    /// <see cref="Attempts"/> times. Answers the bytes before and after, with null after where nothing needed writing.
    /// </summary>
    private (byte[]? Before, byte[]? After) Commit(IReadOnlyList<SettingsEdit> edits, string section)
    {
        for (var attempt = 1; ; attempt++)
        {
            byte[]? before;
            try
            {
                before = file.Bytes();
            }
            catch (Exception held) when (held is IOException or UnauthorizedAccessException)
            {
                throw Refuse(section, JasonConfiguration.Unwritten(held, paths.UserSettingsFile) == SettingsCommit.Busy
                    ? DomainErrors.SettingsFileBusy()
                    : DomainErrors.SettingsFileNotWritable());
            }

            SettingsEditResult edited;
            try
            {
                edited = SettingsDocument.Edit(before, edits);
            }
            catch (SettingsFileRefusedException refusal)
            {
                throw Refuse(section, refusal.Refusal == SettingsRefusal.Unreadable
                    ? DomainErrors.SettingsFileUnreadable(refusal.Failure ?? new SettingsFileFailure(nameof(System.Text.Json.JsonException), null, null))
                    : DomainErrors.SettingsFileNotEditable(Shown(refusal.Setting ?? edits[0].Path), refusal.Refusal));
            }

            if (!edited.Changed)
            {
                return (before, null);
            }

            beforeCommit?.Invoke(attempt);
            switch (file.Commit(before, edited.Bytes))
            {
                case SettingsCommit.Committed:
                    Wrote(logger, section, null);
                    return (before, edited.Bytes);
                case SettingsCommit.Busy:
                    throw Refuse(section, DomainErrors.SettingsFileBusy());
                case SettingsCommit.NotWritable:
                    throw Refuse(section, DomainErrors.SettingsFileNotWritable());
                case SettingsCommit.Changed when attempt >= Attempts:
                    throw Refuse(section, DomainErrors.SettingsFileChanged());
            }
        }
    }

    private DomainException Refuse(string section, DomainException refusal)
    {
        Refused(logger, section, refusal.Code, null);
        return refusal;
    }

    private string Shown(string path) => SettingKey.ShownPath(path, SafeBinding.InstalledPluginIds(paths));
}
