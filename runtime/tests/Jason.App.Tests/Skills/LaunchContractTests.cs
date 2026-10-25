using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.App.Tests.EndToEnd;
using Jason.Contracts.Api;
using Jason.Runtime.Tests;

namespace Jason.App.Tests.Skills;

/// <summary>
/// One launch contract, in every role's file. A launched role is dropped into a work directory it cannot ask
/// anything from, so the rules it is held to have to be in the file it reads — and hand-maintained copies of a
/// rule are chances for a correction to land in some of them and not the rest.
/// </summary>
public partial class LaunchContractTests
{
    [Fact]
    [Critical(Family.D2, "one launch contract")]
    public void Every_role_is_taught_the_same_launch_contract_byte_for_byte()
    {
        var roles = SkillPack.All().Where(skill => skill.IsRole).ToList();
        Assert.NotEmpty(roles);

        var first = roles[0];
        var contract = LaunchContract.Extract(first.File);
        Assert.True(
            contract is not null,
            $"'{first.File}' carries no launch contract. The block between {LaunchContract.Begin} and "
            + $"{LaunchContract.End} is what every role is held to, and a role without it was told nothing about "
            + "how it was launched.");

        foreach (var role in roles.Skip(1))
        {
            var block = LaunchContract.Extract(role.File);
            Assert.True(
                block is not null,
                $"'{role.File}' carries no launch contract between {LaunchContract.Begin} and "
                + $"{LaunchContract.End}.");

            Assert.True(
                string.Equals(block, contract, StringComparison.Ordinal),
                $"'{role.Name}' teaches a different launch contract from '{first.Name}': "
                + LaunchContract.FirstDifference(contract, block));
        }
    }

