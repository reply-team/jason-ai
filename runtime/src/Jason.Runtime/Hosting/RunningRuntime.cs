using Jason.Contracts.Discovery;
using Jason.Runtime.Discovery;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Serilog.Core;

namespace Jason.Runtime.Hosting;

/// <summary>A started runtime: the listening API, its descriptor, the instance lock and the logger, torn down in order.</summary>
public sealed class RunningRuntime : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly JasonPaths _paths;
    private readonly InstanceLock _lock;
    private readonly Logger _logger;
    private bool _stopped;

    internal RunningRuntime(WebApplication app, RuntimeDescriptor descriptor, JasonPaths paths, InstanceLock instanceLock, Logger logger)
    {
        _app = app;
        Descriptor = descriptor;
        _paths = paths;
        _lock = instanceLock;
        _logger = logger;
    }

    public RuntimeDescriptor Descriptor { get; }

    /// <summary>The composed services of the running process, so a test can read what the runtime itself reads.</summary>
    public IServiceProvider Services => _app.Services;

    public Uri BaseUrl => new(Descriptor.BaseUrl, UriKind.Absolute);

    public string Token => Descriptor.Token;

    public Task WaitForShutdownAsync(CancellationToken cancellationToken) => _app.WaitForShutdownAsync(cancellationToken);

    public async Task StopAsync()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        _logger.Information("Runtime {InstanceId} stopping", Descriptor.InstanceId);
        try
        {
            await _app.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            // Whatever stopping the host did — a hosted service that overran the shutdown timeout, an exception
            // from a stop — the rest of the teardown still happens: the descriptor must not advertise an
            // instance that is gone, the lock must not keep the next runtime out, and the host must be disposed
            // so nothing of it lingers in the process. The caller still sees the exception afterwards.
            new DescriptorPublisher(_paths).Remove();
            _lock.Dispose();
            await _app.DisposeAsync().ConfigureAwait(false);
            _logger.Information("Runtime {InstanceId} stopped", Descriptor.InstanceId);
            await _logger.DisposeAsync().ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
