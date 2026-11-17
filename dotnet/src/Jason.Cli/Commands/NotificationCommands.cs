using System.CommandLine;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>notification</c> verb group: one notification channel turned on or off, one verb per operation. There is no
/// verb for every channel at once — notifications as a whole are switched by a line of the settings file — and none for
/// what the operating system itself holds off, which is the person's own switch there.
/// </summary>
public static class NotificationCommands
{
    /// <summary>What both verbs say of the channels, where a person sees them, and what is not switched here.</summary>
    private const string Channels =
        "A channel is the toast, the operating system's own notification, or a notification plugin named in the settings file, by its id; "
        + "jason runtime status --human says each on its Notify: line. "
        + "Written to the settings file and in force at once, with no restart. "
        + "Notifications as a whole are switched by Notifications:Enabled in the settings file, not here, and what the operating system itself holds off is switched in its own settings. ";

    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var notification = new Command("notification", "Turn one notification channel on or off: how Jason tells you that something waits for you.");
        notification.Subcommands.Add(Switch(
            env,
            actor,
            "enable",
            "Turn one notification channel on. A plugin channel is turned on only where it could deliver: the answer says what is missing where it could not. ",
            Operations.NotificationEnable));
        notification.Subcommands.Add(Switch(
            env,
            actor,
            "disable",
            "Turn one notification channel off. The other channels are left as they are. ",
            Operations.NotificationDisable));
        return notification;
    }

    /// <summary>Enable and disable take the same things and differ only in which operation they call.</summary>
    private static Command Switch(CliEnvironment env, Option<string?> actor, string verb, string description, string operation)
    {
        var command = new Command(verb, description + Channels + ActorOption.ChangesJasonInsideARun);
        var channel = new Argument<string>("channel") { Description = "toast, or the id of a notification plugin — for example ntfy." };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(channel);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var body = RequestBody.Empty()
                .Set("channel", parseResult.GetValue(channel))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()), env);

            return OperationRunner.RunAsync(
                env, operation, body, new RunOptions(parseResult.GetValue(human), NotificationRenderers.Channels), cancellationToken);
        });

        return command;
    }
}
