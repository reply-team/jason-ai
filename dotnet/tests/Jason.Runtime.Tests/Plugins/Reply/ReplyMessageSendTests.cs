using System.Text.Json.Nodes;
using Jason.Contracts.Channels;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>message.send</c> as the official package performs it, against the stand-in: the subject and the text held to what
/// Reply would send as written, the account to the address the person approving read, the person found and their contact
/// read before anything goes — opted out at Reply is a stop, another address or profile is a refusal, no LinkedIn profile
/// is filled — and then the one command made for the format, with the text on stdin: <c>reply contact send-email</c>, the
/// text written as HTML, or <c>linkedin-message</c>, <c>linkedin-inmail</c> or <c>linkedin-voice</c>. A person Reply holds
/// nobody for is imported by that same command, from their fields on stdin beside the text. What went out is read back
/// from the account.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public partial class ReplyMessageSendTests
{
    private const string Operation = "message.send";
    private const string ContactId = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string Address = "marta@example.com";
    private const string Profile = "https://www.linkedin.com/in/marta-example";
    private const string Mailbox = "ann@example.com";
    private const string SenderProfile = "https://www.linkedin.com/in/ann-owner";
    private const string Subject = "The Lisbon meetup";
    private const string Body = "Hi Marta, thanks for the chat at the meetup.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A person reached by email, and the caller's mailbox.</summary>
    private static ReplyAccount ByEmail(string? email = Address, bool optedOut = false) =>
        new ReplyAccount().WithContact(1001, email, "Marta", optedOut: optedOut).WithSender(12, "email", "Ann Owner", Mailbox);

    /// <summary>A person reached on LinkedIn, the caller's LinkedIn account, and the two connected.</summary>
    private static ReplyAccount OnLinkedIn(string? linkedIn = Profile, bool optedOut = false, bool connected = true)
    {
        var account = new ReplyAccount().WithContact(1001, null, "Marta", optedOut: optedOut, linkedIn: linkedIn).WithSender(34, "linkedin", "Ann Owner", SenderProfile);
        return connected ? account.WithConnection(1001, 34) : account;
    }

    // -------------------------------------------------------------------------------------------------------
    // Each format, its command and what it carries
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Each format runs the one command made for it, naming the account by the option the CLI takes for that channel, and
    /// carries its text on stdin under the CLI's own names: a voice message's text is the script Reply speaks. Nothing but
    /// the account's list, the contact's read and the send is asked; and no moment is passed on, whatever Reply answers.
    /// </summary>
    [Theory]
    [InlineData("email", "contact send-email", "--email-account-id", "12", """{"subject":"The Lisbon meetup","body":"Hi Marta, thanks for the chat at the meetup."}""", "<60001.direct@mail.example.com>")]
    [InlineData("linkedin_message", "contact linkedin-message", "--linkedin-account-id", "34", """{"body":"Hi Marta, thanks for the chat at the meetup."}""", "70001")]
    [InlineData("linkedin_inmail", "contact linkedin-inmail", "--linkedin-account-id", "34", """{"subject":"The Lisbon meetup","body":"Hi Marta, thanks for the chat at the meetup."}""", "70001")]
    [InlineData("linkedin_voice", "contact linkedin-voice", "--linkedin-account-id", "34", """{"script":"Hi Marta, thanks for the chat at the meetup."}""", "70001")]
    public async Task Each_format_runs_its_own_command_with_its_text_on_stdin(string format, string command, string option, string sender, string stdin, string identifier)
    {
        var channel = format == "email" ? "email" : "linkedin";
        using var account = channel == "email" ? ByEmail() : OnLinkedIn();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(format, pinned: "1001"));

        var item = ReplyOperations.Succeeded(Operation, result)["items"]![0]!.AsObject();
        Assert.Equal((ContactId, "sent", channel, format, sender), ((string?)item["contact_id"], (string?)item["status"], (string?)item["channel"], (string?)item["format"], (string?)item["sender_external_id"]));
        Assert.Equal(identifier, (string?)item["message"]!["external_id"]);
        Assert.Null(item["message"]!["sent_at"]);
        var reference = channel + "-1001";
        Assert.Equal(reference, (string?)item["reference"]);
        Assert.Equal($$"""{"contact":"1001","send":"{{reference}}"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());

        Assert.Equal(["account list", "contact get 1001", command], Lines(account));
        Assert.Equal(channel, account.Calls[0].Option("--channel"));
        var send = account.Calls[^1];
        Assert.Equal(["--json", "-q", .. command.Split(' '), "--contact-id", "1001", option, sender, "--message", "-", "--idempotency-key", "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRK"], send.Args);
        Assert.Equal(stdin, JsonNode.Parse(send.Body!)!.ToJsonString());
        var went = Assert.Single(account.Sent)!;
        Assert.Equal((command, 1001, long.Parse(sender, System.Globalization.CultureInfo.InvariantCulture)), ((string?)went["command"], (int)went["contactId"]!, (long)went["accountId"]!));
    }

    /// <summary>
    /// Reply takes an email's body as HTML, and the text a person approved is plain text: every character HTML reads as
    /// markup is escaped, and every line break is one <c>&lt;br&gt;</c>, so the recipient reads exactly the text approved.
    /// Spaces and tabs stay as written. The subject is a header, and goes as written.
    /// </summary>
    [Fact]
    public async Task An_email_s_text_is_written_as_html_that_shows_exactly_the_text()
    {
        using var account = ByEmail();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        const string written = "Tom & Jerry <3 tom@example.com>\r\nsaid \"5 > 3\" and 'yes'.\nThen\rnothing\u2028more\u2029  two  spaces\tand a tab.";
        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("email", pinned: "1001", subject: "Tom & Jerry <1 > 0>", body: written)));

        var text = Assert.Single(account.Sent)!["text"]!;
        Assert.Equal(
            "Tom &amp; Jerry &lt;3 tom@example.com&gt;<br>said &quot;5 &gt; 3&quot; and &#39;yes&#39;.<br>Then<br>nothing<br>more<br>  two  spaces\tand a tab.",
            (string?)text["body"]);
        Assert.Equal("Tom & Jerry <1 > 0>", (string?)text["subject"]);
    }

    /// <summary>
    /// Only an email's text is written as HTML: a LinkedIn text of every format goes as written, so its recipient never
    /// reads an escape or a break tag the person approving did not write. A voice message's text is the script, and an
    /// InMail's subject goes as written too, a <c>&lt;</c> that would start a tag in an email among it.
    /// </summary>
    [Theory]
    [InlineData("linkedin_message", "body")]
    [InlineData("linkedin_inmail", "body")]
    [InlineData("linkedin_voice", "script")]
    public async Task A_linkedin_text_goes_exactly_as_written(string format, string field)
    {
        using var account = OnLinkedIn();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        const string written = "Tom & Jerry <tom@example.com>\nsaid \"5 > 3\" and 'yes'.";
        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(format, pinned: "1001", subject: "Tom & Jerry <1 > 0> <a", body: written)));

        var text = Assert.Single(account.Sent)!["text"]!;
        Assert.Equal(written, (string?)text[field]);
        if (format == "linkedin_inmail")
        {
            Assert.Equal("Tom & Jerry <1 > 0> <a", (string?)text["subject"]);
        }
    }

    /// <summary>
    /// The lengths the document holds each format to are the ones the CLI takes: the longest text of each format the
    /// document allows, counted in characters, goes through the CLI and out.
    /// </summary>
    [Theory]
    [InlineData("email", 12000, 998)]
    [InlineData("linkedin_message", 8000, 0)]
    [InlineData("linkedin_inmail", 1900, 200)]
    [InlineData("linkedin_voice", 1000, 0)]
    public async Task The_longest_text_the_document_allows_is_one_the_cli_takes(string format, int longest, int subject)
    {
        using var account = format == "email" ? ByEmail() : OnLinkedIn();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var body = string.Concat(Enumerable.Repeat("😀", longest));
        var input = Input(format, pinned: "1001", subject: subject == 0 ? null : string.Concat(Enumerable.Repeat("😀", subject)), body: body);

        var result = await InvokeAsync(api, input);

        Assert.True(result.Outcome is InvocationOutcome.Succeeded, $"the longest {format} the document allows did not go: {(result.Outcome as InvocationOutcome.Failed)?.Error.Code}.");
        Assert.Single(account.Sent);
    }

    /// <summary>
    /// Reply reads a double opening brace as the start of a placeholder of its own, so a subject or a text with one is
    /// refused before anything is called, whether or not it closes and whatever the format: a subject is content too.
    /// </summary>
    [Theory]
    // Critical: no provider template expansion.
    [InlineData("email", "Hi {{firstName}}", null, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.X3)])]
    [InlineData("email", null, "Braces {{ stay as typed.")]
    [InlineData("linkedin_inmail", "{{ company }} and us", null)]
    [InlineData("linkedin_message", null, "Hi {{firstName}}")]
    [InlineData("linkedin_voice", null, "Hi {{ name }}")]
    public async Task A_double_opening_brace_in_the_subject_or_the_text_is_refused_before_anything_is_called(string format, string? subject, string? body)
    {
        using var account = format == "email" ? ByEmail() : OnLinkedIn();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var takesSubject = format is "email" or "linkedin_inmail";
        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format, pinned: "1001", subject: takesSubject ? subject ?? Subject : null, body: body ?? Body)));

        Assert.Equal(("message_invalid", FailureClass.Validation), (error.Code, error.Class));
        Assert.Empty(account.Calls);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// Reply reads <c>@prompt[[</c> as a prompt to a model and sends the model's answer, which nobody approved, so a final
    /// subject or text with one is refused before anything is called, whatever the format, as a double opening brace is.
    /// </summary>
    [Theory]
    [InlineData("email", "Our @prompt[[a line for them]] note", null)]
    [InlineData("email", null, "Hi Marta, @prompt[[write a line about the meetup]]")]
    [InlineData("linkedin_inmail", "@prompt[[a subject]]", null)]
    [InlineData("linkedin_message", null, "Hi @prompt[[ unclosed")]
    [InlineData("linkedin_voice", null, "Hi @prompt[[a greeting]]")]
    public async Task A_prompt_marker_in_a_final_subject_or_text_is_refused_before_anything_is_called(string format, string? subject, string? body)
    {
        using var account = format == "email" ? ByEmail() : OnLinkedIn();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var takesSubject = format is "email" or "linkedin_inmail";
        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format, pinned: "1001", subject: takesSubject ? subject ?? Subject : null, body: body ?? Body)));

        Assert.Equal(("message_invalid", FailureClass.Validation), (error.Code, error.Class));
        Assert.Contains("@prompt[[", error.Message, StringComparison.Ordinal);
        Assert.Empty(account.Calls);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// Reply decodes an email's whole message before it reads it and drops a tag it does not know with everything after
    /// it, so a final subject or text holding a <c>&lt;</c> an HTML reader takes as the start of a tag — one followed by an
    /// ASCII letter, <c>/</c>, <c>!</c> or <c>?</c> — is refused before anything is called. The message names the
    /// character and quotes nothing of the text.
    /// </summary>
    [Theory]
    [InlineData(null, "Write to <name@example.com> for the details.", "body", 10)]
    [InlineData("Notes <name@example.com> end", null, "subject", 7)]
    [InlineData(null, "Done.</p>", "body", 6)]
    [InlineData(null, "Hi <!-- a note -->", "body", 4)]
    [InlineData(null, "Hi <?xml?>", "body", 4)]
    [InlineData(null, "a < b, then <<B", "body", 14)]
    public async Task A_tag_s_start_in_a_final_email_is_refused_where_it_stands_before_anything_is_called(string? subject, string? body, string part, int position)
    {
        using var account = ByEmail();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001", subject: subject ?? Subject, body: body ?? Body)));

        Assert.Equal(("message_invalid", FailureClass.Validation), (error.Code, error.Class));
        var reason = $"The {part} holds, at character {position}, a `<` Reply reads as markup, and Reply drops a tag it does not know with everything after it.";
        Assert.Equal(reason, (string?)error.Details!["reason"]);
        Assert.StartsWith(reason, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("name@", error.Message, StringComparison.Ordinal);
        Assert.Empty(account.Calls);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A <c>&lt;</c> that starts no tag — before a space, a digit or a letter outside ASCII, at the end, or before another
    /// <c>&lt;</c> — is text to Reply: a final email holding one goes, escaped, and reaches the person whole. So does a
    /// character reference the person wrote, which the body carries escaped and the subject as written.
    /// </summary>
    [Theory]
    [InlineData("a < b", "a &lt; b")]
    [InlineData("<3 from us", "&lt;3 from us")]
    [InlineData("it ends <", "it ends &lt;")]
    [InlineData("<< twice, and 1 <= 2", "&lt;&lt; twice, and 1 &lt;= 2")]
    [InlineData("<é and <Ж are letters", "&lt;é and &lt;Ж are letters")]
    [InlineData("&lt;b stays as written", "&amp;lt;b stays as written")]
    public async Task A_lt_that_starts_no_tag_goes_in_a_final_email_and_reaches_the_person_whole(string written, string escaped)
    {
        using var account = ByEmail();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("email", pinned: "1001", subject: written, body: written)));

        var went = Assert.Single(account.Sent)!;
        Assert.Equal((written, escaped), ((string?)went["text"]!["subject"], (string?)went["text"]!["body"]));
        Assert.Equal((written, written), ((string?)went["rendered"]!["subject"], (string?)went["rendered"]!["body"]));
    }

    // -------------------------------------------------------------------------------------------------------
    // A template, written in Reply's syntax
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A message composed as a template reaches Reply in Reply's own syntax, by the same command, and Reply fills it in
    /// for the person: each name the standard lists as Reply names it, a fallback, a comparison and the other branch as
    /// Reply writes them. A template with no tag at all is its text.
    /// </summary>
    [Theory]
    [InlineData(
        "For {{contact.first_name}}",
        "{{contact.first_name}} {{contact.last_name}} of {{contact.company}}, {{contact.title}}, at {{contact.email}}",
        "For {{FirstName}}",
        "{{FirstName}} {{LastName}} of {{Company}}, {{Title}}, at {{Email}}",
        "For Marta",
        "Marta Alvarez of Puerto Analytics, Head of Growth, at marta@example.com")]
    [InlineData(Subject, "Tier {{contact.custom.tier}}", Subject, "Tier {{tier}}", Subject, "Tier gold")]
    [InlineData(
        Subject,
        "{{sender.first_name}} / {{sender.last_name}} / {{sender.name}} / {{sender.address}}",
        Subject,
        "{{SendingEmail.SenderFirstName}} / {{SendingEmail.SenderLastName}} / {{SendingEmail.SenderName}} / {{SendingEmail.Email}}",
        Subject,
        "Ann / Owner / Ann Owner / ann@example.com")]
    [InlineData(Subject, "Hi {{contact.custom.nickname | 'there'}}", Subject, "Hi {{nickname | 'there'}}", Subject, "Hi there")]
    [InlineData(
        Subject,
        "{{#if contact.custom.tier == 'gold'}}Gold{{else}}Other{{/if}} {{#if contact.title != 'CEO'}}and no CEO{{/if}}",
        Subject,
        "{{#if tier == 'gold'}}Gold{{else}}Other{{/if}} {{#if Title != 'CEO'}}and no CEO{{/if}}",
        Subject,
        "Gold and no CEO")]
    [InlineData(
        Subject,
        "{{#if contact.custom.nickname}}Hi {{contact.custom.nickname}}{{else}}Hi {{contact.first_name}}{{/if}}",
        Subject,
        "{{#if nickname}}Hi {{nickname}}{{else}}Hi {{FirstName}}{{/if}}",
        Subject,
        "Hi Marta")]
    [InlineData(Subject, Body, Subject, Body, Subject, Body)]
    public async Task A_template_reaches_reply_in_reply_s_syntax_and_reply_fills_it_in(
        string subject, string body, string sentSubject, string sentBody, string renderedSubject, string renderedBody)
    {
        using var account = Filled();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Template(subject, body)))["items"]![0]!;

        Assert.Equal("sent", (string?)item["status"]);
        Assert.Equal(["account list", "contact get 1001", "contact send-email"], Lines(account));
        var went = Assert.Single(account.Sent)!;
        Assert.Equal((sentSubject, sentBody), ((string?)went["text"]!["subject"], (string?)went["text"]!["body"]));
        Assert.Equal((renderedSubject, renderedBody), ((string?)went["rendered"]!["subject"], (string?)went["rendered"]!["body"]));
    }

    /// <summary>
    /// A template's body is HTML as a final text's is: its text, the text of a fallback and the text a comparison compares
    /// reach Reply escaped, a <c>&lt;</c> as <c>&amp;lt;</c>, a <c>&amp;</c> as <c>&amp;amp;</c> and a <c>'</c> as
    /// <c>&amp;#39;</c>, each line break as one <c>&lt;br&gt;</c>, since Reply decodes the whole message before it reads
    /// it; its tags go as Reply reads them. The subject is a header, and goes as written.
    /// </summary>
    [Fact]
    public async Task A_template_s_text_reaches_reply_escaped_and_its_tags_do_not()
    {
        using var account = Filled();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Template(
            "Tom & Jerry's <3 news> for {{contact.first_name | 'you'}}",
            "Tom & Jerry <3 tom@example.com>\nsaid 'yes', {{contact.custom.nickname | 'R&D < team>\nand co'}}{{#if contact.company == 'Puerto & Co'}} at Puerto{{/if}}.");
        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, input));

        var went = Assert.Single(account.Sent)!;
        Assert.Equal("Tom & Jerry's <3 news> for {{FirstName | 'you'}}", (string?)went["text"]!["subject"]);
        Assert.Equal(
            "Tom &amp; Jerry &lt;3 tom@example.com&gt;<br>said &#39;yes&#39;, {{nickname | 'R&amp;D &lt; team&gt;<br>and co'}}{{#if Company == 'Puerto &amp; Co'}} at Puerto{{/if}}.",
            (string?)went["text"]!["body"]);
        Assert.Equal(
            "Tom & Jerry <3 tom@example.com><br>said 'yes', R&D < team><br>and co.",
            (string?)went["rendered"]!["body"]);
    }

    /// <summary>
    /// Reply decodes the whole message before it reads it, so the text a comparison compares reaches it escaped as the
    /// body's text does, and an <c>&amp;</c> in it is <c>&amp;amp;</c>: Reply then compares the value with the text as
    /// the template wrote it.
    /// </summary>
    [Fact]
    public async Task An_ampersand_in_a_comparison_reaches_reply_escaped_and_compares_as_written()
    {
        using var account = Filled().WithCustomValue(1001, "team", "R&D");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Template(Subject, "{{#if contact.custom.team == 'R&D'}}Hi team{{else}}Hi all{{/if}}")));

        var went = Assert.Single(account.Sent)!;
        Assert.Equal("{{#if team == 'R&amp;D'}}Hi team{{else}}Hi all{{/if}}", (string?)went["text"]!["body"]);
        Assert.Equal("Hi team", (string?)went["rendered"]!["body"]);
    }

    /// <summary>
    /// A custom field under a key Reply keeps for a value of its own — a field of the contact, an engagement count, a
    /// date it computes, one of its own helpers or a word its template engine reads — would be filled with Reply's value,
    /// or read as something else, so it is refused before anything is called, and the refusal says where without
    /// quoting the template.
    /// </summary>
    [Theory]
    [InlineData("contact.custom.Company")]
    [InlineData("contact.custom.FirstName")]
    [InlineData("contact.custom.Opens")]
    [InlineData("contact.custom.last_touch")]
    [InlineData("contact.custom.today")]
    [InlineData("contact.custom.SendingEmail")]
    [InlineData("contact.custom._f")]
    [InlineData("contact.custom.else")]
    [InlineData("contact.custom.null")]
    public async Task A_custom_field_under_a_key_reply_keeps_is_refused_before_anything_is_called(string name)
    {
        using var account = Filled();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Template(Subject, "Hi {{" + name + " | 'there'}}")));

        Assert.Equal(("template_unsupported", FailureClass.Validation), (error.Code, error.Class));
        Assert.Equal(
            "The body names a custom field at character 4 under a key Reply keeps for a value of its own, which Reply would write instead of the field.",
            (string?)error.Details!["reason"]);
        Assert.Empty(account.Calls);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The package reads a template by the same standard the document holds it to, behind the document: a template the
    /// standard refuses is refused before anything is called, by the construct's own name and the character it starts
    /// on, exactly as the standard names them, in the subject as in the body.
    /// </summary>
    [Theory]
    [InlineData(null, "Hi {{toLower contact.first_name}}")]
    [InlineData(null, "Hi {{{contact.first_name}}}")]
    [InlineData(null, "Hi {{ contact.first_name }}")]
    [InlineData(null, "{{#if contact.title}}x")]
    [InlineData(null, "Hi {{contact.phone}}")]
    [InlineData(null, "Hi \\{{contact.first_name}}")]
    [InlineData(null, "Hi {{contact.first_name | 'never closed}}")]
    [InlineData(null, "Hi }} there")]
    [InlineData(null, "Hi @prompt[[ a line ]]")]
    [InlineData("For {{#each contact.custom.items}}x{{/each}}", null)]
    public async Task A_template_the_standard_refuses_is_refused_by_name_before_anything_is_called(string? subject, string? body)
    {
        using var account = Filled();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeUnreadAsync(api, Template(subject ?? Subject, body ?? Body)));

        var (part, written) = subject is null ? ("body", body!) : ("subject", subject);
        var refusal = TemplateGrammar.Check(written)!;
        Assert.Equal(("template_unsupported", FailureClass.Validation), (error.Code, error.Class));
        Assert.Equal(
            $"The {part} writes {refusal.Row.Name} at character {refusal.Position}, which the template standard refuses.",
            (string?)error.Details!["reason"]);
        Assert.Empty(account.Calls);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The document holds a voice message to a final script, since Reply speaks it as written; the package holds it too,
    /// behind the document.
    /// </summary>
    [Fact]
    public async Task A_template_in_a_voice_message_is_refused_before_anything_is_called_though_the_document_was_not_read()
    {
        using var account = OnLinkedIn();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Input("linkedin_voice", pinned: "1001", body: "Hi {{contact.first_name}}");
        input["args"]!["message"]!["compose"] = "template";
        var error = ReplyOperations.Failed(Operation, await InvokeUnreadAsync(api, input));

        Assert.Equal(("template_unsupported", FailureClass.Validation), (error.Code, error.Class));
        Assert.Contains("voice message is spoken from a final script", error.Message, StringComparison.Ordinal);
        Assert.Empty(account.Calls);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A LinkedIn message or an InMail composed as a template reaches Reply in Reply's own syntax, by the same command,
    /// and Reply fills it in for the person: the sender's names as Reply names them on LinkedIn, inside a condition and a
    /// fallback too. Its text goes as written, a line break included; Reply's conversion from HTML to text after filling it
    /// in is Reply's, so no row here writes markup in it.
    /// </summary>
    [Theory]
    [InlineData(
        "linkedin_message",
        null,
        "Hi {{contact.first_name}} {{contact.custom.nickname | 'there'}}, {{sender.first_name}} ({{sender.name}})\nbye",
        null,
        "Hi {{FirstName}} {{nickname | 'there'}}, {{SendingLinkedIn.SenderFirstName}} ({{SendingLinkedIn.SenderName}})\nbye",
        null,
        "Hi Marta there, Ann (Ann Owner)\nbye")]
    [InlineData(
        "linkedin_inmail",
        "For {{contact.first_name}}",
        "{{#if contact.title == 'Head of Growth'}}Hi{{else}}Hello{{/if}} {{contact.last_name}} of {{contact.company}}, tier {{contact.custom.tier}}",
        "For {{FirstName}}",
        "{{#if Title == 'Head of Growth'}}Hi{{else}}Hello{{/if}} {{LastName}} of {{Company}}, tier {{tier}}",
        "For Marta",
        "Hi Alvarez of Puerto Analytics, tier gold")]
    public async Task A_linkedin_template_reaches_reply_in_reply_s_syntax_as_written_and_reply_fills_it_in(
        string format, string? subject, string body, string? sentSubject, string sentBody, string? renderedSubject, string renderedBody)
    {
        using var account = FilledOnLinkedIn();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Input(format, pinned: "1001", subject: subject, body: body);
        input["args"]!["message"]!["compose"] = "template";
        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, input))["items"]![0]!;

        Assert.Equal("sent", (string?)item["status"]);
        Assert.Equal(FormatCommand(format), Lines(account)[^1]);
        var went = Assert.Single(account.Sent)!;
        Assert.Equal((sentSubject, sentBody), ((string?)went["text"]!["subject"], (string?)went["text"]!["body"]));
        Assert.Equal((renderedSubject, renderedBody), ((string?)went["rendered"]!["subject"], (string?)went["rendered"]!["body"]));
    }

    /// <summary>
    /// Reply has no variable for the sending account's address on LinkedIn, its profile, and the arguments hold it: the
    /// package writes the profile they hold in its place, as the example a person approved shows it, a condition on it
    /// decided by it and the branch taken written, and Reply fills in the rest.
    /// </summary>
    [Theory]
    [InlineData(
        "linkedin_message",
        null,
        "Hi, my profile is {{sender.address}}.",
        null,
        "Hi, my profile is " + SenderProfile + ".",
        null,
        "Hi, my profile is " + SenderProfile + ".")]
    [InlineData(
        "linkedin_inmail",
        "From {{sender.address | 'us'}}",
        "{{#if sender.address}}See {{sender.address}}{{else}}No profile{{/if}}, {{contact.first_name}}",
        "From " + SenderProfile,
        "See " + SenderProfile + ", {{FirstName}}",
        "From " + SenderProfile,
        "See " + SenderProfile + ", Marta")]
    [InlineData(
        "linkedin_message",
        null,
        "{{#if sender.address == '" + SenderProfile + "'}}Mine{{else}}Other{{/if}} {{#if sender.address != 'x'}}{{sender.first_name}}{{/if}}",
        null,
        "Mine {{SendingLinkedIn.SenderFirstName}}",
        null,
        "Mine Ann")]
    public async Task A_linkedin_template_naming_the_sender_s_address_reaches_reply_with_the_profile_the_arguments_hold_written_in(
        string format, string? subject, string body, string? sentSubject, string sentBody, string? renderedSubject, string renderedBody)
    {
        using var account = FilledOnLinkedIn();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Input(format, pinned: "1001", subject: subject, body: body);
        input["args"]!["message"]!["compose"] = "template";
        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, input))["items"]![0]!;

        Assert.Equal("sent", (string?)item["status"]);
        var went = Assert.Single(account.Sent)!;
        Assert.Equal((sentSubject, sentBody), ((string?)went["text"]!["subject"], (string?)went["text"]!["body"]));
        Assert.Equal((renderedSubject, renderedBody), ((string?)went["rendered"]!["subject"], (string?)went["rendered"]!["body"]));
    }

    /// <summary>
    /// A LinkedIn account Reply holds no profile for, named with none in the arguments: a template naming its address
    /// writes the fallback, and a condition on it takes the branch for no value, as the example shows them.
    /// </summary>
    [Theory]
    [InlineData("Hi from {{sender.address | 'our team'}}", "Hi from our team")]
    [InlineData("{{#if sender.address}}See {{sender.address}}{{else}}No profile{{/if}}", "No profile")]
    [InlineData("{{#if sender.address == ''}}None{{else}}Some{{/if}}", "None")]
    public async Task A_linkedin_template_naming_an_address_the_arguments_hold_none_of_writes_its_fallback(string body, string sent)
    {
        using var account = FilledOnLinkedIn().WithSender(34, "linkedin", "Ann Owner", null);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Input("linkedin_message", pinned: "1001", body: body);
        input["args"]!["sender"]!["address"] = null;
        input["args"]!["message"]!["compose"] = "template";
        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, input));

        Assert.Equal(sent, (string?)Assert.Single(account.Sent)!["text"]!["body"]);
    }

    /// <summary>
    /// The same address named with no fallback, where the arguments hold none: Reply has no variable for it and there is
    /// nothing to write in its place, so it is refused before anything is called.
    /// </summary>
    [Fact]
    public async Task A_linkedin_template_naming_an_address_the_arguments_hold_none_of_without_a_fallback_is_refused_before_anything_is_called()
    {
        using var account = FilledOnLinkedIn().WithSender(34, "linkedin", "Ann Owner", null);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Input("linkedin_message", pinned: "1001", body: "Hi, {{sender.address}} here");
        input["args"]!["sender"]!["address"] = null;
        input["args"]!["message"]!["compose"] = "template";
        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, input));

        Assert.Equal(("template_unsupported", FailureClass.Validation), (error.Code, error.Class));
        Assert.Equal(
            "The body names `sender.address` at character 5 with no fallback: Reply has no variable for the sending account's address on LinkedIn, and the arguments hold none.",
            (string?)error.Details!["reason"]);
        Assert.Empty(account.Calls);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// Reply's LinkedIn sending account carries a first name and a name, and no last name, nor does the input hold one,
    /// so a LinkedIn template naming it is refused before anything is called, by the name and the character it starts on,
    /// inside a condition as well.
    /// </summary>
    [Theory]
    [InlineData("linkedin_message", "Hi, {{sender.last_name}} here", "body", "sender.last_name", 5)]
    [InlineData("linkedin_inmail", "{{#if contact.title}}Hi{{else}}{{sender.last_name}}{{/if}}", "body", "sender.last_name", 32)]
    public async Task A_linkedin_template_naming_a_sender_field_reply_lacks_there_is_refused_before_anything_is_called(
        string format, string body, string part, string name, int position)
    {
        using var account = FilledOnLinkedIn();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Input(format, pinned: "1001", body: body);
        input["args"]!["message"]!["compose"] = "template";
        var error = ReplyOperations.Failed(Operation, await InvokeUnreadAsync(api, input));

        Assert.Equal(("template_unsupported", FailureClass.Validation), (error.Code, error.Class));
        Assert.Equal($"The {part} names `{name}` at character {position}, which Reply holds no field for on LinkedIn.", (string?)error.Details!["reason"]);
        Assert.Empty(account.Calls);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A LinkedIn template held for a later moment goes to Reply as it stands, to be filled in when it goes: a name Reply
    /// has no value for is not refused now.
    /// </summary>
    [Fact]
    public async Task A_linkedin_template_held_for_later_is_accepted_and_filled_in_when_it_goes()
    {
        using var account = FilledOnLinkedIn();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Input("linkedin_message", pinned: "1001", body: "Hi {{contact.custom.nickname}}");
        input["args"]!["message"]!["compose"] = "template";
        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, WithMoment(input, "2099-11-03T09:00:00-05:00")))["items"]![0]!;

        Assert.Equal("accepted", (string?)item["status"]);
        var went = Assert.Single(account.Sent)!;
        Assert.Equal("Hi {{nickname}}", (string?)went["text"]!["body"]);
        Assert.Null(went["rendered"]);
    }

    /// <summary>
    /// Reply reads <c>@prompt[[</c> in the text it has filled in as a prompt to a model, whose answer nobody approved. The
    /// package refuses a template whose text and fallbacks spell it once Reply fills them in, along any one way through
    /// its conditions, before anything is called. A value is not the package's to read, so a value between two parts
    /// breaks the spelling.
    /// </summary>
    [Theory]
    [InlineData("@prom{{contact.custom.nickname | 'pt[['}} a line", 38)]
    [InlineData("{{contact.custom.nickname | '@pro'}}mpt[[ a line", 41)]
    [InlineData("{{contact.custom.nickname | '@prom'}}{{contact.last_name | 'pt[['}} a line", 64)]
    [InlineData("@prompt{{#if contact.title}}[[{{/if}} a line", 30)]
    [InlineData("@pr{{#if contact.title}}x{{else}}ompt[[{{/if}} a line", 39)]
    [InlineData("@prompt{{#if contact.title}}x{{/if}}[[ a line", 38)]
    [InlineData("{{#if contact.title}}@prompt{{/if}}[[ a line", 37)]
    public async Task A_template_that_spells_a_prompt_once_filled_in_is_refused_before_anything_is_called(string body, int position)
    {
        using var account = Filled();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Template(Subject, body)));

        Assert.Equal(("template_unsupported", FailureClass.Validation), (error.Code, error.Class));
        Assert.Equal(
            $"The body spells `@prompt[[` once Reply fills it in, complete at character {position}, which Reply would read as a prompt to a model.",
            (string?)error.Details!["reason"]);
        Assert.Empty(account.Calls);
        Assert.Empty(account.Sent);
    }

    /// <summary>A value between the parts of a prompt is the person's, and the package does not read it: the template goes.</summary>
    [Fact]
    public async Task A_value_between_the_parts_of_a_prompt_breaks_its_spelling()
    {
        using var account = Filled();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Template(Subject, "@prompt{{contact.first_name}}[[ a line")));

        Assert.Equal("@promptMarta[[ a line", (string?)Assert.Single(account.Sent)!["rendered"]!["body"]);
    }

    /// <summary>
    /// Reply reads a comparison only where its quoted text holds letters, digits, white space and the punctuation its
    /// comparison takes; anywhere else it leaves the tag for its template engine, which reads it otherwise. The package
    /// refuses a comparison with any other character before anything is called, and says where without quoting it.
    /// </summary>
    [Theory]
    [InlineData("{{#if contact.title == 'Why?'}}x{{/if}}", 28)]
    [InlineData("{{#if contact.custom.tier == 'a|b'}}x{{/if}}", 32)]
    [InlineData("{{#if contact.title != 'R&D [lab]'}}x{{/if}}", 29)]
    [InlineData("{{#if contact.company == 'Café ’s'}}x{{/if}}", 32)]
    [InlineData("{{#if contact.title == 'x > y'}}x{{/if}}", 27)]
    public async Task A_comparison_with_a_character_reply_s_comparison_does_not_read_is_refused_before_anything_is_called(string body, int position)
    {
        using var account = Filled();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Template(Subject, body)));

        Assert.Equal(("template_unsupported", FailureClass.Validation), (error.Code, error.Class));
        Assert.Equal(
            $"The body compares a value with a text holding, at character {position}, a character Reply's comparison does not read.",
            (string?)error.Details!["reason"]);
        Assert.Empty(account.Calls);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The punctuation Reply's comparison takes, and a letter of any script, reach Reply escaped as the body's text does,
    /// and Reply compares the value with them as written.
    /// </summary>
    [Fact]
    public async Task A_comparison_with_the_punctuation_reply_reads_compares_as_written()
    {
        using var account = Filled().WithCustomValue(1001, "venue", "Café-Bar, S.A. (Lisbon) #2: 50% off; <=\"1\" @ $5*2+1/3=~`!^");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var body = "{{#if contact.custom.venue == 'Café-Bar, S.A. (Lisbon) #2: 50% off; <=\"1\" @ $5*2+1/3=~`!^'}}Yes{{/if}}";
        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Template(Subject, body)));

        var went = Assert.Single(account.Sent)!;
        Assert.Equal(
            "{{#if venue == 'Café-Bar, S.A. (Lisbon) #2: 50% off; &lt;=&quot;1&quot; @ $5*2+1/3=~`!^'}}Yes{{/if}}",
            (string?)went["text"]!["body"]);
        Assert.Equal("Yes", (string?)went["rendered"]!["body"]);
    }

    /// <summary>
    /// A template's text, a fallback's text and the text a comparison compares are held as a final text is: a
    /// <c>&lt;</c> an HTML reader takes as the start of a tag is refused before anything is called, naming the character,
    /// as <c>template_unsupported</c>: the contract keeps <c>message_invalid</c> for a final message. Whether Reply
    /// sanitizes a template before or after it fills it in no send has shown, so a comparison's text is held too. What
    /// follows a <c>&lt;</c> is read along every way through the conditions, and a value Reply fills in is not in hand, so a
    /// <c>&lt;</c> right before one is refused too: an address filled in there would be the tag Reply drops.
    /// </summary>
    [Theory]
    [InlineData(Subject, "Write to <name@example.com> today.", "body", 10)]
    [InlineData(Subject, "Write to <{{sender.address}}> today.", "body", 10)]
    [InlineData(Subject, "Hi {{contact.first_name | '</b>'}}", "body", 28)]
    [InlineData(Subject, "Hi {{contact.first_name | 'x<'}}b", "body", 29)]
    [InlineData(Subject, "{{#if contact.title == '<b'}}x{{/if}}", "body", 25)]
    [InlineData(Subject, "{{#if contact.title != '</p'}}x{{/if}}", "body", 25)]
    [InlineData(Subject, "<{{#if contact.title}}b{{/if}}", "body", 1)]
    [InlineData(Subject, "Hi {{#if contact.title}}<{{else}}x{{/if}}a", "body", 25)]
    [InlineData(Subject, "<{{#if contact.title}}3{{/if}}b", "body", 1)]
    [InlineData(Subject, "<{{#if contact.title}}b{{else}}3{{/if}}", "body", 1)]
    [InlineData("<b>{{contact.first_name}}", Body, "subject", 1)]
    public async Task A_tag_s_start_in_a_template_is_refused_where_it_stands_before_anything_is_called(string subject, string body, string part, int position)
    {
        using var account = Filled();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Template(subject, body)));

        Assert.Equal(("template_unsupported", FailureClass.Validation), (error.Code, error.Class));
        Assert.Equal(
            $"The {part} holds, at character {position}, a `<` Reply reads as markup, and Reply drops a tag it does not know with everything after it.",
            (string?)error.Details!["reason"]);
        Assert.Empty(account.Calls);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A <c>&lt;</c> that starts no tag goes in a template as in a final text, in its text, a fallback and a comparison,
    /// and before a branch whose text does not start one.
    /// </summary>
    [Fact]
    public async Task A_lt_that_starts_no_tag_goes_in_a_template_and_reaches_the_person_whole()
    {
        using var account = Filled();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var body = "a < b, <3 {{contact.custom.nickname | '<3 you'}} <{{#if contact.title}}3{{/if}}{{#if contact.title != '<3'}} yes{{/if}} <";
        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Template("a < b <3 {{contact.first_name}} <", body)));

        var went = Assert.Single(account.Sent)!;
        Assert.Equal(
            "a &lt; b, &lt;3 {{nickname | '&lt;3 you'}} &lt;{{#if Title}}3{{/if}}{{#if Title != '&lt;3'}} yes{{/if}} &lt;",
            (string?)went["text"]!["body"]);
        Assert.Equal(("a < b <3 Marta <", "a < b, <3 <3 you <3 yes <"), ((string?)went["rendered"]!["subject"], (string?)went["rendered"]!["body"]));
    }

    /// <summary>
    /// A template naming a custom field the person has no value for is refused by Reply before anything is sent, and
    /// reply-cli 0.17.2 names it by its own code with the name as Reply spells it: it is reported as the template
    /// unrendered, final, the name in the details and in the message.
    /// </summary>
    [Fact]
    public async Task A_template_reply_could_not_fill_in_is_unrendered_naming_what_it_could_not_fill()
    {
        using var account = Filled();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Template(Subject, "Hi {{contact.custom.nickname}}")));

        Assert.Equal(("template_unrendered", FailureClass.Validation), (error.Code, error.Class));
        Assert.Equal("contact.missingVariables", (string?)error.Details!["provider_code"]);
        Assert.Equal("""["nickname"]""", error.Details["missing_variables"]?.ToJsonString());
        Assert.EndsWith(" Reply could not fill nickname.", error.Message, StringComparison.Ordinal);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A template Reply could not fill in, as a reply-cli older than 0.17.2 answered it: under a code that says nothing of
    /// it and a sentence that does, it is still reported as the template unrendered, final, with Reply's own words in the
    /// message. The same refusal of a final text is not a template's, and keeps the word it has; and a refusal under the
    /// same code with another sentence is not this one.
    /// </summary>
    [Theory]
    [InlineData("template", "Hi {{contact.custom.nickname}}", "RenderingFail: One or more values for variables are missing or incorrect.", "template_unrendered", "validation")]
    [InlineData("template", "Hi {{contact.first_name}}", "SendFail: the mailbox did not take the message.", "provider_call_failed", "permanent")]
    [InlineData("text", Body, "RenderingFail: One or more values for variables are missing or incorrect.", "provider_call_failed", "permanent")]
    public async Task A_template_reply_could_not_fill_in_is_unrendered_in_reply_s_words(string compose, string body, string detail, string code, string klass)
    {
        using var account = Filled();
        account.Refuses(
            "contact send-email", "reply.refused", status: 400, replyCode: "contact.unknown", detail: detail,
            ids: new JsonObject { ["contact_id"] = 1001, ["email_account_id"] = 12 });

        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Template(Subject, body);
        input["args"]!["message"]!["compose"] = compose;
        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, input));

        Assert.Equal((code, klass), (error.Code, error.Class.ToString().ToLowerInvariant()));
        Assert.Equal("contact.unknown", (string?)error.Details!["provider_code"]);
        if (code == "template_unrendered")
        {
            Assert.EndsWith(" Reply said: RenderingFail: One or more values for variables are missing or incorrect.", error.Message, StringComparison.Ordinal);
        }

        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// Reply's refusal of a template it could not fill in, for a person the send found by their address: the failure keeps
    /// the contact, says the template was refused before anything was sent, and is final in Reply's own words. The same
    /// code with another sentence is not this one: it is the permanent refusal, carrying Reply's sentence, the contact
    /// kept as well. The send is called once either way.
    /// </summary>
    [Theory]
    [InlineData("RenderingFail: One or more values for variables are missing or incorrect.", "template_unrendered", FailureClass.Validation)]
    [InlineData("SendFail: the mailbox did not take the message.", "provider_call_failed", FailureClass.Permanent)]
    public async Task A_template_reply_refuses_for_a_person_it_found_keeps_the_contact_and_reads_reply_s_sentence(string detail, string code, FailureClass klass)
    {
        using var account = Filled();
        account.Refuses(
            "contact send-email", "reply.refused", status: 400, replyCode: "contact.unknown", detail: detail,
            ids: new JsonObject { ["contact_id"] = 1001, ["email_account_id"] = 12 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Input("email", body: "Hi {{contact.first_name}}");
        input["args"]!["message"]!["compose"] = "template";
        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, input));

        Assert.Equal((code, klass), (error.Code, error.Class));
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Equal(detail, (string?)error.Details!["provider_detail"]);
        if (code == "template_unrendered")
        {
            Assert.Contains("before anything was sent", error.Message, StringComparison.Ordinal);
            Assert.EndsWith(" Reply said: " + detail, error.Message, StringComparison.Ordinal);
        }

        Assert.Single(Lines(account), line => line == "contact send-email");
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// reply-cli names a template Reply could not fill in by a code of its own on each channel, and lists the names Reply
    /// could not fill where Reply does: in template mode it is the template unrendered, final, the names in the details and
    /// in the message. The same codes for a final text are a text Reply read as a placeholder, and keep that word, the
    /// names in the details alone.
    /// </summary>
    [Theory]
    [InlineData("email", "template", "contact.missing_variables", "contact.missingVariables", "nickname,tier", "template_unrendered")]
    [InlineData("email", "template", "contact.rendering_failed", "contact.renderingFailed", null, "template_unrendered")]
    [InlineData("linkedin_message", "template", "linkedin.missing_variables", "directLinkedIn.missingVariables", null, "template_unrendered")]
    [InlineData("linkedin_inmail", "template", "linkedin.missing_variables", "directLinkedIn.missingVariables", null, "template_unrendered")]
    [InlineData("email", "text", "contact.missing_variables", "contact.missingVariables", "nickname", "message_invalid")]
    [InlineData("email", "text", "contact.rendering_failed", "contact.renderingFailed", null, "message_invalid")]
    public async Task A_template_reply_cli_says_reply_could_not_fill_in_is_unrendered_with_the_names_reply_lists(
        string format, string compose, string cliCode, string replyCode, string? names, string code)
    {
        var channel = format == "email" ? "email" : "linkedin";
        using var account = channel == "email" ? Filled() : FilledOnLinkedIn();
        account.Refuses(
            FormatCommand(format), cliCode, status: 400, replyCode: replyCode,
            details: names is null ? null : new JsonObject { ["missing_variables"] = new JsonArray([.. names.Split(',').Select(name => JsonValue.Create(name))]) },
            ids: channel == "email"
                ? new JsonObject { ["contact_id"] = 1001, ["email_account_id"] = 12 }
                : new JsonObject { ["contact_id"] = 1001, ["linkedin_account_id"] = 34 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Input(format, pinned: "1001", body: compose == "template" ? "Hi {{contact.first_name}}" : Body);
        input["args"]!["message"]!["compose"] = compose;
        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, input));

        Assert.Equal((code, FailureClass.Validation), (error.Code, error.Class));
        Assert.Equal(replyCode, (string?)error.Details!["provider_code"]);
        Assert.Equal(
            names is null ? null : new JsonArray([.. names.Split(',').Select(name => JsonValue.Create(name))]).ToJsonString(),
            error.Details["missing_variables"]?.ToJsonString());
        if (code == "template_unrendered" && names is not null)
        {
            Assert.EndsWith(" Reply could not fill " + names.Replace(",", ", ", StringComparison.Ordinal) + ".", error.Message, StringComparison.Ordinal);
        }

        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The names Reply lists are bounded as they travel: the first twenty, each to 64 characters, and a list holding
    /// anything but texts is not carried at all.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_names_reply_could_not_fill_travel_bounded(bool mixed)
    {
        using var account = Filled();
        var listed = new JsonArray([.. Enumerable.Range(1, 25).Select(n => JsonValue.Create(Listed(n) + "-and-more"))]);
        if (mixed)
        {
            listed.Add(7);
        }

        account.Refuses(
            "contact send-email", "contact.missing_variables", status: 400, replyCode: "contact.missingVariables",
            details: new JsonObject { ["missing_variables"] = listed },
            ids: new JsonObject { ["contact_id"] = 1001, ["email_account_id"] = 12 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Template(Subject, "Hi {{contact.first_name}}")));

        Assert.Equal("template_unrendered", error.Code);
        if (mixed)
        {
            Assert.Null(error.Details!["missing_variables"]);
            return;
        }

        var carried = error.Details!["missing_variables"]!.AsArray();
        Assert.Equal(20, carried.Count);
        Assert.Equal(Enumerable.Range(1, 20).Select(Listed), carried.Select(name => (string?)name));

        // The first 64 characters of the nth name Reply lists, which runs on past them.
        static string Listed(int n) => new string('n', 62) + n.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A message composed as final text is the text as before: a double opening brace in it is still refused as invalid.</summary>
    [Fact]
    public async Task A_final_text_with_a_double_opening_brace_is_still_refused_as_invalid()
    {
        using var account = ByEmail();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Input("email", pinned: "1001", body: "Braces {{ stay as typed.");
        input["args"]!["message"]!["compose"] = "text";
        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, input));

        Assert.Equal(("message_invalid", FailureClass.Validation), (error.Code, error.Class));
        Assert.Empty(account.Calls);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The document ties a message's format to its channel, and the claim holds it there; the package holds it too, behind
    /// both: an email's format is the email channel's and every other format is LinkedIn's, so a format of the other
    /// channel is refused before anything is called, even where the input reaches the package without the document read.
    /// </summary>
    [Theory]
    [InlineData("email", "linkedin_message")]
    [InlineData("linkedin", "email")]
    public async Task A_format_of_the_other_channel_is_refused_before_anything_is_called_though_the_document_was_not_read(string channel, string format)
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta", linkedIn: Profile)
            .WithSender(12, "email", "Ann Owner", Mailbox).WithSender(34, "linkedin", "Ann Owner", SenderProfile).WithConnection(1001, 34);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Input(format, pinned: "1001");
        input["args"]!["channel"] = channel;
        var error = ReplyOperations.Failed(Operation, await InvokeUnreadAsync(api, input));

        Assert.Equal(("message_invalid", FailureClass.Validation), (error.Code, error.Class));
        Assert.Empty(account.Calls);
        Assert.Empty(account.Sent);
    }

    // -------------------------------------------------------------------------------------------------------
    // The sender
    // -------------------------------------------------------------------------------------------------------

    /// <summary>Reply numbers its mailboxes in 64 bits, so a mailbox past 32 bits is named and sent from.</summary>
    [Fact]
    public async Task A_mailbox_numbered_past_thirty_two_bits_is_named_and_sent_from()
    {
        const long Large = 9_007_199_254_740_991;
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta").WithSender(Large, "email", "Ann Owner", Mailbox);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("email", pinned: "1001", sender: "9007199254740991")))["items"]![0]!;

        Assert.Equal("9007199254740991", (string?)item["sender_external_id"]);
        Assert.Equal("9007199254740991", account.Calls[^1].Option("--email-account-id"));
        Assert.Equal(Large, (long)Assert.Single(account.Sent)!["accountId"]!);
    }

    /// <summary>
    /// A mailbox's address is compared as the runtime keys an address, on both sides, since both are Reply's spelling: in
    /// another case it is the account approved; one the register keys apart is another, though JavaScript would lowercase
    /// both to one text; and one the runtime would not key is nobody's, so it is never named. An address named exactly as
    /// Reply writes it, capitals and all, is the account approved: it is what <c>sender.list</c> returned.
    /// </summary>
    [Theory]
    // Critical: the approved sender is the sender.
    [InlineData("Ann@Example.COM", "ann@example.com", true, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.S2)])]
    [InlineData("Ann@Example.COM", "Ann@Example.COM", true)]
    [InlineData("xİ@example.com", "xi\u0307@example.com", false)]
    [InlineData("ann@example", "ann@example", false)]
    public async Task A_mailbox_is_the_one_approved_only_where_the_runtime_keys_both_addresses_alike(string held, string given, bool alike)
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta").WithSender(12, "email", "Ann Owner", held);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input("email", pinned: "1001", senderAddress: given));

        if (alike)
        {
            ReplyOperations.Succeeded(Operation, result);
            Assert.Single(account.Sent);
            return;
        }

        var error = ReplyOperations.Failed(Operation, result);
        Assert.Equal(("sender_mismatch", FailureClass.Validation), (error.Code, error.Class));
        Assert.Equal(["account list"], Lines(account));
        Assert.Empty(account.Sent);
    }

    [Theory]
    [InlineData("email", "13")]
    [InlineData("email", "s_12")]
    [InlineData("email", "9007199254740992")]
    [InlineData("linkedin_message", "2147483648")]
    public async Task A_sender_reply_does_not_hold_is_not_found_and_nothing_is_sent(string format, string sender)
    {
        using var account = format == "email" ? ByEmail() : OnLinkedIn();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format, pinned: "1001", sender: sender)));

        Assert.Equal(("sender_not_found", FailureClass.Permanent), (error.Code, error.Class));
        Assert.True(Lines(account).All(line => line == "account list"), $"the calls were {string.Join(", ", Lines(account))}.");
        Assert.Empty(account.Sent);
    }

    /// <summary>A mailbox Reply lists as disconnected is the sender's block for its channel, decided before the email.</summary>
    [Fact]
    public async Task A_disconnected_mailbox_is_the_sender_s_block_before_the_email()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta").WithSender(12, "email", "Ann Owner", Mailbox, status: "disconnected");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001")));

        Assert.Equal(("provider_blocked", FailureClass.Blocked), (error.Code, error.Class));
        Assert.Equal("""{"scope":"sender","sender_external_id":"12","reason":"channel"}""", ReplyOperations.Blocked(error));
        Assert.Equal(["account list"], Lines(account));
    }

    // -------------------------------------------------------------------------------------------------------
    // The person
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_person_with_no_pin_is_found_by_the_address_and_pinned_and_nobody_is_made()
    {
        using var account = ByEmail();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input("email"));

        ReplyOperations.Succeeded(Operation, result);
        Assert.Equal(["account list", "contact find", "contact get 1001", "contact send-email"], Lines(account));
        Assert.Equal($$"""{"email":"{{Address}}"}""", account.Calls[1].Body);
        Assert.Single(account.Contacts);
    }

    /// <summary>
    /// Reply holds nobody under the address: nobody is read, and the send's own command imports the person from their
    /// fields — the consumed address, never another — with the text beside them in one object on stdin, then sends. The
    /// contact it made is the one pinned.
    /// </summary>
    [Fact]
    public async Task A_person_reply_holds_nobody_for_is_imported_by_the_send_and_pinned()
    {
        using var account = new ReplyAccount().WithSender(12, "email", "Ann Owner", Mailbox);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input("email"));

        var item = ReplyOperations.Succeeded(Operation, result)["items"]![0]!;
        Assert.Equal("1001", (string?)item["external_ids"]!["contact"]);
        Assert.Equal("""{"contact":"1001","send":"email-1001"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
        Assert.Equal(["account list", "contact find", "contact send-email"], Lines(account));
        var send = account.Calls[^1];
        Assert.Equal(["--json", "-q", "contact", "send-email", "--contact", "-", "--email-account-id", "12", "--message", "-", "--idempotency-key", "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRK"], send.Args);
        Assert.Equal(
            $$$"""{"contact":{"email":"{{{Address}}}","first_name":"Marta","last_name":"Alvarez","company":"Puerto Analytics","title":"Head of Growth"},"message":{"subject":"{{{Subject}}}","body":"{{{Body}}}"}}""",
            JsonNode.Parse(send.Body!)!.ToJsonString());
        var made = Assert.Single(account.Contacts)!;
        Assert.Equal((1001, Address, "Marta"), ((int)made["id"]!, (string?)made["email"], (string?)made["firstName"]));
        Assert.Equal(1001, (int)Assert.Single(account.Sent)!["contactId"]!);
    }

    /// <summary>
    /// On LinkedIn the person is named by the consumed profile alone, as the runtime keys it, and imported keyed by it; an
    /// InMail needs no connection, so it reaches a person nobody held a moment before.
    /// </summary>
    [Fact]
    public async Task A_person_reply_holds_nobody_for_on_linkedin_is_imported_by_the_profile_and_sent_to()
    {
        using var account = new ReplyAccount().WithSender(34, "linkedin", "Ann Owner", SenderProfile);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input("linkedin_inmail"));

        ReplyOperations.Succeeded(Operation, result);
        Assert.Equal(["account list", "contact find", "contact linkedin-inmail"], Lines(account));
        var person = JsonNode.Parse(account.Calls[^1].Body!)!["contact"]!.AsObject();
        Assert.Equal(["company", "first_name", "last_name", "linkedin_url", "title"], person.Select(member => member.Key).Order(StringComparer.Ordinal));
        Assert.Equal(Profile, (string?)person["linkedin_url"]);
        Assert.Equal(Profile, (string?)Assert.Single(account.Contacts)!["linkedInUrl"]);
        Assert.Single(account.Sent);
    }

    /// <summary>
    /// A person with no first name is imported under one derived from what Jason holds, exactly as an add derives it, and
    /// a route that forbids deriving refuses the message instead: nothing is imported and nothing is sent.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_person_with_no_first_name_is_imported_under_a_derived_one_unless_the_route_forbids_it(bool forbidden)
    {
        using var account = new ReplyAccount().WithSender(12, "email", "Ann Owner", Mailbox);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var binding = forbidden ? new JsonObject { ["derive_first_name"] = false } : null;
        var result = await ReplyOperations.InvokeAsync(api, Operation, Input("email", firstName: null), Ct, binding: binding);

        if (forbidden)
        {
            var error = ReplyOperations.Failed(Operation, result);
            Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
            Assert.Equal("the route forbids deriving a first name", (string?)error.Details!["reason"]);
            Assert.Equal(["account list", "contact find"], Lines(account));
            Assert.Empty(account.Contacts);
            Assert.Empty(account.Sent);
            return;
        }

        ReplyOperations.Succeeded(Operation, result);
        Assert.Equal("Alvarez", (string?)JsonNode.Parse(account.Calls[^1].Body!)!["contact"]!["first_name"]);
        Assert.Equal("Alvarez", (string?)Assert.Single(account.Contacts)!["firstName"]);
        Assert.Single(account.Sent);
    }

    /// <summary>
    /// A profile stored before the key is no key: imported, Reply would hold it under a spelling the register was not
    /// asked about, so the message is refused before the import, as it is before a profile is written onto a contact.
    /// </summary>
    [Fact]
    public async Task A_value_stored_before_the_key_is_never_imported_and_nothing_is_sent()
    {
        using var account = new ReplyAccount().WithSender(34, "linkedin", "Ann Owner", SenderProfile);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("linkedin_inmail", value: ReplyProfileKeys.LeftAsItStood)));

        Assert.Equal(("contact_channel_conflict", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(["account list", "contact find"], Lines(account));
        Assert.Empty(account.Contacts);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The command's own refusals of the import, in the words the add operations report them in: an account that holds
    /// no further contact, an address or a profile Reply will not take, and a second contact under the address. Nothing is
    /// sent, and nobody is pinned.
    /// </summary>
    [Theory]
    [InlineData("email", "account.contact_limit_reached", 400, "contact.limitExceeded", null, "limit_reached", "permanent")]
    [InlineData("email", "contact.invalid_email", 400, null, null, "invalid_channel_value", "validation")]
    [InlineData("email", "reply.invalid_request", 400, null, "email", "invalid_channel_value", "validation")]
    [InlineData("linkedin_inmail", "contact.invalid_linkedin_url", 400, null, null, "invalid_channel_value", "validation")]
    [InlineData("email", "contact.not_unique", 200, null, null, "contact_not_unique", "permanent")]
    public async Task A_refusal_of_the_import_is_reported_as_an_add_reports_it(
        string format, string code, int status, string? replyCode, string? field, string expected, string expectedClass)
    {
        using var account = new ReplyAccount().WithSender(12, "email", "Ann Owner", Mailbox).WithSender(34, "linkedin", "Ann Owner", SenderProfile);
        account.Refuses(FormatCommand(format), code, status: status, replyCode: replyCode, details: field is null ? null : new JsonObject { ["fields"] = new JsonArray(field) });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format)));

        Assert.Equal((expected, expectedClass), (error.Code, error.Class.ToString().ToLowerInvariant()));
        Assert.Null(error.ExternalIds);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The lookup found nobody, and the command's own import matched a contact Reply already held, so the command sent
    /// nothing and named it. That contact is read before anything is sent, as every contact Reply holds is, and the message
    /// then goes to it by its identifier, which is pinned: one more command, never a send to a contact nobody read.
    /// </summary>
    [Fact]
    public async Task A_person_the_import_matched_though_the_lookup_missed_is_read_first_and_then_sent_to_by_its_identifier()
    {
        using var account = ByEmail().Unlisted(1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input("email"));

        ReplyOperations.Succeeded(Operation, result);
        Assert.Equal("""{"contact":"1001","send":"email-1001"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
        Assert.Equal(["account list", "contact find", "contact send-email", "contact get 1001", "contact send-email"], Lines(account));
        Assert.Equal("-", account.Calls[2].Option("--contact"));
        Assert.Equal(["--json", "-q", "contact", "send-email", "--contact-id", "1001", "--email-account-id", "12", "--message", "-", "--idempotency-key", "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRK"], account.Calls[^1].Args);
        Assert.Equal($$"""{"subject":"{{Subject}}","body":"{{Body}}"}""", account.Calls[^1].Body);
        Assert.Single(account.Contacts);
        Assert.Equal(1001, (int)Assert.Single(account.Sent)!["contactId"]!);
    }

    /// <summary>
    /// A contact the import matched is held to everything a contact Reply holds is held to, because it is read first: one
    /// Reply holds as opted out is a stop, on either channel, and nothing is sent.
    /// </summary>
    [Theory]
    [InlineData("email")]
    [InlineData("linkedin_inmail")]
    public async Task A_person_the_import_matched_though_the_lookup_missed_held_as_opted_out_is_suppressed_and_nothing_is_sent(string format)
    {
        using var account = (format == "email" ? ByEmail(optedOut: true) : OnLinkedIn(optedOut: true)).Unlisted(1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format)));

        Assert.Equal(("suppressed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Equal(["account list", "contact find", FormatCommand(format), "contact get 1001"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The command said Reply already held the person and did not say whom, so there is no contact to read first: nothing
    /// is sent, and the attempt ends in the package's own word for it rather than as a refusal it has no word for.
    /// </summary>
    [Fact]
    public async Task A_command_that_says_the_person_is_held_without_naming_them_sends_nothing()
    {
        using var account = new ReplyAccount().WithSender(12, "email", "Ann Owner", Mailbox);
        account.Refuses("contact send-email", "contact.already_held", status: 200, ids: new JsonObject { ["email_account_id"] = 12 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email")));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Contains("did not say which", error.Message, StringComparison.Ordinal);
        Assert.Null(error.ExternalIds);
        Assert.Equal(["account list", "contact find", "contact send-email"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A contact that this attempt was not pinned to — one the command's own import made, or one the lookup found — that
    /// Reply then answers it holds no longer is no stale pin: the attempt fails without calling it one, and nobody is
    /// pinned.
    /// </summary>
    [Theory]
    [InlineData("contact send-email")]
    [InlineData("contact get")]
    public async Task A_contact_reply_named_and_then_denies_is_no_stale_pin_and_is_pinned_nowhere(string denies)
    {
        using var account = denies == "contact get" ? ByEmail() : new ReplyAccount().WithSender(12, "email", "Ann Owner", Mailbox);
        account.Refuses(
            denies, "contact.not_found", status: 404, replyCode: "contact.notFound",
            ids: denies == "contact get" ? null : new JsonObject { ["contact_id"] = 1009, ["email_account_id"] = 12 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email")));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal("contact.not_found", (string?)error.Details!["cli_code"]);
        Assert.Null(error.ExternalIds);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A person the lookup found, with no pin, is read before anything is sent exactly as a pinned one is: one Reply holds
    /// as opted out is a stop, and the contact found is pinned on the failure.
    /// </summary>
    [Fact]
    public async Task A_person_the_lookup_found_held_as_opted_out_is_suppressed_and_nothing_is_sent()
    {
        using var account = ByEmail(optedOut: true);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email")));

        Assert.Equal(("suppressed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Equal(["account list", "contact find", "contact get 1001"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A runtime whose documents project the person's identifier and channel alone hands over no first name, last name,
    /// company, title or time zone at all — absent, which is not the null a runtime that projects them writes for a field
    /// Jason holds nothing in. A person imported from that would be named from their address, so the message is refused
    /// before anything is called, naming the fields.
    /// </summary>
    [Theory]
    [InlineData("first_name,last_name,company,title,time_zone")]
    [InlineData("time_zone")]
    public async Task An_input_without_the_person_s_projected_fields_is_refused_before_anything_is_called(string absent)
    {
        using var account = new ReplyAccount().WithSender(12, "email", "Ann Owner", Mailbox);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Input("email");
        foreach (var field in absent.Split(','))
        {
            input["contacts"]![0]!.AsObject().Remove(field);
        }

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, input));

        Assert.Equal(ReplyOperations.ProjectionRefused, (error.Code, error.Class));
        Assert.Equal(ReplyOperations.ProjectionReason(absent), (string?)error.Details!["reason"]);
        Assert.Empty(account.Calls);
    }

    /// <summary>
    /// An import whose answer was lost before the send may have been followed by the send: the command's lost answer is
    /// ambiguous and never repeated, and the contact the command had named by then is pinned.
    /// </summary>
    [Fact]
    public async Task A_lost_answer_to_a_send_that_imported_is_ambiguous_and_pins_the_contact_it_named()
    {
        using var account = new ReplyAccount().WithSender(12, "email", "Ann Owner", Mailbox);
        account.LosesTheOutcome("contact send-email");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email")));

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (error.Code, error.Class));
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Single(account.Sent);
    }

    /// <summary>
    /// Reply's contact is reached at the address the register was asked about or not at all: one Reply holds in another
    /// case is the same, one at another address — or at none — is refused, and an address is never written onto a contact.
    /// </summary>
    [Theory]
    [InlineData("Marta@Example.COM", true)]
    [InlineData("marta.work@example.com", false)]
    [InlineData(null, false)]
    public async Task An_email_goes_only_where_reply_s_contact_is_reached_at_the_consumed_address(string? held, bool alike)
    {
        using var account = ByEmail(email: held);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input("email", pinned: "1001"));

        Assert.DoesNotContain(account.Calls, call => call.Command == "contact update");
        if (alike)
        {
            ReplyOperations.Succeeded(Operation, result);
            Assert.Single(account.Sent);
            return;
        }

        var error = ReplyOperations.Failed(Operation, result);
        Assert.Equal(("contact_channel_conflict", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Equal(["account list", "contact get 1001"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The address Reply holds is keyed by the runtime's own rule, never by JavaScript's lowercasing: a capital I with a dot
    /// at Reply is not the i with a combining dot above the register was asked about, so nothing is sent.
    /// </summary>
    [Fact]
    [Critical(Family.F1, "the key decides who the person is")]
    public async Task An_address_the_register_keys_apart_is_another_person_s_and_nothing_is_sent()
    {
        const string held = "xİ@example.com";
        var consumed = ChannelRules.KeyOf("email", "xi\u0307@example.com");
        Assert.True(consumed is not null && consumed != ChannelRules.KeyOf("email", held), $"the runtime keys '{held}' as '{ChannelRules.KeyOf("email", held)}' and the work item's as '{consumed}'.");
        using var account = ByEmail(email: held);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001", value: consumed)));

        Assert.Equal(("contact_channel_conflict", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// An address Reply holds that is longer than the runtime stores is keyed as it stands, never cut first: cut to the
    /// bound this one would be the consumed address, and the address reached would be another. It is nobody's.
    /// </summary>
    [Fact]
    public async Task An_address_reply_holds_past_the_bound_is_never_cut_to_meet_the_consumed_one()
    {
        var consumed = new string('a', 488) + "@example.com";
        var held = consumed + "m";
        Assert.True(
            consumed.Length == 500 && ChannelRules.KeyOf("email", consumed) == consumed && ChannelRules.KeyOf("email", held) is null,
            $"an address of {consumed.Length} characters is not its own key, or one of {held.Length} keys.");
        using var account = ByEmail(email: held);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001", value: consumed)));

        Assert.True((error.Code, error.Class) == ("contact_channel_conflict", FailureClass.Permanent), $"Reply held {held.Length} characters, and the email ended {error.Code}.");
        Assert.Empty(account.Sent);
    }

    /// <summary>Reply does not refuse a send outside a sequence to a person it holds as opted out, so its contact's mark is the stop, on either channel.</summary>
    [Theory]
    [InlineData("email")]
    [InlineData("linkedin_message")]
    public async Task A_person_reply_holds_as_opted_out_is_suppressed_and_nothing_is_sent(string format)
    {
        using var account = format == "email" ? ByEmail(optedOut: true) : OnLinkedIn(optedOut: true);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format, pinned: "1001")));

        Assert.Equal(("suppressed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(["account list", "contact get 1001"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>Reply consults its blacklist on a direct email, and refuses one to a person it holds: that is a stop too.</summary>
    [Fact]
    [Critical(Family.S1, "the provider's own register")]
    public async Task A_person_on_reply_s_blacklist_is_suppressed_and_nothing_is_sent()
    {
        using var account = ByEmail().WithBlacklisted(1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001")));

        Assert.Equal(("suppressed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal("contact.contactInBlackList", (string?)error.Details!["provider_code"]);
        Assert.Empty(account.Sent);
    }

    /// <summary>A LinkedIn message to a contact carrying no profile gives it the consumed one first, as the invitation does, and then goes.</summary>
    [Fact]
    public async Task A_contact_with_no_profile_is_given_the_consumed_one_first_and_then_messaged()
    {
        using var account = OnLinkedIn(linkedIn: null);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("linkedin_message", pinned: "1001")));

        Assert.Equal(["account list", "contact get 1001", "contact update 1001", "contact linkedin-message"], Lines(account));
        Assert.Equal($$"""{"linkedin_url":"{{Profile}}"}""", account.Calls[2].Body);
        Assert.Single(account.Sent);
    }

    [Fact]
    public async Task A_contact_with_another_profile_is_not_changed_and_nothing_is_sent()
    {
        using var account = OnLinkedIn(linkedIn: "https://www.linkedin.com/in/marta-somebody-else");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("linkedin_inmail", pinned: "1001")));

        Assert.Equal(("contact_channel_conflict", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(["account list", "contact get 1001"], Lines(account));
        Assert.Empty(account.Sent);
    }

    // -------------------------------------------------------------------------------------------------------
    // What Reply refuses
    // -------------------------------------------------------------------------------------------------------

    /// <summary>A LinkedIn message and a voice message go only to a connection; an InMail is how one reaches anybody else.</summary>
    [Theory]
    [InlineData("linkedin_message", true)]
    [InlineData("linkedin_voice", true)]
    [InlineData("linkedin_inmail", false)]
    public async Task A_message_to_a_person_not_connected_is_refused_where_the_format_needs_a_connection(string format, bool needsOne)
    {
        using var account = OnLinkedIn(connected: false);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(format, pinned: "1001"));

        if (!needsOne)
        {
            ReplyOperations.Succeeded(Operation, result);
            Assert.Single(account.Sent);
            return;
        }

        var error = ReplyOperations.Failed(Operation, result);
        Assert.Equal(("not_connected", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Contains("social_invitation.send", error.Message, StringComparison.Ordinal);
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// What Reply refuses that the plugin did not catch first comes back in the operation's own words, Reply's code in the
    /// trace, and nothing went. A field Reply refuses by itself, and a template refusal on an email that slipped past the
    /// plugin's own check, have no word of their own here: they are reported as they come.
    /// </summary>
    [Theory]
    [InlineData("linkedin_inmail", "linkedin.no_inmail_credits", 400, "directLinkedIn.noInMailCredits", null, "insufficient_credits", "permanent")]
    [InlineData("linkedin_inmail", "linkedin.sales_navigator_required", 400, "directLinkedIn.salesNavRequired", null, "sender_unavailable", "permanent")]
    [InlineData("linkedin_inmail", "linkedin.sales_navigator_disconnected", 400, "directLinkedIn.salesNavDisconnected", null, "sender_unavailable", "permanent")]
    [InlineData("linkedin_voice", "linkedin.voice_profile_missing", 400, "directLinkedIn.voiceProfileMissing", null, "sender_unavailable", "permanent")]
    [InlineData("linkedin_voice", "linkedin.voice_too_long", 400, "directLinkedIn.voiceTooLong", null, "message_too_long", "validation")]
    [InlineData("linkedin_message", "linkedin.message_too_long", 400, "directLinkedIn.messageTooLong", null, "message_too_long", "validation")]
    [InlineData("linkedin_message", "linkedin.missing_variables", 400, "directLinkedIn.missingVariables", null, "message_invalid", "validation")]
    [InlineData("linkedin_message", "linkedin.feature_disabled", 403, "directLinkedIn.featureDisabled", null, "feature_unavailable", "permanent")]
    [InlineData("linkedin_message", "contact.no_linkedin_profile", 404, "directLinkedIn.noLinkedInProfile", null, "contact_channel_conflict", "permanent")]
    [InlineData("email", "email_account.not_found", 400, "contact.emailAccountNotFound", null, "sender_not_found", "permanent")]
    [InlineData("email", "contact.not_found", 404, "contact.notFound", null, "contact_not_found", "permanent")]
    [InlineData("email", "reply.invalid_request", 400, null, "body", "provider_call_failed", "permanent")]
    [InlineData("email", "reply.invalid_request", 400, null, "sendAfter", "provider_call_failed", "permanent")]
    [InlineData("email", "reply.refused", 400, "contact.unknown", null, "provider_call_failed", "permanent")]
    [InlineData("email", "reply.refused", 400, "contact.notSupported", null, "provider_call_failed", "permanent")]
    [InlineData("email", "reply.refused", 404, "emailAccount.notFound", null, "provider_call_failed", "permanent")]
    public async Task A_refusal_reply_makes_of_the_message_is_reported_in_the_operation_s_words(
        string format, string cliCode, int status, string? replyCode, string? field, string code, string klass)
    {
        var channel = format == "email" ? "email" : "linkedin";
        using var account = channel == "email" ? ByEmail() : OnLinkedIn();
        account.Refuses(
            FormatCommand(format), cliCode, status: status, replyCode: replyCode,
            details: field is null ? null : new JsonObject { ["fields"] = new JsonArray(field) },
            ids: channel == "email"
                ? new JsonObject { ["contact_id"] = 1001, ["email_account_id"] = 12 }
                : new JsonObject { ["contact_id"] = 1001, ["linkedin_account_id"] = 34 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format, pinned: "1001")));

        Assert.Equal((code, klass), (error.Code, error.Class.ToString().ToLowerInvariant()));
        Assert.Equal(replyCode, (string?)error.Details!["provider_code"]);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A LinkedIn account the send finds turned off, or whose session has to be connected again, is the sender's block
    /// for its channel, whatever the format: the runtime holds the work it sends until a trial gets past it.
    /// </summary>
    [Theory]
    [InlineData("linkedin_message", "linkedin.account_inactive", "directLinkedIn.linkedInAccountInactive")]
    [InlineData("linkedin_inmail", "linkedin.invalid_cookie", "directLinkedIn.invalidCookie")]
    [InlineData("linkedin_voice", "linkedin.account_inactive", "directLinkedIn.linkedInAccountInactive")]
    public async Task A_sender_the_send_finds_turned_off_or_disconnected_is_the_sender_s_block(string format, string cliCode, string replyCode)
    {
        using var account = OnLinkedIn();
        account.Refuses(
            FormatCommand(format), cliCode, status: 409, replyCode: replyCode,
            ids: new JsonObject { ["contact_id"] = 1001, ["linkedin_account_id"] = 34 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format, pinned: "1001")));

        Assert.Equal(("provider_blocked", FailureClass.Blocked), (error.Code, error.Class));
        Assert.Equal("""{"scope":"sender","sender_external_id":"34","reason":"channel"}""", ReplyOperations.Blocked(error));
        Assert.Equal(replyCode, (string?)error.Details!["provider_code"]);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A spent allowance is the sender's block for a limit. reply-cli says no wait on it here, so the block names no
    /// moment it clears, and the runtime tries it on its own cadence.
    /// </summary>
    [Theory]
    [InlineData("daily")]
    [InlineData("weekly")]
    public async Task A_sender_whose_allowance_is_spent_says_which_and_nothing_is_sent(string limit)
    {
        using var account = OnLinkedIn().WithSender(34, "linkedin", "Ann Owner", SenderProfile, limit: limit);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("linkedin_message", pinned: "1001")));

        Assert.Equal(("provider_blocked", FailureClass.Blocked), (error.Code, error.Class));
        Assert.Equal("""{"scope":"sender","sender_external_id":"34","reason":"limit"}""", ReplyOperations.Blocked(error));
        Assert.Equal(limit, (string?)error.Details!["limit"]);
        Assert.Empty(account.Sent);
    }

    /// <summary>Where reply-cli says how long to wait, the block says when it clears: that long after the answer.</summary>
    [Fact]
    public async Task A_spent_allowance_with_a_wait_says_when_the_block_clears()
    {
        const int Wait = 86400;
        using var account = OnLinkedIn();
        account.Refuses(
            FormatCommand("linkedin_message"), "linkedin.limit_reached", status: 429, replyCode: "directLinkedIn.dailyLimitReached",
            ids: new JsonObject { ["contact_id"] = 1001, ["linkedin_account_id"] = 34 },
            details: new JsonObject { ["limit"] = "daily" }, retryAfter: Wait);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var before = DateTimeOffset.UtcNow;
        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("linkedin_message", pinned: "1001")));
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(("provider_blocked", FailureClass.Blocked), (error.Code, error.Class));
        ReplyOperations.Blocked(error);
        Assert.Equal(("sender", "34", "limit"), ((string?)error.Block!["scope"], (string?)error.Block["sender_external_id"], (string?)error.Block["reason"]));
        ReplyOperations.ClearsAfter(error, before, after, Wait);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A wait past a year is no moment the block can say: Reply's allowances renew by the day or the week, and one far
    /// enough out would be written with a year the block contract refuses. The block is still the sender's, and the
    /// runtime tries it on its own cadence.
    /// </summary>
    [Fact]
    public async Task A_wait_past_a_year_leaves_the_block_without_a_moment_it_clears()
    {
        using var account = OnLinkedIn();
        account.Refuses(
            FormatCommand("linkedin_message"), "linkedin.limit_reached", status: 429, replyCode: "directLinkedIn.dailyLimitReached",
            ids: new JsonObject { ["contact_id"] = 1001, ["linkedin_account_id"] = 34 },
            details: new JsonObject { ["limit"] = "daily" }, retryAfter: int.MaxValue);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("linkedin_message", pinned: "1001")));

        Assert.Equal(("provider_blocked", FailureClass.Blocked), (error.Code, error.Class));
        Assert.Equal("""{"scope":"sender","sender_external_id":"34","reason":"limit"}""", ReplyOperations.Blocked(error));
    }

    /// <summary>
    /// A sender's refusal answered by a command that ran from no sending account — here the read of the pinned contact,
    /// before the send — has no account a block could name: it ends in the operation's own permanent word for that
    /// refusal, and holds nothing.
    /// </summary>
    [Theory]
    [InlineData("linkedin.account_inactive", "directLinkedIn.linkedInAccountInactive", "sender_unavailable")]
    [InlineData("linkedin.limit_reached", "directLinkedIn.dailyLimitReached", "sender_limit_reached")]
    public async Task A_sender_s_refusal_from_a_command_run_from_no_sender_is_no_block(string cliCode, string replyCode, string code)
    {
        using var account = OnLinkedIn();
        account.Refuses("contact get", cliCode, status: 409, replyCode: replyCode);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("linkedin_message", pinned: "1001")));

        Assert.Equal((code, FailureClass.Permanent), (error.Code, error.Class));
        Assert.Null(error.Block);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// What an InMail or a voice message needs and the account lacks is about the format, not the sender: the same
    /// account still sends a plain message, so it stays the operation's own refusal and holds nothing. Running out of
    /// InMail credits stays its own word too.
    /// </summary>
    [Theory]
    [InlineData("linkedin_inmail", "linkedin.sales_navigator_required", "directLinkedIn.salesNavRequired", "sender_unavailable")]
    [InlineData("linkedin_inmail", "linkedin.sales_navigator_disconnected", "directLinkedIn.salesNavDisconnected", "sender_unavailable")]
    [InlineData("linkedin_voice", "linkedin.voice_profile_missing", "directLinkedIn.voiceProfileMissing", "sender_unavailable")]
    [InlineData("linkedin_inmail", "linkedin.no_inmail_credits", "directLinkedIn.noInMailCredits", "insufficient_credits")]
    public async Task What_a_format_needs_is_no_block(string format, string cliCode, string replyCode, string code)
    {
        using var account = OnLinkedIn();
        account.Refuses(
            FormatCommand(format), cliCode, status: 400, replyCode: replyCode,
            ids: new JsonObject { ["contact_id"] = 1001, ["linkedin_account_id"] = 34 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format, pinned: "1001")));

        Assert.Equal((code, FailureClass.Permanent), (error.Code, error.Class));
        Assert.Null(error.Block);
    }

    // -------------------------------------------------------------------------------------------------------
    // A lost answer
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A message whose answer was lost may have gone, so it is ambiguous, with the person pinned; and it is repeated,
    /// because the repeat is made under the same key: Reply answers a send it already took with what it answered then,
    /// and sends nothing twice. On every format.
    /// </summary>
    [Theory]
    // Critical: a lost send is repeated only under its own key, and nothing goes twice.
    [InlineData("email", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.W5)])]
    [InlineData("linkedin_message")]
    [InlineData("linkedin_inmail")]
    [InlineData("linkedin_voice")]
    public async Task A_lost_message_is_repeated_under_its_key_and_goes_once(string format)
    {
        using var account = format == "email" ? ByEmail() : OnLinkedIn();
        account.LosesTheOutcome(FormatCommand(format));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format)));

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (error.Code, error.Class));
        Assert.True(OutcomeContract.Retriable(error.Class, OperationCatalog.Find(Operation)!));
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Single(account.Sent);

        var mark = account.Mark();
        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(format, pinned: "1001"), attempt: 2, unsettledSince: Unsettled))["items"]![0]!;

        var reference = (format == "email" ? "email" : "linkedin") + "-1001";
        Assert.Equal(("sent", reference), ((string?)item["status"], (string?)item["reference"]));
        Assert.Equal(["account list", "contact get 1001", FormatCommand(format)], account.CallsSince(mark).Select(call => call.Line));
        Assert.Single(account.Sent);
    }

    /// <summary>An answer that does not say the message went, or names it by an identifier of another shape, may come from one that went.</summary>
    [Theory]
    [InlineData("email", """{"status":"queued","contact_id":1001,"message_id":null,"sent_at":null,"reply_status":"new"}""")]
    [InlineData("email", """{"status":"sent","contact_id":1001,"message_id":17,"sent_at":null,"reply_status":"sent"}""")]
    [InlineData("linkedin_message", """{"status":"sent","contact_id":1001,"activity_id":"70001","sent_at":null}""")]
    [InlineData("linkedin_voice", """{"status":"sent","contact_id":"1001","activity_id":70001,"sent_at":null}""")]
    public async Task An_answer_that_does_not_say_the_message_went_may_hide_one_that_did(string format, string answer)
    {
        using var account = format == "email" ? ByEmail() : OnLinkedIn();
        account.Returns(FormatCommand(format), JsonNode.Parse(answer)!.AsObject());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format, pinned: "1001")));

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (error.Code, error.Class));
    }

    /// <summary>Reply's answer to an email names no message for a send it took without one: the message is answered null, and it went.</summary>
    [Fact]
    public async Task An_email_reply_took_without_a_message_identifier_is_sent_with_none()
    {
        using var account = ByEmail();
        account.Returns("contact send-email", new JsonObject { ["status"] = "sent", ["contact_id"] = 1001, ["message_id"] = null, ["sent_at"] = null, ["reply_status"] = "new" });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("email", pinned: "1001")))["items"]![0]!;

        Assert.Equal("""{"external_id":null,"sent_at":null}""", item["message"]!.ToJsonString());
    }

    /// <summary>
    /// A Message-ID is answered whole or not at all: one past the 998 characters the document holds an identifier to names
    /// nothing once cut, so the message went and its identifier is none, never the start of another.
    /// </summary>
    [Theory]
    [InlineData(998, true)]
    [InlineData(999, false)]
    public async Task An_email_message_identifier_past_the_documents_bound_is_none_and_never_cut(int length, bool kept)
    {
        var identifier = "<" + new string('a', length - 2) + ">";
        using var account = ByEmail();
        account.Returns("contact send-email", new JsonObject { ["status"] = "sent", ["contact_id"] = 1001, ["message_id"] = identifier, ["sent_at"] = null, ["reply_status"] = "sent" });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("email", pinned: "1001")))["items"]![0]!;

        var answered = (string?)item["message"]!["external_id"];
        Assert.True(answered == (kept ? identifier : null), $"an identifier of {length} characters was answered as {(answered is null ? "none" : $"one of {answered.Length}")}.");
    }

    // -------------------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------------------

    private static string FormatCommand(string format) => format switch
    {
        "email" => "contact send-email",
        "linkedin_message" => "contact linkedin-message",
        "linkedin_inmail" => "contact linkedin-inmail",
        _ => "contact linkedin-voice",
    };

    private static Task<PluginInvocationResult> InvokeAsync(
        RuntimeApiFixture api, JsonObject input, int attempt = 1, DateTimeOffset? unsettledSince = null, string? unsettledReference = null) =>
        ReplyOperations.InvokeAsync(api, Operation, input, Ct, attempt: attempt, unsettledSince: unsettledSince, unsettledReference: unsettledReference);

    /// <summary>The input as it stands, the document not read: the package's own checks, which hold behind the document's.</summary>
    private static Task<PluginInvocationResult> InvokeUnreadAsync(RuntimeApiFixture api, JsonObject input) =>
        PluginInvokerTests.InvokeAsync(
            api,
            new PluginInvocationRequest(
                ReplyPlugins.PluginId,
                Operation,
                input,
                Binding: null,
                CorrelationId: "att_01K0REPLYMESSAGESEND",
                AttemptId: "att_01K0REPLYMESSAGESEND",
                AttemptNumber: 1,
                WorkItemId: "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRK",
                CampaignId: "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRE"),
            Ct);

    /// <summary>A person reached by email with every field a template names filled but a nickname, and the caller's mailbox.</summary>
    private static ReplyAccount Filled() =>
        new ReplyAccount()
            .WithContact(1001, Address, "Marta", lastName: "Alvarez", company: "Puerto Analytics", title: "Head of Growth")
            .WithCustomValue(1001, "tier", "gold")
            .WithSender(12, "email", "Ann Owner", Mailbox);

    /// <summary>A person reached on LinkedIn whose fields Reply holds, connected to the caller's LinkedIn account.</summary>
    private static ReplyAccount FilledOnLinkedIn() =>
        new ReplyAccount()
            .WithContact(1001, null, "Marta", lastName: "Alvarez", company: "Puerto Analytics", title: "Head of Growth", linkedIn: Profile)
            .WithCustomValue(1001, "tier", "gold")
            .WithSender(34, "linkedin", "Ann Owner", SenderProfile)
            .WithConnection(1001, 34);

    /// <summary>An email composed as a template, to the pinned person.</summary>
    private static JsonObject Template(string subject, string body)
    {
        var input = Input("email", pinned: "1001", subject: subject, body: body);
        input["args"]!["message"]!["compose"] = "template";
        return input;
    }

    private static IReadOnlyList<string> Lines(ReplyAccount account) => [.. account.Calls.Select(call => call.Line)];

    /// <summary>The package's own diagnostic lines, as the child wrote them on stderr.</summary>
    private static IReadOnlyList<JsonObject> PluginLines(PluginInvocationResult result)
    {
        var log = Path.Combine(result.Launch!.WorkDir, "stderr.log");
        return [.. (File.Exists(log) ? File.ReadAllText(log) : string.Empty).Split('\n')
            .Where(line => line.TrimStart().StartsWith('{'))
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .Where(line => (string?)line["source"] == "plugin")];
    }

    /// <summary>
    /// One message of the format named, its subject where the format takes one; the channel and the account follow the
    /// format: the caller's mailbox on email, its LinkedIn account otherwise.
    /// </summary>
    private static JsonObject Input(
        string format,
        string? pinned = null,
        string? subject = Subject,
        string body = Body,
        string? sender = null,
        string? senderAddress = null,
        string? value = null,
        string? firstName = "Marta")
    {
        var channel = format == "email" ? "email" : "linkedin";
        var message = new JsonObject { ["format"] = format, ["body"] = body };
        if (format is "email" or "linkedin_inmail" && subject is not null)
        {
            message["subject"] = subject;
        }

        return new JsonObject
        {
            ["args"] = new JsonObject
            {
                ["channel"] = channel,
                ["sender"] = new JsonObject
                {
                    ["external_id"] = sender ?? (channel == "email" ? "12" : "34"),
                    ["address"] = senderAddress ?? (channel == "email" ? Mailbox : SenderProfile),
                },
                ["message"] = message,
            },
            ["contacts"] = new JsonArray(new JsonObject
            {
                ["id"] = ContactId,
                ["first_name"] = firstName,
                ["last_name"] = "Alvarez",
                ["company"] = "Puerto Analytics",
                ["title"] = "Head of Growth",
                ["time_zone"] = null,
                ["channels"] = new JsonArray(new JsonObject { ["channel"] = channel, ["value"] = value ?? (channel == "email" ? Address : Profile), ["primary"] = true }),
                ["external_ids"] = pinned is null ? new JsonObject() : new JsonObject { ["contact"] = pinned },
            }),
            ["campaign"] = new JsonObject
            {
                ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRE",
                ["name"] = "Q3 LatAm founders",
                ["status"] = "active",
                ["external_ids"] = new JsonObject(),
            },
            ["idempotency_key"] = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRK",
        };
    }
}
