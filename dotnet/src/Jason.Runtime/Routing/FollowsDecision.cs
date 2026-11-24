using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Routing;

/// <summary>
/// Which earlier letter of the line a message follows, as a reply in that letter's thread, as the claim decides it: a pure
/// function over the facts the claim read, the route and the routed plugin, like the pre-flight it is part of. The caller's
/// <c>args.follows</c> is a wish with three states — absent, the runtime's default; a work item's id, that letter named;
/// <c>new_thread</c>, a letter of its own — and the decision is written beside the arguments, never into them: composed at the
/// root of the input (<see cref="Composed"/>), which the plugin reads and the person approves, and recorded under the
/// reserved context key <see cref="Key"/> less the provider's reference (<see cref="Recorded"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The default never refuses.</b> Absent, the message follows the line's root
/// (<see cref="Jason.Runtime.Campaigns.LineRoots"/>), and every condition that keeps it from doing so makes a new letter and
/// says why: the routed plugin does not take the argument on the channel, the line holds no letter that can be followed,
/// the root went through another plugin or account, or from another mailbox. Only a letter the caller named is refused
/// when it cannot be followed, by five checks of the claim, asked in one order, each with its own code.
/// </para>
/// <para>
/// <b>A repeat keeps its decision.</b> Where the work carries an attempt that may have acted, nothing is decided again and
/// none of the five checks is asked: the decision is the one its first claim recorded, and the reference is read again
/// from the letter it names, whose attempts no longer change. A decision taken again could change the composed input, so
/// the repeat would no longer be the request the provider may already have taken, or send a letter that went into a
/// thread a second time as a new one. A repeat of an attempt claimed before the runtime decided this at all, which
/// recorded nothing, composes nothing either, as that attempt's claim did not.
/// </para>
/// <para>
/// Plugin and binding are compared by the plugin's id and the binding's identity, never the package's digest, as for a
/// read of a send; the mailbox by the letter's result against the message's own <c>args.sender</c>.
/// </para>
/// </remarks>
public abstract record FollowsDecision
{
    /// <summary>
    /// The one name of it everywhere: the argument the caller may write under <c>args</c>, the member the runtime composes at
    /// the root of the input, and the reserved context key the claim records its decision under.
    /// </summary>
    public const string Key = "follows";

    /// <summary>The value of the argument that asks for a letter of its own.</summary>
    public const string NewThread = "new_thread";

    /// <summary>Why a letter is followed: the caller named it.</summary>
    public const string Named = "named";

    /// <summary>Why a letter is followed: the runtime's default followed the line's root.</summary>
    public const string LineRoot = "line_root";

    /// <summary>Why a letter is new: the caller asked for a letter of its own.</summary>
    public const string AskedNewThread = "asked_new_thread";

    /// <summary>Why a letter is new: the line holds no earlier letter that can be followed.</summary>
    public const string NoEarlierLetter = "no_earlier_letter";

    /// <summary>Why a letter is new: the routed plugin does not take the argument on the message's channel.</summary>
    public const string NotOffered = "not_offered";

    /// <summary>Why a letter is new: the line's root went through another plugin or binding than the message is routed to.</summary>
    public const string OtherAccount = "other_account";

    /// <summary>Why a letter is new: the line's root went from another of the provider's accounts than the message's own sender.</summary>
    public const string OtherMailbox = "other_mailbox";

    /// <summary>The member of the composed decision the recorded one leaves out: the provider's reference is provenance.</summary>
    private const string ReferenceMember = "reference";

