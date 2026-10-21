using System.Text;
using Jason.Runtime.Hosting;
using Microsoft.Extensions.Configuration;

namespace Jason.Runtime.Tests.Hosting;

/// <summary>
/// A runtime that cannot compose text refuses to start. The key of a LinkedIn profile link composes its name, so that an
/// accent written as one letter and as a letter and a mark are one profile; .NET in globalization invariant mode composes
/// nothing, and there the two would be two keys, which the rule forbids. The check is a pure function of what this process
/// made of one letter and one mark, so it is held here by that function, both ways, and never by starting a runtime in
/// invariant mode.
/// </summary>
public class TextCompositionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void A_process_that_composes_the_letter_and_its_mark_into_one_letter_may_start()
    {
        Assert.Equal("a\u0301", TextComposition.LetterAndMark);
        Assert.Null(TextComposition.Refusal("\u00e1"));
    }

    /// <summary>
    /// A process that leaves them two says why it will not start, in words a person can act on: the mode by name, the
    /// variable that holds the process in it, and both halves of the cure. The libraries are of no use while the variable
    /// stays set, so a refusal that named only the libraries would send a person to install what changes nothing.
    /// </summary>
    [Theory]
    [InlineData("a\u0301")]
    [InlineData("")]
    [InlineData("a")]
    public void A_process_that_leaves_them_two_is_refused_in_words_that_name_the_mode_and_both_halves_of_the_cure(string composed)
    {
        var refusal = TextComposition.Refusal(composed);

        Assert.NotNull(refusal);
        Assert.Contains("did not start", refusal, StringComparison.Ordinal);
        Assert.Contains("invariant mode", refusal, StringComparison.Ordinal);
        Assert.Contains("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 selects whether or not they are installed", refusal, StringComparison.Ordinal);
        Assert.Contains("Install libicu", refusal, StringComparison.Ordinal);
        Assert.Contains("out of the runtime's environment", refusal, StringComparison.Ordinal);
    }

    /// <summary>This process composes, as every supported installation does, so the check lets it start.</summary>
    [Fact]
    public void This_process_composes_and_the_check_lets_it_start()
    {
        var composed = TextComposition.LetterAndMark.Normalize(NormalizationForm.FormC);

        Assert.True(composed == "\u00e1", $"this process composed the letter and its mark into {composed.Length} characters.");
        TextComposition.EnsureThisProcessComposes();
    }

    /// <summary>
    /// The refusal is said the way every refused start is: on the writer <c>jason runtime run</c> hands the start, which is
    /// standard error in the foreground and the stream <c>jason runtime start</c> reads after "It said:" for a detached
    /// runtime, with 1 as the answer and nothing escaping.
    /// </summary>
    [Fact]
    public async Task A_start_refused_for_it_is_said_where_the_start_was_asked_for()
    {
        using var data = new TempDataDir();
        using var refusals = new StringWriter();
        IConfigurationRoot? built = null;
        var exit = 0;

        var escaped = await Record.ExceptionAsync(async () => exit = await RuntimeHost.RunAsync(
            data.Paths,
            TestRuntimeOptions.Quiet with
            {
                Refusals = refusals,
                ConfigurationBuilt = configuration =>
                {
                    built = configuration;
                    throw new TextCompositionUnavailableException(TextComposition.Refusal("a\u0301")!);
                },
            },
            Ct));
        (built as IDisposable)?.Dispose();

        Assert.Null(escaped);
        Assert.Equal(1, exit);
        Assert.Equal(TextComposition.Refusal("a\u0301"), refusals.ToString().Trim());
    }
}