    /// <summary>
    /// The roster is seeded by a migration, so it is what the runtime really has, and this pack claims to teach
    /// every role on it. Read from the shipped program rather than from a list typed here — a list typed here
    /// would agree with this pack for the rest of time and with the runtime only by luck — and through
    /// <c>role list</c> rather than out of the database, because "verified against a shipped build" is what the
    /// pack claims and the program is the shipped build. It is the one fact in this class that starts a
    /// runtime, which is the price of asking the program instead of asking its storage.
    /// </summary>
    [Fact]
    [Critical(Family.D2, "seeded roles and the pack agree")]
    public async Task The_roles_this_runtime_seeds_and_the_roles_this_pack_teaches_are_the_same_nine()
    {
        using var it = GoldenPath.Create("skill-roster");
        await GoldenPath.WriteSettingsAsync(it, new SettingsShape(Routed: false));
        await GoldenPath.StartAsync(it);

        var listed = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "role", "list")))["items"]!
            .AsArray();
        var seeded = listed
            .Where(role => (bool)role!["builtin"]!)
            .Select(role => (string)role!["name"]!)
            .ToList();
        Assert.NotEmpty(seeded);

        var taught = SkillPack.All().Where(skill => skill.IsRole).Select(skill => skill.Name).ToList();

        foreach (var role in seeded)
        {
            Assert.True(
                taught.Contains(role, StringComparer.Ordinal),
                $"This runtime has the role '{role}' and this pack teaches it nothing: there is no "
                + $"skills/runtime/roles/{role}/SKILL.md. A role with no skill directory launches anyway and "
                + "does the job untaught, at the price of a real attempt.");
        }

        foreach (var role in taught)
        {
            Assert.True(
                seeded.Contains(role, StringComparer.Ordinal),
                $"'skills/runtime/roles/{role}' teaches a role this runtime does not have. Nothing launches a "
                + "role that is not on the roster, so this skill would be read by nobody.");
        }
    }

    /// <summary>
    /// What a launched role reads before it acts, in the one order every role is held to: the client's preferences, the
    /// person's memory files of the client, the project and the campaign by the paths the envelope gives, then the brief,
    /// the later winning; and its own notes at the three levels beside them, never over them. Said once, in the block the
    /// guard above holds identical in every role, because a role told only part of the order would read a campaign's
    /// words as overruled by a client's, or a preference as overruled by a file.
    /// </summary>
    [Fact]
    public void The_launch_contract_reads_the_persons_words_in_one_order_and_by_path()
    {
        var contract = Flattened(LaunchContract.Extract(SkillPack.All().First(skill => skill.IsRole).File) ?? string.Empty);

        var order = new[] { "`jason preference list`", "`memory_files.client`", "`memory_files.project`", "`memory_files.campaign`", "the brief in `context`" };
        var at = order.Select(step => contract.IndexOf(step, StringComparison.Ordinal)).ToList();
        Assert.True(
            at.All(index => index >= 0) && at.Zip(at.Skip(1)).All(pair => pair.First < pair.Second),
            $"The launch contract names the reading order's steps at [{string.Join(", ", at)}] for [{string.Join(", ", order)}]: "
            + "each must be there, in that order.");

        Assert.Contains("where the two disagree, the preference wins", contract, StringComparison.Ordinal);
        Assert.Contains("by the absolute path the envelope gives", contract, StringComparison.Ordinal);
        Assert.Contains("a path that is null means that level has none", contract, StringComparison.Ordinal);
        Assert.Contains("never above them", contract, StringComparison.Ordinal);

        // The direction, not only the order: a flipped one teaches the opposite precedence and every step above still stands.
        Assert.Contains("where two say different things, the later one wins", contract, StringComparison.Ordinal);
        Assert.Contains("in that order, the lower winning where two differ", contract, StringComparison.Ordinal);
        Assert.Contains("are read beside them and never above them", contract, StringComparison.Ordinal);

        // A read that is refused is said, as any refused tool is, and the file is not reached another way; a level with no file says nothing.
        Assert.Contains("a file that is empty or not there says nothing", contract, StringComparison.Ordinal);
        Assert.Contains(
            "If a read is refused, say so in your answer as you would any refused tool, and do not reach that file another way",
            contract,
            StringComparison.Ordinal);

        // Where the disagreement is said: where the answer's shape has room, never inside text written for somebody else.
        Assert.Contains(
            "say that they disagree where the shape you were asked for has room, never inside text written for somebody else",
            contract,
            StringComparison.Ordinal);

        // The client's and a project's note are shared with this role's runs on other campaigns, and a note is written whole.
        Assert.Contains(
            "shared with your runs on other campaigns: read one again immediately before you write it",
            contract,
            StringComparison.Ordinal);

        // The note's address is three ids now, read at three levels, and what each level holds is the role's to keep apart.
        Assert.Contains("`role_memory` names three ids", contract, StringComparison.Ordinal);
        Assert.Contains("`jason rolenote get --role`", contract, StringComparison.Ordinal);
        Assert.Contains("`role_memory.project_id`", contract, StringComparison.Ordinal);
        Assert.Contains("`role_memory.campaign_id`", contract, StringComparison.Ordinal);
        Assert.Contains(
            "Guesses about people and this campaign's state go in the campaign's note; the craft of this project, in the project's; "
            + "your craft in general, in the client's. A note never carries what belongs to a level below it",
            contract,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The envelope's <c>role_memory</c> names a campaign, the campaign's project and a role since version 4. A skill that
    /// still said it names a campaign and a role would send a role to one level of three.
    /// </summary>
    [Fact]
    public void No_skill_says_role_memory_names_a_campaign_and_a_role()
    {
        foreach (var skill in SkillPack.All())
        {
            var text = Flattened(File.ReadAllText(skill.File));
            Assert.False(
                text.Contains("names a campaign and a role", StringComparison.Ordinal),
                $"'{skill.File}' says role_memory names a campaign and a role; it names three ids, one level each.");
        }
    }

    /// <summary>
    /// A run that needs other work done first ends by waiting for it, and its next run is told why it woke: said once, in
    /// the block every role carries, because a role that held its attempt open to watch would keep its own campaign busy,
    /// and the work it watched would never be claimed. The reasons are the ones the runtime records, read from the
    /// enumeration rather than typed here, less the two a role's wait never has.
    /// </summary>
    [Fact]
    public void The_launch_contract_teaches_a_run_to_end_by_waiting_and_to_read_why_it_woke()
    {
        var file = SkillPack.All().First(skill => skill.IsRole).File;
        var block = LaunchContract.Extract(file) ?? string.Empty;
        var contract = Flattened(block);

        var waits = SkillPack.PrintedCommands(block.Split('\n'))
            .Where(line => line.StartsWith("jason workitem wait ", StringComparison.Ordinal))
            .ToList();
        Assert.True(
            waits.Count == 1 && waits[0].Contains(" --attempt ", StringComparison.Ordinal) && waits[0].Contains(" --on ", StringComparison.Ordinal),
            $"The launch contract prints [{string.Join(" | ", waits)}] as its wait; it prints one, naming the attempt and the items waited for.");

        Assert.Contains("raise that work as work items, then end this attempt by waiting for them", contract, StringComparison.Ordinal);
        Assert.Contains("**Never poll, sleep or hold your attempt open** to watch the work you raised", contract, StringComparison.Ordinal);
        Assert.Contains("while your run is alive, nothing else in this campaign is claimed, so the work you are waiting for cannot start", contract, StringComparison.Ordinal);
        Assert.Contains("A campaign review the runtime starts, on its cadence or because a line asked for one, never waits: its wait is refused with `wait_not_allowed`.", contract, StringComparison.Ordinal);
        Assert.Contains("The moment printed above is an example: `--until` names a moment ahead of now, and one already past is refused.", contract, StringComparison.Ordinal);
        Assert.Contains("A wait the runtime refuses changes nothing, and you still hold the attempt", contract, StringComparison.Ordinal);

        Assert.Contains("the envelope's `resumption` says why", contract, StringComparison.Ordinal);
        Assert.Contains("`resumption` is null when this run follows no wait", contract, StringComparison.Ordinal);
        Assert.Contains("`woke_because`", contract, StringComparison.Ordinal);
        foreach (var reason in Enum.GetValues<WaitReleaseReason>().Where(reason => reason is not (WaitReleaseReason.BlockCleared or WaitReleaseReason.BlockTrial)))
        {
            var word = $"`{JsonNamingPolicy.SnakeCaseLower.ConvertName(reason.ToString())}`";
            Assert.True(
                contract.Contains(word, StringComparison.Ordinal),
                $"The launch contract does not name {word}, a reason the runtime records for releasing a role's wait.");
        }

        Assert.DoesNotContain("block_cleared", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("block_trial", contract, StringComparison.Ordinal);
        Assert.Contains("A result marked `result_omitted` did not fit: read it with `jason workitem get`.", contract, StringComparison.Ordinal);
        Assert.Contains("what that means for the work is yours to decide", contract, StringComparison.Ordinal);
    }

    /// <summary>
    /// And one block per file. Two blocks in one file would leave the second unread by every guard here, which
    /// is the quiet way a correction lands in a file and changes nothing.
    /// </summary>
    [Fact]
    public void A_role_carries_the_launch_contract_once()
    {
        foreach (var role in SkillPack.All().Where(skill => skill.IsRole))
        {
            var text = File.ReadAllText(role.File);
            foreach (var marker in new[] { LaunchContract.Begin, LaunchContract.End })
            {
                var found = MarkedBlock.Occurrences(text, marker);
                Assert.True(
                    found == 1,
                    $"'{role.File}' carries {marker} {found} times. One block per file: a second one is read by "
                    + "no guard here, which is the quiet way a correction lands in a file and changes nothing.");
            }
        }
    }

    private static string Flattened(string text) => Whitespace().Replace(text, " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
