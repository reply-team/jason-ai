using System.CommandLine;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>The <c>operation</c> verb group: what a canonical operation takes where a campaign's route sends it.</summary>
public static class OperationCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var operation = new Command("operation", "Describe what a canonical operation takes for a campaign.");
        operation.Subcommands.Add(Describe(env, actor));
        return operation;
    }

    private static Command Describe(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "describe",
            "Print the arguments an operation takes and, for one that carries an audience's definition, the schema the plugin the campaign's route names declares for it.");
        var operation = new Argument<string>("operation") { Description = "The canonical operation, such as audience.search." };
        var campaign = new Option<string?>("--campaign")
        {
            Description = "The campaign, as cmp_...; its route for the operation names the plugin described.",
            Required = true,
        };
        var human = VerbOptions.Human();
        command.Arguments.Add(operation);
        command.Options.Add(campaign);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(campaign))
                .Set("operation", parseResult.GetValue(operation));
            return OperationRunner.RunAsync(env, Operations.OperationDescribe, body, new RunOptions(parseResult.GetValue(human), OperationRenderers.Description), cancellationToken);
        });

        return command;
    }
}
