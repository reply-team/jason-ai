using Jason.Cli;
using Jason.Cli.Link;
using Jason.Cli.Process;

namespace Jason.Cli.Tests.Link;

/// <summary>A registration that names the program it was given, read by the same presence check as a real one.</summary>
internal sealed class FakeRegistration(string? program) : IDesktopRegistration
{
    public string? Program() => program;

    /// <summary>Jason Desktop's program, made under the run's own folder in a folder with a space, and registered.</summary>
    public static FakeRegistration Present(CliEnvironment env)
    {
        var program = System.IO.Path.Combine(env.Paths.Root, "Jason Desktop", "Jason.exe");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(program)!);
        File.WriteAllText(program, "not a program");
        return new FakeRegistration(program);
    }
}

/// <summary>Hands an address on with the outcome it was given, and records every address it was given either way.</summary>
internal sealed class HandingOpener(UrlHandOff outcome = UrlHandOff.Handed) : IUrlOpener
{
    public List<string> Urls { get; } = [];

    public bool Open(string url)
    {
        Urls.Add(url);
        return true;
    }

    public UrlHandOff HandOn(string url, TimeSpan wait)
    {
        Assert.True(wait > TimeSpan.Zero);
        Urls.Add(url);
        return outcome;
    }
}
