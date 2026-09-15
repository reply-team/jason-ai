using System.Globalization;

// A stand-in for a vendor CLI. The first argument names the behaviour; everything a plugin's host.exec has to
// survive — output floods, slow programs, non-zero exits, an environment it cannot see — is one of these, so no
// test has to install a real tool to prove the rules.
const int UnknownBehaviour = 2;

if (args.Length == 0)
{
    await Console.Error.WriteLineAsync("fake-cli: unknown behaviour");
    return UnknownBehaviour;
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

    case "spew" when args.Length > 1 && int.TryParse(args[1], CultureInfo.InvariantCulture, out var bytes):
        {
            var text = new string('x', bytes);
            var writer = args.Length > 2 && args[2] == "stderr" ? Console.Error : Console.Out;
            await writer.WriteAsync(text);
            await writer.FlushAsync();
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
