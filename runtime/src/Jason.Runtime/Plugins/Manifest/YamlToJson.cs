using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Jason.Runtime.Plugins.Manifest;

/// <summary>The manifest did not parse as YAML, at the line and column where it stopped making sense.</summary>
public sealed class YamlInvalidException(int line, int column, string message) : Exception(message)
{
    public int Line { get; } = line;

    public int Column { get; } = column;
}

/// <summary>
/// One YAML document to one <see cref="JsonNode"/>, by the YAML 1.2 core schema and nothing else. The object
/// mapping YamlDotNet offers is deliberately unused: the manifest is validated by our own rules against plain
/// JSON, so no quirk of a deserializer can become a typed surprise, and the rules read the same document the
/// author wrote.
/// </summary>
public static partial class YamlToJson
{
    /// <summary>Deep enough for any manifest, shallow enough that a hand-written document cannot exhaust a stack.</summary>
    public const int MaxDepth = 32;

    public static JsonNode? Convert(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var parser = new Parser(new StringReader(yaml));
        try
        {
            parser.Consume<StreamStart>();
            if (parser.TryConsume<StreamEnd>(out _))
            {
                return null;
            }

            parser.Consume<DocumentStart>();
            var node = ReadNode(parser, 1);
            parser.Consume<DocumentEnd>();

            if (!parser.TryConsume<StreamEnd>(out _))
            {
                var next = parser.Current;
                throw new YamlInvalidException(
                    (int)(next?.Start.Line ?? 0),
                    (int)(next?.Start.Column ?? 0),
                    "A plugin manifest is one YAML document; this file holds more than one.");
            }

            return node;
        }
        catch (YamlException exception)
        {
            throw new YamlInvalidException((int)exception.Start.Line, (int)exception.Start.Column, exception.Message);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            // The scanner reports some malformed documents by throwing rather than by a YamlException; where it
            // stopped is still the useful half of the answer.
            var mark = parser.Current?.Start ?? Mark.Empty;
            throw new YamlInvalidException((int)mark.Line, (int)mark.Column, "The manifest is not valid YAML: " + exception.Message);
        }
    }

    private static JsonNode? ReadNode(IParser parser, int depth)
    {
        if (depth > MaxDepth)
        {
            var here = parser.Current;
            throw new YamlInvalidException(
                (int)(here?.Start.Line ?? 0),
                (int)(here?.Start.Column ?? 0),
                string.Create(CultureInfo.InvariantCulture, $"A plugin manifest nests at most {MaxDepth} levels deep."));
        }

        // An anchor that expands somewhere else would make "what does this file declare" a question only a
        // parser can answer; a manifest is read by people too.
        if (parser.Accept<AnchorAlias>(out var alias))
        {
            throw new YamlInvalidException((int)alias.Start.Line, (int)alias.Start.Column, "A plugin manifest uses no anchors or aliases.");
        }

        if (parser.TryConsume<Scalar>(out var scalar))
        {
            return ReadScalar(scalar);
        }

        if (parser.TryConsume<SequenceStart>(out _))
        {
            var array = new JsonArray();
            while (!parser.TryConsume<SequenceEnd>(out _))
            {
                array.Add(ReadNode(parser, depth + 1));
            }

            return array;
        }

        parser.Consume<MappingStart>();
        var mapping = new JsonObject();
        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>();
            var value = ReadNode(parser, depth + 1);
            if (!mapping.TryAdd(key.Value, value))
            {
                throw new YamlInvalidException((int)key.Start.Line, (int)key.Start.Column, $"The key '{key.Value}' is written twice.");
            }
        }

        return mapping;
    }

    /// <summary>
    /// A quoted scalar is a string, whatever it looks like; a plain one is read by the core schema, where
    /// <c>yes</c> is a word and <c>0644</c> is text — YAML 1.2, not the 1.1 surprises.
    /// </summary>
    private static JsonNode? ReadScalar(Scalar scalar)
    {
        if (scalar.Style is not (ScalarStyle.Plain or ScalarStyle.Any))
        {
            return JsonValue.Create(scalar.Value);
        }

        var value = scalar.Value;
        if (value.Length == 0 || value is "~" or "null" or "Null" or "NULL")
        {
            return null;
        }

        if (value is "true" or "True" or "TRUE")
        {
            return JsonValue.Create(true);
        }

        if (value is "false" or "False" or "FALSE")
        {
            return JsonValue.Create(false);
        }

        if (Decimal().IsMatch(value) && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonValue.Create(integer);
        }

        if (Hexadecimal().IsMatch(value) && long.TryParse(value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hex))
        {
            return JsonValue.Create(hex);
        }

        if (Octal().IsMatch(value))
        {
            return JsonValue.Create(System.Convert.ToInt64(value[2..], 8));
        }

        if (Float().IsMatch(value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
        {
            // Parsing a float that overflows answers with an infinity rather than failing, and JSON has no
            // spelling for one: carried onwards it would make every reader of this manifest throw where it should
            // report. The number is refused here, where the line it is written on is still known.
            if (!double.IsFinite(real))
            {
                throw new YamlInvalidException(
                    (int)scalar.Start.Line,
                    (int)scalar.Start.Column,
                    $"'{value}' is larger than a number this manifest can carry.");
            }

            return JsonValue.Create(real);
        }

        return JsonValue.Create(value);
    }

    [GeneratedRegex(@"^[-+]?[0-9]+$")]
    private static partial Regex Decimal();

    [GeneratedRegex("^0x[0-9a-fA-F]+$")]
    private static partial Regex Hexadecimal();

    [GeneratedRegex("^0o[0-7]+$")]
    private static partial Regex Octal();

    [GeneratedRegex(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$")]
    private static partial Regex Float();
}
