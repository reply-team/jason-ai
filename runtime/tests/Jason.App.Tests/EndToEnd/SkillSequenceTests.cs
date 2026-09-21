using System.Text.Json.Nodes;
using Jason.App.Tests.Skills;
using Jason.Cli.Tests.Documentation;
using Jason.Contracts.Json;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// The sequences the skills print, run. Parsing proves the vocabulary exists; it does not prove the order
/// works, that the identifiers flow from one line into the next, or that the thing the skill says will happen
/// happens. These do, against a real runtime with a real plugin behind it.
/// </summary>
/// <remarks>
/// <para>
/// Each line is asserted to appear verbatim in the skill before it is run, so this proves both directions: the
/// skill cannot change a line without this failing, and the line cannot stop working without this failing.
/// </para>
/// <para>
/// The skills print one canonical placeholder identifier per prefix, which is what makes the substitution
/// unambiguous — and <see cref="Substituted"/> refuses to run a line that still carries one.
/// </para>
/// </remarks>
public class SkillSequenceTests
{
    /// <summary>The one identifier every skill in this pack prints, whatever the prefix in front of it.</summary>
    private const string Placeholder = "01JB6K8TQ2W9V4MZ0C3Y7H5NRD";

    // This test's own fixture, not a copy of another test's that has to track it: the fake account below is
    // built from these four values and nothing outside this file reads them.
    private const int Sequence = 7;
    private const int Person = 1001;
    private const string Address = "marta@example.test";
    private const string FirstName = "Marta";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A campaign, the people it is about, and a unit of work, by the lines the managed path prints. The
    /// contacts file is written first because the CLI reads it while it parses — which is also what a person
    /// does, since a session has no file-writing tool under the shape this pack is measured in.
    /// </summary>
    [Fact]
    public async Task The_managed_path_creates_a_campaign_and_its_work_by_the_lines_it_prints()
    {
        using var it = GoldenPath.Create("skill-managed");
        it.Account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(Person, Address, FirstName);

        await GoldenPath.WriteSettingsAsync(it, new SettingsShape());
        await GoldenPath.StartAsync(it);
        GoldenPath.InstallReplyPackage(it);
        await GoldenPath.ReloadAsync(it, "installed the reply plugin", routed: true);

        var printed = Prints("managed-campaign-work");
        const string Create = "jason campaign create --name \"Q3 LatAm founders\"";
        const string Add = $"jason campaign add-contacts cmp_{Placeholder} --file contacts.json --match-by email";
        const string List = $"jason campaign list-contacts cmp_{Placeholder}";
        const string Work = $"jason workitem create cmp_{Placeholder} --kind provider_op --operation "
            + $"campaign.enroll --contact cnt_{Placeholder} --input \"{{\\\"campaign\\\":{{\\\"external_id\\\":"
            + "\\\"7\\\"},\\\"channel\\\":\\\"email\\\",\\\"collision\\\":\\\"skip\\\",\\\"start\\\":"
            + "{\\\"position\\\":\\\"first_step\\\"},\\\"first_touch\\\":\\\"authored_delay\\\"}\"";
        const string Start = $"jason campaign start cmp_{Placeholder}";
        const string Follow = $"jason workitem list --campaign cmp_{Placeholder}";

        foreach (var line in new[] { Create, Add, List, Work, Start, Follow })
        {
            Assert.Contains(line, printed);
        }

        // The person's own file. The skill prints a relative name because that is what a person types; the
        // child this harness starts does not inherit this installation's directory as its own, so what is
        // typed here is the absolute one.
        var contacts = Path.Combine(it.Root, "contacts.json");
        await File.WriteAllTextAsync(
            contacts,
            new JsonArray(new JsonObject
            {
                ["first_name"] = FirstName,
                ["channels"] = new JsonArray(new JsonObject { ["channel"] = "email", ["value"] = Address }),
            }).ToJsonString(JasonJson.Options),
            Ct);

        var nothing = new Dictionary<string, string>(StringComparer.Ordinal);
        var campaign = (string)GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, [.. Substituted(Create, nothing)])))["id"]!;
        var ids = new Dictionary<string, string>(StringComparer.Ordinal) { ["cmp_"] = campaign };

        var added = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
            it, [.. Substituted(Add, ids, ("contacts.json", contacts))])));
        Assert.Equal(1, (int)added["summary"]!["added"]!);

        var people = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, [.. Substituted(List, ids)])));
        var listed = Assert.Single(people["items"]!.AsArray())!;
        Assert.Equal(Address, (string?)listed["contact"]!["channels"]!.AsArray()[0]!["value"]);
        ids["cnt_"] = (string)listed["contact"]!["id"]!;

        // The unit of work itself, by the line the skill prints for it — which is the half that makes this a
        // managed path rather than an address book. It parks on a person at the claim, the way the walkthrough's
        // own does, so what is asserted afterwards is that it exists and is followed, not that it ran.
        var item = (string)GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, [.. Substituted(Work, ids)])))["id"]!;
        Assert.StartsWith("wi_", item, StringComparison.Ordinal);

        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(it, [.. Substituted(Start, ids)]));

        var work = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, [.. Substituted(Follow, ids)])));
        Assert.Contains(work["items"]!.AsArray(), listing => (string?)listing!["id"] == item);

        var parked = await GoldenPath.PollAsync(it, item, read => (string?)read["status"] == "awaiting_approval");
        Assert.Empty(parked["attempts"]!.AsArray());
    }

    /// <summary>
    /// The approvals skill's own sequence, against a work item that really parked: list what is waiting, read
    /// what it would do, and record the person's decision. The item then moves — read back from the runtime
    /// rather than from what the command printed, because a skill whose lines all succeed and whose work never
    /// moves has taught nothing.
    /// </summary>
    [Fact]
    public async Task The_approvals_skill_decides_a_parked_item_by_the_lines_it_prints()
    {
        using var it = GoldenPath.Create("skill-approvals");
        var parked = await AnEnrolmentWaitingForAPersonAsync(it);

        var printed = Prints("approvals-and-questions");
        const string List = "jason approval list --human";
        const string Read = $"jason approval get apr_{Placeholder}";
        const string Approve =
            $"jason approval approve apr_{Placeholder} --actor human:ada --reason \"checked the list\"";

        foreach (var line in new[] { List, Read, Approve })
        {
            Assert.Contains(line, printed);
        }

        var ids = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["apr_"] = parked.ApprovalId,
            ["cmp_"] = parked.CampaignId,
            ["wi_"] = parked.WorkItemId,
            ["cnt_"] = parked.ContactId,
        };

        foreach (var line in new[] { List, Read, Approve })
        {
            GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(it, [.. Substituted(line, ids)]));
        }

        // The item the person decided about really left the gate.
        var released = await GoldenPath.PollAsync(
            it, parked.WorkItemId, read => (string?)read["status"] != "awaiting_approval");
        Assert.NotEqual("awaiting_approval", (string?)released["status"]);

        // And the decision is the person's, by name. `decided_by` is an actor reference and not a string — a
        // type and an id — so it is read the way the walkthrough's own test reads it.
        var decided = GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "approval", "get", parked.ApprovalId)));
        Assert.Equal("approved", (string?)decided["status"]);
        Assert.Equal("human", (string?)decided["decided_by"]!["type"]);
        Assert.Equal("ada", (string?)decided["decided_by"]!["id"]);
    }

    /// <summary>
    /// The golden path's own opening, up to the moment a person is asked: an installation with the official
    /// package routed, a campaign, the person it is about, and one enrolment parked at the claim.
    /// </summary>
    private static async Task<(string CampaignId, string ContactId, string WorkItemId, string ApprovalId)>
        AnEnrolmentWaitingForAPersonAsync(Installation it)
    {
        it.Account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(Person, Address, FirstName);

        await GoldenPath.WriteSettingsAsync(it, new SettingsShape());
        await GoldenPath.StartAsync(it);
        GoldenPath.InstallReplyPackage(it);
        await GoldenPath.ReloadAsync(it, "installed the reply plugin", routed: true);

        var campaign = (string)GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "campaign", "create", "--name", "Q3 LatAm founders")))["id"]!;

        var contacts = Path.Combine(it.Root, "approval-contacts.json");
        await File.WriteAllTextAsync(
            contacts,
            new JsonArray(new JsonObject
            {
                ["first_name"] = FirstName,
                ["channels"] = new JsonArray(new JsonObject { ["channel"] = "email", ["value"] = Address }),
            }).ToJsonString(JasonJson.Options),
            Ct);
        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(
            it, "campaign", "add-contacts", campaign, "--file", contacts, "--match-by", "email"));

        var contact = (string)Assert.Single(GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "campaign", "list-contacts", campaign)))["items"]!.AsArray())!["contact"]!["id"]!;

        var enrolment = new JsonObject
        {
            ["campaign"] = new JsonObject { ["external_id"] = Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            ["channel"] = "email",
            ["collision"] = "skip",
            ["start"] = new JsonObject { ["position"] = "first_step" },
            ["first_touch"] = "authored_delay",
        };

        var item = (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
            it,
            "workitem",
            "create",
            campaign,
            "--kind",
            "provider_op",
            "--operation",
            "campaign.enroll",
            "--contact",
            contact,
            "--input",
            enrolment.ToJsonString(JasonJson.Options))))["id"]!;

        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(it, "campaign", "start", campaign));
        await GoldenPath.PollAsync(it, item, read => (string?)read["status"] == "awaiting_approval");

        var waiting = Assert.Single(GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "approval", "list")))["items"]!.AsArray())!;

        return (campaign, contact, item, (string)waiting["id"]!);
    }

    /// <summary>
    /// Every <c>jason …</c> line a skill prints, as whole lines. Whole lines rather than a substring search
    /// over the text, because a substring match would let a printed line grow an option — the skill would then
    /// be teaching a command this test never runs, which is exactly what it exists to prevent.
    /// </summary>
    internal static IReadOnlyList<string> Prints(string skill)
    {
        var lines = new List<string>();
        var fenced = false;
        foreach (var line in File.ReadAllLines(SkillPack.Find(skill).File))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            var text = line.Trim();
            if (fenced && text.StartsWith("jason ", StringComparison.Ordinal))
            {
                lines.Add(text);
            }
        }

        return lines;
    }

    /// <summary>The printed line with this run's own identifiers in place of the skill's placeholders.</summary>
    internal static IReadOnlyList<string> Substituted(
        string line,
        IReadOnlyDictionary<string, string> ids,
        params (string Printed, string Real)[] names)
    {
        foreach (var (prefix, id) in ids)
        {
            line = line.Replace(prefix + Placeholder, id, StringComparison.Ordinal);
        }

        foreach (var (printed, real) in names)
        {
            line = line.Replace(printed, real, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(Placeholder, line, StringComparison.Ordinal);
        return ShellWords.Split(line);
    }
}
