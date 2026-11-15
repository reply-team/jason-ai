using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Profiles;

/// <summary>
/// <c>profile.set_default</c> and <c>profile.unset_default</c>: the configured default execution profile,
/// <c>Roles:DefaultExecutionProfile</c>, named and taken away through the runtime rather than by a hand edit of the
/// settings file. Each writes the file through the runtime's one writer of it (<see cref="SettingsWriter"/>), which keeps
/// every byte it does not own and replaces the file by a temporary file and a rename, and answers once the value is the
/// one the runtime works from: the next claim resolves to it, with no restart.
/// </summary>
/// <remarks>
/// <para>
/// <b>A name that answers.</b> Settings are read before the database is open, so a name written by hand is held to the
/// shape of a name and nothing more, and a name nothing answers is the claim's visible refusal. Through the runtime the
/// database is open, so the name must answer a profile when it is written (<c>profile_not_found</c>); a disabled one is
/// still a profile, and what it means for work is the claim's, as at every other level. A profile is never renamed or
/// deleted, so the name keeps answering it.
/// </para>
/// <para>
/// <b>In force, or put back.</b> Writing the file loads it and tells every section at once, so the options the claim reads
/// answer the new value when the write returns. Where they do not — another setting of the Roles section a person broke by
/// hand keeps the runtime on the last Roles settings that validated — the act is refused as <c>setting_not_in_force</c>
/// and the file is put back. No plugin reload runs: nothing in the section is frozen by one.
/// </para>
/// <para>
/// <b>One line per change.</b> A write that changed the value in force is journaled as <c>default_profile_updated</c>, in the
/// same act; the state already asked for writes nothing and says nothing.
/// </para>
/// </remarks>
public sealed class DefaultProfileService(
    JasonDbContext db,
    JournalWriter journal,
    ReloadGate gate,
    SettingsWriter writer,
    SettingsFile settingsFile,
    LiveSettings<RolesOptions> roles)
{
    /// <summary>The setting both acts own, as the configuration system spells its path.</summary>
    public const string Setting = RolesOptions.Section + ":" + nameof(RolesOptions.DefaultExecutionProfile);

    public async Task<ProfileDefaultDto> SetAsync(ProfileSetDefaultRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        ProfileValidation.ValidateSetDefault(request, errors);
        errors.ThrowIfAny();
        await ActorVerification.VerifyAsync(db, actor, cancellationToken).ConfigureAwait(false);

        var name = request.Name!.Trim();
        if (!await db.ExecutionProfiles.AnyAsync(profile => profile.Name == name, cancellationToken).ConfigureAwait(false))
        {
            throw DomainErrors.ProfileNotFound(name);
        }

        return await WriteAsync([SettingsEdit.Set(Setting, JsonValue.Create(name))], name, actor, Text(request.Reason), cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProfileDefaultDto> UnsetAsync(ProfileUnsetDefaultRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        ProfileValidation.ValidateUnsetDefault(request, errors);
        errors.ThrowIfAny();
        await ActorVerification.VerifyAsync(db, actor, cancellationToken).ConfigureAwait(false);

        return await WriteAsync(
            [SettingsEdit.Remove(Setting), SettingsEdit.RemoveIfEmpty(RolesOptions.Section)],
            wanted: null,
            actor,
            Text(request.Reason),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Both acts' one shape: under the reload gate, which the writer runs under, the file is edited; then the value in force
    /// is read. A file the edit left as it was may say what was asked while its watch has not yet told the runtime, so it is
    /// read now; a value in force that is still not the one asked for is refused, and a written file is put back.
    /// </summary>
    private async Task<ProfileDefaultDto> WriteAsync(IReadOnlyList<SettingsEdit> edits, string? wanted, ActorRef actor, string? reason, CancellationToken cancellationToken)
    {
        await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var before = roles.Current.DefaultExecutionProfile;
            return await writer.WriteAsync(
                edits,
                RolesOptions.Section,
                async (wrote, token) =>
                {
                    if (!wrote && Same(before, wanted))
                    {
                        return new ProfileDefaultDto(before);
                    }

                    if (!wrote)
                    {
                        settingsFile.Read();
                    }

                    var now = roles.Current.DefaultExecutionProfile;
                    if (!Same(now, wanted))
                    {
                        throw DomainErrors.SettingNotInForce(Setting, RolesOptions.Section);
                    }

                    if (wrote && !Same(before, now))
                    {
                        journal.Append(
                            db,
                            actor,
                            JournalKinds.DefaultProfileUpdated,
                            campaign: null,
                            key: Setting,
                            old: before is null ? null : JsonValue.Create(before),
                            updated: now is null ? null : JsonValue.Create(now),
                            reason: reason);
                        await db.SaveChangesAsync(token).ConfigureAwait(false);
                    }

                    return new ProfileDefaultDto(now);
                },
                (refused, putBack, _) => PutBackSaid(refused, putBack),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    /// <summary>A refusal after the write, with what became of the file said in one more detail under the settings file's own field.</summary>
    private static DomainException PutBackSaid(DomainException refused, SettingsPutBack putBack)
    {
        var detail = putBack switch
        {
            SettingsPutBack.PutBack => "The settings file was put back as it was; the default in force is unchanged.",
            SettingsPutBack.ChangedSince => "The settings file was changed by another writer after this one wrote it, so it was left as that writer left it; the default in force is unchanged.",
            SettingsPutBack.Held => "Another program held the settings file open, so it was not put back: it holds the default this wrote, which is not in force. Mend the Roles section, or put the default back by hand.",
            _ => "The settings file could not be written again, so it was not put back: it holds the default this wrote, which is not in force. Mend the Roles section, or put the default back by hand.",
        };
        return new DomainException(
            refused.StatusCode,
            refused.Code,
            refused.Message,
            refused.Retryable,
            [.. refused.Details ?? [], new ErrorDetail(PluginLoader.SettingsFileField, SettingsWriter.Code(putBack), detail)]);
    }

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.Ordinal);

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
