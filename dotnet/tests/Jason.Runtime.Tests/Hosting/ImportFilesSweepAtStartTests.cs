using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Jason.Contracts.Api;
using Jason.Contracts.Harness;
using Jason.Contracts.Hosts;
using Jason.Runtime.Configuration;
using Jason.Runtime.Harness;
using Jason.Runtime.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jason.Runtime.Tests.Hosting;

/// <summary>
/// The import-file sweep runs beside a composed runtime rather than inside its start: the runtime answers and its endpoint
/// descriptor is published while a folder is still being completed, and a folder that cannot be completed is said once in
/// the log while the runtime goes on running.
/// </summary>
public sealed class ImportFilesSweepAtStartTests
{
    /// <summary>
    /// How long the row waits for a start that the sweep holds. A start of a test runtime returns well inside it; the bound
    /// only turns a start that waits on the sweep into a failure of the row rather than a test that never ends.
    /// </summary>
    private static readonly TimeSpan StartBound = TimeSpan.FromSeconds(60);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_runtime_answers_and_publishes_its_descriptor_while_the_sweep_holds_its_first_folder()
    {
        using var dir = new TempDataDir();
        var root = MarkTheRoot(dir);
        using var letGo = new ManualResetEventSlim();
        var offered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = TestRuntimeOptions.Quiet with
        {
            ConfigureServices = services =>
            {
                TestRuntimeOptions.TurnTheOutsideOff(services);
                services.AddSingleton<ImportFilesSweep.FolderCompletion>((folder, level, id) =>
                {
                    offered.TrySetResult();
                    letGo.Wait(Ct);
                    return HarnessFolders.CompleteImportFiles(folder, level, id);
                });
            },
        };

        // Off this thread, so that a start which waits on the sweep is seen waiting rather than holding the test with it.
        var start = Task.Run(() => RuntimeHost.StartAsync(dir.Paths, options, Ct), Ct);
        var heldTheStart = false;
        var descriptorWhileHeld = false;
        HttpStatusCode? answerWhileHeld = null;
        var sweepRanOn = false;
        try
        {
            await offered.Task.WaitAsync(Ct);
            var started = await start.WaitAsync(StartBound, Ct);
            descriptorWhileHeld = File.Exists(dir.Paths.DescriptorFile);
            answerWhileHeld = await SystemInfoAsync(started);
            sweepRanOn = !Sweep(started).ExecuteTask!.IsCompleted;
        }
        catch (TimeoutException)
        {
            heldTheStart = true;
        }
        finally
        {
            letGo.Set();
        }

        await using var runtime = await start;
        await Sweep(runtime).ExecuteTask!.WaitAsync(Ct);

        Assert.False(heldTheStart, $"The start had not returned {StartBound.TotalSeconds} s after the sweep took its first folder: it waits on the sweep.");
        Assert.True(descriptorWhileHeld, "The endpoint descriptor was not published while the sweep held its first folder.");
        Assert.Equal(HttpStatusCode.OK, answerWhileHeld);
        Assert.True(sweepRanOn, "The sweep had ended before its first folder was let go.");
        foreach (var file in AgentHosts.ImportFiles)
        {
            Assert.Equal(file.ImportLine, await File.ReadAllTextAsync(Path.Combine(root, file.FileName), Ct));
        }
    }

    [Fact]
    public async Task A_folder_that_throws_is_said_once_and_the_runtime_keeps_running()
    {
        const string Thrown = "a folder the stand-in refused";
        using var dir = new TempDataDir();
        MarkTheRoot(dir);
        var options = TestRuntimeOptions.Quiet with
        {
            ConfigureServices = services =>
            {
                TestRuntimeOptions.TurnTheOutsideOff(services);
                services.AddSingleton<ImportFilesSweep.FolderCompletion>((_, _, _) => throw new InvalidOperationException(Thrown));
            },
        };

        var runtime = await RuntimeHost.StartAsync(dir.Paths, options, Ct);
        try
        {
            var sweep = Sweep(runtime).ExecuteTask!;
            await sweep.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);

            Assert.True(sweep.IsCompletedSuccessfully, $"The sweep ended {sweep.Status}: a sweep that ends faulted stops the host.");
            Assert.Equal(HttpStatusCode.OK, await SystemInfoAsync(runtime));
            Assert.False(runtime.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        }
        finally
        {
            // Stopping flushes and closes the rolling file sink, so what is on disk now is everything there is.
            await runtime.StopAsync();
        }

        var logs = string.Concat(await Task.WhenAll(
            Directory.GetFiles(dir.Paths.LogsDirectory, "*", SearchOption.AllDirectories).Select(file => File.ReadAllTextAsync(file, Ct))));
        Assert.Equal(1, Occurrences(logs, "The import-file sweep could not read the recorded folders; the runtime continues"));
        Assert.DoesNotContain(Thrown, logs, StringComparison.Ordinal);
    }

    /// <summary>
    /// Writes the dispatcher-off settings and marks the harness root the runtime will read, in the test's own home, so the
    /// sweep has one folder to offer; answers the root.
    /// </summary>
    private static string MarkTheRoot(TempDataDir dir)
    {
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
        var root = HarnessRoot.Of(new HarnessOptions(), dir.Home);
        var marker = HarnessMarker.PathIn(root);
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllBytes(marker, new HarnessMarker(HarnessMarker.Root, null).Bytes());
        return root;
    }

    private static ImportFilesSweep Sweep(RunningRuntime runtime) =>
        runtime.Services.GetServices<IHostedService>().OfType<ImportFilesSweep>().Single();

    private static async Task<HttpStatusCode> SystemInfoAsync(RunningRuntime runtime)
    {
        using var http = new HttpClient { BaseAddress = runtime.BaseUrl };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", runtime.Token);
        using var body = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(Operations.Route(Operations.SystemInfo), body, Ct);
        return response.StatusCode;
    }

    private static int Occurrences(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
