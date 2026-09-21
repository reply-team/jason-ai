using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Jason.Cli.Discovery;
using Jason.Cli.Http;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Skills;

/// <summary>
/// <c>jason skills</c>: putting the skills this build ships where the things that read them look.
/// </summary>
/// <remarks>
/// A CLI verb rather than an API operation, because it fetches, verifies and copies files on this machine —
/// which is what the CLI already does for the product's own binary. Two destinations, because there are two
/// readers: the role halves go to the runtime's own data directory, where it reads them at every launch, and
/// the interactive and business packs go to the agent harnesses a person actually uses.
/// </remarks>
public static class SkillsCommands
{
    public static Command Build(CliEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        var command = new Command("skills", "Install and update the skills this build ships.");
        command.Subcommands.Add(Install(env));
        return command;
    }

    private static Command Install(CliEnvironment env)
    {
        var install = new Command("install", "Deploy the skill packs into this runtime and your agent harnesses.");

        var source = new Option<string?>("--source") { Description = "Where the packs come from: a directory on this machine, or a git source. Defaults to the repository this build was published from." };
        var reference = new Option<string?>("--ref") { Description = "Which ref to take. Defaults to the pin this build carries." };
        var root = new Option<string?>("--root") { Description = "Deploy into this directory as a harness root, instead of detection." };
        var host = new Option<string?>("--host") { Description = "Deploy only into this detected harness." };
        var pack = new Option<string?>("--pack") { Description = "Deploy only this pack." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Print exactly what would be written where, and change nothing." };
        var force = new Option<bool>("--force") { Description = "Overwrite files you have edited. Without it they are reported and kept." };

        foreach (var option in new Option[] { source, reference, root, host, pack, dryRun, force })
        {
            install.Options.Add(option);
        }

        install.SetAction((parse, cancellationToken) => RunAsync(
            env,
            new InstallOptions(
                parse.GetValue(source),
                parse.GetValue(reference),
                parse.GetValue(root),
                parse.GetValue(host),
                parse.GetValue(pack),
                parse.GetValue(dryRun),
                parse.GetValue(force)),
            cancellationToken));

        return install;
    }

    private sealed record InstallOptions(string? Source, string? Ref, string? Root, string? Host, string? Pack, bool DryRun, bool Force);

    private static async Task<int> RunAsync(CliEnvironment env, InstallOptions options, CancellationToken cancellationToken)
    {
        IReadOnlyList<HarnessRoot> harnesses;
        try
        {
            harnesses = Roots(env, options);
        }
        catch (SkillsRefused refused)
        {
            env.Error.WriteLine(refused.Message);
            return ExitCodes.ApiError;
        }

        StagedSource staged;
        try
        {
            staged = await SkillsSource.StageAsync(env, options.Source, options.Ref, cancellationToken).ConfigureAwait(false);
        }
        catch (SkillsSourceUnavailable unavailable)
        {
            env.Error.WriteLine(unavailable.Message);
            return ExitCodes.ApiError;
        }

        var (cap, fromRuntime) = await CapAsync(env, cancellationToken).ConfigureAwait(false);
        var plan = SkillsPlanner.Compose(staged, env.Paths.RoleSkillsDirectory, harnesses, cap, fromRuntime, options.Pack);

        // Printed before a byte is written, in every mode. Writing into somebody's home directory is not a
        // silent act, and a plan nobody saw is one nobody could have stopped.
        SkillsPlanner.Describe(env.Out, plan, options.DryRun);

        if (plan.Refusals.Count > 0)
        {
            foreach (var refusal in plan.Refusals)
            {
                env.Error.WriteLine(refusal);
            }

            return ExitCodes.ApiError;
        }

        if (options.DryRun)
        {
            return ExitCodes.Success;
        }

        var report = SkillsDeployer.Apply(plan, env.Paths, options.Force, TimeProvider.System);
        foreach (var problem in report.Problems)
        {
            env.Error.WriteLine(problem);
        }

        env.Out.WriteLine(report.Written == 0 && report.Problems.Count == 0
            ? string.Create(CultureInfo.InvariantCulture, $"Already current: nothing to write ({report.Unchanged} files already in place).")
            : string.Create(CultureInfo.InvariantCulture, $"Written: {report.Written} files, {report.Unchanged} already in place."));

        return report.Succeeded ? ExitCodes.Success : ExitCodes.ApiError;
    }

    /// <summary>Where the interactive packs go. <c>--root</c> replaces detection rather than narrowing it.</summary>
    private static IReadOnlyList<HarnessRoot> Roots(CliEnvironment env, InstallOptions options)
    {
        if (options.Root is { } named)
        {
            return HarnessLocators.At(named).Detect();
        }

        if (env.Harnesses is not { } locator)
        {
            throw new SkillsRefused("harness_detection_unavailable: this environment does not detect agent harnesses. Name a directory with --root.");
        }

        var detected = locator.Detect();
        if (options.Host is { } only)
        {
            if (!Enum.TryParse<AgentHostKind>(only.Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true, out var kind))
            {
                throw new SkillsRefused($"'{only}' is not a harness this build knows.");
            }

            detected = [.. detected.Where(root => root.Host == kind)];
        }

        return detected;
    }

    /// <summary>
    /// The cap to validate against, from the runtime if it answers. It is a live setting, so a guessed one
    /// validates against the wrong number — and the output says which was used, because a pass against the
    /// wrong cap is not a pass.
    /// </summary>
    private static async Task<(int Cap, bool FromRuntime)> CapAsync(CliEnvironment env, CancellationToken cancellationToken)
    {
        var descriptor = new DescriptorReader(env.Paths).Read();
        if (descriptor is null)
        {
            return (SkillsPlanner.DocumentedCap, false);
        }

        try
        {
            using var client = new RuntimeClient(descriptor, env.HttpHandler);
            var response = await client.PostAsync(Operations.SystemInfo, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                return (SkillsPlanner.DocumentedCap, false);
            }

            var info = JsonSerializer.Deserialize<SystemInfoResponse>(response.Body, JasonJson.Options);
            return info?.Skills is { } skills ? (skills.MaxSkillBytes, true) : (SkillsPlanner.DocumentedCap, false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (SkillsPlanner.DocumentedCap, false);
        }
    }
}

/// <summary>A deployment this build will not perform, in words the operator can act on.</summary>
public sealed class SkillsRefused(string message) : Exception(message);
