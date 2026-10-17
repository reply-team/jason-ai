using System.Text.Json.Nodes;
using Jason.PluginHost;

// The plugin host's mode, runner and host functions exactly as the shipped executable runs them, with a kill that
// never comes back in place of the real one. The runtime starts this as it starts the shipped executable, with the
// mode word first.
if (args is not ["plugin-host", .. var rest])
{
    await Console.Error.WriteLineAsync("usage: Jason.StuckKillPluginHost plugin-host <the plugin host's arguments>");
    return 2;
}

// Its own number first, on the stream the runtime keeps: a test that gives up on the invocation ends what it finds there.
var started = new JsonObject
{
    ["source"] = "stand-in",
    ["message"] = "stuck_kill_host_started",
    ["data"] = new JsonObject { ["pid"] = Environment.ProcessId },
};
await Console.Error.WriteLineAsync(started.ToJsonString());

PluginHostMode.Runner = new InvocationRunner { Kill = static _ => Thread.Sleep(Timeout.Infinite) };
return await PluginHostMode.RunAsync(rest, Console.In, Console.Out, Console.Error, CancellationToken.None);
