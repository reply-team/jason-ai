using System.Globalization;
using Jason.Contracts.Api;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Microsoft.AspNetCore.Http;

namespace Jason.Runtime.Domain;

/// <summary>Every error code the business surface can answer, spelled exactly once.</summary>
public static class DomainErrors
{
    public static NotFoundException CampaignNotFound(string id) => new("campaign_not_found", $"No campaign with id '{id}'.");

    public static NotFoundException ContactNotFound(string id) => new("contact_not_found", $"No contact with id '{id}'.");

    public static ConflictException InvalidTransition(CampaignStatus from, CampaignStatus to) =>
        new("invalid_transition", $"A campaign cannot go from {SnakeCaseEnumConverter<CampaignStatus>.Format(from)} to {SnakeCaseEnumConverter<CampaignStatus>.Format(to)}.");

    public static ConflictException CampaignArchived(string id) => new("campaign_archived", $"Campaign '{id}' is archived; only journal entries can still be appended.");

    public static ConflictException ContactArchived(string id) => new("contact_archived", $"Contact '{id}' is archived.");

    /// <summary>
    /// A create under a key that already names an entity made by a different request. It names the entity and never
    /// quotes the key, which is the caller's text; and it is final, because sending it again cannot make the two
    /// requests the same.
    /// </summary>
    public static ConflictException IdempotencyKeyReused(string id) =>
        new("idempotency_key_reused", $"This idempotency key already names '{id}', which a different request created; give a different request a key of its own.");

    public static InvalidRequestException ContextTooLarge(int limitBytes) =>
        new("context_too_large", string.Create(CultureInfo.InvariantCulture, $"The campaign context must serialize to at most {limitBytes} bytes."));

    public static InvalidRequestException ReservedKind(string kind) =>
        new("reserved_kind", $"Journal kind '{kind}' is written by the runtime itself and cannot be appended by callers.");

    public static InvalidRequestException BatchTooLarge(int limit) =>
        new("batch_too_large", string.Create(CultureInfo.InvariantCulture, $"At most {limit} items per call."));

    public static NotFoundException WorkItemNotFound(string id) => new("work_item_not_found", $"No work item with id '{id}'.");

    public static NotFoundException UiNotInstalled(string why) => new("ui_not_installed", why);

    public static NotFoundException UiTokenNotFound(string id) => new("ui_token_not_found", $"No UI token '{id}' exists; `jason ui tokens` lists the live ones.");

    public static ConflictException WorkItemTerminal(string id, WorkItemStatus status) =>
        new("workitem_terminal", $"Work item '{id}' is {SnakeCaseEnumConverter<WorkItemStatus>.Format(status)}; finished items are not changed.");

    public static ConflictException ContactNotMember(string contactId, string campaignId) =>
        new("contact_not_member", $"Contact '{contactId}' is not a member of campaign '{campaignId}'.");

    public static ConflictException StaleAttempt(string attemptId) =>
        new("stale_attempt", $"Attempt '{attemptId}' is not the current running attempt of this work item; stop working on it.");

    public static InvalidRequestException ResultTooLarge(int limitBytes) =>
        new("result_too_large", string.Create(CultureInfo.InvariantCulture, $"A result must serialize to at most {limitBytes} bytes."));

    /// <summary>
    /// The answer is not the shape the work item asked for. Refused rather than stored: an executor still holds
    /// its attempt when it hears this, so it can answer again properly, and a paragraph explaining how thoroughly
    /// the job was done is never recorded as the job being done.
    /// </summary>
    public static DomainException ResultInvalid(IReadOnlyList<ErrorDetail> details) =>
        new(
            StatusCodes.Status400BadRequest,
            "result_invalid",
            "The result does not satisfy the result_format this work item declared.",
            retryable: false,
            details);

    /// <summary>
    /// A value this request carried could not be measured: a pattern in the schema it is held to did not finish
    /// matching, twice. That is a fact about the pattern and the machine and never about the value, so the request is
    /// neither accepted nor refused — nothing was written, and sending it again is the whole remedy. A 503 rather than
    /// a 400, because a 400 tells its sender to change something that may be perfectly right.
    /// </summary>
    public static DomainException ValidationTimedOut(IReadOnlyList<ErrorDetail> details) =>
        new(
            StatusCodes.Status503ServiceUnavailable,
            AttemptErrors.ValidationTimedOut,
            "A pattern this request is measured against did not finish matching in time, twice, so nothing was decided and nothing was changed; send it again.",
            retryable: true,
            details);

    public static ConflictException RoleExists(string name) => new("role_exists", $"A role named '{name}' already exists.");

    public static NotFoundException RoleNotFound(string name) => new("role_not_found", $"No role named '{name}'.");

