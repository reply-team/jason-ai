using Jason.PluginHost;
using Jason.PluginHost.Scripting;

namespace Jason.PluginHost.Tests;

public class PluginHostModeTests
{
    [Fact]
    public void Stub_mode_reports_the_missing_protocol_and_fails()
    {
        using var stderr = new StringWriter();
        var exit = PluginHostMode.Run([], stderr);
        Assert.Equal(1, exit);
        Assert.Contains("not implemented", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Embedded_javascript_engine_evaluates_a_script() =>
        Assert.Equal(2d, ScriptEvaluator.Evaluate("1 + 1"));

    [Fact]
    public void Runaway_scripts_are_stopped() =>
        Assert.ThrowsAny<Exception>(() => ScriptEvaluator.Evaluate("while (true) {}"));
}
