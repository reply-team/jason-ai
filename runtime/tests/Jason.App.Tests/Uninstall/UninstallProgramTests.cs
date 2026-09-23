using System.Text.Json.Nodes;
using Jason.App.Tests.EndToEnd;

namespace Jason.App.Tests.Uninstall;

/// <summary>
/// <c>jason uninstall</c> as the shipped program, started the way a build from source is started.
/// </summary>
/// <remarks>
/// Every other test of this verb substitutes the seam that touches the machine, which is what makes them safe
/// and is also what hides this: the question "which file am I installed as?" is answered before the seam is
/// reached, and answering it wrongly is not something a recording remover can show you.
/// </remarks>
public class UninstallProgramTests
{
    /// <summary>
    /// A build started through the muxer names no executable to remove — because there is not one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Environment.ProcessPath</c> under <c>dotnet jason.dll</c> is the muxer: on this machine
    /// <c>C:\Program Files\dotnet\dotnet.exe</c>. Read raw, this verb planned to remove it, to take its
    /// directory off this account's PATH, and — on Windows, where the image of a running process cannot be
    /// deleted but <em>can</em> be renamed — to move it aside, which succeeds. That is the .NET installation
    /// of whoever ran it, and `dotnet run --project runtime/src/Jason.App` is how this repository's own README
    /// says to run from source.
    /// </para>
    /// <para>
    /// <c>jason update apply</c> asked the same question in wave 13 and answered it properly: one element in
    /// <c>SelfExecutable.Command</c> is a published executable, two is <c>dotnet &lt;assembly&gt;</c> and there
    /// is no single file. This verb's own comment claimed it worked it out "the same way" and did not.
    /// </para>
    /// <para>
    /// It is a dry run, so this test is safe to fail.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_build_running_through_the_muxer_names_no_executable_to_remove()
    {
        using var it = GoldenPath.Create("uninstall-muxer");

        var result = await GoldenPath.JasonAsync(it, "uninstall", "--dry-run");

        var plan = JsonNode.Parse(result.Output)?["plan"]
            ?? throw new InvalidOperationException($"uninstall --dry-run printed no plan: {result.Output}{result.Error}");

        Assert.True(
            plan["executable"] is null,
            $"This build runs through the muxer and the plan names '{plan["executable"]}' as the executable to "
            + "remove. That file is not this installation; it is whatever is running this one.");

        Assert.True(
            plan["install_directory"] is null,
            $"The plan names '{plan["install_directory"]}' as an install directory to remove and to take off "
            + "this account's PATH. It is the muxer's directory.");
    }

    /// <summary>And it says so, rather than reporting a clean uninstall of a file it never found.</summary>
    [Fact]
    public async Task And_it_says_why_nothing_was_removed_rather_than_reporting_a_clean_uninstall()
    {
        using var it = GoldenPath.Create("uninstall-muxer-says");

        // The plan, not the report: a dry run never reaches the report, so the plan is the whole of what a
        // person sees -- and a list of what would go with no executable in it reads as one that forgot.
        var result = await GoldenPath.JasonAsync(it, "uninstall", "--human", "--dry-run");

        Assert.Contains("dotnet", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no executable", result.Output, StringComparison.Ordinal);
    }
}
