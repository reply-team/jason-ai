namespace Jason.Cli.Autostart;

/// <summary>
/// The codes autostart refuses with. One place, because the page prints them and a test reads them from here:
/// a code renamed in the code turns the page red rather than quietly disagreeing with it.
/// </summary>
public static class AutostartCodes
{
    /// <summary>
    /// This machine has no way to start something at logon that this product knows about — or the environment
    /// this verb was handed names no registrar, which is the same answer for a different reason and is the
    /// answer a test gets when it forgets to substitute one.
    /// </summary>
    public const string Unsupported = "autostart_unsupported";

    /// <summary>
    /// The tool that registers it refused: a logon right this account has not got, a manager that is not
    /// running, a file that cannot be written. The message carries what the tool itself said.
    /// </summary>
    public const string Refused = "autostart_refused";

    /// <summary>Both codes, for the page's guard to read rather than for anything to iterate at runtime.</summary>
    public static IReadOnlyList<string> All { get; } = [Unsupported, Refused];
}

/// <summary>Autostart refused, with the code a person sees and a script can branch on.</summary>
public sealed class AutostartException : Exception
{
    public AutostartException()
        : this(AutostartCodes.Refused, "Autostart could not be changed.")
    {
    }

    public AutostartException(string message)
        : this(AutostartCodes.Refused, message)
    {
    }

    public AutostartException(string message, Exception? innerException)
        : this(AutostartCodes.Refused, message, innerException)
    {
    }

    public AutostartException(string code, string message)
        : base(message) => Code = code;

    public AutostartException(string code, string message, Exception? innerException)
        : base(message, innerException) => Code = code;

    /// <summary>Lowercase snake_case, as every error code in this product is.</summary>
    public string Code { get; } = AutostartCodes.Refused;
}
