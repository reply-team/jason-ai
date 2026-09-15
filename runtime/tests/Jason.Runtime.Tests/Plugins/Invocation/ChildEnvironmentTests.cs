using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Invocation;

/// <summary>
/// What a plugin's process is allowed to see of the machine. The environment is built from nothing rather than
/// inherited, so a child cannot find the data directory, the database or anything else this runtime holds —
/// only the machine configuration every program needs and the variables the user granted this plugin.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ChildEnvironmentTests
{
    [Fact]
    public void A_child_gets_the_machine_configuration_its_grants_and_nothing_else()
    {
        var unique = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var granted = "FAKE_GRANTED_" + unique;
        var unlisted = "FAKE_UNLISTED_" + unique;
        var reserved = BaseEnvironment.ReservedPrefix + "TEST_" + unique;

        Environment.SetEnvironmentVariable(granted, "granted-value");
        Environment.SetEnvironmentVariable(unlisted, "unlisted-value");
        Environment.SetEnvironmentVariable(reserved, "reserved-value");
        try
        {
            var environment = ChildEnvironment.Build([granted, reserved]);

            Assert.True(environment.ContainsKey("PATH"), "a child that cannot find a program can run none");
            Assert.Equal("granted-value", environment[granted]);
            Assert.False(environment.ContainsKey(unlisted), "a variable nobody granted reached the child");

            // Granting a reserved name does not make it grantable: the runtime's own configuration is never
            // the plugin's business, and the data directory least of all.
            Assert.False(environment.ContainsKey(reserved), "a JASON_ variable reached the child");
            Assert.DoesNotContain(
                environment.Keys,
                name => name.StartsWith(BaseEnvironment.ReservedPrefix, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(JasonPaths.DataDirectoryVariable, environment.Keys, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(granted, null);
            Environment.SetEnvironmentVariable(unlisted, null);
            Environment.SetEnvironmentVariable(reserved, null);
        }
    }

    [Fact]
    public void A_granted_variable_the_machine_does_not_have_stays_absent()
    {
        var missing = "FAKE_MISSING_" + Guid.NewGuid().ToString("N").ToUpperInvariant();

        var environment = ChildEnvironment.Build([missing]);

        Assert.False(environment.ContainsKey(missing));
    }
}
