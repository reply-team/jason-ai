using System.Diagnostics;
using Serilog.Core;

namespace Jason.Runtime.Hosting;

/// <summary>
/// The one line a runtime writes when an exception nothing caught is ending its process, then the log closed so the
/// line is on disk before the process goes. Without it such a death left nothing in the runtime's own log, and
/// <c>jason logs</c> and <c>jason doctor</c> showed a runtime that had simply stopped.
/// </summary>
/// <remarks>
/// <para>
/// The line carries the exception's type and the methods it was thrown through, innermost first, each as its type's
/// full name and the method's name. Never the message, which can quote a path, a statement or a person's text, and never
/// a frame's file and line: a file path in a frame is where the build was made, not a path this installation runs from,
/// so nothing a diagnostic bundle rewrites would rewrite it. At most <see cref="MaxFrames"/> frames are written.
/// </para>
/// <para>
/// It does not try to keep the process alive: the event is raised when the process is already ending. Only the runtime
/// mode watches for it, from the moment its log exists until the log is closed; the first exception is written and any
/// that follows finds the log closed.
/// </para>
/// </remarks>
internal sealed class UnhandledExceptionLine : IDisposable
{
    /// <summary>The template of the line.</summary>
    internal const string Template = "The runtime is ending on an unhandled {ExceptionType}; its stack, innermost frame first: {Frames}";

    /// <summary>How many frames the line carries at most, so one line stays one line.</summary>
    internal const int MaxFrames = 64;

    private readonly Logger _logger;
    private int _written;

    private UnhandledExceptionLine(Logger logger) => _logger = logger;

    /// <summary>Watches this process for an unhandled exception until disposed, and writes it to <paramref name="logger"/>.</summary>
    internal static UnhandledExceptionLine Watch(Logger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        var line = new UnhandledExceptionLine(logger);
        AppDomain.CurrentDomain.UnhandledException += line.OnUnhandled;
        return line;
    }

    public void Dispose() => AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;

    /// <summary>Writes the line for <paramref name="thrown"/> once, then closes the log so it is on disk.</summary>
    internal void Write(object? thrown)
    {
        if (Interlocked.Exchange(ref _written, 1) != 0)
        {
            return;
        }

        var (type, frames) = Describe(thrown);
        _logger.Fatal(Template, type, frames);
        _logger.Dispose();
    }

    /// <summary>An exception's type and its frames as the line names them.</summary>
    internal static (string Type, string[] Frames) Describe(object? thrown)
    {
        if (thrown is null)
        {
            return ("unknown", []);
        }

        var type = NameOf(thrown.GetType());
        if (thrown is not Exception exception)
        {
            return (type, []);
        }

        var frames = new StackTrace(exception, fNeedFileInfo: false).GetFrames()
            .Select(frame => frame.GetMethod())
            .Select(method => method is null ? "unknown" : (method.DeclaringType is { } declaring ? NameOf(declaring) + "." : "") + method.Name)
            .Take(MaxFrames)
            .ToArray();
        return (type, frames);
    }

    /// <summary>A type's full name, a constructed generic by its definition so no assembly-qualified argument comes with it.</summary>
    private static string NameOf(Type type)
    {
        var named = type.IsGenericType && !type.IsGenericTypeDefinition ? type.GetGenericTypeDefinition() : type;
        return named.FullName ?? named.Name;
    }

    private void OnUnhandled(object sender, UnhandledExceptionEventArgs args) => Write(args.ExceptionObject);
}
