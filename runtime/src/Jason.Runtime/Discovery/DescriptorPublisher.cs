using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;

namespace Jason.Runtime.Discovery;

/// <summary>
/// Publishes <c>run/runtime.json</c> atomically: the content is written to an owner-only temporary file in
/// the same directory, flushed to disk, then renamed over the target, so no reader ever sees a partial
/// descriptor or a token in a world-readable file.
/// </summary>
public sealed class DescriptorPublisher(JasonPaths paths)
{
    public RuntimeDescriptor Publish(RuntimeInfo info, Uri baseUrl, string token)
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
            info.StartedAt);

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
            File.Move(temporary, paths.DescriptorFile, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        FilePermissions.VerifyRestrictedToCurrentUser(paths.DescriptorFile);
        return descriptor;
    }

    public void Remove() => TryDelete(paths.DescriptorFile);

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
