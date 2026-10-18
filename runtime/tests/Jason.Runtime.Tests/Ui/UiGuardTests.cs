using System.Net;
using System.Reflection;
using System.Text;
using Jason.Contracts.Api;
using Jason.Runtime.Ui;

namespace Jason.Runtime.Tests.Ui;

/// <summary>
/// The rules that keep the runtime command-line first with a UI beside it, held here rather than in review: the
/// door is the only POST that needs no bearer, the runtime starts with no UI directory, and a runtime with no module
/// is a runtime with one 404 more and nothing else.
/// </summary>
public class UiGuardTests(UiGuardTests.SharedRuntime shared) : IClassFixture<UiGuardTests.SharedRuntime>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> EveryOperation() =>
        [.. typeof(Operations).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)];

    [Theory]
    [MemberData(nameof(EveryOperation))]
    public async Task Every_operation_needs_a_bearer(string operation)
    {
        using var http = new HttpClient { BaseAddress = shared.Api.Runtime.BaseUrl };
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(Operations.Route(operation), content, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_door_is_the_only_post_under_ui_and_it_answers_without_a_bearer()
    {
        using var http = new HttpClient { BaseAddress = shared.Api.Runtime.BaseUrl };
        http.DefaultRequestHeaders.Add("Origin", $"http://127.0.0.1:{shared.Api.Runtime.BaseUrl.Port}");

        using var doorBody = new StringContent("{}", Encoding.UTF8, "application/json");
        using var door = await http.PostAsync(UiPaths.Door, doorBody, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, door.StatusCode);
        Assert.Contains("\"ticket_invalid\"", await door.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        using var elsewhereBody = new StringContent("{}", Encoding.UTF8, "application/json");
        using var elsewhere = await http.PostAsync("/ui/anything", elsewhereBody, Ct);
        Assert.True(elsewhere.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"POST /ui/anything answered {(int)elsewhere.StatusCode}");
    }

    [Fact]
    public async Task A_runtime_starts_with_no_ui_directory_and_answers_404_for_the_page()
    {
        Assert.False(Directory.Exists(shared.Api.Paths.UiDirectory), "the runtime made a ui directory nobody asked for");
        var (status, body, _) = await UiServingTests.GetAsync(shared.Api, "/ui/");
        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Contains("\"ui_not_installed\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ui_noun_holds_exactly_three_operations()
    {
        var names = EveryOperation().Select(row => row.Data).Where(n => n.StartsWith("ui.", StringComparison.Ordinal)).Order(StringComparer.Ordinal);
        Assert.Equal(["ui.open", "ui.revoke", "ui.tokens"], names);
    }

    /// <summary>One runtime for the theory over every operation: starting one per row would cost a minute for nothing.</summary>
    public sealed class SharedRuntime : IAsyncLifetime
    {
        private RuntimeApiFixture? _api;

        public RuntimeApiFixture Api => _api ?? throw new InvalidOperationException("The shared runtime has not started.");

        public async ValueTask InitializeAsync() => _api = await RuntimeApiFixture.StartAsync(CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            if (_api is not null)
            {
                await _api.DisposeAsync();
            }
        }
    }
}
