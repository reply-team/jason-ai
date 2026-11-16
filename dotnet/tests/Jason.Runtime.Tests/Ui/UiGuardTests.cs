using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Runtime.Hosting.Modules;
using Jason.Runtime.Ui;

namespace Jason.Runtime.Tests.Ui;

/// <summary>
/// The rules that keep the runtime command-line first with a UI beside it, held here rather than in review: the
/// door is the only POST that needs no bearer, the runtime starts with no UI directory, a runtime with no module
/// is a runtime with one 404 more and nothing else, and the headers a file is served with are chosen by the layout, never
/// by a file's name.
/// </summary>
public class UiGuardTests(UiGuardTests.SharedRuntime shared) : IClassFixture<UiGuardTests.SharedRuntime>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> EveryOperation() =>
        [.. typeof(Operations).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)];

    // Critical: every operation needs the bearer.
    public static TheoryData<string> CriticalEveryOperation() =>
        CriticalRows.Mark(EveryOperation(), Family.S5, data => data is ["system.info"]);

    [Theory]
    [MemberData(nameof(CriticalEveryOperation))]
    public async Task Every_operation_needs_a_bearer(string operation)
    {
        using var http = new HttpClient { BaseAddress = shared.Api.Runtime.BaseUrl };
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(Operations.Route(operation), content, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Critical(Family.S5, "the pairing door is the one exception")]
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
    [Critical(Family.B6, "without ui/ the runtime is unchanged")]
    public async Task A_runtime_starts_with_no_ui_directory_and_answers_404_for_the_page()
    {
        Assert.False(Directory.Exists(shared.Api.Paths.UiDirectory), "the runtime made a ui directory nobody asked for");
        var (status, body, _) = await UiServingTests.GetAsync(shared.Api, "/ui/");
        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Contains("\"ui_not_installed\"", body, StringComparison.Ordinal);
    }

    /// <summary>With no UI directory a path under <c>sealed/</c> is answered as any other: the same refusal, and nothing made on disk.</summary>
    [Fact]
    public async Task With_no_ui_directory_a_path_under_sealed_answers_as_any_other_path()
    {
        var (status, body, _) = await UiServingTests.GetAsync(shared.Api, "/ui/sealed/page.html");
        var (_, other, _) = await UiServingTests.GetAsync(shared.Api, "/ui/page.html");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(other, body);
        Assert.Contains("\"ui_not_installed\"", body, StringComparison.Ordinal);
        Assert.False(Directory.Exists(shared.Api.Paths.UiDirectory), "the runtime made a ui directory nobody asked for");
    }

    /// <summary>
    /// No header rule of the server names a file. The set an answer carries is a function of the directory of the layout the
    /// path is in, and of nothing else: every name gets the set of its directory, at the root the console's, the names a
    /// build of the UI or a page of it could have included; and no literal the rules or the route load is a file's name.
    /// </summary>
    [Fact]
    public void No_header_rule_of_the_server_names_a_file()
    {
        var console = UiFiles.HeadersFor("index.html");
        var @sealed = UiFiles.HeadersFor("sealed/page.html");
        Assert.NotEqual(console, @sealed);

        string[] names = ["index.html", "demo.html", "sealed.html", "sealed", "manifest.json", "favicon.svg", "app-abc123.js", "THIRD-PARTY-NOTICES", "page"];
        foreach (var name in names)
        {
            Assert.Equal(console, UiFiles.HeadersFor(name));
            Assert.Equal(console, UiFiles.HeadersFor("assets/" + name));
            Assert.Equal(console, UiFiles.HeadersFor("other/" + name));
            Assert.Equal(@sealed, UiFiles.HeadersFor("sealed/" + name));
            Assert.Equal(@sealed, UiFiles.HeadersFor("sealed/deeper/" + name));
        }

        Assert.Equal(console, UiFiles.HeadersFor(string.Empty));

        var fileName = new Regex(@"(?<![\w.-])[\w-]+\.(?:html?|m?js|css|json|map|svg|png|ico|woff2?|txt)\b", RegexOptions.IgnoreCase);
        var literals = IlStrings.Of(typeof(UiFiles)).Concat(IlStrings.Of(typeof(UiModuleEndpoints))).ToList();
        Assert.Contains(UiFiles.SealedContentSecurityPolicy, literals);
        var named = literals.Where(literal => fileName.IsMatch(literal) || literal.Contains("demo", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(named.Count == 0, $"a header rule names a file: {string.Join(", ", named)}");
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
