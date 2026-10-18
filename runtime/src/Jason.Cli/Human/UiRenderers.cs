using Jason.Contracts.Api;

namespace Jason.Cli.Human;

public static class UiRenderers
{
    public static string Opened(UiOpenResponse opened)
    {
        ArgumentNullException.ThrowIfNull(opened);
        return $"{opened.Url}{Environment.NewLine}(pairs one browser, until {RenderText.Moment(opened.ExpiresAt)})";
    }

    public static string? Tokens(string json)
    {
        var tokens = RenderText.Read<UiTokensResponse>(json);
        if (tokens?.Items is null)
        {
            return null;
        }

        if (tokens.Items.Count == 0)
        {
            return "No browser is paired. `jason ui open` pairs one.";
        }

        var table = new HumanTable("PAIRED", "BY", "LAST USED", "ID");
        foreach (var token in tokens.Items)
        {
            table.Row(RenderText.Moment(token.IssuedAt), token.IssuedTo, RenderText.Moment(token.LastUsedAt) ?? "never", token.Id);
        }

        return table.Render();
    }

    public static string? Revoked(string json)
    {
        var revoked = RenderText.Read<UiRevokeResponse>(json);
        return revoked is null ? null : revoked.Revoked == 1 ? "1 browser unpaired." : $"{revoked.Revoked} browsers unpaired.";
    }
}
