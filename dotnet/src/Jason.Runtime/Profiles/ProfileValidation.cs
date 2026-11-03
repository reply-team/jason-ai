using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Contracts.Hosts;
using Jason.Contracts.Programs;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Profiles;

/// <summary>
/// What a profile has to look like before anything is written. Every rule here is refused now rather than
/// discovered at a launch: a profile is read when work is already claimed and an agent is about to start, and a
/// field nobody checked until then would fail an attempt for something a person typed days earlier.
/// </summary>
internal static partial class ProfileValidation
{
    /// <summary>The same shape as a role's name: work, campaigns, roles and settings all spell it from memory.</summary>
    public static Regex ProfileName { get; } = Name();

    public static void ValidateCreate(ProfileCreateRequest request, IReadOnlySet<string>? reserved, ValidationErrors errors, IAgentHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);

        ValidateName(request.Name, errors);
        ValidateDescription(request.Description, errors);
        ValidateHost(request.Host, errors);
        ValidateProgram(request.Program, host, errors);
        ValidateArgs(request.Args, reserved, errors);
        ValidateDeny(request.Deny, errors);
        ValidateTools(request.Tools, errors);
        ValidateCliCommand(request.CliCommand, errors);
        ValidateHostVersion(request.HostVersionVerified, errors);
        ValidateReason(request.Reason, errors);
    }

    /// <summary>
    /// A patch is checked for what it names. An absent field is not a value the caller offered, so there is
    /// nothing to hold it to; a present one is held to exactly the rule it would have been held to at creation.
    /// </summary>
    public static void ValidateUpdate(ProfileUpdateRequest request, IReadOnlySet<string>? reserved, ValidationErrors errors, IAgentHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);

        ValidateName(request.Name, errors);
        if (request.Description.IsSet)
        {
            ValidateDescription(request.Description.Value, errors);
        }

        if (request.Host.IsSet)
        {
            // Null would leave the revision without a host, and a revision is a whole statement of how to launch
            // something. Clearing it is refused the same way an unknown one is.
            ValidateHost(request.Host.Value, errors);
        }

        if (request.Program.IsSet)
        {
            ValidateProgram(request.Program.Value, host, errors);
        }

        if (request.Args.IsSet)
        {
            ValidateArgs(request.Args.Value, reserved, errors);
        }

        if (request.Deny.IsSet)
        {
            ValidateDeny(request.Deny.Value, errors);
        }

        if (request.Tools.IsSet)
        {
            ValidateTools(request.Tools.Value, errors);
        }

        if (request.CliCommand.IsSet)
        {
            ValidateCliCommand(request.CliCommand.Value, errors);
        }

        if (request.HostVersionVerified.IsSet)
        {
            ValidateHostVersion(request.HostVersionVerified.Value, errors);
        }

        ValidateReason(request.Reason, errors);
    }

    /// <summary>A read names a profile, and may name one of its revisions. They are numbered from 1.</summary>
    public static void ValidateGet(ProfileGetRequest request, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);

        ValidateName(request.Name, errors);
        if (request.Revision is < 1)
        {
            errors.Add("revision", "invalid", "revision must be 1 or more; revisions are numbered from 1.");
        }
    }

    /// <summary>Taking a profile out of service, or putting it back: the name, and why.</summary>
    public static void ValidateToggle(ProfileToggleRequest request, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);

        ValidateName(request.Name, errors);
        ValidateReason(request.Reason, errors);
    }

    /// <summary>
    /// The host as the closed vocabulary spells it. A value outside it is a name this build has no launcher for,
    /// and the message says what it does have rather than leaving the caller to guess.
    /// </summary>
    public static AgentHostKind? ParseHost(string? host) => AgentHosts.Parse(host);

    /// <summary>A granted tool as the closed vocabulary spells it, or nothing for a word outside it.</summary>
    public static AgentTool? ParseTool(string? tool)
    {
        var text = tool?.Trim();
        return text is null
            ? null
            : Enum.GetValues<AgentTool>()
                .Where(known => string.Equals(SnakeCaseEnumConverter<AgentTool>.Format(known), text, StringComparison.Ordinal))
                .Select(known => (AgentTool?)known)
                .FirstOrDefault();
    }

    /// <summary>The grant as it will be stored, once every entry is known to be a word of the vocabulary.</summary>
    public static List<AgentTool> Tools(IReadOnlyList<JsonNode?>? values) =>
        values is null ? [] : [.. values.Select(value => ParseTool(Text(value))!.Value)];

    private static void ValidateName(string? name, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("name", "required", "name is required.");
            return;
        }

        if (!ProfileName.IsMatch(name.Trim()))
        {
            errors.Add(
                "name",
                "invalid",
                string.Create(CultureInfo.InvariantCulture, $"name must be lowercase letters, digits and hyphens, starting with a letter, at most {ProfileService.MaxNameLength} characters."));
        }
    }

    private static void ValidateDescription(string? description, ValidationErrors errors)
    {
        if (description is not null && description.Trim().Length > ProfileService.MaxDescriptionLength)
        {
            errors.Add("description", "too_long", string.Create(CultureInfo.InvariantCulture, $"description must be at most {ProfileService.MaxDescriptionLength} characters."));
        }
    }

    private static void ValidateHost(string? host, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            errors.Add("host", "required", "host is required.");
            return;
        }

        if (ParseHost(host) is null)
        {
            errors.Add("host", "invalid", "host must be one of: " + AgentHosts.Vocabulary + ".");
        }
    }

    private static void ValidateProgram(string? program, IAgentHost? host, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(program))
        {
            errors.Add("program", "required", "program is required.");
            return;
        }

        if (program.Trim().Length > ProfileService.MaxProgramLength)
        {
            errors.Add("program", "too_long", string.Create(CultureInfo.InvariantCulture, $"program must be at most {ProfileService.MaxProgramLength} characters."));
            return;
        }

        // A path is handed to the operating system as it stands. A control character in one is never a file
        // somebody meant, and storing it would leave a profile that can only ever fail at the claim.
        if (program.Any(char.IsControl))
        {
            errors.Add("program", "invalid", "program must not contain control characters.");
        }

        // A script is started through a shell, which the program lookup refuses for every program Jason starts, so such a
        // profile could only ever fail at the claim. Refused here in the lookup's words; a bare name without the
        // extension is looked up on the search path (on Windows as an .exe, a .com or npm's own shim). Read as the operating system reads the path too, so
        // a script whose name Windows trims back to one is refused here and not started at the claim.
        if (ProgramResolver.NamesShellScript(program) && !NamesTheHostsLauncher(program, host))
        {
            errors.Add("program", "shell_script", ShellScriptMessage("program"));
        }
    }

    private static string ShellScriptMessage(string subject) =>
        subject + " must not be " + ProgramLookup.ShellScript + " (.cmd, .bat or .ps1): " + ProgramLookup.ProgramsNotShells + ". "
        + "Name the program itself, or a bare name without its extension, which Jason looks up on the search path (on Windows as an .exe, a .com or npm's own shim).";

    /// <summary>
    /// Whether a script is the launcher the profile's own host is installed as, named by its full path: only the host
    /// knows which file names those are, and neither a bare name, which is looked up on the search path, nor a relative
    /// path, which would be read against the runtime's own working directory, is one.
    /// </summary>
    private static bool NamesTheHostsLauncher(string program, IAgentHost? host)
    {
        var path = program.Trim();
        return host is not null
            && Path.IsPathFullyQualified(path)
            && host.NamesItsLauncher(Path.GetFileName(path));
    }

    /// <summary>The list as it will be stored, once it is known to be one. Blank entries never reach here.</summary>
    public static List<string> Strings(IReadOnlyList<JsonNode?>? values) =>
        values is null ? [] : [.. values.Select(value => value!.GetValue<string>().Trim())];

    private static void ValidateArgs(IReadOnlyList<JsonNode?>? args, IReadOnlySet<string>? reserved, ValidationErrors errors)
    {
        ValidateStrings(args, "args", ProfileService.MaxArgs, ProfileService.MaxArgLength, errors);
        if (args is null || reserved is null)
        {
            return;
        }

        // A host parser takes the last value it is given, and a profile's arguments go last, so an argument
        // naming a flag the runtime composed would silently replace it — making a session interactive,
        // unconfined, or unable to report, while every document still promised otherwise. Refused where it is
        // written, not dropped where it is launched: a profile that quietly lost half of what it said would be
        // its own kind of lie. Anything the host takes and the runtime does not compose — a model, say — is
        // untouched by this.
        for (var index = 0; index < args.Count; index++)
        {
            if (Text(args[index]) is not { } value)
            {
                continue;
            }

            if (ReservedFlag(value, reserved) is { } flag)
            {
                errors.Add(
                    string.Create(CultureInfo.InvariantCulture, $"args[{index}]"),
                    "reserved",
                    $"'{flag}' is composed by the runtime for every attempt and cannot be set by a profile.");
            }
        }
    }

    /// <summary>The flag an argument names, the text before any <c>=</c>, when the host composes it; otherwise null.</summary>
    private static string? ReservedFlag(string value, IReadOnlySet<string> reserved)
    {
        var flag = value.Trim();
        var separator = flag.IndexOf('=', StringComparison.Ordinal);
        if (separator > 0)
        {
            flag = flag[..separator];
        }

        return reserved.Contains(flag) ? flag : null;
    }

    /// <summary>
    /// What the revision's host says of its words, once the host is parsed, held to the revision as it would be launched.
    /// One rule for every host: a grant of a tool the host does not have is refused, naming the host and the tool; a deny
    /// of such a tool is kept as the person's word, since it holds anyway; a deny of a tool the host has and cannot take
    /// away, or of a word it does not read as a rule, is refused naming the host; and so is a grant of a tool the host
    /// has but Jason cannot make true, and arguments the host's own reading refuses. Each is <c>invalid</c>; stored
    /// arguments a patch leaves alone are also held to the host's reserved flags, each <c>reserved</c>, and a stored program it leaves alone to the host's rule for a script, <c>shell_script</c>. Every message
    /// names the host and the word; then the grant is held beside the deny. Where a patch did not name a list,
    /// the message says it is the profile's.
    /// </summary>
    public static void ValidateForHost(
        ExecutionProfileRevision revision,
        IAgentHost host,
        ValidationErrors errors,
        bool toolsNamed = true,
        bool denyNamed = true,
        bool argsNamed = true,
        bool programNamed = true)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(errors);

        var word = AgentHosts.Word(host.Kind);

        // The program a request names was held to this host where it was read. One it leaves alone is the stored one, and
        // a patch that moves the profile to another host would otherwise start a script that is this host's launcher
        // under a host that is not installed as one, or a script that is no launcher under this one.
        if (!programNamed && ProgramResolver.NamesShellScript(revision.Program) && !NamesTheHostsLauncher(revision.Program, host))
        {
            errors.Add("program", "shell_script", ShellScriptMessage("the profile's program"));
        }

        // Arguments a request names were held to this host's flags where they were read. Arguments it leaves alone are
        // the stored ones, and a patch that moves the profile to another host would otherwise launch them after the
        // flags that host composes, where the last value wins.
        if (!argsNamed)
        {
            for (var index = 0; index < revision.Args.Count; index++)
            {
                if (ReservedFlag(revision.Args[index], host.ReservedFlags) is { } flag)
                {
                    errors.Add(
                        string.Create(CultureInfo.InvariantCulture, $"args[{index}]"),
                        "reserved",
                        $"the profile's args[{index}] '{flag}' is composed by the runtime on {word} for every attempt and cannot be set by a profile.");
                }
            }
        }

        if (host.ArgsProblem(revision.Args) is { } refused)
        {
            errors.Add("args", "invalid", $"{(argsNamed ? "" : "the profile's ")}args are refused on {word}: {refused}");
        }

        var toolsOwner = toolsNamed ? "" : "the profile's ";
        for (var index = 0; index < revision.Tools.Count; index++)
        {
            var tool = revision.Tools[index];
            var field = string.Create(CultureInfo.InvariantCulture, $"tools[{index}]");
            var name = SnakeCaseEnumConverter<AgentTool>.Format(tool);
            if (host.GrantProblem(tool) is { } untrue)
            {
                errors.Add(field, "invalid", $"{toolsOwner}{field} grants {name}, which {word} has and Jason cannot make true on it: {untrue}");
            }
            else if (host.ToolRule(tool) is null)
            {
                errors.Add(field, "invalid", $"{toolsOwner}{field} grants {name}, which {word} does not have; a deny of it is kept, since it holds anyway.");
            }
        }

        var denyOwner = denyNamed ? "" : "the profile's ";
        for (var index = 0; index < revision.Deny.Count; index++)
        {
            if (host.DenyProblem(revision.Deny[index]) is { } undeniable)
            {
                var field = string.Create(CultureInfo.InvariantCulture, $"deny[{index}]");
                errors.Add(field, "invalid", $"{denyOwner}{field} '{revision.Deny[index]}' cannot be denied on {word}: {undeniable}");
            }
        }

        // A grant refused above is not held beside the deny as well: one problem per grant.
        ValidateGrantBesideDeny(
            revision.Tools,
            revision.Deny,
            tool => host.GrantProblem(tool) is null ? host.ToolRule(tool) : null,
            errors,
            toolsNamed,
            denyNamed);
    }

    /// <summary>
    /// A grant the revision's own deny takes away. The host's deny wins over an allow rule, so a tool granted and
    /// denied by the host's own name for it would be a grant no launch can use; the pair is refused on the grant,
    /// naming the deny. Only the bare name, spelled as the host spells it, collides: a deny that is a pattern, such as
    /// <c>WebFetch(domain:example.com)</c>, is not refused and is written beside the grant, and how much of the grant
    /// it leaves is the host's to decide. Where a patch did not name one of the two lists, the message says that
    /// index is the profile's, since the request carried no such list. A tool the host has no rule for collides with
    /// nothing.
    /// </summary>
    public static void ValidateGrantBesideDeny(
        IReadOnlyList<AgentTool> tools,
        IReadOnlyList<string> deny,
        Func<AgentTool, string?> rule,
        ValidationErrors errors,
        bool toolsNamed = true,
        bool denyNamed = true)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(deny);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(errors);

        var toolsOwner = toolsNamed ? "" : "the profile's ";
        var denyOwner = denyNamed ? "" : "the profile's ";
        for (var index = 0; index < tools.Count; index++)
        {
            if (rule(tools[index]) is not { } name)
            {
                continue;
            }

            var denied = -1;
            for (var entry = 0; entry < deny.Count; entry++)
            {
                if (string.Equals(deny[entry], name, StringComparison.Ordinal))
                {
                    denied = entry;
                    break;
                }
            }

            if (denied >= 0)
            {
                errors.Add(
                    string.Create(CultureInfo.InvariantCulture, $"tools[{index}]"),
                    "denied",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{toolsOwner}tools[{index}] grants {SnakeCaseEnumConverter<AgentTool>.Format(tools[index])}, which {denyOwner}deny[{denied}] takes away by the host's name for it, '{name}'; the host's deny wins over the grant, so name it in one of the two."));
            }
        }
    }

    private static void ValidateDeny(IReadOnlyList<JsonNode?>? deny, ValidationErrors errors) =>
        ValidateStrings(deny, "deny", ProfileService.MaxDenyEntries, ProfileService.MaxDenyEntryLength, errors);

    /// <summary>
    /// The tools a person grants by name: words of the closed vocabulary, each at most once. A list longer than the
    /// vocabulary cannot be a grant, so it is refused by its count as one problem. A word named twice is refused rather
    /// than folded into one: it is likelier a slip for the other word than emphasis, and folding it would grant less than
    /// the person meant without telling them.
    /// </summary>
    private static void ValidateTools(IReadOnlyList<JsonNode?>? tools, ValidationErrors errors)
    {
        if (tools is null)
        {
            return;
        }

        var vocabulary = string.Join(", ", Enum.GetValues<AgentTool>().Select(SnakeCaseEnumConverter<AgentTool>.Format));
        if (tools.Count > Enum.GetValues<AgentTool>().Length)
        {
            errors.Add("tools", "too_long", $"tools names each of {vocabulary} at most once.");
            return;
        }

        var granted = new HashSet<AgentTool>();
        for (var index = 0; index < tools.Count; index++)
        {
            var field = string.Create(CultureInfo.InvariantCulture, $"tools[{index}]");
            if (ParseTool(Text(tools[index])) is not { } tool)
            {
                errors.Add(field, "invalid", $"every tools entry must be one of: {vocabulary}.");
            }
            else if (!granted.Add(tool))
            {
                errors.Add(field, "duplicate", $"tools names each of {vocabulary} at most once.");
            }
        }
    }

    /// <summary>
    /// A bounded list of non-blank, bounded strings. The count is checked before the entries so that a list of
    /// ten thousand does not answer with ten thousand problems.
    /// </summary>
    private static void ValidateStrings(IReadOnlyList<JsonNode?>? values, string field, int maxCount, int maxLength, ValidationErrors errors)
    {
        if (values is null)
        {
            return;
        }

        if (values.Count > maxCount)
        {
            errors.Add(field, "too_long", string.Create(CultureInfo.InvariantCulture, $"{field} must have at most {maxCount} entries."));
            return;
        }

        for (var index = 0; index < values.Count; index++)
        {
            if (Text(values[index]) is { } value && !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength)
            {
                continue;
            }

            errors.Add(
                string.Create(CultureInfo.InvariantCulture, $"{field}[{index}]"),
                "invalid",
                string.Create(CultureInfo.InvariantCulture, $"every {field} entry must be a non-blank string of at most {maxLength} characters."));
        }
    }

    /// <summary>An entry that is a JSON string, or nothing — an object, a number and a null are all "not a string".</summary>
    private static string? Text(JsonNode? entry) =>
        entry is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>
    /// A bare file name, because this is what the launched agent types to call home and the runtime is what puts
    /// something of that name within its reach. A path would name a program the runtime did not put there.
    /// </summary>
    private static void ValidateCliCommand(string? cliCommand, ValidationErrors errors)
    {
        if (cliCommand is null)
        {
            return;
        }

        var value = cliCommand.Trim();
        if (value.Length == 0)
        {
            errors.Add("cli_command", "invalid", "cli_command must be a non-blank command name, or absent to use the runtime's own.");
            return;
        }

        if (value.Length > ProfileService.MaxCliCommandLength)
        {
            errors.Add("cli_command", "too_long", string.Create(CultureInfo.InvariantCulture, $"cli_command must be at most {ProfileService.MaxCliCommandLength} characters."));
            return;
        }

        // One word, and nothing that could turn the allow rule into a different rule. The rule is built by
        // putting this word inside a pattern, so a space or a bracket here does not break the word — it changes
        // what the agent is permitted to run, which is the one thing that rule exists to state exactly.
        var shaped = value.AsSpan().IndexOfAny('/', '\\', ':') >= 0
            || value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character))
            || value.AsSpan().IndexOfAny("()*\"'") >= 0;
        if (shaped || value is "." or "..")
        {
            errors.Add(
                "cli_command",
                "invalid",
                "cli_command must be a bare command word: no directory, no path, no drive, no spaces and no brackets.");
        }
    }

    /// <summary>
    /// A host version is a version: three numbers, and a pre-release after `-` if any. It is journaled as metadata on that
    /// understanding, so free text is refused rather than recorded; build metadata after `+` takes no part in a version's
    /// order and could carry anything, so it is refused too. Blank is no version at all, and is stored as none.
    /// </summary>
    private static void ValidateHostVersion(string? hostVersion, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(hostVersion))
        {
            return;
        }

        var trimmed = hostVersion.Trim();
        if (trimmed.Length > ProfileService.MaxHostVersionLength)
        {
            errors.Add("host_version_verified", "too_long", string.Create(CultureInfo.InvariantCulture, $"host_version_verified must be at most {ProfileService.MaxHostVersionLength} characters."));
        }
        else if (trimmed.Contains('+', StringComparison.Ordinal) || !Contracts.Update.SemanticVersion.TryParse(trimmed, out _))
        {
            errors.Add("host_version_verified", "version_invalid", "host_version_verified must be a version such as 2.1.289: three numbers, and a pre-release after '-' if any.");
        }
    }

    private static void ValidateReason(string? reason, ValidationErrors errors)
    {
        if (reason is not null && reason.Trim().Length > ProfileService.MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {ProfileService.MaxReasonLength} characters."));
        }
    }

    [GeneratedRegex(@"^[a-z][a-z0-9-]{0,63}\z")]
    private static partial Regex Name();
}
