using System.Text.Json.Nodes;
using Jason.App.Tests.EndToEnd;

namespace Jason.App.Tests.Skills;

/// <summary>
/// One launch contract, in every role's file. A launched role is dropped into a work directory it cannot ask
/// anything from, so the rules it is held to have to be in the file it reads — and hand-maintained copies of a
/// rule are chances for a correction to land in some of them and not the rest.
/// </summary>
public class LaunchContractTests
{
    [Fact]
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
                var found = Occurrences(text, marker);
                Assert.True(
                    found == 1,
                    $"'{role.File}' carries {marker} {found} times. One block per file: a second one is read by "
                    + "no guard here, which is the quiet way a correction lands in a file and changes nothing.");
            }
        }
    }

    private static int Occurrences(string text, string marker)
    {
        var count = 0;
        for (var at = text.IndexOf(marker, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(marker, at + marker.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
