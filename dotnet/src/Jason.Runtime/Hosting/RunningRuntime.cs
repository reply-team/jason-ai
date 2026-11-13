using Jason.Contracts.Discovery;
using Jason.Runtime.Discovery;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog.Core;

namespace Jason.Runtime.Hosting;

/// <summary>
/// A started runtime: the listening API, its descriptor, the instance lock, the configuration and the logger, torn down
/// in order.
/// </summary>
public sealed class RunningRuntime : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly JasonPaths _paths;
    private readonly DescriptorPublisher _publisher;
    private readonly InstanceLock _lock;
    private readonly ConfigurationRoot _configuration;
    private readonly Logger _logger;
    private readonly UnhandledExceptionLine? _unhandled;
    private bool _stopped;

    internal RunningRuntime(
        WebApplication app,
        RuntimeDescriptor descriptor,
        JasonPaths paths,
        DescriptorPublisher publisher,
        InstanceLock instanceLock,
        ConfigurationRoot configuration,
        Logger logger,
        UnhandledExceptionLine? unhandled)
    {
        _unhandled = unhandled;
        _app = app;
        Descriptor = descriptor;
        _paths = paths;
        _publisher = publisher;
        _lock = instanceLock;
        _configuration = configuration;
        _logger = logger;
    }

    public RuntimeDescriptor Descriptor { get; }

    /// <summary>The composed services of the running process, so a test can read what the runtime itself reads.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>The configuration this runtime owns and disposes when it stops, so a test can see it given back.</summary>
    internal ConfigurationRoot Configuration => _configuration;

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
            if (_publisher.Remove() is var removal and not DescriptorRemoval.Removed)
            {
                // Said rather than left in silence: a CLI that reads it still tells it apart as stale, but whoever reads
                // the log after a stop should not have to work that out. A closed phrase, never the exception's message.
                _logger.Warning(
                    "The endpoint descriptor at {Descriptor} could not be removed: {Why}. A CLI reading it will find no runtime at its address and say so.",
                    _paths.DescriptorFile,
                    _publisher.Why(removal));
            }

            _lock.Dispose();
            try
            {
                await _app.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                // Whatever disposing the host did. The configuration watches the settings file, and that watch is an
                // inotify instance on Linux: a per-user budget every process shares. Left to the finalizer, each runtime
                // started and stopped in one process — every in-process test host — kept its watch until a collection
                // happened to run; and the log is the one place the stop is recorded.
                _configuration.Dispose();
                _logger.Information("Runtime {InstanceId} stopped", Descriptor.InstanceId);
                _unhandled?.Dispose();
                await _logger.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
