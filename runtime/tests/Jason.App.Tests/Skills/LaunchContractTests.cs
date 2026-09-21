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
}
