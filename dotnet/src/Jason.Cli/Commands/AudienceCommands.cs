using System.CommandLine;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>audience</c> verb group: a campaign's audience reads, answered now by the plugin the campaign's route for each
/// operation names. Nothing is recorded: no work item, no journal line. A search, which spends, is a work item of its own.
/// </summary>
public static class AudienceCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var audience = new Command("audience", "Read what a campaign's provider says about an audience: its size, a filter's values, whether the account may search.");
        audience.Subcommands.Add(Preview(env, actor));
        audience.Subcommands.Add(FilterValues(env, actor));
        audience.Subcommands.Add(Availability(env, actor));
        return audience;
    }

    private static Command Preview(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "preview",
            "Ask the provider how many people an audience holds and show a sample of them, without contact details. Free, and nothing is recorded. "
                + "The definition is the provider's own shape: 'jason operation describe audience.preview --campaign <id>' prints what it takes.");
        var campaign = Campaign();
        var definition = new Option<string?>("--definition")
        {
            Description = "The audience's definition, as a JSON object in the provider's own shape. Give it or --audience, not both.",
        };
        var number = new Option<int?>("--audience") { Description = "An audience the campaign holds, by its number. Give it or --definition, not both." };
        var perCompany = new Option<int?>("--people-per-company") { Description = "How many people of one company count, from 1 to 100; 1 when absent." };
        var human = VerbOptions.Human();
        command.Options.Add(campaign);
        command.Options.Add(definition);
        command.Options.Add(number);
        command.Options.Add(perCompany);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var written = parseResult.GetValue(definition);
            var named = parseResult.GetValue(number);
            if ((written is null) == (named is null))
            {
                throw new UsageException("Name the audience with --definition or --audience, exactly one of the two.");
            }

            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(campaign))
                .Set("audience", named)
                .Set("definition", VerbOptions.Object(written, "--definition"))
                .Set("people_per_company", parseResult.GetValue(perCompany));
            return OperationRunner.RunAsync(env, Operations.AudiencePreview, body, new RunOptions(parseResult.GetValue(human), AudienceRenderers.Preview), cancellationToken);
        });

        return command;
    }

    private static Command FilterValues(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("filter-values", "List the values one of the provider's filters takes, starting with some text, in the provider's order. Nothing is recorded.");
        var campaign = Campaign();
        var filter = new Option<string?>("--filter")
        {
            Description = "The typeahead a key of the definition is paired with, which `jason operation describe audience.search --campaign <id>` prints under each key, such as job-titles; not the key itself.",
            Required = true,
        };
        var query = new Option<string?>("--query") { Description = "The text the values start with, at most 200 characters; every value when absent." };
        var limit = new Option<int?>("--limit") { Description = "How many values, from 1 to 100; 20 when absent." };
        var human = VerbOptions.Human();
        command.Options.Add(campaign);
        command.Options.Add(filter);
        command.Options.Add(query);
        command.Options.Add(limit);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(campaign))
                .Set("filter", parseResult.GetValue(filter))
                .Set("query", parseResult.GetValue(query))
                .Set("limit", parseResult.GetValue(limit));
            return OperationRunner.RunAsync(env, Operations.AudienceFilterValues, body, new RunOptions(parseResult.GetValue(human), AudienceRenderers.FilterValues), cancellationToken);
        });

        return command;
    }

    private static Command Availability(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("availability", "Ask the provider whether the campaign's account may search now, and what it has left where the provider says. Nothing is recorded.");
        var campaign = Campaign();
        var human = VerbOptions.Human();
        command.Options.Add(campaign);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty().Set("campaign_id", parseResult.GetValue(campaign));
            return OperationRunner.RunAsync(env, Operations.AudienceAvailability, body, new RunOptions(parseResult.GetValue(human), AudienceRenderers.Availability), cancellationToken);
        });

        return command;
    }

    private static Option<string?> Campaign() => new("--campaign")
    {
        Description = "The campaign, as cmp_...; its route for the operation names the provider that answers.",
        Required = true,
    };
}
