namespace Jason.Cli;

/// <summary>An exception and everything it wraps, in words.</summary>
/// <remarks>
/// The outermost message is often the least useful one. A type initializer that fails says only that it failed,
/// and what failed is two exceptions down: a published build once answered an uninstall with exactly "The type
/// initializer for 'System.Text.Json.JsonSerializer' threw an exception." — after it had removed everything —
/// and the one line that would have said why was a <see cref="FileNotFoundException"/> whose message was empty
/// and whose file name was the assembly it could no longer load.
/// </remarks>
public static class Causes
{
    /// <summary>What each exception wrapped by <paramref name="exception"/> says, innermost last.</summary>
    public static IEnumerable<string> Within(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var cause = exception.InnerException; cause is not null; cause = cause.InnerException)
        {
            yield return Said(cause);
        }
    }

    /// <summary>The whole chain on one line, outermost first.</summary>
    public static string Line(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return string.Join(", because ", Within(exception).Prepend(exception.Message.TrimEnd('.'))) + ".";
    }

    /// <summary>One exception: its type, and its message — or the file it names, where it says nothing else.</summary>
    private static string Said(Exception cause)
    {
        var message = cause.Message.Length == 0 && cause is FileNotFoundException { FileName: { Length: > 0 } file }
            ? $"could not load '{file}'"
            : cause.Message;
        return $"{cause.GetType().Name}: {message}".TrimEnd(' ', ':');
    }
}
