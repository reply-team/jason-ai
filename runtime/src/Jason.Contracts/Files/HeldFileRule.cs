using System.Globalization;

namespace Jason.Contracts.Files;

/// <summary>
/// How long a file another program holds open is tried again before the act that needs it gives up, and how often.
/// </summary>
/// <param name="Wait">
/// How long after the first refusal the file is still tried, so a first try slow in itself does not spend the wait before
/// the first pause. The try that finds it held at that moment is the last.
/// </param>
/// <param name="Interval">The pause between two tries.</param>
/// <param name="AccessDeniedCounts">
/// Whether an access-denied counts as the file being held. Windows refuses to replace a file a reader holds with an
/// access-denied rather than a sharing violation, so replacing the endpoint descriptor counts it, and so does a rename of
/// a directory with a file in it held open, as a plugin package's moves count it. Anywhere else an
/// access-denied is a permission or a read-only file, and waiting does not change either.
/// </param>
public sealed record HeldFileRule(TimeSpan Wait, TimeSpan Interval, bool AccessDeniedCounts)
{
    /// <summary>The wait as a sentence says it: "10 seconds", "2 seconds", "1 second".</summary>
    /// <remarks>Every sentence that names a wait takes its number from here, so the words and the rule cannot drift apart.</remarks>
    public string WaitInWords =>
        Wait == TimeSpan.FromSeconds(1)
            ? "1 second"
            : Wait.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " seconds";
}
