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
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

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
            ActorOption.Parse(parseResult.GetValue(actor));
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
        var includeArchived = new Option<bool>("--include-archived") { Description = "Include archived contacts, which are hidden by default." };
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Options.Add(channel);
        command.Options.Add(value);
        command.Options.Add(includeArchived);
        command.Options.Add(limit);
        command.Options.Add(cursor);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty()
                .Set("channel", parseResult.GetValue(channel))
                .Set("value", parseResult.GetValue(value))
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
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

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
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return await OperationRunner
                .RunAsync(env, Operations.ContactArchive, body, new RunOptions(parseResult.GetValue(human), ContactRenderers.Contact), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Argument<string> ContactId() => new("contact-id") { Description = "The contact, as cnt_…." };

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