    /// <summary>
    /// A note past the cap. Its own code rather than the campaign context's: a role reading the refusal has to
    /// know which of the two documents it was writing is too big, and how much of it to take out.
    /// </summary>
    public static InvalidRequestException RoleNoteTooLarge(long sizeBytes, int limitBytes) =>
        new("role_note_too_large", string.Create(CultureInfo.InvariantCulture, $"A role note is {sizeBytes} bytes of UTF-8 in its canonical form, and must be at most {limitBytes}."));

    public static ConflictException ProfileExists(string name) => new("profile_exists", $"An execution profile named '{name}' already exists.");

    public static NotFoundException ProfileNotFound(string name) => new("profile_not_found", $"No execution profile named '{name}'.");

    /// <summary>
    /// A revision the profile has never had. Its own code rather than the profile's: the profile is there, and a
    /// caller who asked for a revision number needs to be told which of the two they got wrong.
    /// </summary>
    public static NotFoundException ProfileRevisionNotFound(string name, int revision) =>
        new("profile_revision_not_found", string.Create(CultureInfo.InvariantCulture, $"Execution profile '{name}' has no revision {revision}."));

    /// <summary>
    /// A wait names a moment later than the ceiling allows. Refused rather than moved to the ceiling: a deadline silently
    /// changed is one the role still believes.
    /// </summary>
    public static InvalidRequestException WaitTooLong(int maxWaitSeconds) =>
        new("wait_too_long", string.Create(CultureInfo.InvariantCulture, $"A wait may name a moment at most {maxWaitSeconds} seconds ahead (Dispatcher:AiRole:MaxWaitSeconds); name an earlier one."));

    /// <summary>A wait names more work items than one wait may.</summary>
    public static InvalidRequestException WaitTargetInvalid(int named) =>
        new("wait_target_invalid", string.Create(CultureInfo.InvariantCulture, $"A wait names at most {WorkItemWaitRequest.MaxWaitItems} work items; this one named {named}."));

    /// <summary>
    /// A named item a wait may not name, the first of them in the order named, with why: it does not exist, it is in
    /// another campaign, or no attempt of the waiting item raised it. Waiting only on work its own runs raised is what
    /// keeps a wait from naming its own ancestor.
    /// </summary>
    public static InvalidRequestException WaitTargetNotRaised(string target, string id, string why) =>
        new("wait_target_invalid", $"'{target}' cannot be waited for by '{id}': {why}; a wait names only work its own runs raised in its campaign.");

    /// <summary>A decision named as what to wait for. Its answer already wakes the campaign's manager, by design.</summary>
    public static InvalidRequestException WaitTargetDecision(string target) =>
        new("wait_target_invalid", $"'{target}' is a decision, and a wait names work items; an answer wakes the campaign's manager instead.");

    /// <summary>
    /// Work that may not end by waiting. A manager's check-in waits by its cadence; a provider operation's attempt ends
    /// through its plugin's outcome.
    /// </summary>
    public static InvalidRequestException WaitNotAllowed(string id, string why) =>
        new("wait_not_allowed", $"Work item '{id}' cannot wait: {why}");

    /// <summary>A resume of an item that holds no wait to end.</summary>
    public static ConflictException WorkItemNotWaiting(string id, WorkItemStatus status) =>
        new("workitem_not_waiting", $"Work item '{id}' is {SnakeCaseEnumConverter<WorkItemStatus>.Format(status)}; only a waiting item can be resumed.");

    /// <summary>
    /// The item has waited as often as it may. The attempt that asked still holds the item, so it is told to finish or
    /// fail instead; another wait cannot be made to fit.
    /// </summary>
    public static ConflictException WaitLimitReached(string id, int maxWaits) =>
        new("wait_limit_reached", string.Create(CultureInfo.InvariantCulture, $"Work item '{id}' has already waited {maxWaits} times (Dispatcher:AiRole:MaxWaits); finish or fail this attempt instead."));

    /// <summary>Two writers reached the same row; the loser is told to read again rather than given a merged result.</summary>
    public static DomainException ConcurrentUpdate() =>
        new(StatusCodes.Status409Conflict, "concurrent_update", "Another change reached the same row first; read it again and retry.", retryable: true);

    /// <summary>
    /// The settings file is invalid and this runtime has never read one that was not, so there is nothing to
    /// work from. It is the file that is broken rather than the request: retryable, because repairing the file
    /// is all it takes. The section is named because three of them are read this way, and an operator told only
    /// that "the settings" are invalid would have the whole file to search.
    /// </summary>
    public static DomainException SettingsUnreadable(string section) =>
        new(
            StatusCodes.Status503ServiceUnavailable,
            "settings_unreadable",
            $"The runtime's '{section}' settings are invalid and none have validated since it started; repair the settings file and try again.",
            retryable: true);

