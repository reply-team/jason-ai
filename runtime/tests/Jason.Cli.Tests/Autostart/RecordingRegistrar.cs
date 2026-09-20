using Jason.Cli.Autostart;

namespace Jason.Cli.Tests.Autostart;

/// <summary>
/// The machine, as far as autostart ever needs one: what was registered, how often, and what it answers when
/// it is asked. Nothing here touches the operating system, which is the point — the real registrars act on the
/// machine whatever data directory a test is pointed at.
/// </summary>
public sealed class RecordingRegistrar(AutostartPlatform platform = AutostartPlatform.Windows) : IAutostartRegistrar
{
    /// <summary>Every registration applied to this machine, in order. Two of them means `enable` registered twice.</summary>
    public List<AutostartRegistration> Applied { get; } = [];

    /// <summary>How many times something was taken away, including the times there was nothing to take.</summary>
    public int Removals { get; private set; }

    /// <summary>What this machine holds, which `enable` and `disable` change and `status` reads.</summary>
    public AutostartState State { get; set; } = new(false, [], null);

    /// <summary>Something for the next call to throw, for the tests about a tool that refuses.</summary>
    public AutostartException? Refuses { get; set; }

    public AutostartPlatform Platform => platform;

    public AutostartState Read() => Refuses is null ? State : throw Refuses;

    public void Apply(AutostartRegistration registration)
    {
        if (Refuses is not null)
        {
            throw Refuses;
        }

        ArgumentNullException.ThrowIfNull(registration);

        // A machine holds one registration per account: a second `enable` replaces what the first left.
        Applied.Add(registration);
        State = new AutostartState(true, registration.Run, registration.ArtifactPath);
    }

    public void Remove(AutostartRegistration registration)
    {
        if (Refuses is not null)
        {
            throw Refuses;
        }

        Removals++;
        State = new AutostartState(false, [], null);
    }
}
