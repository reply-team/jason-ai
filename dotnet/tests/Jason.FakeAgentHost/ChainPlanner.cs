using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Contracts.Json;

namespace Jason.FakeAgentHost;

/// <summary>
/// A planner that does its work over days without staying alive for any of it. Each run reads the envelope's
/// <c>resumption</c> — nothing else tells it where it is — does one step, and ends: by waiting, or at the last step
/// by finishing. The steps, by the run they fall to:
/// <list type="number">
/// <item>No resumption: raise one researcher per contact of <c>contacts</c>, and wait on all of them, until
/// <c>research_until</c> at the latest.</item>
/// <item>Woken once: raise one <c>message.send</c> per contact of <c>contacts</c>, and wait on all of them, until
/// <c>send_until</c> at the latest.</item>
/// <item>Woken twice: hold until <c>hold_until</c>, a moment and nothing else.</item>
/// <item>Woken three times: raise one analyst and wait on it.</item>
/// <item>Woken four times: read the analyst's result from the resumption, ask a person whether to go on, change or
/// stop, and finish with what it read.</item>
/// </list>
/// Each run writes the resumption it was handed to standard output, one JSON line, so a test reads exactly what the
/// run was told. A call the runtime refuses ends the run without an answer, which the runtime sees as a lost attempt.
/// </summary>
internal static class ChainPlanner
{
    public const string Name = "chain-planner";

    public static async Task<int> RunAsync(LaunchEnvelope envelope, string rawEnvelope, RuntimeApi api)
    {
        var told = JsonNode.Parse(rawEnvelope)?["resumption"];
        await Console.Out.WriteLineAsync(told?.ToJsonString() ?? "null").ConfigureAwait(false);

        var step = envelope.Resumption is { } resumption ? resumption.ResumptionNumber + 1 : 1;
        await Diagnostics.WriteAsync(string.Create(CultureInfo.InvariantCulture, $"step={step}")).ConfigureAwait(false);
        var brief = envelope.Context;
        switch (step)
        {
            case 1:
            {
                var raised = new List<string>();
                foreach (var contact in brief["contacts"]!.AsArray())
                {
                    raised.Add(await RaiseAsync(envelope, api, WorkItemKind.AiRole, (string?)brief["researcher_role"], null, (string?)contact, new JsonObject { ["brief"] = "what the company does" }).ConfigureAwait(false)
                        ?? throw Refused("a researcher"));
                }

                return await WaitAsync(envelope, api, raised, Moment(brief, "research_until")).ConfigureAwait(false);
            }

            case 2:
            {
                var raised = new List<string>();
                foreach (var contact in brief["contacts"]!.AsArray())
                {
                    var input = new JsonObject
                    {
                        ["channel"] = "email",
                        ["sender"] = new JsonObject { ["external_id"] = "s_12", ["address"] = "ann@example.com" },
                        ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "The research", ["body"] = "Hello, from what the research found." },
                    };
                    raised.Add(await RaiseAsync(envelope, api, WorkItemKind.ProviderOp, null, "message.send", (string?)contact, new JsonObject { ["input"] = input }).ConfigureAwait(false)
                        ?? throw Refused("a send"));
                }

                return await WaitAsync(envelope, api, raised, Moment(brief, "send_until")).ConfigureAwait(false);
            }

            case 3:
                return await WaitAsync(envelope, api, [], Moment(brief, "hold_until")).ConfigureAwait(false);

            case 4:
            {
                var analyst = await RaiseAsync(envelope, api, WorkItemKind.AiRole, (string?)brief["analyst_role"], null, null, new JsonObject { ["brief"] = "how did the sends do" }).ConfigureAwait(false)
                    ?? throw Refused("an analyst");
                return await WaitAsync(envelope, api, [analyst], null).ConfigureAwait(false);
            }

            default:
            {
                var analysis = envelope.Resumption!.Items.Single().Result;
                var (status, body) = await api.CallAsync(
                    Operations.DecisionRaise,
                    new DecisionRaiseRequest(
                        envelope.WorkItemId,
                        envelope.AttemptId,
                        "the analysis is in: go on, change the message, or stop?",
                        [new DecisionOption("go on", null), new DecisionOption("change", null), new DecisionOption("stop", null)],
                        [new DecisionReference(DecisionReferenceKind.WorkItem, envelope.Resumption.Items.Single().WorkItemId)],
                        "what comes next is a person's call"))
                    .ConfigureAwait(false);
                if (status != 200 || JsonNode.Parse(body)?["id"] is not { } decision)
                {
                    throw Refused("the question");
                }

                var answer = await api.CallAsync(
                    Operations.WorkItemComplete,
                    new WorkItemCompleteRequest(
                        envelope.WorkItemId,
                        envelope.AttemptId,
                        CompletionStatus.Succeeded,
                        new JsonObject { ["analysis"] = analysis?.DeepClone(), ["decision"] = decision.DeepClone() },
                        null,
                        null))
                    .ConfigureAwait(false);
                return answer.Status == 200 ? Behaviours.Success : Behaviours.UnexpectedAnswer;
            }
        }
    }

    private static async Task<string?> RaiseAsync(
        LaunchEnvelope envelope, RuntimeApi api, WorkItemKind kind, string? role, string? operation, string? contact, JsonObject context)
    {
        var (status, body) = await api.CallAsync(
            Operations.WorkItemCreate,
            new WorkItemCreateRequest(
                envelope.CampaignId,
                kind,
                role,
                operation,
                contact,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                context,
                null,
                new ActorRef(ActorType.Attempt, envelope.AttemptId),
                "the plan's next step"))
            .ConfigureAwait(false);
        if (status != 200)
        {
            await Diagnostics.WriteAsync(string.Create(CultureInfo.InvariantCulture, $"{Operations.WorkItemCreate} was refused with {status}: {body}")).ConfigureAwait(false);
            return null;
        }

        return (string?)JsonNode.Parse(body)?["id"];
    }

    private static async Task<int> WaitAsync(LaunchEnvelope envelope, RuntimeApi api, IReadOnlyList<string> on, DateTimeOffset? until)
    {
        var (status, body) = await api.CallAsync(
            Operations.WorkItemWait,
            new WorkItemWaitRequest(envelope.WorkItemId, envelope.AttemptId, on.Count == 0 ? null : on, null, until, "the next step needs these first"))
            .ConfigureAwait(false);
        if (status != 200)
        {
            await Diagnostics.WriteAsync(string.Create(CultureInfo.InvariantCulture, $"{Operations.WorkItemWait} was refused with {status}: {body}")).ConfigureAwait(false);
            return Behaviours.UnexpectedAnswer;
        }

        return Behaviours.Success;
    }

    private static DateTimeOffset Moment(JsonObject brief, string key) =>
        brief[key].Deserialize<DateTimeOffset>(JasonJson.Options);

    private static InvalidOperationException Refused(string what) => new($"the runtime refused {what}; see the lines above");
}