    /// <summary>
    /// A reload that found something wrong with a package. The whole candidate set stays out and the previous
    /// snapshot stays active: a registry that is only partly right hides the problem instead of showing it.
    /// </summary>
    public static DomainException PluginReloadRejected(IReadOnlyList<ErrorDetail> details) =>
        new(
            StatusCodes.Status409Conflict,
            "plugin_reload_rejected",
            string.Create(CultureInfo.InvariantCulture, $"The plugin reload was rejected: {details.Count} problem(s) in the candidate set; the previous snapshot stays active."),
            retryable: false,
            details);

    /// <summary>
    /// A campaign route write refused because it would leave searches still followed with no route to the account each
    /// ran under. The details name each search; nothing was written and the routes in force stay active.
    /// </summary>
    public static DomainException RouteOrphansSearch(IReadOnlyList<ErrorDetail> details) =>
        new(
            StatusCodes.Status409Conflict,
            Routing.RouteProblemCodes.OrphansSearch,
            string.Create(CultureInfo.InvariantCulture, $"The route change was refused: {details.Count} search(es) still followed would have no route to the account each ran under; the routes in force stay active."),
            retryable: false,
            details);

    /// <summary>
    /// A campaign route write refused because it would leave sends the provider still holds for a moment to come with no
    /// route to the account each ran under. Each detail covers one campaign's sends at one account; nothing was written
    /// and the routes in force stay active.
    /// </summary>
    public static DomainException RouteOrphansSend(IReadOnlyList<ErrorDetail> details, int sends) =>
        new(
            StatusCodes.Status409Conflict,
            Routing.RouteProblemCodes.OrphansSend,
            string.Create(CultureInfo.InvariantCulture, $"The route change was refused: {sends} send(s) still held for a later moment would have no route to the account each ran under; each detail names one campaign's sends at one account, and the routes in force stay active."),
            retryable: false,
            details);

    public static NotFoundException ApprovalNotFound(string id) => new("approval_not_found", $"No approval with id '{id}'.");

    /// <summary>
    /// The settings writer met a file the runtime's own parser does not read. The place is said as a person finds it in
    /// an editor — the line and the byte — and with no exception's type, which the log says; nothing of the file is quoted.
    /// </summary>
    public static DomainException SettingsFileUnreadable(Configuration.SettingsFileFailure failure) =>
        new(
            StatusCodes.Status409Conflict,
            Plugins.Manifest.ProblemCodes.SettingsFileUnreadable,
            failure.Place() is { } place
                ? $"The settings file could not be read ({place}), so nothing was written; fix it, then run the command again."
                : "The settings file could not be read, so nothing was written; fix it, then run the command again.",
            retryable: false);

    /// <summary>
    /// The settings writer cannot change the setting without touching bytes it does not own (<see cref="Configuration.SettingsDocument"/>).
    /// The setting is named by its path, each key written as <see cref="Jason.Contracts.Configuration.SettingKey.ShownPath"/> writes it.
    /// </summary>
    public static DomainException SettingsFileNotEditable(string setting, Configuration.SettingsRefusal refusal) =>
        new(
            StatusCodes.Status409Conflict,
            "settings_file_not_editable",
            $"The settings file says {setting} in a way this writer cannot change without touching what it does not own ({Why(refusal)}), so nothing was written; change it by hand, then run jason plugin reload.",
            retryable: false);

