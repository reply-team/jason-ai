using System.Globalization;
using System.Text;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the preference verbs. Every renderer returns null when the body is not what it expects, and
/// the runner falls back to printing the response as it came. A listing carries every value, so it is rendered from that
/// one answer.
/// </summary>
public static class PreferenceRenderers
{
    private const int Label = 6;

    private static JsonSerializerOptions Indented { get; } = new(JasonJson.Options) { WriteIndented = true };

    /// <summary>One preference: its key, whether and when it was set and by whom, its size and hash, and the value itself.</summary>
    public static string? Preference(string json)
    {
        var preference = RenderText.Read<PreferenceDto>(json);
        if (preference?.Key is null)
        {
            return null;
        }

        if (preference.Value is null)
        {
            return RenderText.Lines([Line("Key:", preference.Key), Line("Set:", "no")]);
        }

        var text = new StringBuilder();
        text.AppendLine(Line("Key:", preference.Key));
        text.AppendLine(Line("Set:", RenderText.Moment(preference.UpdatedAt)));
        text.AppendLine(Line("By:", RenderText.Actor(preference.UpdatedBy)));
        text.AppendLine(Line("Size:", string.Create(CultureInfo.InvariantCulture, $"{preference.ValueBytes} bytes")));
        text.AppendLine(Line("Hash:", RenderText.Digest(preference.ValueHash)));
        text.AppendLine();
        text.Append(preference.Value.ToJsonString(Indented));
        return text.ToString();
    }

    /// <summary>Every key the page holds, one paragraph each: whether it is set, when and by whom, and the value where it is.</summary>
    public static string? PreferenceList(string json)
    {
        var page = RenderText.Read<Page<PreferenceDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var paragraphs = page.Items.Select(preference => preference.Value is null
            ? preference.Key + ": not set"
            : preference.Key + ": set " + RenderText.Moment(preference.UpdatedAt) + " by " + RenderText.Shown(RenderText.Actor(preference.UpdatedBy))
                + Environment.NewLine + preference.Value.ToJsonString(Indented));

        return RenderText.WithCursor(string.Join(Environment.NewLine + Environment.NewLine, paragraphs), page.NextCursor);
    }

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : RenderText.Shown(value));
}
