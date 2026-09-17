using System.CommandLine;
using System.Text.Json.Nodes;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>report</c> verb group: effects performed outside Jason, told to Jason afterwards. Nothing here asks
/// the runtime to do anything — submitting a report is saying what already happened, and there is deliberately
/// no verb that changes or withdraws one.
/// </summary>
public static class ReportCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var report = new Command("report", "Tell Jason about an effect you produced outside it, and read what has been reported.");
        report.Subcommands.Add(Submit(env, actor));
        report.Subcommands.Add(Get(env, actor));
        report.Subcommands.Add(List(env, actor));
        return report;
    }

    private static Command Submit(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("submit", "Report an effect you performed outside Jason. It is recorded as your word, not as work Jason did.");
        var effect = new Option<string?>("--effect") { Description = "What was done, in your own words — for example email_sent." };
        var tool = new Option<string?>("--tool") { Description = "What you did it with: a CLI, an MCP server, a script, your own hands." };
        var summary = new Option<string?>("--summary") { Description = "One sentence a person reading the campaign later would need." };
        var provider = new Option<string?>("--provider") { Description = "The provider it went through, if there was one." };
        var account = new Option<string?>("--account") { Description = "The account it acted as, named by identity. Never a credential." };
        var occurredAt = new Option<string?>("--occurred-at") { Description = "When it happened, ISO-8601. Say so in --unknown if you do not know." };
        var observedAt = new Option<string?>("--observed-at") { Description = "When you saw that it had happened, ISO-8601." };
        var campaign = new Option<string?>("--campaign") { Description = "The campaign it concerns, if it concerns one." };
        var contact = new Option<string?>("--contact") { Description = "The person it reached, if Jason knows them." };
        var workItem = new Option<string?>("--work-item") { Description = "The work item it relates to, if there is one." };
        var operation = new Option<string?>("--operation") { Description = "The canonical operation it corresponds to. An unknown one is still accepted." };
        var externalId = VerbOptions.Repeatable("--external-id", "An identifier the other tool gave it, as kind=value. Repeat for more.");
        var evidence = new Option<string?>("--evidence") { Description = "A JSON object with whatever evidence you have. A note, not a payload." };
        var unknown = VerbOptions.Repeatable("--unknown", "A field you cannot supply, by name. Repeat for more.");
        var uncertainty = new Option<string?>("--uncertainty") { Description = "What you are unsure of, in your own words." };
        var idempotencyKey = new Option<string?>("--idempotency-key") { Description = "Your own key for this report, so a retry is not a second effect." };
        var reason = VerbOptions.Reason();
        var file = VerbOptions.File("The object may hold effect, tool, summary, provider, account, occurred_at, observed_at, campaign_id, contact_id, work_item_id, operation, external_ids, evidence, unknown_fields, uncertainty and idempotency_key.");
        var human = VerbOptions.Human();

        foreach (var option in (Option[])
        [
            effect, tool, summary, provider, account, occurredAt, observedAt, campaign, contact, workItem,
            operation, externalId, evidence, unknown, uncertainty, idempotencyKey, reason, file, human,
        ])
        {
            command.Options.Add(option);
        }

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), arrayProperty: null, cancellationToken).ConfigureAwait(false);
            body
                .Set("effect", parseResult.GetValue(effect))
                .Set("tool", parseResult.GetValue(tool))
                .Set("summary", parseResult.GetValue(summary))
                .Set("provider", parseResult.GetValue(provider))
                .Set("account", parseResult.GetValue(account))
                .Set("occurred_at", parseResult.GetValue(occurredAt))
                .Set("observed_at", parseResult.GetValue(observedAt))
                .Set("campaign_id", parseResult.GetValue(campaign))
                .Set("contact_id", parseResult.GetValue(contact))
                .Set("work_item_id", parseResult.GetValue(workItem))
                .Set("operation", parseResult.GetValue(operation))
                .Set("external_ids", ExternalIds(parseResult.GetValue(externalId)))
                .Set("evidence", VerbOptions.Object(parseResult.GetValue(evidence), "--evidence"))
                .Set("unknown_fields", VerbOptions.Strings(parseResult.GetValue(unknown)))
                .Set("uncertainty", parseResult.GetValue(uncertainty))
                .Set("idempotency_key", parseResult.GetValue(idempotencyKey))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return await OperationRunner.RunAsync(
                env, Operations.ReportSubmit, body, new RunOptions(parseResult.GetValue(human), ReportRenderers.Report), cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    private static Command Get(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("get", "One report: what was said, who said it, and what it was about.");
        var id = new Argument<string>("report-id") { Description = "The report, as report list shows it." };
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty().Set("report_id", parseResult.GetValue(id));

            return OperationRunner.RunAsync(
                env, Operations.ReportGet, body, new RunOptions(parseResult.GetValue(human), ReportRenderers.Report), cancellationToken);
        });

        return command;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "What has been reported from outside Jason.");
        var campaign = new Option<string?>("--campaign") { Description = "Only reports about one campaign." };
        var contact = new Option<string?>("--contact") { Description = "Only reports about one person." };
        var workItem = new Option<string?>("--work-item") { Description = "Only reports about one work item." };
        var operation = new Option<string?>("--operation") { Description = "Only reports naming one operation." };
        var since = new Option<string?>("--since") { Description = "Only reports received at or after this time, ISO-8601." };
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();

        foreach (var option in (Option[])[campaign, contact, workItem, operation, since, limit, cursor, human])
        {
            command.Options.Add(option);
        }

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(campaign))
                .Set("contact_id", parseResult.GetValue(contact))
                .Set("work_item_id", parseResult.GetValue(workItem))
                .Set("operation", parseResult.GetValue(operation))
                .Set("since", parseResult.GetValue(since))
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(
                env, Operations.ReportList, body, new RunOptions(parseResult.GetValue(human), ReportRenderers.ReportList), cancellationToken);
        });

        return command;
    }

    /// <summary>
    /// <c>kind=value</c>, once per identifier. The CLI splits on the first <c>=</c> only, because an identifier
    /// may well contain one; what the pair means is the API's business.
    /// </summary>
    private static JsonArray? ExternalIds(string[]? values)
    {
        if (values is not { Length: > 0 })
        {
            return null;
        }

        var ids = new JsonArray();
        foreach (var value in values)
        {
            var split = value.IndexOf('=', StringComparison.Ordinal);
            if (split <= 0)
            {
                throw new UsageException($"--external-id must be written as kind=value; '{value}' is not.");
            }

            ids.Add(new JsonObject
            {
                ["kind"] = value[..split],
                ["value"] = value[(split + 1)..],
            });
        }

        return ids;
    }
}
