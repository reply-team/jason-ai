namespace Jason.PluginHost.Tests.Fixtures;

/// <summary>
/// The tests that time something, kept away from everything else in this assembly while they run: a timing taken
/// beside a dozen other tests measures the dozen.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "timing";
}
