using Jason.Contracts.Api;
using Jason.Contracts.Update;

namespace Jason.Runtime.Update;

/// <summary>
/// What the last successful check learned, held in memory and replaced whole. Nothing here is persisted: a fact
/// about a web page is not runtime state, a stale one on disk would outlive its truth, and a restart checks
/// again after the initial delay anyway. A failed check leaves what was there.
/// </summary>
public sealed class UpdateAdvertisement
{
    private UpdateInfo? _current;

    /// <summary>Null until a check has succeeded.</summary>
    public UpdateInfo? Current => Volatile.Read(ref _current);

    public void Record(UpdateManifest manifest, SemanticVersion running, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Volatile.Write(ref _current, new UpdateInfo(manifest.IsNewerThan(running), manifest.Version.ToString(), at, manifest.ReleaseNotesUrl));
    }
}
