using Jason.Contracts.Plugins;

namespace Jason.Contracts.Tests.Plugins;

/// <summary>
/// The codes a package answers without the provider having judged the work. A provider block is cleared only by that
/// judgment, so the runtime reads this one set where it decides whether a failure got past a block.
/// </summary>
public class UnjudgedOutcomeCodesTests
{
    [Fact]
    public void The_provider_input_code_is_reserved_under_validation()
    {
        Assert.Equal(FailureClass.Validation, UnjudgedOutcomeCodes.Reserved[UnjudgedOutcomeCodes.ProviderInputInvalid]);
    }

    [Fact]
    public void The_capability_code_is_reserved_under_permanent()
    {
        Assert.Equal(FailureClass.Permanent, UnjudgedOutcomeCodes.Reserved[UnjudgedOutcomeCodes.CapabilityNotOffered]);
    }

    [Fact]
    public void The_set_is_every_host_code_and_every_reserved_code_and_nothing_else()
    {
        Assert.Equal(
            HostOutcomeCodes.All.Concat(UnjudgedOutcomeCodes.Reserved.Keys).Order(StringComparer.Ordinal),
            UnjudgedOutcomeCodes.All.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void No_reserved_code_is_a_host_code()
    {
        Assert.Empty(UnjudgedOutcomeCodes.Reserved.Keys.Intersect(HostOutcomeCodes.All));
    }
}
