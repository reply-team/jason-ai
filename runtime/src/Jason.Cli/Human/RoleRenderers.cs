using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the role verbs. Every renderer returns null when the body is not what it
/// expects, and the runner falls back to printing the response as it came.
/// </summary>
public static class RoleRenderers
{
    private const int Label = 15;

    /// <summary>One role, with the command that launches it spelled the way a shell would show it.</summary>
    public static string? Role(string json)
    {
        var role = RenderText.Read<RoleDto>(json);
        if (role?.Id is null || role.Name is null)
        {
            return null;
        }

        return RenderText.Lines(
        [
            Line("Id:", role.Id),
            Line("Name:", role.Name),
            Line("Builtin:", role.Builtin ? "yes" : "no"),
            Line("Description:", role.Description),
            Line("Entry command:", EntryCommand(role.EntryCommand)),
            Line("Defaults:", RenderText.Keys(role.ProfileDefaults)),
            Line("Profile:", role.ExecutionProfile),
            Line("Created:", RenderText.Moment(role.CreatedAt)),
            Line("Updated:", RenderText.Moment(role.UpdatedAt)),
        ]);
    }

    /// <summary>The registry as a table: what exists, what ships with Jason, and what can actually be launched.</summary>
    public static string? RoleList(string json)
    {
        var page = RenderText.Read<Page<RoleDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var table = new HumanTable("NAME", "BUILTIN", "PROFILE", "ENTRY COMMAND", "ID");
        foreach (var role in page.Items)
        {
            table.Row(role.Name, role.Builtin ? "yes" : "no", role.ExecutionProfile, EntryCommand(role.EntryCommand), role.Id);
        }

        return RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    private static string? EntryCommand(IReadOnlyList<string>? entryCommand) =>
        entryCommand is { Count: > 0 } ? string.Join(' ', entryCommand) : null;

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : value);
}
