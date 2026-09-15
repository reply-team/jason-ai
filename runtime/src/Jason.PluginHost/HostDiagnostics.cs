using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;

namespace Jason.PluginHost;

/// <summary>
/// Stderr, as JSON Lines. Both the host's own diagnostics and the plugin's <c>host.log</c> lines go through here,
/// distinguished by <c>source</c>, so there is exactly one place where a line is redacted, capped and counted —
/// and exactly one channel a plugin can write to, since stdout belongs to the outcome alone.
/// </summary>
public sealed class HostDiagnostics(
    TextWriter stderr,
    Redactor redactor,
    LogLimits limits,
    string pluginId,
    string invocationId,
    TimeProvider clock)
{
    public const string HostSource = "host";
    public const string PluginSource = "plugin";

    /// <summary>The one line written when the log budget is spent, after which the log is silent.</summary>
    public const string TruncationNotice = "log_truncated";

    private const int MaxMessageLength = 4096;
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private readonly Lock _gate = new();
    private long _written;
    private bool _silenced;

    /// <summary>How many lines the plugin itself wrote; the host's own lines are not the plugin's business.</summary>
    public int LogLines { get; private set; }

    /// <summary>
    /// Masks what this invocation's granted variables hold, for text that is about to leave the process by
    /// another door than a log line — an outcome's message, an error's details. One redactor, one rule.
    /// </summary>
    public string Redact(string text) => redactor.Redact(text);

    public void Host(string level, string message, JsonNode? data = null) => Write(HostSource, level, message, data);

    public void Plugin(string level, string message, JsonNode? data = null) => Write(PluginSource, level, message, data);

    private void Write(string source, string level, string message, JsonNode? data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(level);
        ArgumentNullException.ThrowIfNull(message);

        lock (_gate)
        {
            if (source == PluginSource)
            {
                LogLines++;
            }

            if (_silenced)
            {
                return;
            }

            var truncated = false;
            var text = redactor.Redact(message);
            if (text.Length > MaxMessageLength)
            {
                text = text[..MaxMessageLength];
                truncated = true;
            }

            JsonNode? payload = null;
            if (data is not null)
            {
                var serialised = redactor.Redact(data.ToJsonString());
                if (serialised.Length > limits.LineBytes)
                {
                    payload = JsonValue.Create(serialised[..limits.LineBytes]);
                    truncated = true;
                }
                else
                {
                    payload = JsonNode.Parse(serialised);
                }
            }

            var line = new JsonObject
            {
                ["ts"] = clock.GetUtcNow().UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture),
                ["level"] = level,
                ["source"] = source,
                ["plugin"] = pluginId,
                ["invocation_id"] = invocationId,
                ["message"] = text,
            };

            if (payload is not null)
            {
                line["data"] = payload;
            }

            if (truncated)
            {
                line["truncated"] = true;
            }

            _written += WriteLine(line);
            if (_written >= limits.TotalBytes)
            {
                _silenced = true;
                WriteLine(new JsonObject
                {
                    ["ts"] = clock.GetUtcNow().UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture),
                    ["level"] = "warn",
                    ["source"] = HostSource,
                    ["plugin"] = pluginId,
                    ["invocation_id"] = invocationId,
                    ["message"] = TruncationNotice,
                });
            }
        }
    }

    private int WriteLine(JsonObject line)
    {
        var rendered = line.ToJsonString();
        stderr.WriteLine(rendered);
        stderr.Flush();
        return rendered.Length + 1;
    }
}
