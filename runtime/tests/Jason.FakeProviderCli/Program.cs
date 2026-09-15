using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

// A stand-in for a vendor CLI. The first argument names the behaviour; everything a plugin's host.exec has to
// survive — output floods, slow programs, non-zero exits, an environment it cannot see — is one of these, so no
// test has to install a real tool to prove the rules.
const int UnknownBehaviour = 2;

if (args.Length == 0)
{
    await Console.Error.WriteLineAsync("fake-cli: unknown behaviour");
    return UnknownBehaviour;
}

// The other half of the program: a provider account held in a directory, which is what the canonical operations
// are implemented against. A plugin is given the directory by its binding and nothing else.
if (args is ["--workspace", var workspaceRoot, .. var subcommand] && subcommand.Length > 0)
{
    return await Workspace.RunAsync(workspaceRoot, subcommand);
}

switch (args[0])
{
    case "echo-args":
        foreach (var argument in args[1..])
        {
            await Console.Out.WriteLineAsync(argument);
        }

        return 0;

    case "print-env" when args.Length > 1:
        await Console.Out.WriteLineAsync(Environment.GetEnvironmentVariable(args[1]) ?? "<unset>");
        return 0;

    case "sleep" when args.Length > 1 && int.TryParse(args[1], CultureInfo.InvariantCulture, out var milliseconds):
        await Task.Delay(milliseconds);
        await Console.Out.WriteLineAsync("slept");
        return 0;

    case "exit" when args.Length > 1 && int.TryParse(args[1], CultureInfo.InvariantCulture, out var code):
        await Console.Error.WriteLineAsync("exiting");
        return code;

    case "stderr-exit" when args.Length > 2 && int.TryParse(args[1], CultureInfo.InvariantCulture, out var stderrExitCode):
        // A last word on stderr, chosen by the caller: whoever reads that line can be handed one it did not
        // expect, which is what a program that broke in an unforeseen way leaves behind.
        await Console.Error.WriteLineAsync(args[2]);
        return stderrExitCode;

    case "spew" when args.Length > 1 && int.TryParse(args[1], CultureInfo.InvariantCulture, out var bytes):
        {
            var text = new string('x', bytes);
            var writer = args.Length > 2 && args[2] == "stderr" ? Console.Error : Console.Out;
            await writer.WriteAsync(text);
            await writer.FlushAsync();
            return 0;
        }

    case "spew-orphan" when args.Length > 2 && int.TryParse(args[1], CultureInfo.InvariantCulture, out var floodBytes):
        {
            // Both at once: the middle process is waited for rather than left to run, so that by the time the
            // flood starts the helper it left behind is certainly running and certainly out of reach of a tree
            // kill. Whoever reads this stream can end the flood; what holds the pipe open outlives that.
            using (var middle = StartSelf("spawn-orphan-inner", args[2]))
            {
                middle?.WaitForExit();
            }

            await Console.Out.WriteAsync(new string('x', floodBytes));
            await Console.Out.FlushAsync();
            return 0;
        }

    case "spawn-orphan" when args.Length > 1:
        // Leaves a program behind that this process is no longer an ancestor of: the middle one starts the
        // lingering one and exits at once, so killing this process tree never reaches it. It still holds the
        // standard handles it inherited, which is how a helper a program left behind keeps a caller's pipes
        // open long after that program is gone.
        StartSelf("spawn-orphan-inner", args[1])?.Dispose();
        await Task.Delay(Timeout.Infinite);
        return 0;

    case "spawn-orphan-inner" when args.Length > 1:
        StartSelf("hold", args[1])?.Dispose();
        return 0;

    case "hold" when args.Length > 1 && int.TryParse(args[1], CultureInfo.InvariantCulture, out var holdMs):
        // Writes nothing and reads nothing: the point is only that the inherited handles stay open.
        await Task.Delay(holdMs);
        return 0;

    case "outcome-orphan" when args.Length > 1:
        {
            // A child that keeps the protocol to the letter and still leaves something behind. The envelope is
            // read so that the answer belongs to this invocation, a helper is started through a middle process
            // that exits at once — so no kill of this tree could reach it — and it holds the inherited handles
            // long after this program is gone. An update notifier left running by a vendor CLI does exactly
            // this, and the answer is already written when it happens.
            var envelope = await Console.In.ReadToEndAsync();
            using var request = JsonDocument.Parse(envelope);
            var invocationId = request.RootElement.GetProperty("invocation_id").GetString();

            using (var middle = StartSelf("spawn-orphan-inner", args[1]))
            {
                middle?.WaitForExit();
            }

            await Console.Out.WriteAsync(
                """
                {"protocol_version":1,"invocation_id":"INVOCATION","status":"succeeded","result":{"ok":true},"external_ids":null,"error":null,"diagnostics":{"duration_ms":1,"exec_calls":0,"http_calls":0,"log_lines":0}}
                """.Replace("INVOCATION", invocationId, StringComparison.Ordinal));
            await Console.Out.FlushAsync();
            return 0;
        }

    case "stdin-length":
        {
            var read = await Console.In.ReadToEndAsync();
            await Console.Out.WriteLineAsync(read.Length.ToString(CultureInfo.InvariantCulture));
            return 0;
        }

    case "stdout" when args.Length > 1:
        await Console.Out.WriteAsync(args[1]);
        await Console.Out.FlushAsync();
        return 0;

    case "--version":
    case "-v":
    case "-V":
    case "version":
        Record(args);
        await Console.Out.WriteLineAsync("fake-cli 1.2.3");
        return 0;

    default:
        await Console.Error.WriteLineAsync("fake-cli: unknown behaviour");
        return UnknownBehaviour;
}

// This program again, with the standard handles it was given rather than pipes of its own: no redirection, so
// the new process inherits them, and no console of its own, so nothing replaces them.
static Process? StartSelf(params string[] arguments)
{
    var host = Environment.ProcessPath!;
    var info = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true };

    // Started through the muxer, the program is an argument rather than the executable.
    if (string.Equals(Path.GetFileNameWithoutExtension(host), "dotnet", StringComparison.OrdinalIgnoreCase))
    {
        info.ArgumentList.Add(System.Reflection.Assembly.GetEntryAssembly()!.Location);
    }

    foreach (var argument in arguments)
    {
        info.ArgumentList.Add(argument);
    }

    return Process.Start(info);
}

// A version check is the one thing this program does without being asked by a test: the reload runs it. Any
// variable whose name starts with the prefix names a file the run is appended to, so a test can prove exactly
// which executables were started, with which arguments, and from where.
static void Record(string[] args)
{
    foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
    {
        var name = entry.Key as string;
        var file = entry.Value as string;
        if (name is null || file is null || !name.StartsWith("FAKE_CLI_RECORD", StringComparison.Ordinal) || file.Length == 0)
        {
            continue;
        }

        File.AppendAllText(file, string.Create(
            CultureInfo.InvariantCulture,
            $"argv={string.Join(' ', args)} cwd={Environment.CurrentDirectory}{Environment.NewLine}"));
    }
}