    private static string Why(Configuration.SettingsRefusal refusal) => refusal switch
    {
        Configuration.SettingsRefusal.Twice => "twice",
        Configuration.SettingsRefusal.NotAnObject => "through a value that is not an object",
        Configuration.SettingsRefusal.NotUtf8 => "not in UTF-8",
        Configuration.SettingsRefusal.ColonKey => "with a key that holds a colon, which is the same setting written a second way",
        Configuration.SettingsRefusal.CheckRefused => "in a form this writer's own check refused",
        Configuration.SettingsRefusal.Link => "through a link to another file, which a write would replace with a file of its own",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "a refusal with no words."),
    };

    /// <summary>Another writer kept changing the settings file between the writer's read and its commit; nothing was written.</summary>
    public static DomainException SettingsFileChanged() =>
        new(
            StatusCodes.Status409Conflict,
            "settings_file_changed",
            "The settings file kept changing while this wrote it, so nothing was written; run the command again.",
            retryable: true);

    /// <summary>Another program held the settings file open, so it could not be read or replaced; nothing was written.</summary>
    public static DomainException SettingsFileBusy() =>
        new(
            StatusCodes.Status409Conflict,
            "settings_file_busy",
            "Another program holds the settings file open, so nothing was written; close it, then run the command again.",
            retryable: true);

    /// <summary>
    /// The settings file or its folder cannot be written, and waiting does not mend that: it is read-only, this user may
    /// not write it, or the disk is full. Nothing was written.
    /// </summary>
    public static DomainException SettingsFileNotWritable() =>
        new(
            StatusCodes.Status409Conflict,
            "settings_file_not_writable",
            "The settings file cannot be written: it or its folder is read-only, this user may not write it, or the disk is full, so nothing was written; mend that, then run the command again.",
            retryable: false);

    /// <summary>
    /// A <c>JASON_</c> environment variable sets the path the writer would write, and overrides the file: a write would
    /// answer success for a setting that is not the one in force. Named by the setting and the variable as the environment
    /// spells it (<see cref="Configuration.SettingsFile.Shadowing"/>), never by what either holds.
    /// </summary>
    public static DomainException SettingShadowed(string setting, string variable) =>
        new(
            StatusCodes.Status409Conflict,
            "setting_shadowed",
            $"{setting} is set by the environment variable {variable}, which overrides the settings file, so nothing was written; remove the variable or change it, then restart the runtime.",
            retryable: false);

    /// <summary>No provider block has this id. Nothing deletes a block, so this is a wrong id.</summary>
    public static NotFoundException BlockNotFound(string id) => new("block_not_found", $"No provider block with id '{id}'.");

    /// <summary>
    /// The block is cleared already: by an attempt that got past it, perhaps a moment earlier, or by a person. Its work is
    /// released by the next scan either way.
    /// </summary>
    public static ConflictException BlockNotOpen(string id) =>
        new("block_not_open", $"Provider block '{id}' is cleared already; only an open block can be cleared.");

    /// <summary>
    /// A clear is a person's word that the cause is gone — the invoice paid, the account reconnected — and only an attempt
    /// that gets past the block can say so for the runtime: a role or a run that clears one is deciding it for them.
    /// </summary>
    public static InvalidRequestException BlockNotHuman(ActorType type) =>
        new(
            "block_not_human",
            $"An actor of type '{SnakeCaseEnumConverter<ActorType>.Format(type)}' cannot clear a provider block; a clear is a person's word that the cause is gone.");

    /// <summary>The same code a decision without a named person is refused with: a clear records whose word it is.</summary>
    public static InvalidRequestException BlockActorRequired() =>
        new("actor_required", "actor.id is required on a block clear: what is recorded has to name the person whose word it is.");

    /// <summary>
    /// The decision has already been made, or the work it was about has moved on without it. Either way there is
    /// nothing here to decide, and the row says which of the two happened.
    /// </summary>
    public static ConflictException ApprovalNotPending(string id, ApprovalStatus status) =>
        new(
            "approval_not_pending",
            status == ApprovalStatus.Pending
                ? $"Approval '{id}' is still pending, and the work it is about is no longer waiting for it; read it again."
                : $"Approval '{id}' is {SnakeCaseEnumConverter<ApprovalStatus>.Format(status)}; only a pending decision about work that is still waiting can be made.");

    /// <summary>
    /// A role or an attempt tried to decide. Approval is a person's to give: the runtime's own actor is already
    /// refused to every caller, and a role that asks on somebody's behalf is asking to be that person.
    /// </summary>
    public static InvalidRequestException ApprovalNotHuman(ActorType type) =>
        new(
            "approval_not_human",
            $"An actor of type '{SnakeCaseEnumConverter<ActorType>.Format(type)}' cannot approve or reject; a decision is a person's to make.");

    /// <summary>The question is gone, or was never there. Nothing deletes a decision, so this is a wrong id.</summary>
    public static NotFoundException DecisionNotFound(string id) =>
        new("decision_not_found", $"Decision '{id}' does not exist.");

    /// <summary>
    /// It has been answered already, or the campaign it was about is archived. Either way there is nothing
    /// here to decide, and the row says which of the two happened.
    /// </summary>
    public static ConflictException DecisionNotPending(string id, DecisionStatus status) =>
        new(
            "decision_not_pending",
            $"Decision '{id}' is {SnakeCaseEnumConverter<DecisionStatus>.Format(status)}; only a pending question can be answered.");

    /// <summary>
    /// A role or an attempt tried to answer. The point of escalation is that the answer comes from outside the
    /// run that asked; a role answering its own question is the run deciding after all.
    /// </summary>
    public static InvalidRequestException DecisionNotHuman(ActorType type) =>
        new(
            "decision_not_human",
            $"An actor of type '{SnakeCaseEnumConverter<ActorType>.Format(type)}' cannot answer a decision; a question raised for a person is a person's to answer.");

    /// <summary>
    /// A person's own word — an approval, a rejection, a standing approval set or taken back, an answer — sent by a CLI that
    /// says it runs inside an attempt, whatever actor the body claims. The attempt is quoted as the request named it and
    /// never looked up: presence is the fact. The message carries the remedy, for the agent that reads it.
    /// </summary>
    public static ForbiddenException DecidedInsideARun(string attempt) =>
        new(
            "decided_inside_a_run",
            $"This request comes from inside attempt `{attempt}`. An approval or an answer is a person's own word, given outside the run that asked: leave it to the person (`jason approval list`, `jason decision list`) or raise it as a decision.");

    /// <summary>
    /// A plugin's grant given or taken back from inside a run, refused by the run's header with the code the run gate
    /// gives, before the body is read. A grant is taken from any actor outside a run; what is refused is a run changing
    /// Jason itself.
    /// </summary>
    public static ForbiddenException ChangesJasonInsideARun(string attempt) =>
        new(
            "decided_inside_a_run",
            $"This request comes from inside attempt `{attempt}`. Nothing inside a run changes Jason itself, and what a plugin may use is part of it: leave it to the person, or raise it as a decision.");

    /// <summary>
    /// A route for every campaign asked for from inside a run: the global scope of <c>route.set</c> and <c>route.unset</c>,
    /// refused by the run's header with the code a person's own word is refused with there.
    /// </summary>
    public static ForbiddenException GlobalRouteInsideARun(string attempt) =>
        new(
            "decided_inside_a_run",
            $"This request comes from inside attempt `{attempt}`. A route for every campaign is a person's own word, given outside any run: leave it to the person, or raise it as a decision.");

    /// <summary>
    /// The chosen answer is not one of the ones offered. What is recorded is the label, so a label nobody
    /// offered would be an answer that reads like a quotation and is not one.
    /// </summary>
    public static InvalidRequestException DecisionOptionUnknown(string option) =>
        new("decision_option_unknown", $"'{option}' is not one of the options this decision named.");

    /// <summary>
    /// A causal reference has to point at something in this campaign. References are identifiers rather than
    /// copies precisely so they can be read later — one that resolves to nothing never could be.
    /// </summary>
    public static InvalidRequestException DecisionReferenceUnresolved(string kind, string id) =>
        new("decision_reference_unresolved", $"The {kind} reference '{id}' is not one of this campaign's own rows.");

    /// <summary>
    /// A verdict that names a decision carries that person's answer, so the decision has to be one of the verdict's own
    /// campaign: another campaign's question is no answer about this reply, and is refused as if it were not there.
    /// </summary>
    public static InvalidRequestException VerdictDecisionNotFound(string id) =>
        new("decision_not_found", $"Decision '{id}' is not a question of this campaign; a verdict carries the answer to one of its own.");

    /// <summary>A question nobody has answered, or one taken back, has no answer for a verdict to carry.</summary>
    public static InvalidRequestException VerdictDecisionNotAnswered(string id, DecisionStatus status) =>
        new(
            "decision_not_answered",
            $"Decision '{id}' is {SnakeCaseEnumConverter<DecisionStatus>.Format(status)}; only an answered question carries a person's answer into a verdict.");

    /// <summary>
    /// What a verdict carries as a person's word has to have been given by a person who gave their name; an answer the
    /// row says anybody else gave is not that, whatever wrote the row.
    /// </summary>
    public static InvalidRequestException VerdictDecisionNotHuman(string id) =>
        new("decision_not_human", $"Decision '{id}' was not answered by a named person; only a person's answer stands as their word in a verdict.");

    /// <summary>
    /// The verdict records the category the person chose, or it is not their answer. Neither the option nor the category is
    /// repeated: both are words somebody wrote.
    /// </summary>
    public static InvalidRequestException VerdictDecisionOptionDiffers(string id) =>
        new("decision_option_differs", $"The option chosen in decision '{id}' is not the category this verdict records; record the category the person chose.");

    /// <summary>
    /// A person's answer is about the reply its question was asked about: the run that raised the decision recorded a
    /// verdict on that reply first. A decision raised about another reply, or by a run that recorded no verdict before it
    /// asked, is no answer about this one. No identifier of the reply is repeated: the caller sent them.
    /// </summary>
    public static InvalidRequestException VerdictDecisionNotAboutReply(string id) =>
        new("decision_not_about_reply", $"Decision '{id}' was not raised by a run that recorded a verdict on this reply; a verdict carries the answer to the question asked about its own reply.");

    /// <summary>
    /// A standing approval is a decision made in advance, so it is refused to whoever may not decide: the code a decision's
    /// refusal carries, in the words of what was being done. No role and no run grants one.
    /// </summary>
    public static InvalidRequestException StandingApprovalNotHuman(ActorType type) =>
        new(
            "approval_not_human",
            $"An actor of type '{SnakeCaseEnumConverter<ActorType>.Format(type)}' cannot set or unset a standing approval; it is a person's decision, made in advance.");

    public static InvalidRequestException StandingApprovalActorRequired() =>
        new("actor_required", "actor.id is required on a standing approval: what is recorded has to name the person whose decision it is.");

    /// <summary>
    /// An absent actor is an anonymous human, which is right for creating work and wrong for deciding it: an
    /// accountable decision names the person who made it.
    /// </summary>
    public static InvalidRequestException ActorRequired() =>
        new("actor_required", "actor.id is required on a decision: what is recorded has to name the person who made it.");

    /// <summary>
    /// The same rule for a report, in its own words: a report with no author is not provenance, and its reporter may be a
    /// person or a role.
    /// </summary>
    public static InvalidRequestException ReportActorRequired() =>
        new("actor_required", "actor.id is required on a report: what is recorded has to name who reports it.");

    /// <summary>
    /// The runtime performs effects; it does not report them. Every caller is already refused the system actor,
    /// but a reporter is told why in the words of the thing they were doing.
    /// </summary>
    public static InvalidRequestException ReporterReserved() =>
        new("reporter_reserved", "actor type 'system' cannot report an effect: the runtime performs effects rather than reporting them, and a report has to name who did.");

    /// <summary>
    /// The ids in a report disagree with each other. Not a fact about the world worth keeping — a typo, and one
    /// the caller can see at a glance once it is named.
    /// </summary>
    public static InvalidRequestException CorrelationInconsistent(string message) => new("correlation_inconsistent", message);

    public static NotFoundException ReportNotFound(string id) => new("report_not_found", $"No report with id '{id}'.");

    public static NotFoundException EventSourceNotFound(string id) =>
        new("event_source_not_found", $"No account whose events are read has id '{id}'; `jason events sources` lists them.");

    /// <summary>
    /// The account is not read now: no route binds it at a plugin that reads events here. Nothing it is asked for could
    /// happen until one does, so it is refused rather than kept waiting unseen.
    /// </summary>
    public static ConflictException EventSourceUnbound(string id) =>
        new("event_source_unbound", $"No route binds account '{id}' at a plugin that reads events here now, so it is not read; route it again first.");

    /// <summary>As many spans of one account wait already as one account may have waiting: one has to end before another is asked for.</summary>
    public static ConflictException EventBackfillLimit(string id, int limit) =>
        new("event_backfill_limit", string.Create(CultureInfo.InvariantCulture, $"Account '{id}' already has {limit} spans waiting to be read again; one has to end before another is asked for."));

    /// <summary>The kind has no cursor: no plugin of the active load subscribes to it, so there is nothing to rewind.</summary>
    public static NotFoundException SubscriptionKindUnsubscribed(string kind) =>
        new("subscription_kind_unsubscribed", $"No plugin of the active load subscribes to kind '{kind}'; `jason subscription list` lists the kinds that are.");

    /// <summary>A rewind names a line the journal does not hold, or one of another kind than the one rewound.</summary>
    public static NotFoundException SubscriptionLineNotFound(string kind, string line) =>
        new("subscription_line_not_found", $"The journal holds no line '{line}' of kind '{kind}'.");

    /// <summary>
    /// A rewind whose target is after the kind's cursor would pass lines over unhandled, which is a skip, and no person is
    /// given one: the cursor moves back or stays.
    /// </summary>
    public static ConflictException SubscriptionRewindForward(string kind) =>
        new("subscription_rewind_forward", $"The target is after the cursor of kind '{kind}'; a rewind moves a cursor back, never forward.");

    public static InvalidRequestException SubscriptionRewindNotHuman(ActorType type) =>
        new(
            "subscription_rewind_not_human",
            $"An actor of type '{SnakeCaseEnumConverter<ActorType>.Format(type)}' cannot rewind a subscription; handing lines to plugins again is a person's word.");

    public static InvalidRequestException SubscriptionRewindActorRequired() =>
        new("actor_required", "actor.id is required on a rewind: what is recorded has to name the person whose word it is.");

    public static NotFoundException ProjectNotFound(string id) => new("project_not_found", $"No project with id '{id}'.");

    public static NotFoundException ProjectSlugNotFound(string slug) => new("project_not_found", $"No project with slug '{slug}'.");

    /// <summary>The installation's default project is gone, which only a hand on the database does; not to be retried.</summary>
    public static NotFoundException NoDefaultProject() =>
        new("project_not_found", "This installation has no default project; name the project with project_id.");

    /// <summary>
    /// The slug is taken: a project holds it already. The project is named, in the message and as <c>project_id</c> in the
    /// details, because a create sent again after its answer was lost lands here and finds the project it made.
    /// </summary>
    public static DomainException ProjectExists(string slug, string id) =>
        new(
            StatusCodes.Status409Conflict,
            "project_exists",
            $"Project '{id}' already has the slug '{slug}'; a slug names one project.",
            details: [new ErrorDetail("project_id", "exists", id)]);

    /// <summary>
    /// The folder at the harness root holds a marker that names something other than a root — a project's folder, a
    /// campaign's, or a marker this runtime cannot read. Nothing is written into it: whose folder it is was said by
    /// whoever marked it, and the runtime never takes a folder over.
    /// </summary>
    public static DomainException HarnessRootTaken(string root) =>
        new(
            StatusCodes.Status409Conflict,
            "harness_root_taken",
            $"The folder '{root}' is marked as something other than the harness root, so nothing was written there or changed; set Harness:Root to another folder, or move that one away.");

    /// <summary>
    /// The disk refused to make a folder the request needed — the account may not write there, or a file stands where a
    /// folder belongs. The request is refused whole: its project's or campaign's row and folder are as they were, and only
    /// a project's first need, met in a step of its own before, stands. Not retryable as it stands,
    /// since what is in the way is for the person to see and repair.
    /// </summary>
    public static DomainException HarnessFolderUnavailable(string path) =>
        new(
            StatusCodes.Status409Conflict,
            "harness_folder_unavailable",
            $"The folder '{path}' could not be made, so the request was refused and its project or campaign is as it was; check that this account may write there and that no file stands where a folder belongs, then try again.");

    /// <summary>
    /// A campaign's folder could not be moved into its new project's folder by one rename. A folder is never copied and
    /// deleted instead: the update is refused whole, and the campaign stays where it was, on the disk and in the database;
    /// the project it was to move to keeps its first need and its <c>campaigns/</c>, its own structure.
    /// </summary>
    public static DomainException HarnessFolderNotMoved(string from, string to) =>
        new(
            StatusCodes.Status409Conflict,
            "harness_folder_unavailable",
            $"The folder '{from}' could not be moved into '{to}' by one rename, so the campaign and its folder stay where they were; check that no program holds a file in it open or is working in it, such as an agent session started there, that this account may write in both, and that both are on one volume, then try again.");

    /// <summary>
    /// A folder is already at the path a project's or a campaign's folder takes, and its marker names something else — a
    /// level of another kind, another project, an id no row of this runtime holds, a marker that cannot be read. It is a
    /// problem for the person to see, not one to walk around: nothing is written, and the person renames or moves the
    /// folder. The runtime never deletes it. A create whose commit failed after its folders were made leaves such a folder,
    /// and it is named here at the next attempt.
    /// </summary>
    public static DomainException HarnessFolderTaken(string path) =>
        new(
            StatusCodes.Status409Conflict,
            "harness_folder_taken",
            $"The folder '{path}' is already there and is marked for something else, so nothing was written into it; rename or move that folder away, then try again.");

    /// <summary>
    /// A folder already stands at the name a campaign's folder would take in its new project's <c>campaigns/</c>, marked or
    /// not, and is not another campaign's this runtime holds. A rename never lands on a folder that is there, whose ever it
    /// is: the move is refused, and the person renames or moves that folder.
    /// </summary>
    public static DomainException HarnessMoveTargetTaken(string path) =>
        new(
            StatusCodes.Status409Conflict,
            "harness_folder_taken",
            $"The folder '{path}' is already there, where the campaign's folder would move, and a move never lands on a folder that is there, so nothing was moved; rename or move that folder away, then try again.");

    /// <summary>
    /// The folder recorded for a project is not where it was recorded, or no longer carries the marker that names it —
    /// moved, renamed or replaced by hand — so a campaign is neither made in it nor moved to it. Never made again by the
    /// policy, which would leave the person's words in one folder and the work in another: the folder is put back, or the
    /// recorded path changes when the person says where it went.
    /// </summary>
    public static DomainException HarnessFolderMissing(string owner, string path) =>
        new(
            StatusCodes.Status409Conflict,
            "harness_folder_missing",
            $"The folder recorded for '{owner}', '{path}', is not there or no longer carries its marker, so the campaign was not made or moved; move it back to '{path}', or record where it is now with jason project link.");

    /// <summary>
    /// The folder recorded for a campaign being moved is not where it was recorded, or no longer carries the campaign's
    /// marker. The campaign stays where it was. The repair that works at once is to put the folder back; relinking the
    /// campaign's project finds the folder wherever it is below the project's, by its marker.
    /// </summary>
    public static DomainException HarnessCampaignFolderMissing(string campaign, string path) =>
        new(
            StatusCodes.Status409Conflict,
            "harness_folder_missing",
            $"The folder recorded for '{campaign}', '{path}', is not there or no longer carries its marker, so the campaign stays where it was; move it back to '{path}', or relink its project with jason project link, which finds a campaign's folder by its marker.");

    /// <summary>
    /// A link names another folder for a project whose recorded folder is still there with its marker — a copy, most
    /// often. A link records where a folder went and never moves or copies one, so two folders would then claim one
    /// project: nothing is written, and the person removes or renames one of the two.
    /// </summary>
    public static DomainException HarnessFolderPresent(string project, string recorded, string path) =>
        new(
            StatusCodes.Status409Conflict,
            "harness_folder_present",
            $"Project '{project}' still has its folder at '{recorded}', with its marker, so '{path}' was not linked and nothing was written; a link records where a folder went and never moves or copies one: remove or rename one of the two folders, then link again.");

    /// <summary>
    /// A link of a project alone, whose recorded folder is not there or no longer carries the project's marker. The policy
    /// never makes it again beside the moved one; the person says where it went.
    /// </summary>
    public static DomainException HarnessLinkFolderMissing(string project, string recorded) =>
        new(
            StatusCodes.Status409Conflict,
            "harness_folder_missing",
            $"The folder recorded for '{project}', '{recorded}', is not there or no longer carries its marker, so nothing was linked; move it back to '{recorded}', or name where it is now with jason project link --path.");

    /// <summary>
    /// A link names, for one project, the folder another project records — marked or not, it is that project's — so it
    /// never becomes a second project's, and nothing is written.
    /// </summary>
    public static DomainException HarnessFolderRecorded(string path, string owner) =>
        new(
            StatusCodes.Status409Conflict,
            "harness_folder_taken",
            $"The folder '{path}' is the folder recorded for project '{owner}', so it does not become another project's and nothing was written; name another folder, or link '{owner}' somewhere else first.");

    /// <summary>
    /// A link names a folder at, inside or holding another level's — one the runtime records for another project or for a
    /// campaign, or one carrying a project's or a campaign's marker above it. A session started in one would read the other's
    /// memory files, so it never becomes a project's, and nothing is written.
    /// </summary>
    /// <param name="standing">Where it stands to the other folder, naming it: "is inside '…/campaigns/q3-leads', the folder recorded for campaign 'cmp_…'".</param>
    public static DomainException HarnessFolderNested(string path, string standing) =>
        new(
            StatusCodes.Status409Conflict,
            "harness_folder_taken",
            $"The folder '{path}' {standing}, so it does not become a project's and nothing was written: an agent session started in one would read the other's memory files. Name a folder apart from every project's and campaign's, then link again.");

    /// <summary>
    /// A named preference is the person's word about their business: a role or a run that sets one is deciding it for
    /// them, so it reads a preference and never writes one, as it never decides an approval.
    /// </summary>
    public static InvalidRequestException PreferenceNotHuman(ActorType type) =>
        new(
            "preference_not_human",
            $"An actor of type '{SnakeCaseEnumConverter<ActorType>.Format(type)}' cannot set or unset a preference; a preference is a person's word.");

    /// <summary>The same code a decision without a named person is refused with: a preference records whose word it is.</summary>
    public static InvalidRequestException PreferenceActorRequired() =>
        new("actor_required", "actor.id is required on a preference: what is recorded has to name the person whose word it is.");

    public static ValidationException Required(string field) => new([new ErrorDetail(field, "required", $"{field} is required.")]);

    /// <summary>A grant names a plugin the active snapshot does not hold, so there is no manifest to hold it to.</summary>
    public static NotFoundException PluginNotFound(string id) =>
        new(
            "plugin_not_found",
            $"No plugin '{id}' is loaded, so nothing was granted; jason plugin list names the ones that are, and a package copied in loads at jason plugin reload.");

    /// <summary>
    /// <c>plugin.grant</c> asked for what the plugin's manifest does not request: stricter than a grant written by hand,
    /// which the load keeps as a warning, since a verb that writes what the load ignores misleads whoever ran it.
    /// </summary>
    public static ConflictException GrantUnrequested(string message) => new("grant_unrequested", message);

    /// <summary>
    /// <c>plugin.revoke</c> asked to take back an entry that a <c>*</c> in the same list grants: the verb takes one entry
    /// out and never rewrites the person's list, so the star is not expanded into the names it stands for.
    /// </summary>
    public static ConflictException GrantHeldByStar(string setting, string entry, string id, string flag) =>
        new(
            "grant_held_by_star",
            $"{setting} grants '*', which covers '{entry}', so nothing was taken back: take back '*' with jason plugin revoke {id} --{flag} '*', then grant what it should keep.");
}
