namespace Jason.Cli.Autostart;

/// <summary>
/// The one place autostart touches the operating system: the commands are run here, the document is written
/// here, and nothing else in this product knows how a logon task, a LaunchAgent or a user unit is made.
/// </summary>
/// <remarks>
/// <b>This seam is defaulted the other way round from the three beside it.</b> An HTTP handler, a process table
/// and an install path are all safe to default to the real thing in a test, because a real one points at a
/// temporary directory or an unreachable host — a real registrar acts on the machine whatever the data
/// directory says. Two guards in this repository type page lines for real, so the fail-closed answer is the
/// only safe one: <see cref="CliEnvironment.Default"/> names this machine's registrar explicitly, and an
/// environment that names none gets <see cref="AutostartRegistrars.Unsupported"/>.
/// </remarks>
public interface IAutostartRegistrar
{
    /// <summary>Which shape this machine registers, or <see cref="AutostartPlatform.Unsupported"/>.</summary>
    AutostartPlatform Platform { get; }

    /// <summary>What this account has registered, read from the machine rather than composed again.</summary>
    AutostartState Read();

    /// <summary>Puts the registration in place, replacing whatever this account had registered before.</summary>
    void Apply(AutostartRegistration registration);

    /// <summary>Takes it away, and says what went. Removing what is not there is not an error.</summary>
    AutostartRemoval Remove(AutostartRegistration registration);
}

/// <summary>What a removal took away, so that whatever reports it says what really happened.</summary>
/// <param name="Registration">Whether this account's registration was there and was removed by this call.</param>
/// <param name="Legacy">
/// Whether this call removed the Windows task 0.1.0 registered for this account, which nothing reads beforehand: a
/// plan that could not see it is followed by a report that must still say it went.
/// </param>
public sealed record AutostartRemoval(bool Registration, bool Legacy);
