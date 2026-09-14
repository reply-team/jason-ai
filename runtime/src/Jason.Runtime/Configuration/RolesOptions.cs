namespace Jason.Runtime.Configuration;

/// <summary>
/// How a role is launched when the role itself names no entry command: one command for every role, which is
/// how a single agent host serves the whole roster.
/// </summary>
public sealed class RolesOptions
{
    public const string Section = "Roles";

    public List<string> DefaultEntryCommand { get; set; } = [];
}
