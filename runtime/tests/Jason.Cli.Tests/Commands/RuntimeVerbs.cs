using System.Net;
using System.Text;
using System.Text.Json;
using Jason.Cli.Discovery;
using Jason.Cli.Process;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests.Commands;

/// <summary>The descriptor, the response bodies and the environment the runtime-verb tests all work against.</summary>
internal static class RuntimeVerbs
{
    public const int Pid = 77;
    public const string Token = "the-token";
    public const string BaseUrl = "http://127.0.0.1:5000";

    /// <summary>
    /// Long enough that a test waiting for a descriptor never expires by accident. The descriptor these tests
    /// wait for is published by a thread-pool continuation, and a machine running the whole suite — real
    /// runtimes and all — can hold one for seconds, so this is sized for the worst scheduling rather than for
    /// the delay itself. A wait whose expiry is the subject uses <see cref="ShortTimeout"/>.
    /// </summary>
    public static TimeSpan Timeout => TimeSpan.FromSeconds(30);

    /// <summary>For the tests whose subject is the wait running out.</summary>
    public static TimeSpan ShortTimeout => TimeSpan.FromMilliseconds(300);

    public static TimeSpan Poll => TimeSpan.FromMilliseconds(20);

    public static RuntimeDescriptor Descriptor(string instanceId, int pid = Pid) =>
        new("v1", "0.1.0-dev", instanceId, pid, BaseUrl, Token, DateTimeOffset.UnixEpoch);

    public static string InfoJson(string instanceId, int pid = Pid) => JsonSerializer.Serialize(
        new SystemInfoResponse(
            "0.1.0-dev",
            "v1",
            instanceId,
            pid,
            DateTimeOffset.UnixEpoch,
            "/data",
            new DatabaseInfo(["20260913225419_InitialCreate"]),
            new DispatcherInfo(DispatcherState.Running, 10, 4, 0, null, 0, 0),
            new PluginsInfo(0, "snp_EMPTY", DateTimeOffset.UnixEpoch, true),
            new RoutesInfo("rts_EMPTY", DateTimeOffset.UnixEpoch, null, 0, 0)),
        JasonJson.Options);

    public static string ShutdownJson(string instanceId, int pid = Pid) =>
        JsonSerializer.Serialize(new ShutdownResponse(instanceId, pid, Stopping: true), JasonJson.Options);

    public static HttpResponseMessage Response(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static ErrorBody Envelope(string stdout) =>
        JsonSerializer.Deserialize<ErrorResponse>(stdout, JasonJson.Options)!.Error;

    public static (CliEnvironment Env, StringWriter Out, StringWriter Error) Environment(
        TempPaths dir,
        HttpMessageHandler handler,
        IRuntimeProcessControl processes)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        return (new CliEnvironment(output, error, dir.Paths, handler, null, processes), output, error);
    }

    /// <summary>A runtime that answers <c>system.info</c> as whichever instance the descriptor on disk names.</summary>
    public static HttpMessageHandler EchoesTheDescriptor(TempPaths dir, Func<string, string>? answeringAs = null) =>
        new FakeHandler(_ =>
        {
            var current = new DescriptorReader(dir.Paths).Read()
                ?? throw new HttpRequestException("connection refused");
            return Response(HttpStatusCode.OK, InfoJson(answeringAs is null ? current.InstanceId : answeringAs(current.InstanceId)));
        });

    public static HttpMessageHandler NeverCalled() =>
        new FakeHandler(_ => throw new InvalidOperationException("the runtime must not be called here"));

    /// <summary>A launch that publishes a descriptor a moment later, the way a runtime coming up does.</summary>
    /// <summary>
    /// A launch whose child publishes its descriptor a moment later, so the CLI really does wait for one.
    /// </summary>
    /// <remarks>
    /// On a thread of its own rather than the thread pool. The publish is the single thing the waiting CLI is
    /// waiting for, and a pool queued behind a whole suite's blocked work can take longer to get around to a
    /// 60 ms delay than the wait allows — which is this helper failing at its one job and reporting it as the
    /// command under test failing at its own. It has happened twice in this repository now: once against a
    /// five-second wait, which was answered by lengthening the wait to thirty seconds, and once against the
    /// thirty. A thread that is not the pool's cannot be starved by the pool.
    /// </remarks>
    public static Func<JasonPaths, IProcessHandle> PublishesAfterAWhile(TempPaths dir, RuntimeDescriptor descriptor) =>
        paths =>
        {
            _ = paths;
            var publisher = new Thread(() => Publish(dir, descriptor))
            {
                IsBackground = true,
                Name = "descriptor-publisher",
            };

            publisher.Start();
            return new FakeProcessHandle(4242);
        };

    /// <summary>
    /// The publish itself, a moment later, and survivable. This runs on a thread of its own, so an exception
    /// escaping it does not fail a test — it terminates the whole test host, taking every unrelated test in
    /// the process with it and reporting nothing about which test was to blame.
    /// </summary>
    /// <remarks>
    /// And it can fail for a reason that is nobody's bug. The CLI this serves is polling the same file, and a
    /// reader holds it in a way that denies a writer for as long as it is reading — so a publish landing in
    /// the middle of a poll is refused. It is retried for a moment, because a publish arriving slightly later
    /// is what this helper promises anyway, and then given up on quietly: by that point the test waiting for a
    /// descriptor has its own opinion about not getting one, and that opinion is a legible failure rather
    /// than a dead process.
    /// </remarks>
    public static void Publish(TempPaths dir, RuntimeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(dir);
        Thread.Sleep(60);

        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                dir.WriteDescriptor(descriptor);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(25);
            }
        }
    }

    /// <summary>A launch whose child is gone by the time the CLI looks at it.</summary>
    public static Func<JasonPaths, IProcessHandle> DiesWith(int exitCode) =>
        _ => new FakeProcessHandle(4242) { HasExited = true, ExitCode = exitCode };
}
