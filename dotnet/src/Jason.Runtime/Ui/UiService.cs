using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Hosting;

namespace Jason.Runtime.Ui;

/// <summary>The three <c>ui</c> operations. Nothing here knows a screen: a link, a list of paired browsers, and a revocation.</summary>
public sealed class UiService(UiTokenStore tokens, UiTickets tickets, UiModule module, ListeningEndpoint listening)
{
    public Task<UiOpenResponse> OpenAsync(UiOpenRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (module.ActiveDirectory() is null)
        {
            throw DomainErrors.UiNotInstalled(module.NotInstalled());
        }

        var route = request switch
        {
            { ApprovalId: { Length: > 0 } approval } => "/attention/approvals/" + Uri.EscapeDataString(approval.Trim()),
            { DecisionId: { Length: > 0 } decision } => "/attention/questions/" + Uri.EscapeDataString(decision.Trim()),
            _ => "/attention",
        };
        var (ticket, expires) = tickets.Mint();
        return Task.FromResult(new UiOpenResponse($"http://127.0.0.1:{listening.Port}{UiPaths.Prefix}/#{route}?t={ticket}", expires));
    }

    public async Task<UiTokensResponse> TokensAsync(UiTokensRequest request, CancellationToken cancellationToken) =>
        new(await tokens.ListAsync(cancellationToken).ConfigureAwait(false));

    public async Task<UiRevokeResponse> RevokeAsync(UiRevokeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.All != true && string.IsNullOrWhiteSpace(request.TokenId))
        {
            throw DomainErrors.Required("token_id");
        }

        return new(await tokens.RevokeAsync(request.TokenId?.Trim(), request.All == true, cancellationToken).ConfigureAwait(false));
    }
}
