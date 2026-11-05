using System.CommandLine;
using System.Text.Json.Nodes;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>contact</c> verb group: one verb per contact operation. Channels are given as <c>--email</c> for the
/// common case and <c>--channel &lt;name&gt;=&lt;value&gt;</c> for every other one; the runtime validates and
/// normalizes them.
/// </summary>
public static class ContactCommands
{
    /// <summary>The scalar fields <c>--clear</c> can set back to nothing. Channels and custom are replaced, not cleared.</summary>
    private static readonly string[] Clearable = ["first_name", "last_name", "company", "title", "time_zone"];

    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var contact = new Command("contact", "Create and maintain the people campaigns reach out to.");
        contact.Subcommands.Add(Create(env, actor));
        contact.Subcommands.Add(Get(env, actor));
        contact.Subcommands.Add(List(env, actor));
        contact.Subcommands.Add(Update(env, actor));
        contact.Subcommands.Add(Archive(env, actor));
        contact.Subcommands.Add(Threads(env, actor));
        contact.Subcommands.Add(Thread(env, actor));
        return contact;
    }

    private static Command Create(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("create", "Create a contact. Every field is optional, channels included.");
        var fields = new ContactFields();
        var file = VerbOptions.File("The object holds the contact fields (first_name, last_name, company, title, time_zone, channels, custom).");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        fields.AddTo(command);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), null, cancellationToken).ConfigureAwait(false);
            fields.LayOver(body, parseResult);
            body.Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()), env);

            return await OperationRunner
                .RunAsync(env, Operations.ContactCreate, body, new RunOptions(parseResult.GetValue(human), ContactRenderers.Contact), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command Get(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("get", "Show one contact with its channels.");
        var id = ContactId();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty().Set("contact_id", parseResult.GetValue(id));
            return OperationRunner.RunAsync(env, Operations.ContactGet, body, new RunOptions(parseResult.GetValue(human), ContactRenderers.Contact), cancellationToken);
        });

        return command;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "List contacts, optionally only those reachable on a channel.");
        var channel = new Option<string?>("--channel") { Description = "Only contacts having this channel, for example email." };
        var value = new Option<string?>("--value") { Description = "Only contacts whose channel value is this one; needs --channel." };
        var query = new Option<string?>("--query") { Description = "Only people whose name, company, title or any channel value contains this text; case and accents do not matter, a phone number matches however it is spaced, and a pasted LinkedIn link finds its profile." };
        var campaign = new Option<string?>("--campaign") { Description = "Only people on this campaign, as cmp_...; excluded members are left out." };
        var order = Choice<ContactListOrder>.Only(new Option<string?>("--order") { Description = "created (the default, oldest first) or activity (the person something last happened to first; people with nothing yet come last)." });
        var includeArchived = new Option<bool>("--include-archived") { Description = "Include archived contacts, which are hidden by default." };
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Options.Add(channel);
        command.Options.Add(value);
        command.Options.Add(query);
        command.Options.Add(campaign);
        command.Options.Add(order);
        command.Options.Add(includeArchived);
        command.Options.Add(limit);
        command.Options.Add(cursor);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty()
                .Set("channel", parseResult.GetValue(channel))
                .Set("value", parseResult.GetValue(value))
                .Set("query", parseResult.GetValue(query))
                .Set("campaign_id", parseResult.GetValue(campaign))
                .Set("order", parseResult.GetValue(order))
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            if (parseResult.GetValue(includeArchived))
            {
                body.Set("include_archived", true);
            }

            return OperationRunner.RunAsync(env, Operations.ContactList, body, new RunOptions(parseResult.GetValue(human), ContactRenderers.ContactList), cancellationToken);
        });

        return command;
    }

    private static Command Update(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("update", "Change a contact. Absent options leave their field alone; channels and custom are replaced whole.");
        var id = ContactId();
        var fields = new ContactFields();
        var clear = VerbOptions.Repeatable("--clear", $"A field to clear: {string.Join(", ", Clearable)}. Applied after the value options. Repeat the option for more than one.");
        var file = VerbOptions.File("The object holds the fields to patch (first_name, last_name, company, title, time_zone, channels, custom).");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        fields.AddTo(command);
        command.Options.Add(clear);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), null, cancellationToken).ConfigureAwait(false);
            body.Set("contact_id", parseResult.GetValue(id));
            fields.LayOver(body, parseResult);

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
                .RunAsync(env, Operations.ContactUpdate, body, new RunOptions(parseResult.GetValue(human), ContactRenderers.Contact), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command Archive(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("archive", "Archive a contact. Archived contacts stay on record and are hidden from listings.");
        var id = ContactId();
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
            body.Set("contact_id", parseResult.GetValue(id))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()), env);

            return await OperationRunner
                .RunAsync(env, Operations.ContactArchive, body, new RunOptions(parseResult.GetValue(human), ContactRenderers.Contact), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command Threads(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("threads", "Read a person's conversation threads from the provider now, the one that moved last first. Nothing is recorded: no work item, no journal line.");
        var id = ContactId();
        var plugin = Plugin();
        var limit = new Option<int?>("--limit") { Description = "How many threads, from 1 to 100; 20 when absent." };
        var offset = Offset();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(plugin);
        command.Options.Add(limit);
        command.Options.Add(offset);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty()
                .Set("contact_id", parseResult.GetValue(id))
                .Set("plugin_id", parseResult.GetValue(plugin))
                .Set("limit", parseResult.GetValue(limit))
                .Set("offset", parseResult.GetValue(offset));
            return OperationRunner.RunAsync(env, Operations.ContactThreads, body, new RunOptions(parseResult.GetValue(human), ContactRenderers.Threads), cancellationToken);
        });

        return command;
    }

    private static Command Thread(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("thread", "Read one of a person's threads from the provider now, its messages first written first. Nothing is recorded.");
        var id = ContactId();
        var thread = new Argument<string>("thread-id") { Description = "The thread, as the provider names it in the list of threads." };
        var plugin = Plugin();
        var limit = new Option<int?>("--limit") { Description = "How many messages, from 1 to 20; 20 when absent." };
        var offset = Offset();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Arguments.Add(thread);
        command.Options.Add(plugin);
        command.Options.Add(limit);
        command.Options.Add(offset);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty()
                .Set("contact_id", parseResult.GetValue(id))
                .Set("thread_id", parseResult.GetValue(thread))
                .Set("plugin_id", parseResult.GetValue(plugin))
                .Set("limit", parseResult.GetValue(limit))
                .Set("offset", parseResult.GetValue(offset));
            return OperationRunner.RunAsync(env, Operations.ContactThread, body, new RunOptions(parseResult.GetValue(human), ContactRenderers.Thread), cancellationToken);
        });

        return command;
    }

    private static Option<string?> Plugin() =>
        new("--plugin") { Description = "The provider's plugin, as its id; needed only for a person several providers know." };

    private static Option<int?> Offset() => new("--offset") { Description = "How many to skip, for the next page; 0 when absent." };

    private static Argument<string> ContactId() => new("contact-id") { Description = "The contact, as cnt_...." };

    /// <summary>The contact fields <c>create</c> and <c>update</c> share, so both spell them the same way.</summary>
    private sealed class ContactFields
    {
        private readonly Option<string?> _firstName = new("--first-name") { Description = "Given name." };
        private readonly Option<string?> _lastName = new("--last-name") { Description = "Family name." };
        private readonly Option<string?> _company = new("--company") { Description = "Where they work." };
        private readonly Option<string?> _title = new("--title") { Description = "Their role there." };
        private readonly Option<string?> _timeZone = new("--time-zone") { Description = "Their time zone as an IANA id, for example Europe/Kyiv." };
        private readonly Option<string[]> _email = VerbOptions.Repeatable("--email", "An email address. Repeat the option for more than one.");
        private readonly Option<string[]> _channel = VerbOptions.Repeatable("--channel", "Another way to reach them, written <name>=<value>, for example linkedin=https://www.linkedin.com/in/ada. Repeat the option for more than one.");
        private readonly Option<string?> _custom = new("--custom") { Description = "Everything else worth keeping about them, as a JSON object." };

        public void AddTo(Command command)
        {
            command.Options.Add(_firstName);
            command.Options.Add(_lastName);
            command.Options.Add(_company);
            command.Options.Add(_title);
            command.Options.Add(_timeZone);
            command.Options.Add(_email);
            command.Options.Add(_channel);
            command.Options.Add(_custom);
        }

        public void LayOver(JsonObject body, ParseResult parseResult)
        {
            body.Set("first_name", parseResult.GetValue(_firstName))
                .Set("last_name", parseResult.GetValue(_lastName))
                .Set("company", parseResult.GetValue(_company))
                .Set("title", parseResult.GetValue(_title))
                .Set("time_zone", parseResult.GetValue(_timeZone))
                .Set("channels", Channels(parseResult.GetValue(_email), parseResult.GetValue(_channel)))
                .Set("custom", VerbOptions.Object(parseResult.GetValue(_custom), "--custom"));
        }

        /// <summary>The channel options as the API's channels array; naming any of them states the whole list.</summary>
        private static JsonArray? Channels(string[]? emails, string[]? channels)
        {
            var entries = new JsonArray();
            foreach (var email in emails ?? [])
            {
                entries.Add(Entry("email", email));
            }

            foreach (var pair in channels ?? [])
            {
                var separator = pair.IndexOf('=');
                if (separator <= 0 || separator == pair.Length - 1)
                {
                    throw new UsageException("--channel must be written <name>=<value>, for example linkedin=https://www.linkedin.com/in/ada.");
                }

                entries.Add(Entry(pair[..separator], pair[(separator + 1)..]));
            }

            return entries.Count == 0 ? null : entries;
        }

        private static JsonObject Entry(string channel, string value) => new()
        {
            ["channel"] = channel,
            ["value"] = value,
            ["primary"] = false,
        };
    }
}
