using Jason.Cli.Doctor;
using Jason.Cli.Uninstall;
using Jason.Contracts.Files;

namespace Jason.Cli.Update;

/// <summary>
/// A move of the executable during an update or a rollback, tried again while another program holds the file it moves:
/// a scanner reading a file that was just written or just run.
/// </summary>
/// <remarks>
/// <para>
/// The rule is the uninstall's own (<see cref="InstallationRemovers.HeldFiles"/>), so the two agree on what a held file is
/// and how long it is waited for. Only a sharing or lock violation is a hold. An access-denied is not, because what refuses
/// a move here with one is a permission, a read-only directory, or a program still running from the file the move would
/// replace, and waiting changes none of them.
/// </para>
/// <para>
/// Only the file being moved is named, because only its hold reaches the wait: every move here goes where nothing is, or,
/// where it replaces a file, a hold on that file is refused as an access-denied.
/// </para>
/// </remarks>
internal static class ExecutableMove
{
    /// <summary>
    /// Runs the move under the rule. A refusal that is not a hold escapes as it came, for the step's own handling; a file
    /// still held at the end of the wait is refused here.
    /// </summary>
    /// <param name="env">Whose home directory the refusal writes the path from.</param>
    /// <param name="rule">How long, and how often.</param>
    /// <param name="clock">What the wait is measured by.</param>
    /// <param name="pause">How a pause is taken.</param>
    /// <param name="what">The step, as its every refusal begins: "moving the installed executable aside".</param>
    /// <param name="source">The file the move moves.</param>
    /// <param name="remedy">What to do about a file that stayed held.</param>
    /// <param name="move">The one rename.</param>
    /// <exception cref="UpdateException">
    /// <c>update_file_refused</c>, retryable, when the file was still held at the end of the wait: the file from the home
    /// directory and how long it was tried, without the operating system's message, which repeats the absolute path and says
    /// nothing the sentence does not.
    /// </exception>
    public static void Run(
        CliEnvironment env,
        HeldFileRule rule,
        TimeProvider clock,
        Action<TimeSpan, CancellationToken> pause,
        string what,
        string source,
        string remedy,
        Action move)
    {
        ArgumentNullException.ThrowIfNull(env);
        if (HeldFileRetry.Run(move, rule, clock, pause) is { } held)
        {
            var sentence = new HeldOpen(source, isDirectory: false, rule, held).Said(IdentityFacts.HomeFor(env.Identity));
            throw new UpdateException(UpdateCodes.FileRefused, $"{what} failed: {sentence} {remedy}", held, retryable: true);
        }
    }
}
