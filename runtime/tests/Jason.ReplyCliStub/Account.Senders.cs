using System.Text.Json.Nodes;

// Deliberately in the global namespace with the rest of this program.

/// <summary>
/// Who the credential signs in as, and the accounts a send can go from, answered as reply-cli 0.14.0 documents them:
/// <c>auth whoami</c> prints Reply's principal as Reply gives it, and <c>account list</c> the mailboxes first and then the
/// LinkedIn accounts, teammates' included, each with the Reply user whose it is.
/// </summary>
internal static partial class Account
{
    private const string SendersFile = "senders.json";
    private const string IdentityFile = "identity.json";
    private const string FeaturesFile = "features.json";

    private static readonly string[] AccountChannels = ["email", "linkedin"];

    /// <summary>Reply's words for an account's state and the CLI's; a word not here is <c>other</c>.</summary>
    private static readonly Dictionary<string, string> EmailStatuses = new(StringComparer.Ordinal)
    {
        ["connected"] = "active",
        ["disconnected"] = "disconnected",
        ["unknown"] = "other",
    };

    /// <summary>A LinkedIn account at its daily limit is on and connected, so it is active: a send there meets the limit.</summary>
    private static readonly Dictionary<string, string> LinkedInStatuses = new(StringComparer.Ordinal)
    {
        ["enabled"] = "active",
        ["disabled"] = "inactive",
        ["cookieInvalid"] = "disconnected",
        ["dailyLimitReached"] = "active",
    };

    /// <summary><c>auth whoami</c>: Reply's principal, printed as Reply gives it.</summary>
    private static Ending AuthWhoami(string root)
    {
        var file = Path.Combine(root, IdentityFile);
        return Done(File.Exists(file) && JsonNode.Parse(File.ReadAllText(file)) is JsonObject identity ? identity : []);
    }

    /// <summary>
    /// <c>account list [--channel email|linkedin]</c>: every account of the channels asked for, the mailboxes first. Without
    /// <c>--channel</c> both lists are read, and one the account's plan or role does not include fails the whole command.
    /// </summary>
    private static Ending AccountList(string root, Parsed line)
    {
        var channel = line.Option("--channel");
        if (channel is not null && !AccountChannels.Contains(channel))
        {
            return Refused("usage.choice", "--channel must be one of: email, linkedin.", exit: 2);
        }

        var items = new JsonArray();
        foreach (var asked in AccountChannels.Where(name => channel is null || channel == name))
        {
            if (!Enabled(root, asked == "email" ? "emailAccounts" : "linkedInAccounts"))
            {
                return Refused(
                    "access.feature_unavailable", "The account's plan or role does not include this.", status: 403,
                    replyCode: asked == "email" ? "emailAccount.forbidden" : "linkedInAccount.forbidden");
            }

            foreach (var sender in Records(root, SendersFile).Where(sender => Text(sender, "channel") == asked))
            {
                items.Add(AccountListed(sender));
            }
        }

        return Done(new JsonObject { ["items"] = items });
    }

    /// <summary>One account as the list answers it: its name and address null when blank, Reply's state word beside the CLI's.</summary>
    private static JsonObject AccountListed(JsonObject sender)
    {
        var channel = Text(sender, "channel");
        var word = Text(sender, "status");
        var words = channel == "email" ? EmailStatuses : LinkedInStatuses;
        return new JsonObject
        {
            ["id"] = sender["id"]!.GetValue<long>(),
            ["channel"] = channel,
            ["name"] = TextOrNull(sender, "name"),
            ["address"] = TextOrNull(sender, "address"),
            ["owner_user_id"] = sender["ownerUserId"] is JsonValue owner && owner.TryGetValue<int>(out var user) && user > 0 ? user : null,
            ["status"] = words.TryGetValue(word, out var neutral) ? neutral : "other",
            ["reply_status"] = word.Length == 0 ? null : word,
        };
    }

    /// <summary>The account a send names, by its channel and id, or null when the account holds none.</summary>
    private static JsonObject? SenderOf(string root, string channel, long id) =>
        Records(root, SendersFile).FirstOrDefault(sender => Text(sender, "channel") == channel && sender["id"]!.GetValue<long>() == id);

    /// <summary>Whether the account's plan or role includes a feature; one never planted as missing is included.</summary>
    private static bool Enabled(string root, string feature)
    {
        var file = Path.Combine(root, FeaturesFile);
        return !(File.Exists(file)
            && JsonNode.Parse(File.ReadAllText(file)) is JsonObject features
            && features[feature] is JsonValue value
            && value.TryGetValue<bool>(out var included)
            && !included);
    }

    private static string? TextOrNull(JsonObject parent, string name) => EcmaTrim(Text(parent, name)).Length == 0 ? null : Text(parent, name);
}
