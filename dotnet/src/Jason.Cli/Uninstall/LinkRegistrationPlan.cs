using Jason.Contracts.Notifications;

namespace Jason.Cli.Uninstall;

/// <summary>The <c>jason:</c> link registration as this account's classes hold it.</summary>
/// <param name="Marker">What its <see cref="LinkRegistration.MarkerValue"/> holds, or null where it holds nothing.</param>
/// <param name="Command">The command the shell runs for a link, or null where there is none.</param>
public sealed record LinkRegistrationRead(string? Marker, string? Command);

/// <summary>What an uninstall does with the <c>jason:</c> link registration.</summary>
public enum LinkRegistrationFate
{
    /// <summary>Jason's marker, and this installation's command or none: it goes.</summary>
    Removed,

    /// <summary>Jason's marker and another installation's command: that one's own uninstall removes it.</summary>
    Kept,

    /// <summary>No Jason marker: another program's, never touched.</summary>
    Untouched,
}

/// <summary>The link registration's row of an uninstall plan.</summary>
/// <param name="Fate">What becomes of it.</param>
/// <param name="Command">The command it holds, named in the plan where it is kept.</param>
public sealed record LinkRegistrationPlan(LinkRegistrationFate Fate, string? Command)
{
    /// <summary>
    /// The one decision, made when the plan is read and made again by the remover just before the delete; null where
    /// there is no key.
    /// </summary>
    /// <param name="read">The registration as it stands.</param>
    /// <param name="command">
    /// The command this installation's toast host writes, compared as the host compares it, without regard to case; null
    /// for an installation whose path no command can hold, which writes no link.
    /// </param>
    public static LinkRegistrationPlan? Of(LinkRegistrationRead? read, string? command)
    {
        if (read is null)
        {
            return null;
        }

        if (!string.Equals(read.Marker, LinkRegistration.Marker, StringComparison.Ordinal))
        {
            return new LinkRegistrationPlan(LinkRegistrationFate.Untouched, null);
        }

        return read.Command is null || (command is not null && LinkRegistration.SameCommand(read.Command, command))
            ? new LinkRegistrationPlan(LinkRegistrationFate.Removed, read.Command)
            : new LinkRegistrationPlan(LinkRegistrationFate.Kept, read.Command);
    }
}
