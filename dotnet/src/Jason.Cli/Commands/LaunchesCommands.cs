using System.CommandLine;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>launches</c> verb group: the daily limits of launches as a person speaks to them — a read of where they stand,
/// and a lift of today's limits on their word.
/// </summary>
public static class LaunchesCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var launches = new Command("launches", "The daily limits of launches each role runs under in each campaign: read where they stand, or lift today's on your word.");
        launches.Subcommands.Add(List(env, actor));
        launches.Subcommands.Add(Lift(env, actor));
        return launches;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "list",
            "The daily limits of launches (Launches:MaxPerDay) as they stand now. The answer says the installation's day they "
            + "count in, from its midnight to the next as UTC moments, in its time zone (Runtime:TimeZone, or the system's own); "
            + "the lift that stands today, or null; each role's limit a day as the dispatcher reads it now, so an edit of the "
            + "settings shows at once, null for a role that has none, and whether the number is Jason's default or the settings'; "
            + "and held, how many work items wait on a daily limit now. With --campaign, each role also says started_today, its "
            + "runs that started in that campaign since the installation's midnight, as the dispatcher counts them, and held is "
            + "that campaign's. A read: it changes nothing, and runs inside a run as outside one. A limit is set in the settings "
            + "file, under Launches:MaxPerDay:<role>; no command sets one.");
        var campaign = new Option<string?>("--campaign") { Description = "Also each role's runs started today in this campaign, as cmp_..., and only its held items." };
        var human = VerbOptions.Human();
        command.Options.Add(campaign);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty().Set("campaign_id", parseResult.GetValue(campaign));
            return OperationRunner.RunAsync(
                env, Operations.LaunchesList, body, new RunOptions(parseResult.GetValue(human), LaunchesRenderers.List), cancellationToken);
        });

        return command;
    }

    private static Command Lift(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "lift",
            "Lift today's daily limits of launches (Launches:MaxPerDay) in every campaign and for every role, until the "
            + "installation's next midnight in its time zone (Runtime:TimeZone, or the system's own). What the limits hold now "
            + "is let go at once, and nothing new is held until that midnight, when the limits hold again. The answer says "
            + "until when, as a UTC moment, the zone, how many held items were let go, and whether today was lifted already: a "
            + "second lift the same day writes nothing and still lets go what was held since. --actor is required: a lift is a "
            + "person's word, and it names one with an id. An agent cannot lift the limits: a role or an attempt is refused with "
            + "lift_not_human. " + ActorOption.NoDefault + " " + ActorOption.RefusedInsideARun);
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var body = RequestBody.Empty()
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()));
            ActorOption.Named(body, "launches lift", "a lift is a person's word");

            return OperationRunner.RunAsync(
                env, Operations.LaunchesLift, body, new RunOptions(parseResult.GetValue(human), LaunchesRenderers.Lift), cancellationToken);
        });

        return command;
    }
}
