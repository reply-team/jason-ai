using System.Text.Json;
using Jason.Contracts.Execution;
using Jason.Contracts.Json;
using Jason.FakeAgentHost;

// A stand-in agent host. It is handed one JSON envelope on stdin, reads the descriptor named in it whenever it
// wants to call the Runtime API, and then behaves as badly as its first argument tells it to. Everything the
// dispatcher has to survive is one of these behaviours, so nothing in the test suite has to pretend to be a
// process.
const int UnreadableEnvelope = 64;

// Not a behaviour and not a host: this program asked to hold a pipe open. It reads no envelope and answers
// nothing, because what it is for is being something a finished host left behind.
if (args is ["linger"])
{
    await Task.Delay(TimeSpan.FromSeconds(30));
    return 0;
}

string raw;
LaunchEnvelope envelope;
try
{
    raw = await Console.In.ReadToEndAsync();
    envelope = JsonSerializer.Deserialize<LaunchEnvelope>(raw, JasonJson.Options)
        ?? throw new JsonException("it was empty");
}
catch (Exception ex) when (ex is JsonException or IOException or ArgumentException or NotSupportedException)
{
    await Diagnostics.WriteAsync($"the launch envelope could not be read: {ex.Message}");
    return UnreadableEnvelope;
}

// Resolved before the host says anything, so a run driven by a script file still announces what it really ran.
var (behaviour, options) = Behaviours.Resolve(args);

// A host launched through an execution profile has a command line the runtime composed, so argv names no
// behaviour and the brief does instead.
if (!Behaviours.Known(behaviour))
{
    var fromContext = Behaviours.FromContext(envelope);
    if (Behaviours.Known(fromContext.Behaviour))
    {
        (behaviour, options) = fromContext;
    }
}

using var api = new RuntimeApi(envelope.Runtime?.DescriptorFile ?? string.Empty);
await Diagnostics.WriteStartLineAsync(behaviour, envelope, api.ReadDescriptor()?.Token);

// From here on the host has one obligation of its own: to stop if the runtime that launched it goes away.
Watchdog.Start(envelope, api);

return await Behaviours.RunAsync(behaviour, options, envelope, raw, api);
