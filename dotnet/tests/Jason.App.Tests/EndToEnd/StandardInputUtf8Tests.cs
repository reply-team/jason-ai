using Jason.Runtime.Tests;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// A person's words reach the program on standard input as UTF-8, the way the skills' here-documents hand them over, and
/// are stored as they were written. Run against the shipped program as a real process, because what a console's code page
/// does to a pipe happens only in a real process: on Windows the console's own reader decodes by the console's code page,
/// and Cyrillic read that way was stored garbled.
/// </summary>
public class StandardInputUtf8Tests
{
    private const string Words = "Привет, Мария! Ёлки, «тест» — 你好 ✓";

    [Fact]
    [Critical(Family.F4, "the CLI is a child of every agent host, and its standard input is where a person's words reach it")]
    public async Task Words_in_another_alphabet_piped_as_UTF8_are_stored_as_they_were_written()
    {
        using var it = GoldenPath.Create("stdin-utf8");
        await GoldenPath.WriteSettingsAsync(it, new SettingsShape(Routed: false));
        await GoldenPath.StartAsync(it);

        // Written raw, not as \u escapes: what a here-document of a person's words hands the program.
        var body = "{\"context\":{\"brief\":\"" + Words + "\"}}";
        var created = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, ["campaign", "create", "--name", "Piped", "--file", "-"], body)));
        var campaign = (string)created["id"]!;

        var read = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "campaign", "get", campaign)));

        Assert.Equal(Words, (string?)read["context"]?["brief"]);
    }
}
