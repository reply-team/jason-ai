namespace Jason.Cli.Session;

/// <summary>
/// The first message of a session on Jason itself (<c>jason session open --setup</c>): two lines that say what the session
/// is for, then a short core — where the parts are read, what is never touched, and the skill that teaches the rest of the
/// job, by its name. The skill's own text is not carried: a host's command line is the only way a first message reaches an
/// interactive session, and a long one is not known to survive every launch, so the brief stays short and the skill is
/// read where it is deployed.
/// </summary>
/// <remarks>
/// It spells no path and no part of the layout: the session reads every folder from <c>jason status</c>, so a layout that
/// moves a folder changes nothing here. It carries no secret, token or id, since a command line is readable by every
/// process of the account.
/// </remarks>
public static class SetupBrief
{
    /// <summary>The skill that teaches the job, named by the brief and looked for in the host's skills root.</summary>
    public const string SkillName = "customising-jason";

    /// <summary>The two lines in front of the core.</summary>
    public static string Lead { get; } = string.Join(
        "\n",
        "You are in a session for changing this Jason installation itself: its roles, its plugins and its settings.",
        "Follow the instructions below; ask the person what they want changed if they have not said.");

    /// <summary>The core: where the parts are, what is never touched, and the skill.</summary>
    public static string Core { get; } = string.Join(
        "\n",
        "Where the parts are: run `jason status` and read the `setup` section of what it prints. It names the role skills folder, the plugins folder, the custom UI folder and the settings file. Read every folder there; never assume one.",
        string.Empty,
        "What you never touch:",
        "- The settings file, while `settings_file_holds_secrets` is true in that section: plugins' secrets are kept in it. Never open it.",
        "- Anything of Jason's outside the role skills folder and the plugins folder.",
        "- A secret: it is the person's own to type. Never ask them to give one to you.",
        string.Empty,
        $"The skill `{SkillName}` teaches the rest of this job; use it.");

    /// <summary>The whole brief: the two lines, a blank line, the core.</summary>
    public static string Text { get; } = Lead + "\n\n" + Core;
}
