using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>The two operations on the wire: reachable, authenticated like everything else, and happy with no body.</summary>
public class PluginEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Both_operations_are_mapped_and_answer_the_registry()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var (listStatus, listBody) = await api.PostAsync(Operations.PluginList, null, Ct);
        var (reloadStatus, reloadBody) = await api.PostAsync(Operations.PluginReload, null, Ct);

        Assert.Equal(HttpStatusCode.OK, listStatus);
        Assert.Equal(HttpStatusCode.OK, reloadStatus);
        Assert.Contains("\"plugin_count\":0", listBody, StringComparison.Ordinal);
        Assert.Contains("\"source\":\"reload\"", reloadBody, StringComparison.Ordinal);
        Assert.Contains("\"activated\":true", reloadBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_registry_is_read_with_no_body_at_all()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        using var http = new HttpClient { BaseAddress = api.Runtime.BaseUrl };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", api.Runtime.Token);

        using var response = await http.PostAsync(Operations.Route(Operations.PluginList), content: null, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var registry = JsonSerializer.Deserialize<PluginRegistryDto>(await response.Content.ReadAsStringAsync(Ct), JasonJson.Options)!;
        Assert.Empty(registry.Plugins);
    }

    [Fact]
    public async Task Neither_operation_answers_without_the_capability_token()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        using var http = new HttpClient { BaseAddress = api.Runtime.BaseUrl };

        foreach (var operation in new[] { Operations.PluginList, Operations.PluginReload })
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(Operations.Route(operation), content, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
