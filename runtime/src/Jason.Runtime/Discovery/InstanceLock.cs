using Jason.Contracts.Discovery;

namespace Jason.Runtime.Discovery;

/// <summary>
/// Exactly one runtime per OS user: an exclusive handle on <c>run/runtime.lock</c> held for the process
/// lifetime. The operating system releases it when the process dies, so a crash never wedges the next start.
/// </summary>
public sealed class InstanceLock : IDisposable
{
    private readonly FileStream _handle;

    private InstanceLock(FileStream handle) => _handle = handle;

    public static InstanceLock Acquire(JasonPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Directory.CreateDirectory(paths.RunDirectory);
        try
        {
            var handle = new FileStream(paths.LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1, FileOptions.None);
            return new InstanceLock(handle);
        }
        catch (IOException ex)
        {
            throw new RuntimeAlreadyRunningException(paths.LockFile, ex);
        }
    }

    public void Dispose() => _handle.Dispose();
}

public sealed class RuntimeAlreadyRunningException(string lockFile, Exception inner)
    : InvalidOperationException($"Another Jason runtime already holds the instance lock '{lockFile}'. Exactly one runtime per user may run.", inner);
