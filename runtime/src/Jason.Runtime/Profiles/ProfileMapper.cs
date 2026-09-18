using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Profiles;

/// <summary>The stored profile as the API answers with it: the row, and one revision beside it.</summary>
public static class ProfileMapper
{
    /// <summary><paramref name="revision"/> is the one asked for, which is the current one unless a caller named another.</summary>
    public static ExecutionProfileDto ToDto(ExecutionProfile profile, ExecutionProfileRevision revision)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(revision);

        return new ExecutionProfileDto(
            profile.PublicId,
            profile.Name,
            profile.Description,
            profile.DisabledAt is not null,
            profile.CurrentRevision,
            ToDto(revision),
            WorkItemMapper.Utc(profile.CreatedAt),
            WorkItemMapper.Utc(profile.UpdatedAt));
    }

    /// <summary>
    /// One revision, with the command the launched agent will actually type. A profile that named none stored
    /// null, and answering null would leave the reader to work out what the runtime would do; the answer is the
    /// runtime's own executable name, so that is what is said.
    /// </summary>
    public static ExecutionProfileRevisionDto ToDto(ExecutionProfileRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);

        return new ExecutionProfileRevisionDto(
            revision.Number,
            revision.Host,
            revision.Program,
            revision.Args,
            revision.Deny,
            revision.CliCommand ?? ProfileService.DefaultCliCommand,
            revision.HostVersionVerified,
            WorkItemMapper.Utc(revision.CreatedAt));
    }
}
