using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Plugins;
using Jint;
using Jint.Native;

namespace Jason.PluginHost.Sdk;

/// <summary>
/// <c>host.exec</c>: starts one of the programs the manifest declared and the user granted, at the path the
/// runtime resolved, with an argument array and never a shell — nothing a plugin writes is ever parsed by
/// <c>cmd</c> or <c>sh</c>. The call blocks until the program ends, its timeout kills the whole tree, and its
/// output is captured up to a cap with the truncation flagged rather than hidden.
/// </summary>
public sealed partial class ExecService(HostServices services)
{
    public const string Function = "host.exec";

    public const int MaxExecutableLength = 256;
    public const int MaxArgs = 256;
    public const int MaxArgsBytes = 1_048_576;
    public const int MaxStdinBytes = 1_048_576;
    public const int MaxEnv = 32;
    public const int MaxEnvValueBytes = 4096;

    /// <summary>How long a killed process is given to actually go away before the host stops waiting for it.</summary>
    private static readonly TimeSpan KillGrace = TimeSpan.FromSeconds(5);

    public static IReadOnlySet<string> Allowed { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "executable", "args", "stdin", "timeout_ms", "env" };

    public JsValue Invoke(Engine engine, JsValue[] args)
    {
        ArgumentNullException.ThrowIfNull(engine);

        var options = ArgumentReader.Options(engine, args, Function, Allowed);
        var name = ArgumentReader.RequiredString(engine, options, "executable", Function, MaxExecutableLength);
        var granted = services.Grants.Exec;
        if (granted is null)
        {
            throw new HostRuleException(
                OutcomeCodes.CapabilityNotGranted,
                "This plugin was not granted the exec capability.",
                new JsonObject { ["capability"] = "exec", ["requested"] = name });
        }

        // A name, never a path: the runtime decided what this name means at reload, and the child does not look
        // anything up for itself.
        var executable = name.AsSpan().IndexOfAny('/', '\\', ':') >= 0
            ? null
            : granted.Executables.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));

        if (executable is null)
        {
            throw new HostRuleException(
                OutcomeCodes.ExecutableNotAllowed,
                $"'{name}' is not one of the executables this plugin was granted.",
                new JsonObject { ["executable"] = name });
        }

        var arguments = ArgumentReader.OptionalStrings(engine, options, "args", Function, MaxArgs, MaxArgsBytes);
        var stdin = ArgumentReader.OptionalString(engine, options, "stdin", Function, MaxStdinBytes);
        var environment = ReadEnvironment(engine, options);
        var timeout = Budget(engine, options);

        services.Budget.Exec();
        return JsJson.FromJson(engine, Run(executable, arguments, stdin, environment, timeout));
    }

    private TimeSpan Budget(Engine engine, JsonObject options)
    {
        var remaining = services.Remaining();
        var asked = ArgumentReader.OptionalInt(engine, options, "timeout_ms", Function, 1, services.Limits.TimeoutMs);
        var wanted = asked is null ? remaining : TimeSpan.FromMilliseconds(asked.Value);
        return wanted < remaining ? wanted : remaining;
    }

    private static IReadOnlyList<KeyValuePair<string, string>> ReadEnvironment(Engine engine, JsonObject options)
    {
        var environment = ArgumentReader.OptionalStringMap(engine, options, "env", Function, MaxEnv, MaxEnvValueBytes);
        foreach (var (name, _) in environment)
        {
            // Naming a variable is naming what runs: a loader or interpreter hook would let the plugin choose the
            // code executed inside a program the user granted, which is not the permission the user gave.
            if (!VariableName().IsMatch(name) || !BaseEnvironment.MayAPluginSet(name))
            {
                throw ArgumentReader.TypeError(engine, $"{Function}: '{name}' is not a variable a plugin may set.");
            }
        }

        return environment;
    }

    private JsonObject Run(
        ExecutableGrant executable,
        IReadOnlyList<string> arguments,
        string? stdin,
        IReadOnlyList<KeyValuePair<string, string>> environment,
        TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        var startInfo = new ProcessStartInfo(executable.Path)
        {
            WorkingDirectory = services.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // The child inherits this process's environment, which the runtime already minimised; these are the
        // extra variables the plugin asked for, and they live for this one child.
        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new IOException($"Starting '{executable.Name}' produced no process.");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            // A granted executable that will not start is a fact about this machine, not a broken rule: the
            // plugin is told, and decides for itself what kind of failure that is.
            services.Diagnostics.Host(
                "warn",
                "exec_launch_failed",
                new JsonObject { ["executable"] = executable.Name, ["detail"] = ex.Message });

            return new JsonObject
            {
                ["exit_code"] = -1,
                ["stdout"] = string.Empty,
                ["stderr"] = ex.Message,
                ["truncated"] = new JsonObject { ["stdout"] = false, ["stderr"] = false },
                ["timed_out"] = false,
                ["duration_ms"] = watch.ElapsedMilliseconds,
            };
        }

        using (process)
        {
            var stdout = new BoundedCapture(services.Limits.Exec.OutputBytes);
            var stderr = new BoundedCapture(services.Limits.Exec.OutputBytes);

            // Drained from the first moment: a child that talks before it reads would otherwise fill its output
            // buffer and wait forever for a reader that is itself waiting to finish writing.
            var pumps = Task.WhenAll(
                stdout.DrainAsync(process.StandardOutput.BaseStream, CancellationToken.None),
                stderr.DrainAsync(process.StandardError.BaseStream, CancellationToken.None));
            var written = WriteStdinAsync(process, stdin);

            var timedOut = false;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(services.Deadline))
            {
                deadline.CancelAfter(timeout);
                try
                {
                    process.WaitForExitAsync(deadline.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    timedOut = true;
                    TryKill(process);
                    WaitQuietly(process);
                }
            }

            // After a kill the wait for the pipes is bounded by the same grace. A program may leave something
            // running that inherited its standard handles, and the read end only reports the end of the stream
            // once every writer has let go of it: waiting for that would hold this call open indefinitely for a
            // program that has already been ended. What was captured by then is what the plugin is told.
            Settle(pumps, timedOut);
            Settle(written, timedOut);

            var exitCode = ExitCodeOf(process);
            var result = new JsonObject
            {
                ["exit_code"] = exitCode,
                ["stdout"] = stdout.Text,
                ["stderr"] = stderr.Text,
                ["truncated"] = new JsonObject { ["stdout"] = stdout.Truncated, ["stderr"] = stderr.Truncated },
                ["timed_out"] = timedOut,
                ["duration_ms"] = watch.ElapsedMilliseconds,
            };

            services.Diagnostics.Host(
                "info",
                "exec",
                new JsonObject
                {
                    ["executable"] = executable.Name,
                    ["args"] = new JsonArray([.. arguments.Select(argument => JsonValue.Create(argument))]),
                    ["exit_code"] = exitCode,
                    ["timed_out"] = timedOut,
                    ["duration_ms"] = watch.ElapsedMilliseconds,
                    ["truncated"] = stdout.Truncated || stderr.Truncated,
                });

            return result;
        }
    }

    private static async Task WriteStdinAsync(Process process, string? stdin)
    {
        try
        {
            if (stdin is not null)
            {
                await process.StandardInput.WriteAsync(stdin).ConfigureAwait(false);
            }

            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The child exited before reading its input. That is its own answer, delivered as an exit code.
        }
        catch (ObjectDisposedException)
        {
            // Same story, seen from the other side of an already-closed pipe.
        }
    }

    /// <summary>
    /// Waits for one of the pipe tasks, giving up after the kill grace when the program has already been ended.
    /// An abandoned task is left to finish on its own — its pipe ends when the last writer does — and its
    /// failure is observed there rather than thrown here, where there is no longer anyone to tell.
    /// </summary>
    private static void Settle(Task work, bool bounded)
    {
        if (bounded)
        {
            Task.WhenAny(work, Task.Delay(KillGrace)).GetAwaiter().GetResult();
            if (!work.IsCompleted)
            {
                work.ContinueWith(
                    static abandoned => _ = abandoned.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
                return;
            }
        }

        work.GetAwaiter().GetResult();
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already gone between the timeout and the kill; there is nothing left to end.
        }
        catch (Win32Exception)
        {
            // The operating system refused the kill — the process is exiting, or no longer ours to end.
        }
        catch (NotSupportedException)
        {
            // Killing a whole tree is not available here; the process itself is handled above.
        }
        catch (AggregateException)
        {
            // Part of the tree could not be ended; the program the plugin started is what matters.
        }
    }

    private static void WaitQuietly(Process process)
    {
        try
        {
            process.WaitForExit((int)KillGrace.TotalMilliseconds);
        }
        catch (SystemException)
        {
            // It is gone, or it will not go; either way the call is over and the plugin is told it timed out.
        }
    }

    private static int ExitCodeOf(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex VariableName();
}
