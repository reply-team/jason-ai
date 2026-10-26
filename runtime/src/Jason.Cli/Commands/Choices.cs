using System.CommandLine;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Jason.Contracts.Json;

namespace Jason.Cli.Commands;

/// <summary>
/// The options whose value is one of the words of an API enum. The words are read from the enum as the API's own JSON
/// spells it, so the CLI, the help a guard holds to them and the runtime cannot name different ones; a word outside them
/// is refused while the command line is parsed, before any request, in the CLI's words. Without this the runtime's JSON
/// binding refused it, naming the request type it could not build.
/// </summary>
internal static class Choices
{
    private static readonly ConditionalWeakTable<Option, IReadOnlyList<string>> Held = [];

    /// <summary>The words <paramref name="option"/> takes, or null where it takes any text.</summary>
    public static IReadOnlyList<string>? Of(Option option) => Held.TryGetValue(option, out var words) ? words : null;

    /// <summary><c>a</c>, <c>a or b</c>, <c>a, b or c</c>.</summary>
    public static string Spoken(IReadOnlyList<string> words) =>
        words.Count < 2 ? string.Concat(words) : $"{string.Join(", ", words.Take(words.Count - 1))} or {words[^1]}";

    internal static TOption Hold<TOption>(TOption option, IReadOnlyList<string> words, Func<string, bool> reads)
        where TOption : Option
    {
        Held.AddOrUpdate(option, words);
        option.Validators.Add(result =>
        {
            foreach (var token in result.Tokens.Where(token => !reads(token.Value)))
            {
                result.AddError($"{option.Name} takes {Spoken(words)}; '{token.Value}' is not one of them.");
            }
        });
        return option;
    }
}

/// <summary>The words of <typeparamref name="TEnum"/>, as the API reads and writes them.</summary>
internal static class Choice<TEnum>
    where TEnum : struct, Enum
{
    public static IReadOnlyList<string> Words { get; } =
        [.. Enum.GetValues<TEnum>().Select(value => JsonSerializer.SerializeToElement(value, JasonJson.Options).GetString()!)];

    /// <summary>
    /// Holds <paramref name="option"/> to these words. What is taken is decided by the API's JSON itself, which also reads
    /// some other spellings of a word (<c>Active</c> for <c>active</c>), so the CLI refuses exactly what the runtime would;
    /// the value goes to the runtime as it was typed.
    /// </summary>
    public static TOption Only<TOption>(TOption option)
        where TOption : Option => Choices.Hold(option, Words, Reads);

    /// <summary>Whether the API's JSON reads <paramref name="text"/> as one of these words.</summary>
    public static bool Reads(string text)
    {
        try
        {
            JsonSerializer.Deserialize<TEnum>(JsonSerializer.Serialize(text), JasonJson.Options);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
