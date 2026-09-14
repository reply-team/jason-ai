using System.Collections;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Execution;
using Jason.Contracts.Json;

namespace Jason.FakeAgentHost;

/// <summary>
/// How the host reaches the Runtime API. The descriptor is read again before every single call, never cached:
/// the capability token is minted per runtime instance, so a host that outlived a restart has to pick up the
/// new one to finish its attempt. A call that cannot be made is reported on stderr and handed back as status
/// zero — the host has no business dying because the runtime was not there.
/// </summary>
internal sealed class RuntimeApi(string descriptorFile) : IDisposable
{
    /// <summary>The status used for "no answer at all", which is not a status any HTTP response can carry.</summary>
    public const int NoAnswer = 0;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public RuntimeDescriptor? ReadDescriptor()
    {
        if (string.IsNullOrWhiteSpace(descriptorFile))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RuntimeDescriptor>(File.ReadAllText(descriptorFile), JasonJson.Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or JsonException)
        {
            return null;
        }
    }

    public async Task<(int Status, string Body)> CallAsync<TRequest>(string operation, TRequest request)
    {
        var descriptor = ReadDescriptor();
        if (descriptor is null)
        {
            await Diagnostics.WriteAsync($"{operation}: the descriptor '{descriptorFile}' could not be read").ConfigureAwait(false);
            return (NoAnswer, string.Empty);
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(descriptor.BaseUrl, UriKind.Absolute), Operations.Route(operation)))
            {
                Content = new StringContent(JsonSerializer.Serialize(request, JasonJson.Options), Encoding.UTF8, "application/json"),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", descriptor.Token);

            using var response = await _http.SendAsync(message).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return ((int)response.StatusCode, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            await Diagnostics.WriteAsync($"{operation}: {ex.Message}").ConfigureAwait(false);
            return (NoAnswer, string.Empty);
        }
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Everything the host says about itself goes to stderr, prefixed so a log tail shows who spoke.</summary>
internal static class Diagnostics
{
    public const string Prefix = "fake-agent-host: ";

    public static Task WriteAsync(string message) => Console.Error.WriteLineAsync(Prefix + message);

    /// <summary>
    /// The one line every run opens with, before any behaviour acts. It answers, in the child's own voice, the
    /// questions the launcher is judged on: which attempt it was told about, whether the data directory reached
    /// it, where it was started, and — the one that matters — whether the capability token is anywhere in its
    /// environment. It must never be, so the answer is expected to be <c>false</c> whenever it can be checked.
    /// </summary>
    public static Task WriteStartLineAsync(string behaviour, LaunchEnvelope envelope, string? token) =>
        WriteAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"behaviour={behaviour} attempt={envelope.AttemptId} number={envelope.AttemptNumber} data-dir={DataDirectory()} cwd={Environment.CurrentDirectory} token-in-env={TokenInEnvironment(token)}"));

    private static string DataDirectory() =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(JasonPaths.DataDirectoryVariable)) ? "unset" : "set";

    /// <summary><c>n/a</c> when the descriptor could not be read: without the token there is nothing to look for.</summary>
    private static string TokenInEnvironment(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return "n/a";
        }

        foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
        {
            if (string.Equals(variable.Value as string, token, StringComparison.Ordinal))
            {
                return "true";
            }
        }

        return "false";
    }
}
