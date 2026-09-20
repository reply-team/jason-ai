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

    /// <summary>Takes it away. Removing what is not there is not an error.</summary>
    void Remove(AutostartRegistration registration);
}
