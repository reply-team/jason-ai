using System.CommandLine;
using System.Text.Json.Nodes;
using Jason.Cli.Folders;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>campaign</c> verb group: one verb per campaign operation, named the same and carrying the same
/// fields. The campaign id is positional, the request body may come from a file or standard input, and the
/// scalar options are laid over it.
/// </summary>
public static class CampaignCommands
{
    /// <summary>The fields <c>--clear</c> can set back to nothing. A campaign always has a name, so that is not one.</summary>
    private static readonly string[] Clearable = ["execution_profile", "review_seconds"];

    /// <summary>The largest page the runtime gives, so the names of every campaign usually take one request.</summary>
    private const int NamesPage = 1000;

    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var campaign = new Command("campaign", "Create campaigns, describe them in their context and give them contacts.");
        campaign.Subcommands.Add(Create(env, actor));
        campaign.Subcommands.Add(Get(env, actor));
        campaign.Subcommands.Add(List(env, actor));
        campaign.Subcommands.Add(Update(env, actor));
        campaign.Subcommands.Add(Transition("start", "Start a draft campaign, or resume a paused one.", Operations.CampaignStart, env, actor));
        campaign.Subcommands.Add(Transition("pause", "Pause an active campaign.", Operations.CampaignPause, env, actor));
        campaign.Subcommands.Add(Transition("archive", "Archive a campaign. Archiving is final.", Operations.CampaignArchive, env, actor));
        campaign.Subcommands.Add(UpdateContext(env, actor));
        campaign.Subcommands.Add(AddContacts(env, actor));
        campaign.Subcommands.Add(RemoveContacts(env, actor));
        campaign.Subcommands.Add(ListContacts(env, actor));
        campaign.Subcommands.Add(Summary(env, actor));
        campaign.Subcommands.Add(Activity(env, actor));
        campaign.Subcommands.Add(ActivityByPeriod(env, actor));
        return campaign;
    }

    private static Command Create(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("create", "Create a campaign.");
        var name = new Option<string?>("--name") { Description = "The campaign name." };
        var context = new Option<string?>("--context") { Description = "The initial campaign context, as a JSON object." };
        var project = ProjectOption(
            "The project the campaign is in, as prj_.... Absent, it is the project whose folder, or whose campaign's folder, the command runs in, and the default project elsewhere.");
        var idempotencyKey = VerbOptions.IdempotencyKey();
        var file = VerbOptions.File("The object holds name, context, project_id and idempotency_key.");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Options.Add(name);
        command.Options.Add(context);
        command.Options.Add(project);
        command.Options.Add(idempotencyKey);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), null, cancellationToken).ConfigureAwait(false);
            body.Set("name", parseResult.GetValue(name))
                .Set("context", VerbOptions.Object(parseResult.GetValue(context), "--context"))
                .Set("project_id", parseResult.GetValue(project))
                .Set("idempotency_key", parseResult.GetValue(idempotencyKey))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()), env);

            if ((await WorkingFolder.FillProjectAsync(env, body, WorkingFolder.ByProjectOption, cancellationToken).ConfigureAwait(false)).ExitCode is { } stopped)
            {
                return stopped;
            }

            return await OperationRunner
                .RunAsync(env, Operations.CampaignCreate, body, new RunOptions(parseResult.GetValue(human), CampaignRenderers.Campaign), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command Get(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("get", "Show one campaign with its context.");
        var id = CampaignId();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty().Set("campaign_id", parseResult.GetValue(id));
            return OperationRunner.RunAsync(env, Operations.CampaignGet, body, new RunOptions(parseResult.GetValue(human), CampaignRenderers.Campaign), cancellationToken);
        });

        return command;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "List campaigns. Archived ones appear only when asked for, by status or with --include-archived.");
        var status = Choice<CampaignStatus>.Only(new Option<string?>("--status") { Description = "Only campaigns in this status: draft, active, paused or archived." });
        var query = new Option<string?>("--query") { Description = "Only campaigns whose name contains this text; case and accents do not matter." };
        var project = ProjectOption(
            "Only the campaigns of this project, as prj_.... Absent, it is the project whose folder, or whose campaign's folder, the command runs in, and every project elsewhere.");
        var everyProject = new Option<bool>("--all-projects") { Description = "The campaigns of every project, wherever the command runs; not with --project." };
        var includeArchived = new Option<bool>("--include-archived") { Description = "Include archived campaigns, which are hidden by default." };
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Options.Add(status);
        command.Options.Add(query);
        command.Options.Add(project);
        command.Options.Add(everyProject);
        command.Options.Add(includeArchived);
        command.Options.Add(limit);
        command.Options.Add(cursor);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var every = parseResult.GetValue(everyProject);
            if (every && parseResult.GetValue(project) is not null)
            {
                throw new UsageException("--all-projects lists every project's campaigns, so it takes no --project.");
            }

            var body = RequestBody.Empty()
                .Set("status", parseResult.GetValue(status))
                .Set("query", parseResult.GetValue(query))
                .Set("project_id", parseResult.GetValue(project))
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));
            if (parseResult.GetValue(includeArchived))
            {
                body.Set("include_archived", true);
            }

            var fill = every ? FolderFill.None : await WorkingFolder.FillProjectAsync(env, body, WorkingFolder.ByProjectOrAllProjects, cancellationToken).ConfigureAwait(false);
            if (fill.ExitCode is { } stopped)
            {
                return stopped;
            }

            // A listing scoped by the folder it was typed in says so above its rows, so a person reading it knows why it
            // holds one project's campaigns, from the project the fill already read.
            Func<string, string?> render = fill.Project is { } scoped
                ? json => CampaignRenderers.CampaignList(json) is { } rows ? CampaignRenderers.ScopedByFolder(scoped, rows) : null
                : CampaignRenderers.CampaignList;
            return await OperationRunner
                .RunAsync(env, Operations.CampaignList, body, new RunOptions(parseResult.GetValue(human), render), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command Update(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("update", "Change a campaign attribute. Absent options leave their field alone.");
        var id = CampaignId();
        var name = new Option<string?>("--name") { Description = "The new campaign name." };
        var executionProfile = new Option<string?>("--execution-profile")
        {
            Description = "The execution profile this campaign's agent work uses, unless a work item names its own.",
        };
        var reviewSeconds = new Option<int?>("--review-seconds")
        {
            Description = "How often this campaign is reviewed, in seconds. Absent leaves it alone; --clear review_seconds puts it back on the installation's cadence.",
        };
        var project = ProjectOption("Move the campaign to this project, as prj_.... A campaign is always in one, so it cannot be cleared.");
        var clear = VerbOptions.Repeatable(
            "--clear",
            $"A field to set back to nothing: {string.Join(", ", Clearable)}. Repeat the option for more than one.");
        var file = VerbOptions.File("The object holds the fields to patch (name, execution_profile, review_seconds, project_id).");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(name);
        command.Options.Add(executionProfile);
        command.Options.Add(reviewSeconds);
        command.Options.Add(project);
        command.Options.Add(clear);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), null, cancellationToken).ConfigureAwait(false);
            body.Set("campaign_id", parseResult.GetValue(id))
                .Set("name", parseResult.GetValue(name))
                .Set("execution_profile", parseResult.GetValue(executionProfile))
                .Set("review_seconds", parseResult.GetValue(reviewSeconds))
                .Set("project_id", parseResult.GetValue(project));

            // An explicit null is what tells the runtime "take this away" from "say nothing about it", and it
            // is written after the value options so that clearing wins over a value given for the same field.
            foreach (var field in parseResult.GetValue(clear) ?? [])
            {
                if (!Clearable.Contains(field, StringComparer.Ordinal))
                {
                    throw new UsageException($"--clear names one of: {string.Join(", ", Clearable)}.");
                }

                body.Set(field, null, onlyIfNotNull: false);
            }

            body.Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()), env);

            return await OperationRunner
                .RunAsync(env, Operations.CampaignUpdate, body, new RunOptions(parseResult.GetValue(human), CampaignRenderers.Campaign), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command Transition(string verb, string description, string operation, CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(verb, description);
        var id = CampaignId();
        var file = VerbOptions.File("The object holds the request fields (reason).");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), null, cancellationToken).ConfigureAwait(false);
            body.Set("campaign_id", parseResult.GetValue(id))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()), env);

            return await OperationRunner
                .RunAsync(env, operation, body, new RunOptions(parseResult.GetValue(human), CampaignRenderers.Campaign), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command UpdateContext(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("update-context", "Edit top-level keys of the campaign context. Keys not named stay as they are.");
        var id = CampaignId();
        var set = new Option<string?>("--set") { Description = "Top-level keys to write, as a JSON object." };
        var unset = VerbOptions.Repeatable("--unset", "A top-level key to remove. Repeat the option for more than one.");
        var file = VerbOptions.File("The object holds set and unset.");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(set);
        command.Options.Add(unset);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), null, cancellationToken).ConfigureAwait(false);
            body.Set("campaign_id", parseResult.GetValue(id))
                .Set("set", VerbOptions.Object(parseResult.GetValue(set), "--set"))
                .Set("unset", VerbOptions.Strings(parseResult.GetValue(unset)))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()), env);

            return await OperationRunner
                .RunAsync(env, Operations.CampaignUpdateContext, body, new RunOptions(parseResult.GetValue(human), CampaignRenderers.Campaign), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command AddContacts(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("add-contacts", "Add contacts to a campaign, creating or matching them as the import asks.");
        var id = CampaignId();
        var file = new Option<string>("--file")
        {
            Description = "The contacts as JSON — a path, or - for standard input. Either an array of items, or an object holding contacts and match_by. "
                + "An item is a contact payload (first_name, last_name, company, title, time_zone, channels, custom) or {\"contact_id\": \"cnt_...\"}.",
            Required = true,
        };
        var matchBy = new Option<string?>("--match-by") { Description = "Deduplicate on an existing contact by this key: a channel name such as email, or custom:<field>." };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(file);
        command.Options.Add(matchBy);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), "contacts", cancellationToken).ConfigureAwait(false);
            body.Set("campaign_id", parseResult.GetValue(id))
                .Set("match_by", parseResult.GetValue(matchBy))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()), env);

            return await OperationRunner
                .RunAsync(env, Operations.CampaignAddContacts, body, new RunOptions(parseResult.GetValue(human), CampaignRenderers.Batch), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command RemoveContacts(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("remove-contacts", "Remove contacts from a campaign. They stay on record as excluded.");
        var id = CampaignId();
        var contact = VerbOptions.Repeatable("--contact", "A contact to remove, as cnt_.... Repeat the option for more than one.");
        var file = VerbOptions.File("The object holds contact_ids.");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(contact);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), null, cancellationToken).ConfigureAwait(false);
            body.Set("campaign_id", parseResult.GetValue(id))
                .Set("contact_ids", VerbOptions.Strings(parseResult.GetValue(contact)))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()), env);

            return await OperationRunner
                .RunAsync(env, Operations.CampaignRemoveContacts, body, new RunOptions(parseResult.GetValue(human), CampaignRenderers.RemoveBatch), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command ListContacts(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "list-contacts",
            "List the people of the campaigns named, or of every campaign not archived, in every project, each once with the campaigns that list them; or exactly "
            + "the people one count of campaign summary or campaign activity counts, the latest first for an activity. "
            + "Excluded ones appear only when asked for by state.");
        var ids = CampaignIds();
        var state = Choice<MembershipState>.Only(new Option<string?>("--state") { Description = "Only members in this state: enrolled, paused, finished or excluded." });
        var summary = Choice<CampaignSummaryFilter>.Only(new Option<string?>("--summary")
        {
            Description = "Only the people one count of campaign summary counts: waiting, failed, working, new, idle, sequence, out or removed.",
        });
        var activity = Choice<CampaignActivityFilter>.Only(new Option<string?>("--activity")
        {
            Description = "Only the people one count of campaign activity counts in the period of --since and --until: "
                + "people (anything at all), added, worked_on, contacted, replied, opted_out or removed. Removed people are included.",
        });
        var since = VerbOptions.Moment("--since", "With --activity: the period's start, included, as ISO-8601 UTC, or now.");
        var until = VerbOptions.Moment("--until", "With --activity: the period's end, not included, as ISO-8601 UTC, or now.");
        var query = new Option<string?>("--query") { Description = "Only people found by this text: a name, company, title or any channel value." };
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Arguments.Add(ids);
        command.Options.Add(state);
        command.Options.Add(summary);
        command.Options.Add(activity);
        command.Options.Add(since);
        command.Options.Add(until);
        command.Options.Add(query);
        command.Options.Add(limit);
        command.Options.Add(cursor);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty()
                .Set("campaign_ids", Ids(parseResult.GetValue(ids)))
                .Set("state", parseResult.GetValue(state))
                .Set("summary", parseResult.GetValue(summary))
                .Set("activity", parseResult.GetValue(activity))
                .Set("since", VerbOptions.ResolveMoment(parseResult.GetValue(since), TimeProvider.System))
                .Set("until", VerbOptions.ResolveMoment(parseResult.GetValue(until), TimeProvider.System))
                .Set("query", parseResult.GetValue(query))
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(env, Operations.CampaignListContacts, body, new RunOptions(parseResult.GetValue(human), CampaignRenderers.Members), cancellationToken);
        });

        return command;
    }

    private static Command Summary(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "summary",
            "How many of each campaign's people are waiting on you, failed, working, new, idle, in the sequence, out or removed, now. "
            + "A person may be in several counts; campaign list-contacts --summary lists who.");
        var ids = CampaignIds();
        var human = VerbOptions.Human();
        command.Arguments.Add(ids);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty().Set("campaign_ids", Ids(parseResult.GetValue(ids)));
            return OperationRunner.RunAsync(env, Operations.CampaignSummary, body, Named(env, parseResult.GetValue(human), CampaignRenderers.Summary), cancellationToken);
        });

        return command;
    }

    private static Command Activity(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "activity",
            "How many of each campaign's people had anything happen to them, were added, worked on, contacted, replied for the first time, "
            + "opted out or removed in a period. "
            + "A person may be in several counts; campaign list-contacts --activity lists who.");
        var ids = CampaignIds();
        var since = VerbOptions.Moment("--since", "The period's start, included, as ISO-8601 UTC, or now.");
        since.Required = true;
        var until = VerbOptions.Moment("--until", "The period's end, not included, as ISO-8601 UTC, or now.");
        until.Required = true;
        var human = VerbOptions.Human();
        command.Arguments.Add(ids);
        command.Options.Add(since);
        command.Options.Add(until);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty()
                .Set("campaign_ids", Ids(parseResult.GetValue(ids)))
                .Set("since", VerbOptions.ResolveMoment(parseResult.GetValue(since), TimeProvider.System))
                .Set("until", VerbOptions.ResolveMoment(parseResult.GetValue(until), TimeProvider.System));
            return OperationRunner.RunAsync(env, Operations.CampaignActivity, body, Named(env, parseResult.GetValue(human), CampaignRenderers.Activity), cancellationToken);
        });

        return command;
    }

    private static Command ActivityByPeriod(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "activity-by-period",
            "One count of campaign activity over time: how many people it counts in each hour, day or week of a window, across "
            + "the campaigns named or every campaign not archived, in every project. A person counted on two days is in both, and once in the total `campaign activity` "
            + "gives for the window.");
        var ids = CampaignIds();
        var count = Choice<CampaignActivityFilter>.Only(new Option<string?>("--count")
        {
            Description = "The count: people (anything at all), added, worked_on, contacted, replied, opted_out or removed.",
            Required = true,
        });
        var period = Choice<ActivityPeriod>.Only(new Option<string?>("--period") { Description = "What each bucket is: hour, day or week (Monday to Sunday).", Required = true });
        var since = VerbOptions.Moment("--since", "The window's start, included, as ISO-8601 UTC, or now.");
        since.Required = true;
        var until = VerbOptions.Moment("--until", "The window's end, not included, as ISO-8601 UTC, or now.");
        until.Required = true;
        var zone = new Option<string?>("--zone")
        {
            Description = "The IANA time zone the hours, days and weeks are cut in, such as Europe/Kyiv; this machine's when absent.",
        };
        var human = VerbOptions.Human();
        command.Arguments.Add(ids);
        command.Options.Add(count);
        command.Options.Add(period);
        command.Options.Add(since);
        command.Options.Add(until);
        command.Options.Add(zone);
        command.Options.Add(human);
        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty()
                .Set("campaign_ids", Ids(parseResult.GetValue(ids)))
                .Set("count", parseResult.GetValue(count))
                .Set("period", parseResult.GetValue(period))
                .Set("since", VerbOptions.ResolveMoment(parseResult.GetValue(since), TimeProvider.System))
                .Set("until", VerbOptions.ResolveMoment(parseResult.GetValue(until), TimeProvider.System))
                .Set("zone", parseResult.GetValue(zone) ?? ThisMachinesZone());
            return OperationRunner.RunAsync(env, Operations.CampaignActivityByPeriod, body, new RunOptions(parseResult.GetValue(human), CampaignRenderers.ActivityByPeriod), cancellationToken);
        });
        return command;
    }

    /// <summary>The days a person at this machine lives by: its zone's IANA id, or UTC when it has none.</summary>
    private static string ThisMachinesZone()
    {
        var local = TimeZoneInfo.Local;
        if (local.HasIanaId)
        {
            return local.Id;
        }

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(local.Id, out var iana) ? iana : "UTC";
    }

    /// <summary>
    /// How counts that name each campaign by its id alone are shown to people: by each campaign's name, read from the
    /// campaign listing, archived campaigns included, page after page until every campaign the counts show is named. A
    /// listing that cannot be read leaves the rest unnamed, and the counts are shown all the same. JSON output asks
    /// nothing more.
    /// </summary>
    private static RunOptions Named(CliEnvironment env, bool human, Func<string, IReadOnlyDictionary<string, string>, string?> render) =>
        new(human)
        {
            RenderHumanAsync = async (json, cancellationToken) =>
                render(json, await CampaignNamesAsync(env, CountedIds(json), cancellationToken).ConfigureAwait(false)),
        };

    private static async Task<IReadOnlyDictionary<string, string>> CampaignNamesAsync(
        CliEnvironment env, IReadOnlySet<string> ids, CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        string? cursor = null;
        while (ids.Any(id => !names.ContainsKey(id)))
        {
            var body = RequestBody.Empty().Set("include_archived", true).Set("limit", NamesPage).Set("cursor", cursor);
            var (_, response) = await OperationRunner.SendAsync(env with { Out = TextWriter.Null }, Operations.CampaignList, body, cancellationToken)
                .ConfigureAwait(false);
            if (response is not { IsSuccess: true } || RenderText.Read<Page<CampaignSummaryDto>>(response.Body) is not { Items: { } items } page)
            {
                break;
            }

            foreach (var campaign in items)
            {
                names[campaign.Id] = campaign.Name;
            }

            cursor = page.NextCursor;
            if (cursor is null)
            {
                break;
            }
        }

        return names;
    }

    private static HashSet<string> CountedIds(string json)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            if (JsonNode.Parse(json) is JsonObject counts && counts["campaigns"] is JsonArray rows)
            {
                foreach (var row in rows)
                {
                    if (row?["campaign_id"] is JsonValue value && value.TryGetValue<string>(out var id))
                    {
                        ids.Add(id);
                    }
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // A body that is not the counts is printed raw by the renderer; there is nothing to name.
        }

        return ids;
    }

    /// <summary>The campaigns named, or nothing at all when none is: then the runtime answers for every campaign not archived, in every project.</summary>
    private static JsonArray? Ids(string[]? ids) =>
        ids is null || ids.Length == 0 ? null : new JsonArray([.. ids.Select(id => (JsonNode?)JsonValue.Create(id))]);

    private static Argument<string[]> CampaignIds() => new("campaign-id")
    {
        Description = "The campaigns, as cmp_...; none means every campaign not archived, in every project.",
        Arity = ArgumentArity.ZeroOrMore,
    };

    private static Argument<string> CampaignId() => new("campaign-id") { Description = "The campaign, as cmp_...." };

    /// <summary><c>--project</c> takes a project's id, as every verb that names a project does.</summary>
    private static Option<string?> ProjectOption(string description) => new("--project") { Description = description };
}
