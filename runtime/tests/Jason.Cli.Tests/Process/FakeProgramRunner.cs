using Jason.Cli.Process;

namespace Jason.Cli.Tests.Process;

/// <summary>One program a caller asked for, and never ran.</summary>
internal sealed record ProgramRequest(string Program, IReadOnlyList<string> Arguments, string? WorkingDirectory, TimeSpan Timeout);

/// <summary>
/// A program runner that runs nothing and records what it was asked for. Every guard in this repository that
/// types a documented command line does so for real, so the day a page prints a verb that runs a program, this
/// is what has to be behind it.
/// </summary>
internal sealed class FakeProgramRunner : IProgramRunner
{
    private readonly Func<ProgramRequest, ProgramResult> _answer;

    public FakeProgramRunner()
        : this(_ => new ProgramResult(0, string.Empty, string.Empty, false))
    {
    }

    public FakeProgramRunner(Func<ProgramRequest, ProgramResult> answer) => _answer = answer;

    public List<ProgramRequest> Requested { get; } = [];

    public Task<ProgramResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var request = new ProgramRequest(program, [.. arguments], workingDirectory, timeout);
        Requested.Add(request);
        return Task.FromResult(_answer(request));
    }
}
