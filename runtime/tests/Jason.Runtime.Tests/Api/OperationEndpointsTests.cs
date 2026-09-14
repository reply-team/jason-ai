using System.Net;
using System.Text;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Api;
using Jason.Runtime.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Api;

public class OperationEndpointsTests
{
    private const string Operation = "probe.echo";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed record ProbeRequest(string? Name, int? Count);

    public sealed record ProbeResponse(string Echo, int TotalCount);

    [Fact]
    public async Task An_empty_body_binds_to_a_request_whose_fields_are_all_absent()
    {
        await using var api = await ProbeApi.StartAsync(_ => new ProbeResponse("ok", 0), Ct);

        var (status, body) = await api.PostAsync(string.Empty, Ct);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("{\"echo\":\"ok\",\"total_count\":0}", body);
        Assert.Equal(new ProbeRequest(null, null), api.LastRequest);
    }

    [Fact]
    public async Task A_successful_response_is_the_bare_dto_in_the_api_dialect()
    {
        await using var api = await ProbeApi.StartAsync(request => new ProbeResponse(request.Name!, request.Count!.Value), Ct);

        var (status, body) = await api.PostAsync("{\"name\":\"latam\",\"count\":3}", Ct);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("{\"echo\":\"latam\",\"total_count\":3}", body);
    }

    [Fact]
    public async Task A_body_that_is_not_json_for_the_operation_is_an_invalid_request()
    {
        await using var api = await ProbeApi.StartAsync(_ => new ProbeResponse("ok", 0), Ct);

        var error = await api.PostErrorAsync("{not json", HttpStatusCode.BadRequest, Ct);

        Assert.Equal("invalid_request", error.Code);
        Assert.False(error.Retryable);
    }

    [Fact]
    public async Task Domain_exceptions_become_their_own_status_and_code()
    {
        await using var notFound = await ProbeApi.StartAsync(_ => throw new NotFoundException("campaign_not_found", "No campaign with id 'cmp_X'."), Ct);
        var missing = await notFound.PostErrorAsync("{}", HttpStatusCode.NotFound, Ct);
        Assert.Equal("campaign_not_found", missing.Code);
        Assert.Null(missing.Details);

        await using var conflict = await ProbeApi.StartAsync(_ => throw new ConflictException("invalid_transition", "A campaign cannot go from draft to paused."), Ct);
        Assert.Equal("invalid_transition", (await conflict.PostErrorAsync("{}", HttpStatusCode.Conflict, Ct)).Code);

        await using var invalid = await ProbeApi.StartAsync(_ => throw new InvalidRequestException("context_too_large", "The campaign context is too large."), Ct);
        Assert.Equal("context_too_large", (await invalid.PostErrorAsync("{}", HttpStatusCode.BadRequest, Ct)).Code);
    }

    [Fact]
    public async Task Validation_failures_carry_one_detail_per_field()
    {
        await using var api = await ProbeApi.StartAsync(_ => throw new ValidationException([new ErrorDetail("name", "required", "name is required.")]), Ct);

        var error = await api.PostErrorAsync("{}", HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal("name", Assert.Single(error.Details!).Field);
    }

    [Fact]
    public async Task An_unexpected_failure_is_a_retryable_internal_error_that_says_nothing_about_the_request()
    {
        await using var api = await ProbeApi.StartAsync(_ => throw new InvalidOperationException("a distinctive secret value"), Ct);

        var error = await api.PostErrorAsync("{\"name\":\"a distinctive secret value\"}", HttpStatusCode.InternalServerError, Ct);

        Assert.Equal("internal_error", error.Code);
        Assert.True(error.Retryable);
        Assert.DoesNotContain("distinctive", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_modules_are_wired_into_the_running_runtime()
    {
        await using var runtime = await RuntimeApiFixture.StartAsync(Ct);

        var info = await runtime.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);
        Assert.Equal("v1", info.ApiVersion);

        var unknown = await runtime.PostErrorAsync("campaign.no_such_verb", null, HttpStatusCode.NotFound, Ct);
        Assert.Equal("not_found", unknown.Code);
    }

    /// <summary>One operation mapped over a service the test controls, hosted the way the runtime hosts its own.</summary>
    private sealed class ProbeApi : IAsyncDisposable
    {
        private WebApplication _app = null!;
        private HttpClient _http = null!;

        public ProbeRequest? LastRequest { get; private set; }

        public static async Task<ProbeApi> StartAsync(Func<ProbeRequest, ProbeResponse> handler, CancellationToken cancellationToken)
        {
            var api = new ProbeApi();
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(api);
            var app = builder.Build();
            app.MapOperation<ProbeApi, ProbeRequest, ProbeResponse>(Operation, (service, request, _) =>
            {
                service.LastRequest = request;
                return Task.FromResult(handler(request));
            });

            await app.StartAsync(cancellationToken);
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            api._app = app;
            api._http = new HttpClient { BaseAddress = new Uri(address) };
            return api;
        }

        public async Task<(HttpStatusCode Status, string Body)> PostAsync(string body, CancellationToken cancellationToken)
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(Operations.Route(Operation), content, cancellationToken);
            return (response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }

        public async Task<ErrorBody> PostErrorAsync(string body, HttpStatusCode expected, CancellationToken cancellationToken)
        {
            var (status, text) = await PostAsync(body, cancellationToken);
            Assert.Equal(expected, status);
            return JsonSerializer.Deserialize<ErrorResponse>(text, JasonJson.Options)!.Error;
        }

        public async ValueTask DisposeAsync()
        {
            _http.Dispose();
            await _app.DisposeAsync();
        }
    }
}
