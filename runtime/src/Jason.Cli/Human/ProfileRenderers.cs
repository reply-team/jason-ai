using System.Globalization;
using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the profile verbs. Every renderer returns null when the body is not what it
/// expects, and the runner falls back to printing the response as it came.
/// </summary>
public static class ProfileRenderers
{
    private const int Label = 16;

    /// <summary>
    /// One profile, at the revision that was asked for. The revision line says which one this is, and says so
    /// against the one in force — reading an old revision and thinking it is the live one is the one mistake
    /// this page can cause.
    /// </summary>
    public static string? Profile(string json)
    {
        var profile = RenderText.Read<ExecutionProfileDto>(json);
        if (profile?.Id is null || profile.Name is null || profile.Revision is null)
        {
            return null;
        }

        return RenderText.Lines(
        [
            Line("Id:", profile.Id),
            Line("Name:", profile.Name),
            Line("Description:", profile.Description),
            Line("Host:", RenderText.Snake(profile.Revision.Host)),
            Line("Program:", profile.Revision.Program),
            Line("Args:", Args(profile.Revision.Args)),
            Line("Deny:", Deny(profile.Revision.Deny)),
            Line("CLI command:", profile.Revision.CliCommand),
            Line("Host verified:", profile.Revision.HostVersionVerified),
            Line("Revision:", Revision(profile)),
            Line("Disabled:", profile.Disabled ? "yes" : "no"),
            Line("Created:", RenderText.Moment(profile.CreatedAt)),
            Line("Updated:", RenderText.Moment(profile.UpdatedAt)),
        ]);
    }

    /// <summary>The registry as a table: what can launch work, what it starts, and which revision is in force.</summary>
    public static string? ProfileList(string json)
    {
        var page = RenderText.Read<Page<ExecutionProfileDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var table = new HumanTable("NAME", "HOST", "PROGRAM", "REVISION", "DISABLED", "ID");
        foreach (var profile in page.Items)
        {
            table.Row(
                profile.Name,
                profile.Revision is null ? null : RenderText.Snake(profile.Revision.Host),
                profile.Revision?.Program,
                profile.CurrentRevision.ToString(CultureInfo.InvariantCulture),
                profile.Disabled ? "yes" : "no",
                profile.Id);
        }

        return RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    /// <summary>The revision on the page, and the one in force when they are not the same one.</summary>
    private static string Revision(ExecutionProfileDto profile) =>
        profile.Revision.Number == profile.CurrentRevision
            ? profile.CurrentRevision.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{profile.Revision.Number} (in force: {profile.CurrentRevision})");

    /// <summary>The arguments as a shell would show them; the deny rules as the list they are.</summary>
    private static string? Args(IReadOnlyList<string>? args) => args is { Count: > 0 } ? string.Join(' ', args) : null;

    private static string? Deny(IReadOnlyList<string>? deny) => deny is { Count: > 0 } ? string.Join(", ", deny) : null;

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : value);
}