    private static readonly IReadOnlySet<string> OnlyTheArgument = new HashSet<string>([Key], StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> NoArgument = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// The message follows the letter <paramref name="WorkItem"/> made by <paramref name="Operation"/>, as a reply in its
    /// thread, by the provider's <paramref name="Reference"/> for that send: null only on a repeat whose recorded letter holds
    /// none any more, which the operation's schema then refuses rather than send a reply as a new letter.
    /// </summary>
    public sealed record Follow(string WorkItem, string? Operation, string? Reference, string Because) : FollowsDecision;

    /// <summary>The message is a letter of its own, <paramref name="Because"/> one of the five reasons a letter is new.</summary>
    public sealed record NewLetter(string Because) : FollowsDecision;

    /// <summary>The letter the caller named cannot be followed: the claim refuses the work with <paramref name="Code"/>, and nothing is sent.</summary>
    public sealed record Refused(string Code, string Message) : FollowsDecision;

    /// <summary>
    /// The arguments the work carries that ask nothing of the routed plugin, as this decision reads them: <see cref="Key"/>
    /// where the caller asked for a letter of its own, which every plugin sends, and none otherwise. The twenty-first check
    /// passes over them, so it asks the decision rather than knowing any value of any argument itself.
    /// </summary>
    public IReadOnlySet<string> AsksNothing => this is NewLetter { Because: AskedNewThread } ? OnlyTheArgument : NoArgument;

    /// <summary>
    /// The member composed at the root of the input: the letter followed, by its work item, its operation and the provider's
    /// reference, or, with all three null, a new letter, and why either way. Null for a refusal, which composes nothing.
    /// </summary>
    public JsonObject? Composed() => this switch
    {
        Follow follow => Member(follow.WorkItem, follow.Operation, follow.Reference, follow.Because),
        NewLetter letter => Member(null, null, null, letter.Because),
        _ => null,
    };

    /// <summary>What the claim records under <see cref="Key"/>: the composed member less the provider's reference.</summary>
    public static JsonObject Recorded(JsonObject composed)
    {
        ArgumentNullException.ThrowIfNull(composed);
        var recorded = composed.DeepClone().AsObject();
        recorded.Remove(ReferenceMember);
        return recorded;
    }

    /// <summary>
    /// The decision, or null for an operation whose input carries no letter it follows, and for a repeat of an attempt
    /// claimed before the decision was recorded at all: nothing is composed then.
    /// </summary>
    public static FollowsDecision? Decide(OperationContract contract, PreflightFacts facts, Route route, LoadedPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(plugin);

        if (!CanonicalInput.Follows(contract))
        {
            return null;
        }

        var item = facts.Item;
        if (facts.Unsettled is not null)
        {
            return item.Context[Key] is JsonObject recorded ? Again(recorded, facts.Followed) : null;
        }

        var channel = CanonicalInput.ConsumedChannel(contract, item);
        return Written(item) switch
        {
            (Wish.NewThread, _) => new NewLetter(AskedNewThread),
            (Wish.Named, var named) => Checked(contract.Id, facts, route, named, channel)
                ?? new Follow(facts.Followed!.WorkItem, facts.Followed.Operation, facts.Followed.Reference, Named),
            _ => Default(contract.Id, facts, route, plugin, channel),
        };
    }

    /// <summary>
    /// The work item the arguments name under <c>args.follows</c>, the letter the claim reads for the five checks, or null where
    /// they name none: nothing written, <see cref="NewThread"/>, or a value that is no text, which the first check refuses.
    /// </summary>
    public static string? NamedLetter(WorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Written(item) is (Wish.Named, var named) ? named : null;
    }

    /// <summary>Whether the arguments write nothing under <c>args.follows</c>: the runtime's default decides.</summary>
    public static bool LeftToTheRuntime(WorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Written(item).Wish == Wish.Absent;
    }

    /// <summary>The work item the first claim recorded under <see cref="Key"/>, which a repeat reads again; null where it recorded none.</summary>
    public static string? RecordedLetter(WorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Context[Key] is JsonObject recorded ? Text(recorded["work_item"]) : null;
    }

    /// <summary>
    /// Refuses <see cref="Key"/> where a caller names it in the context of work whose input carries the letter it follows,
    /// at create or in a patch: it is the claim's record of what it decided, and a value written beside it would say
    /// something the claim never decided.
    /// </summary>
    public static void RefuseWritten(string? operation, bool named, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (named && operation is not null && OperationCatalog.Find(operation) is { } contract && CanonicalInput.Follows(contract))
        {
            errors.Add(
                "context." + Key,
                "not_allowed",
                $"follows is the runtime's on '{operation}': the claim records there which earlier letter the message follows, and why. Write the wish under input instead: the work item of the letter it follows, or new_thread for a letter of its own; left out, the runtime decides.");
        }
    }

    /// <summary>The runtime's default, in its published order: it makes a new letter wherever it cannot follow the root.</summary>
    private static FollowsDecision Default(string operation, PreflightFacts facts, Route route, LoadedPlugin plugin, string? channel)
    {
        if (channel is null || !plugin.Manifest.Arguments.Takes(operation, Key, channel))
        {
            return new NewLetter(NotOffered);
        }

        if (facts.Followed is not { } root)
        {
            return new NewLetter(NoEarlierLetter);
        }

        if (!SameAccount(root, route))
        {
            return new NewLetter(OtherAccount);
        }

        return SameMailbox(root, facts.Item)
            ? new Follow(root.WorkItem, root.Operation, root.Reference, LineRoot)
            : new NewLetter(OtherMailbox);
    }

    /// <summary>
    /// The thirty-first to thirty-fifth checks of the claim, for a letter the caller named, in their order; null where none
    /// refuses it.
    /// </summary>
    private static FollowsDecision? Checked(string operation, PreflightFacts facts, Route route, string? named, string? channel)
    {
        var letter = facts.Followed;
        if (letter is null
            || !string.Equals(letter.WorkItem, named, StringComparison.Ordinal)
            || !string.Equals(letter.Operation, operation, StringComparison.Ordinal)
            || letter.ContactId is not { } person
            || facts.Contact is not { } contact
            || person != contact.Id
            || !string.Equals(letter.Channel, channel, StringComparison.Ordinal))
        {
            return new Refused(
                AttemptErrors.FollowsUnknown,
                $"This campaign holds no letter under '{named}' for this '{operation}' to follow: no '{operation}' work item of it by that id about the same person on the '{channel}' channel. Name one that is, leave follows out for the line's root, or write new_thread for a letter of its own.");
        }

        if (letter.Status != WorkItemStatus.Succeeded || letter.Reference is null)
        {
            return new Refused(
                AttemptErrors.FollowsReferenceAbsent,
                $"The letter '{letter.WorkItem}' has not succeeded with the provider's reference, so the provider kept nothing to place this '{operation}' in its thread by. Name a letter that went, or leave follows out for the line's root.");
        }

        if (letter.TakenBack)
        {
            return new Refused(
                AttemptErrors.FollowsTakenBack,
                $"The letter '{letter.WorkItem}' was taken back by a succeeded message.cancel of this campaign: it never went, so there is no thread to follow. Name a letter that went, leave follows out for the line's root, or write new_thread for a letter of its own.");
        }

        if (!SameAccount(letter, route))
        {
            return new Refused(
                AttemptErrors.FollowsRouteMismatch,
                $"The letter '{letter.WorkItem}' went through another plugin or account than this '{operation}' is routed to, and its thread is that account's, so the message follows it there or not at all. Route the operation, or its channel, to the plugin and account the letter went through.");
        }

        return SameMailbox(letter, facts.Item)
            ? null
            : new Refused(
                AttemptErrors.FollowsSenderMismatch,
                $"The letter '{letter.WorkItem}' went from another of the provider's accounts than this '{operation}' names under sender, and a thread is the mailbox's that sent it. Send from the account the letter went from, or name a letter that went from this one.");
    }

    /// <summary>A repeat's decision: the one its first claim recorded, with the reference read again from the letter it names.</summary>
    private static FollowsDecision Again(JsonObject recorded, EarlierLetter? letter)
    {
        var because = Text(recorded["because"]) ?? string.Empty;
        return Text(recorded["work_item"]) is { } followed
            ? new Follow(
                followed,
                Text(recorded["operation"]),
                letter is { } read && string.Equals(read.WorkItem, followed, StringComparison.Ordinal) ? read.Reference : null,
                because)
            : new NewLetter(because);
    }

    /// <summary>Whether the letter went through the plugin and the binding this work is routed to.</summary>
    private static bool SameAccount(EarlierLetter letter, Route route) =>
        string.Equals(letter.PluginId, route.PluginId, StringComparison.Ordinal)
        && string.Equals(letter.BindingIdentity, route.BindingIdentity, StringComparison.Ordinal);

    /// <summary>
    /// Whether the letter went from the provider's account the work's own arguments name under <c>args.sender</c>: both known,
    /// and the same. A mailbox either side does not say is never assumed the same.
    /// </summary>
    private static bool SameMailbox(EarlierLetter letter, WorkItem item) =>
        letter.Sender is { } sent
        && item.Context[WorkItemService.InputKey] is JsonObject arguments
        && Text(arguments["sender"]?["external_id"]) is { } sender
        && string.Equals(sent, sender, StringComparison.Ordinal);

    /// <summary>What the caller wrote under <c>args.follows</c>, and the name where it is one.</summary>
    private static (Wish Wish, string? Named) Written(WorkItem item)
    {
        if (item.Context[WorkItemService.InputKey] is not JsonObject arguments || !arguments.TryGetPropertyValue(Key, out var value))
        {
            return (Wish.Absent, null);
        }

        var text = Text(value);
        return string.Equals(text, NewThread, StringComparison.Ordinal) ? (Wish.NewThread, null) : (Wish.Named, text);
    }

    private static JsonObject Member(string? workItem, string? operation, string? reference, string because) => new()
    {
        ["work_item"] = workItem,
        ["operation"] = operation,
        [ReferenceMember] = reference,
        ["because"] = because,
    };

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    /// <summary>The three states of the caller's wish.</summary>
    private enum Wish
    {
        Absent,
        NewThread,
        Named,
    }
}
