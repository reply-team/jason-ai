using System.Globalization;
using System.Text.Json.Nodes;

namespace Jason.PluginHost;

/// <summary>
/// The kills a timed-out <c>host.exec</c> started and stopped waiting for when its grace ran out. Each is still ending
/// its program and holds that program's process until it has, and the host does not exit before every one has ended:
/// an exit would cut a kill short and leave its program running where the runtime's own kill no longer reaches it.
/// </summary>
public sealed class UnfinishedKills
{
    /// <summary>The line said at the invocation's deadline for each kill still running then.</summary>
    public const string Message = "exec_kill_unfinished";

    private readonly Lock _gate = new();
    private readonly List<(int Pid, Task Kill)> _kills = [];

    /// <summary>A kill its call stopped waiting for, and the process id of the program it is ending.</summary>
    public void Add(int pid, Task kill)
    {
        ArgumentNullException.ThrowIfNull(kill);
        lock (_gate)
        {
            _kills.Add((pid, kill));
        }
    }

    /// <summary>
    /// Waits for every kill to end. Until the deadline it waits without a word; at the deadline each kill still running is
    /// said once, as an <see cref="Message"/> line with its process id and the deadline, and the wait goes on with no end
    /// of its own. A kill that failed has ended all the same and is not waited for any longer, as a kill that threw was
    /// never waited for before: the host has no other way to end what its own kill could not.
    /// </summary>
    public async Task WaitAsync(DateTimeOffset deadline, TimeProvider clock, HostDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(diagnostics);

        (int Pid, Task Kill)[] kills;
        lock (_gate)
        {
            kills = [.. _kills];
        }

        if (kills.Length == 0)
        {
            return;
        }

        var all = Task.WhenAll(kills.Select(entry => entry.Kill));
        var left = deadline - clock.GetUtcNow();
        if (left > TimeSpan.Zero)
        {
            using var quiet = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await Task.WhenAny(all, Task.Delay(left, clock, quiet.Token)).ConfigureAwait(false);
            await quiet.CancelAsync().ConfigureAwait(false);
        }

        foreach (var (pid, _) in kills.Where(entry => !entry.Kill.IsCompleted))
        {
            diagnostics.Host(
                "warn",
                Message,
                new JsonObject
                {
                    ["pid"] = pid,
                    ["deadline"] = deadline.UtcDateTime.ToString(InvocationRunner.DeadlineFormat, CultureInfo.InvariantCulture),
                });
        }

        await all.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}
