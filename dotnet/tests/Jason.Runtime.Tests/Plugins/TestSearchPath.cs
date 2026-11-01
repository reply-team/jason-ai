using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>A search path a test owns outright, so resolving an executable never depends on the machine.</summary>
public sealed class TestSearchPath : ISearchPath
{
    public string? Path { get; set; }

    public string? PathExt { get; set; } = OperatingSystem.IsWindows() ? ".COM;.EXE;.BAT;.CMD" : null;
}
