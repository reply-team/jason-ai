using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Jason.Runtime.Configuration;
using Jason.Runtime.Hosting;
using Jason.Runtime.Logging;

namespace Jason.Runtime.Tests.Hosting;

/// <summary>
/// What the line an unhandled exception leaves names, read off exceptions thrown here. A run of the real executable that
/// dies that way is <c>LogInvariantTests</c>, in the application's tests.
/// </summary>
public sealed class UnhandledExceptionLineTests
{
    [Fact]
    public void A_frame_names_a_type_and_a_method_a_generic_type_by_its_definition_and_never_a_file_even_where_symbols_are_beside_it()
    {
        var (type, frames) = UnhandledExceptionLine.Describe(Caught(() => new Holder<string>().Throw()));

        Assert.Equal("System.InvalidOperationException", type);
        Assert.Equal(typeof(Holder<>).FullName + ".Throw", frames[0]);
        Assert.All(frames, frame => Assert.Matches(@"^[A-Za-z0-9_.`<>$|+\[\],-]+\z", frame));
    }

    [Fact]
    public void A_deep_stack_is_cut_to_the_bound_innermost_first()
    {
        var (_, frames) = UnhandledExceptionLine.Describe(Caught(() => Recurse(UnhandledExceptionLine.MaxFrames * 2)));

        Assert.Equal(UnhandledExceptionLine.MaxFrames, frames.Length);
        Assert.All(frames, frame => Assert.Equal(typeof(UnhandledExceptionLineTests).FullName + "." + nameof(Recurse), frame));
    }

    [Fact]
    public void A_thrown_object_that_is_no_exception_is_named_by_its_type_alone()
    {
        Assert.Equal(("System.String", Array.Empty<string>()), UnhandledExceptionLine.Describe("SENTINEL_NOT_AN_EXCEPTION"));
        Assert.Equal(("unknown", Array.Empty<string>()), UnhandledExceptionLine.Describe(null));
    }

    [Fact]
    public void The_line_is_written_once_without_the_message_and_the_log_is_closed_after_it()
    {
        using var dir = new TempDataDir();
        var logger = JasonLogging.Create(dir.Paths, new LoggingOptions(), console: false);
        using var line = UnhandledExceptionLine.Watch(logger);

        line.Write(Caught(() => throw new InvalidOperationException("SENTINEL_MESSAGE at C:\\made-up\\file.txt")));
        line.Write(new FormatException("SENTINEL_SECOND"));
        logger.Information("After the log was closed");

        var written = string.Join('\n', Directory.GetFiles(dir.Paths.LogsDirectory, "runtime-*.jsonl").Select(File.ReadAllText));
        var events = written.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(text => JsonNode.Parse(text)!.AsObject()).ToList();
        var only = Assert.Single(events);
        Assert.Equal(UnhandledExceptionLine.Template, (string?)only["@mt"]);
        Assert.Equal("Fatal", (string?)only["@l"]);
        Assert.Equal("System.InvalidOperationException", (string?)only["ExceptionType"]);
        Assert.NotEmpty(Assert.IsType<JsonArray>(only["Frames"]));
        Assert.Null(only["@x"]);
        Assert.DoesNotContain("SENTINEL_", written, StringComparison.Ordinal);
        Assert.DoesNotContain("made-up", written, StringComparison.Ordinal);
    }

    private static Exception Caught(Action thrower)
    {
        try
        {
            thrower();
        }
        catch (InvalidOperationException caught)
        {
            return caught;
        }

        throw new InvalidOperationException("Nothing was thrown.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Recurse(int depth)
    {
        if (depth == 0)
        {
            throw new InvalidOperationException("deep");
        }

        Recurse(depth - 1);
        GC.KeepAlive(depth);
    }

    private sealed class Holder<T>
    {
        private readonly string _name = typeof(T).Name;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Throw() => throw new InvalidOperationException(_name);
    }
}
