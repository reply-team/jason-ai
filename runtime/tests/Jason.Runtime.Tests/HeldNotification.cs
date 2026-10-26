using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Jason.Runtime.Tests;

/// <summary>
/// A section of the test's own, told of a change to the configuration as every section is. The first time it is told
/// after it is made, it says so and waits to be let go, which holds whoever is telling the sections — the settings file's
/// watch, or a read — in the middle of telling them, for as long as the test needs; the 30 seconds a wait for it is
/// capped at are a guard against a hang, never a margin.
/// </summary>
public sealed class HeldNotification : IDisposable
{
    private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _letGo = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IDisposable _registration;
    private int _armed = 1;

    public HeldNotification(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _registration = ChangeToken.OnChange(configuration.GetReloadToken, () =>
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                _held.TrySetResult();
                _letGo.Task.Wait();
            }
        });
    }

    /// <summary>Waits until something is telling the sections and is held there.</summary>
    public Task WaitUntilHeldAsync(CancellationToken cancellationToken) => _held.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

    /// <summary>Lets whatever is held go on telling the sections.</summary>
    public void LetGo() => _letGo.TrySetResult();

    public void Dispose()
    {
        LetGo();
        _registration.Dispose();
    }
}
