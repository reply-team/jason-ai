using System.Text;

namespace Jason.PluginHost.Tests.Fixtures;

/// <summary>
/// A stream the mode writes to that a test reads while the mode is still running: what the host has said so far, before
/// it has returned. A <see cref="StringWriter"/> cannot be read safely while another thread writes to it.
/// </summary>
public sealed class WatchedWriter : TextWriter
{
    private readonly Lock _gate = new();
    private readonly StringBuilder _text = new();

    public override Encoding Encoding => Encoding.UTF8;

    /// <summary>Everything written so far.</summary>
    public string Text
    {
        get
        {
            lock (_gate)
            {
                return _text.ToString();
            }
        }
    }

    public override void Write(char value)
    {
        lock (_gate)
        {
            _text.Append(value);
        }
    }

    public override void Write(string? value)
    {
        lock (_gate)
        {
            _text.Append(value);
        }
    }

    public override void Write(char[] buffer, int index, int count)
    {
        lock (_gate)
        {
            _text.Append(buffer, index, count);
        }
    }

    public override Task WriteAsync(string? value)
    {
        Write(value);
        return Task.CompletedTask;
    }

    public override Task FlushAsync() => Task.CompletedTask;

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Waits until <paramref name="text"/> has been written. A run that ends first never will, and the failure says what
    /// was written by then; so does a run still silent after thirty seconds, which no test here comes near.
    /// </summary>
    public async Task SeenAsync(string text, Task run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (!Text.Contains(text, StringComparison.Ordinal))
        {
            if (run.IsCompleted && !Text.Contains(text, StringComparison.Ordinal))
            {
                Assert.Fail($"The run ended without writing {text}. It wrote: {Text}");
            }

            if (waited.Elapsed > TimeSpan.FromSeconds(30))
            {
                Assert.Fail($"The run had not written {text} after {waited.Elapsed.TotalSeconds:0} s. It wrote: {Text}");
            }

            await Task.Delay(20, cancellationToken);
        }
    }
}
