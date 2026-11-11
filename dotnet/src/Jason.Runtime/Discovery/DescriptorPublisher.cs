using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Files;
using Jason.Contracts.Json;

namespace Jason.Runtime.Discovery;

/// <summary>What became of the endpoint descriptor at a stop.</summary>
public enum DescriptorRemoval
{
    /// <summary>It is gone, or was never there.</summary>
    Removed,

    /// <summary>Another program held it for the whole of the wait.</summary>
    Held,

    /// <summary>Removing it was refused at once: an access-denied, a read-only file or a permission.</summary>
    Refused,
}

/// <summary>
/// Publishes <c>run/runtime.json</c> atomically: the content is written to an owner-only temporary file in
/// the same directory, flushed to disk, then renamed over the target, so no reader ever sees a partial
/// descriptor or a token in a world-readable file.
/// </summary>
/// <remarks>
/// Every CLI command reads the descriptor, and on Windows a reader's open handle refuses the rename over it — with an
/// access-denied, not a sharing violation — and refuses its delete, for as long as the read lasts. A read is one small
/// file read at once, so each is tried again for a while before it is given up on. The wait is timed by the runtime's
/// own clock, the system's outside a test, so a test decides when it is over; each pause is still taken in real time,
/// because it is a wait for another program, which lets go in real time whatever the runtime's clock says.
/// </remarks>
public sealed class DescriptorPublisher(JasonPaths paths)
{
    /// <summary>
    /// How long the rename over the descriptor is tried while it is held: ten seconds, every 50 ms, with an access-denied
    /// counted.
    /// </summary>
    /// <remarks>
    /// A reader holds the file for one read of a few hundred bytes. <c>jason runtime start</c> reads it every 200 ms and
    /// waits thirty seconds for it, so a hold that outlasts this is not one of this installation's reads, and no more than
    /// a third of the start's wait is spent here. A read-only descriptor is answered with the same access-denied, and is
    /// refused at once instead.
    /// </remarks>
    public static HeldFileRule PublishRule { get; } = new(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(50), AccessDeniedCounts: true);

    /// <summary>
    /// How long the delete at a stop is tried while the descriptor is held: two seconds, every 50 ms, sharing and lock
    /// violations only.
    /// </summary>
    /// <remarks>
    /// Shorter, because a stop that cannot remove it leaves a descriptor every reader already tells apart as stale, and a
    /// stop is not kept waiting for that. An access-denied there is a permission or a read-only file, and is said at once.
    /// </remarks>
    public static HeldFileRule RemoveRule { get; } = new(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50), AccessDeniedCounts: false);

    /// <summary>The rule the rename is tried under: <see cref="PublishRule"/>, except in a test.</summary>
    internal HeldFileRule Publishing { get; init; } = PublishRule;

    /// <summary>The rule the delete is tried under: <see cref="RemoveRule"/>, except in a test.</summary>
    internal HeldFileRule Removing { get; init; } = RemoveRule;

    /// <summary>What the waits are timed by: the runtime's clock when a runtime made it, the system's otherwise.</summary>
    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>How a pause is taken.</summary>
    internal Action<TimeSpan, CancellationToken> Pause { get; init; } = HeldFileRetry.Sleep;

    /// <summary>The one rename over the descriptor; a test's own, for a refusal no file system gives on demand.</summary>
    internal Action<string, string> Rename { get; init; } = (temporary, descriptor) => File.Move(temporary, descriptor, overwrite: true);

    /// <summary>
    /// The executable file this runtime runs as, written into the descriptor: <see cref="SelfExecutable.InstalledImage"/>,
    /// except in a test, which is never a single-file image.
    /// </summary>
    internal Func<string?> InstalledImage { get; init; } = () => SelfExecutable.InstalledImage;

    /// <param name="info">The runtime the descriptor describes.</param>
    /// <param name="baseUrl">Where its API listens.</param>
    /// <param name="token">The capability token a client presents.</param>
    /// <param name="cancellationToken">Ends a wait on a held descriptor, as stopping a start that is under way does.</param>
    public RuntimeDescriptor Publish(RuntimeInfo info, Uri baseUrl, string token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentException.ThrowIfNullOrEmpty(token);

        Directory.CreateDirectory(paths.RunDirectory);
        FilePermissions.RestrictDirectory(paths.RunDirectory);

        var descriptor = new RuntimeDescriptor(
            ApiVersion.Current,
            info.RuntimeVersion,
            info.InstanceId,
            info.Pid,
            baseUrl.GetLeftPart(UriPartial.Authority),
            token,
            info.StartedAt,
            InstalledImage());

        var temporary = Path.Combine(paths.RunDirectory, $".runtime.{Guid.NewGuid():N}.tmp");
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough,
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(temporary, options))
            {
                JsonSerializer.Serialize(stream, descriptor, JasonJson.Options);
                stream.Flush(flushToDisk: true);
            }

            FilePermissions.RestrictFile(temporary);
            Replace(temporary, cancellationToken);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        FilePermissions.VerifyRestrictedToCurrentUser(paths.DescriptorFile);
        return descriptor;
    }

    /// <summary>
    /// Removes the descriptor at a stop, waiting a while for a reader to let go, and says what became of it. It never
    /// throws: a stop goes on whatever the answer, and the answer is for the stop to say.
    /// </summary>
    public DescriptorRemoval Remove()
    {
        try
        {
            return HeldFileRetry.Run(() => File.Delete(paths.DescriptorFile), Removing, Clock, Pause) is null
                ? DescriptorRemoval.Removed
                : DescriptorRemoval.Held;
        }
        catch (DirectoryNotFoundException)
        {
            // No run directory, so no descriptor in it.
            return DescriptorRemoval.Removed;
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return DescriptorRemoval.Refused;
        }
    }

    /// <summary>A removal that did not happen, as the closed phrase a stop's warning carries.</summary>
    public string Why(DescriptorRemoval removal) => removal switch
    {
        DescriptorRemoval.Held => $"another program held it for {Removing.WaitInWords}",
        DescriptorRemoval.Refused => "removing it was refused",
        _ => throw new ArgumentOutOfRangeException(nameof(removal), removal, "the descriptor was removed"),
    };

    /// <summary>
    /// The rename over the descriptor, tried again while a reader holds it, and a refusal with its reason where it does not
    /// go through: held for the whole wait, refused by something no wait ends, or a read-only file.
    /// </summary>
    private void Replace(string temporary, CancellationToken cancellationToken)
    {
        Exception? held;
        try
        {
            held = HeldFileRetry.Run(() => Move(temporary), Publishing, Clock, Pause, cancellationToken);
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            throw DescriptorUnpublishableException.Refused(paths.DescriptorFile, paths.RunDirectory, refused);
        }

        if (held is not null)
        {
            throw DescriptorUnpublishableException.Held(paths.DescriptorFile, Publishing, held);
        }
    }

    /// <summary>
    /// One rename. A read-only descriptor is refused at once rather than waited for: Windows answers it with the same
    /// access-denied as a reader's handle, and no wait ends it.
    /// </summary>
    private void Move(string temporary)
    {
        try
        {
            Rename(temporary, paths.DescriptorFile);
        }
        catch (UnauthorizedAccessException denied) when (OperatingSystem.IsWindows() && IsReadOnly(paths.DescriptorFile))
        {
            throw DescriptorUnpublishableException.ReadOnly(paths.DescriptorFile, denied);
        }
    }

    private static bool IsReadOnly(string path)
    {
        try
        {
            return File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly);
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
