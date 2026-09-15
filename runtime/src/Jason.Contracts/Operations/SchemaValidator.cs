using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Jason.Contracts.Operations;

/// <summary>
/// The restricted JSON Schema dialect Jason publishes, and the only code that enforces it. The subset is small on
/// purpose: a plugin author's fixtures stay portable to any 2020-12 validator, and a keyword we would silently
/// ignore — a rule that does not run — is refused instead of accepted.
/// </summary>
/// <remarks>
/// Both entry points are fed schemas written by third parties, so neither may be made to hang or to throw. Patterns
/// run without backtracking and under a match timeout, references resolve only inside their own document and never
/// in a loop, and descent stops at a fixed depth.
/// </remarks>
public static class SchemaValidator
{
    /// <summary>How far into a schema and its value the validator descends before it refuses to go on.</summary>
    public const int MaxDepth = 32;

    /// <summary>The largest schema the dialect check reads, matching the manifest's own binding budget.</summary>
    public const int MaxSchemaBytes = 64 * 1024;

    /// <summary>The published vocabulary, in the order the documentation lists it.</summary>
    public static IReadOnlyList<string> Keywords { get; } =
    [
        "$schema", "$id", "$defs", "$ref",
        "type", "properties", "required", "additionalProperties",
        "enum", "const",
        "minLength", "maxLength", "pattern",
        "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf",
        "items", "minItems", "maxItems", "uniqueItems",
        "anyOf", "allOf", "not",
        "format",
        "title", "description", "examples",
    ];

    /// <summary>The one string <c>format</c> may name: the others mean different things to different validators.</summary>
    public const string SupportedFormat = "date-time";

    private const int MaxCachedPatterns = 512;
    private const string RefPrefix = "#/$defs/";

    /// <summary>The range a double may be in before the cast to decimal would overflow.</summary>
    private const double MinDecimal = -7.9e28;

    private const double MaxDecimal = 7.9e28;

    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly ConcurrentDictionary<string, Regex?> CompiledPatterns = new(StringComparer.Ordinal);

    private static readonly HashSet<string> TypeNames = new(StringComparer.Ordinal)
    {
        "object", "array", "string", "number", "integer", "boolean", "null",
    };

    /// <summary>Validates a value against a schema of this dialect; an empty list means valid.</summary>
    /// <remarks>
    /// Every failure is reported, not the first, because a caller fixing its arguments should see all of them at
    /// once. A defect in the schema itself is reported at the value's own pointer with the schema-level reason
    /// code, since that is the place a reader is looking when the rule fails to run.
    /// </remarks>
    public static IReadOnlyList<SchemaProblem> Validate(JsonNode? value, JsonObject schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var problems = new List<SchemaProblem>();
        Apply(value, schema, schema, string.Empty, 0, problems, []);
        return problems;
    }

    /// <summary>
    /// Checks a schema itself against the published dialect. This is what a manifest's binding schema passes before
    /// the runtime will ever apply it, so the pointers here address the schema document rather than any value.
    /// </summary>
    public static IReadOnlyList<SchemaProblem> CheckDialect(JsonObject schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var problems = new List<SchemaProblem>();

        // First, because everything after it reads the schema by writing it out. A schema built in memory can hold
        // an infinity or a not-a-number, JSON has no spelling for either, and a writer handed one throws — which
        // is how a single package once took every plugin on a machine down without a problem code naming it.
        CheckFinite(schema, string.Empty, 0, problems);
        if (problems.Count != 0)
        {
            return problems;
        }

        if (TooLarge(schema))
        {
            problems.Add(new SchemaProblem(
                string.Empty,
                "schema_too_large",
                string.Create(CultureInfo.InvariantCulture, $"A schema is at most {MaxSchemaBytes} bytes of UTF-8, and this one is larger.")));
            return problems;
        }

        CheckSchema(schema, schema, string.Empty, 0, problems);
        return problems;
    }

    private static bool TooLarge(JsonObject schema)
    {
        try
        {
            return Encoding.UTF8.GetByteCount(schema.ToJsonString()) > MaxSchemaBytes;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            // A schema too deep to write is refused by the depth rule below, which says something more useful, and
            // one holding a number no writer will write is refused above. Neither may leave here as an exception:
            // measuring a document is not the place a caller learns what is wrong with it.
            return false;
        }
    }

    /// <summary>
    /// Every number in the schema, held against the one thing JSON insists on: that it can be written down. Only a
    /// document built in memory can fail this — text spelling <c>1e400</c> parses to an infinity — which is exactly
    /// the shape a manifest's binding schema arrives in, read from YAML rather than from JSON.
    /// </summary>
    private static void CheckFinite(JsonNode? node, string pointer, int depth, List<SchemaProblem> problems)
    {
        if (depth > MaxDepth)
        {
            // Deeper than this is refused by the depth rule, and the writer is guarded wherever it runs.
            return;
        }

        switch (node)
        {
            // An object nothing can read holds no numbers this walk could weigh either; the dialect check below
            // reaches the same object and is where it is named.
            case JsonObject map when Unreadable(map):
                break;

            case JsonObject map:
                foreach (var (name, child) in map)
                {
                    CheckFinite(child, Child(pointer, name), depth + 1, problems);
                }

                break;

            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    CheckFinite(array[index], Child(pointer, index.ToString(CultureInfo.InvariantCulture)), depth + 1, problems);
                }

                break;

            case JsonValue value when NotFinite(value):
                problems.Add(new SchemaProblem(
                    pointer,
                    "schema_number_not_finite",
                    "A number here is an infinity or a not-a-number, which JSON cannot write, so no validator could read this schema back."));
                break;

            default:
                break;
        }
    }

    private static bool NotFinite(JsonValue value) =>
        value.GetValueKind() == JsonValueKind.Number
        && ((value.TryGetValue(out double real) && !double.IsFinite(real))
            || (value.TryGetValue(out float single) && !float.IsFinite(single)));

    // ---------------------------------------------------------------------------------------------------------
    // Applying a schema to a value
    // ---------------------------------------------------------------------------------------------------------

    private static void Apply(
        JsonNode? value,
        JsonObject schema,
        JsonObject root,
        string pointer,
        int depth,
        List<SchemaProblem> problems,
        HashSet<(string Target, string Pointer)> openReferences)
    {
        if (depth > MaxDepth)
        {
            problems.Add(new SchemaProblem(
                pointer,
                "schema_too_deep",
                string.Create(CultureInfo.InvariantCulture, $"This dialect validates {MaxDepth} levels of nesting, and the document goes deeper.")));
            return;
        }

        // Every object value reaches this line, because descent into a property, an item or a branch comes back
        // through here, so asking once is asking of all of them.
        if ((value is JsonObject instance && Unreadable(instance)) || Unreadable(schema))
        {
            problems.Add(Duplicated(pointer));
            return;
        }

        foreach (var (keyword, argument) in schema)
        {
            ApplyKeyword(keyword, argument, value, schema, root, pointer, depth, problems, openReferences);
        }
    }

    private static void ApplyKeyword(
        string keyword,
        JsonNode? argument,
        JsonNode? value,
        JsonObject schema,
        JsonObject root,
        string pointer,
        int depth,
        List<SchemaProblem> problems,
        HashSet<(string Target, string Pointer)> openReferences)
    {
        switch (keyword)
        {
            case "$schema" or "$id" or "title" or "description" or "examples" or "$defs":
                break;

            case "$ref":
                ApplyRef(argument, value, root, pointer, depth, problems, openReferences);
                break;

            case "type":
                ApplyType(argument, value, pointer, problems);
                break;

            case "properties":
                ApplyProperties(argument, value, root, pointer, depth, problems, openReferences);
                break;

            case "required":
                ApplyRequired(argument, value, pointer, problems);
                break;

            case "additionalProperties":
                ApplyAdditionalProperties(argument, value, schema, pointer, problems);
                break;

            case "enum":
                ApplyEnum(argument, value, pointer, problems);
                break;

            case "const":
                if (!SameValue(argument, value))
                {
                    problems.Add(new SchemaProblem(pointer, "const", $"The only value accepted here is {Canonical(argument, 0)}."));
                }

                break;

            case "minLength" or "maxLength":
                ApplyLength(keyword, argument, value, pointer, problems);
                break;

            case "pattern":
                ApplyPattern(argument, value, pointer, problems);
                break;

            case "minimum" or "maximum" or "exclusiveMinimum" or "exclusiveMaximum" or "multipleOf":
                ApplyNumeric(keyword, argument, value, pointer, problems);
                break;

            case "items":
                ApplyItems(argument, value, root, pointer, depth, problems, openReferences);
                break;

            case "minItems" or "maxItems":
                ApplyItemCount(keyword, argument, value, pointer, problems);
                break;

            case "uniqueItems":
                ApplyUniqueItems(argument, value, pointer, problems);
                break;

            case "anyOf" or "allOf":
                ApplyCombinator(keyword, argument, value, root, pointer, depth, problems, openReferences);
                break;

            case "not":
                ApplyNot(argument, value, root, pointer, depth, problems, openReferences);
                break;

            case "format":
                ApplyFormat(argument, value, pointer, problems);
                break;

            default:
                problems.Add(new SchemaProblem(
                    pointer,
                    "schema_keyword_unknown",
                    $"`{keyword}` is not part of this dialect, so the rule it states would not have run."));
                break;
        }
    }

    private static void ApplyRef(
        JsonNode? argument,
        JsonNode? value,
        JsonObject root,
        string pointer,
        int depth,
        List<SchemaProblem> problems,
        HashSet<(string Target, string Pointer)> openReferences)
    {
        if (!TryString(argument, out var target))
        {
            problems.Add(Malformed(pointer, "$ref", "a string naming a definition in the same document"));
            return;
        }

        var resolved = Resolve(root, target);
        if (resolved is null)
        {
            problems.Add(new SchemaProblem(
                pointer,
                "schema_ref_unresolved",
                $"`{target}` names no definition here; this dialect resolves `{RefPrefix}<name>` and nothing else."));
            return;
        }

        // A reference is a cycle when it comes back to the same definition over the same value: the chain would
        // never consume anything and never end.
        var visit = (target, pointer);
        if (!openReferences.Add(visit))
        {
            problems.Add(new SchemaProblem(pointer, "schema_cyclic", $"`{target}` refers back to itself without consuming any of the value."));
            return;
        }

        Apply(value, resolved, root, pointer, depth + 1, problems, openReferences);
        openReferences.Remove(visit);
    }

    private static void ApplyType(JsonNode? argument, JsonNode? value, string pointer, List<SchemaProblem> problems)
    {
        var accepted = new List<string>();
        if (TryString(argument, out var single))
        {
            accepted.Add(single);
        }
        else if (argument is JsonArray array)
        {
            foreach (var entry in array)
            {
                if (TryString(entry, out var name))
                {
                    accepted.Add(name);
                }
            }
        }

        if (accepted.Count == 0 || accepted.Exists(name => !TypeNames.Contains(name)))
        {
            problems.Add(Malformed(pointer, "type", "one of " + string.Join(", ", TypeNames.Order(StringComparer.Ordinal)) + ", or a list of them"));
            return;
        }

        if (!accepted.Exists(name => IsOfType(name, value)))
        {
            problems.Add(new SchemaProblem(
                pointer,
                "type",
                $"Expected {string.Join(" or ", accepted)} here, and this is {KindOf(value)}."));
        }
    }

    private static void ApplyProperties(
        JsonNode? argument,
        JsonNode? value,
        JsonObject root,
        string pointer,
        int depth,
        List<SchemaProblem> problems,
        HashSet<(string Target, string Pointer)> openReferences)
    {
        if (argument is not JsonObject declared)
        {
            problems.Add(Malformed(pointer, "properties", "an object of property names to schemas"));
            return;
        }

        if (Unreadable(declared))
        {
            problems.Add(Duplicated(pointer));
            return;
        }

        if (value is not JsonObject instance)
        {
            return;
        }

        foreach (var (name, sub) in declared)
        {
            if (!instance.TryGetPropertyValue(name, out var property))
            {
                continue;
            }

            if (sub is JsonObject subSchema)
            {
                Apply(property, subSchema, root, Child(pointer, name), depth + 1, problems, openReferences);
            }
            else
            {
                problems.Add(Malformed(Child(pointer, name), "properties", "a schema, which is always an object"));
            }
        }

    }

    private static void ApplyRequired(JsonNode? argument, JsonNode? value, string pointer, List<SchemaProblem> problems)
    {
        if (argument is not JsonArray names)
        {
            problems.Add(Malformed(pointer, "required", "an array of property names"));
            return;
        }

        if (value is not JsonObject instance)
        {
            return;
        }

        foreach (var entry in names)
        {
            if (!TryString(entry, out var name))
            {
                problems.Add(Malformed(pointer, "required", "an array of property names"));
                continue;
            }

            // An explicit null satisfies `required`: the caller said something, and saying null is saying something.
            if (!instance.ContainsKey(name))
            {
                problems.Add(new SchemaProblem(Child(pointer, name), "required", $"`{name}` is required here."));
            }
        }
    }

    private static void ApplyAdditionalProperties(
        JsonNode? argument,
        JsonNode? value,
        JsonObject schema,
        string pointer,
        List<SchemaProblem> problems)
    {
        if (!TryBoolean(argument, out var allowed))
        {
            problems.Add(Malformed(pointer, "additionalProperties", "true or false; this dialect has no schema form of it"));
            return;
        }

        if (allowed || value is not JsonObject instance)
        {
            return;
        }

        var declared = schema["properties"] as JsonObject;
        if (declared is not null && Unreadable(declared))
        {
            problems.Add(Duplicated(pointer));
            return;
        }

        foreach (var (name, _) in instance)
        {
            if (declared is null || !declared.ContainsKey(name))
            {
                problems.Add(new SchemaProblem(
                    Child(pointer, name),
                    "additional_properties",
                    $"`{name}` is not a property this schema declares, and it accepts no others."));
            }
        }
    }

    private static void ApplyEnum(JsonNode? argument, JsonNode? value, string pointer, List<SchemaProblem> problems)
    {
        if (argument is not JsonArray allowed || allowed.Count == 0)
        {
            problems.Add(Malformed(pointer, "enum", "a non-empty array of the values this place accepts"));
            return;
        }

        foreach (var candidate in allowed)
        {
            if (SameValue(candidate, value))
            {
                return;
            }
        }

        problems.Add(new SchemaProblem(
            pointer,
            "enum",
            "The values accepted here are " + string.Join(", ", allowed.Select(entry => Canonical(entry, 0))) + "."));
    }

    private static void ApplyLength(string keyword, JsonNode? argument, JsonNode? value, string pointer, List<SchemaProblem> problems)
    {
        if (!TryInteger(argument, out var bound) || bound < 0)
        {
            problems.Add(Malformed(pointer, keyword, "a whole number of characters, zero or more"));
            return;
        }

        if (!TryString(value, out var text))
        {
            return;
        }

        var length = text.EnumerateRunes().Count();
        if (keyword == "minLength" && length < bound)
        {
            problems.Add(new SchemaProblem(pointer, "min_length", string.Create(CultureInfo.InvariantCulture, $"At least {bound} characters are expected here.")));
        }
        else if (keyword == "maxLength" && length > bound)
        {
            problems.Add(new SchemaProblem(pointer, "max_length", string.Create(CultureInfo.InvariantCulture, $"At most {bound} characters are accepted here.")));
        }
    }

    private static void ApplyPattern(JsonNode? argument, JsonNode? value, string pointer, List<SchemaProblem> problems)
    {
        if (!TryString(argument, out var pattern))
        {
            problems.Add(Malformed(pointer, "pattern", "a regular expression written as a string"));
            return;
        }

        var regex = Compile(pattern);
        if (regex is null)
        {
            problems.Add(new SchemaProblem(
                pointer,
                "schema_pattern_invalid",
                $"`{pattern}` is not a pattern this dialect can run: it must compile without backtracking, which rules out lookaround and backreferences."));
            return;
        }

        if (!TryString(value, out var text))
        {
            return;
        }

        try
        {
            if (!regex.IsMatch(text))
            {
                problems.Add(new SchemaProblem(pointer, "pattern", $"The value here must match `{pattern}`."));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            problems.Add(new SchemaProblem(pointer, "schema_pattern_invalid", $"`{pattern}` did not finish matching within the time this dialect allows."));
        }
    }

    private static void ApplyNumeric(string keyword, JsonNode? argument, JsonNode? value, string pointer, List<SchemaProblem> problems)
    {
        if (!TryNumber(argument, out var bound) || (keyword == "multipleOf" && bound <= 0))
        {
            problems.Add(Malformed(pointer, keyword, keyword == "multipleOf" ? "a number greater than zero" : "a number"));
            return;
        }

        if (!TryNumber(value, out var number))
        {
            return;
        }

        switch (keyword)
        {
            case "minimum" when number < bound:
                problems.Add(new SchemaProblem(pointer, "minimum", Bounded("at least", bound)));
                break;
            case "maximum" when number > bound:
                problems.Add(new SchemaProblem(pointer, "maximum", Bounded("at most", bound)));
                break;
            case "exclusiveMinimum" when number <= bound:
                problems.Add(new SchemaProblem(pointer, "exclusive_minimum", Bounded("greater than", bound)));
                break;
            case "exclusiveMaximum" when number >= bound:
                problems.Add(new SchemaProblem(pointer, "exclusive_maximum", Bounded("less than", bound)));
                break;
            case "multipleOf" when number % bound != 0:
                problems.Add(new SchemaProblem(pointer, "multiple_of", Bounded("a multiple of", bound)));
                break;
            default:
                break;
        }
    }

    private static string Bounded(string relation, decimal bound) =>
        string.Create(CultureInfo.InvariantCulture, $"The value here must be {relation} {bound}.");

    private static void ApplyItems(
        JsonNode? argument,
        JsonNode? value,
        JsonObject root,
        string pointer,
        int depth,
        List<SchemaProblem> problems,
        HashSet<(string Target, string Pointer)> openReferences)
    {
        if (argument is not JsonObject itemSchema)
        {
            problems.Add(Malformed(pointer, "items", "a schema, which is always an object"));
            return;
        }

        if (value is not JsonArray array)
        {
            return;
        }

        for (var index = 0; index < array.Count; index++)
        {
            Apply(array[index], itemSchema, root, Child(pointer, index.ToString(CultureInfo.InvariantCulture)), depth + 1, problems, openReferences);
        }
    }

    private static void ApplyItemCount(string keyword, JsonNode? argument, JsonNode? value, string pointer, List<SchemaProblem> problems)
    {
        if (!TryInteger(argument, out var bound) || bound < 0)
        {
            problems.Add(Malformed(pointer, keyword, "a whole number of items, zero or more"));
            return;
        }

        if (value is not JsonArray array)
        {
            return;
        }

        if (keyword == "minItems" && array.Count < bound)
        {
            problems.Add(new SchemaProblem(pointer, "min_items", string.Create(CultureInfo.InvariantCulture, $"At least {bound} items are expected here.")));
        }
        else if (keyword == "maxItems" && array.Count > bound)
        {
            problems.Add(new SchemaProblem(pointer, "max_items", string.Create(CultureInfo.InvariantCulture, $"At most {bound} items are accepted here.")));
        }
    }

    private static void ApplyUniqueItems(JsonNode? argument, JsonNode? value, string pointer, List<SchemaProblem> problems)
    {
        if (!TryBoolean(argument, out var unique))
        {
            problems.Add(Malformed(pointer, "uniqueItems", "true or false"));
            return;
        }

        if (!unique || value is not JsonArray array)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in array)
        {
            if (!seen.Add(Canonical(item, 0)))
            {
                problems.Add(new SchemaProblem(pointer, "unique_items", "Every item here must differ from every other."));
                return;
            }
        }
    }

    private static void ApplyCombinator(
        string keyword,
        JsonNode? argument,
        JsonNode? value,
        JsonObject root,
        string pointer,
        int depth,
        List<SchemaProblem> problems,
        HashSet<(string Target, string Pointer)> openReferences)
    {
        if (argument is not JsonArray branches || branches.Count == 0)
        {
            problems.Add(Malformed(pointer, keyword, "a non-empty array of schemas"));
            return;
        }

        // A combinator reports once, at its own pointer: the branches' own problems describe roads not taken, and
        // listing them all would bury the one thing the caller has to change.
        var first = -1;
        SchemaProblem? firstProblem = null;
        var satisfied = 0;

        for (var index = 0; index < branches.Count; index++)
        {
            if (branches[index] is not JsonObject branch)
            {
                problems.Add(Malformed(pointer, keyword, "a non-empty array of schemas"));
                return;
            }

            var branchProblems = new List<SchemaProblem>();
            Apply(value, branch, root, pointer, depth + 1, branchProblems, openReferences);
            if (branchProblems.Count == 0)
            {
                satisfied++;
            }
            else if (firstProblem is null)
            {
                first = index;
                firstProblem = branchProblems[0];
            }
        }

        if (keyword == "anyOf" && satisfied == 0)
        {
            problems.Add(new SchemaProblem(
                pointer,
                "any_of",
                $"No alternative this schema allows accepts the value here; the first reported `{firstProblem!.Reason}` at `{Shown(firstProblem.Pointer)}`."));
        }
        else if (keyword == "allOf" && satisfied < branches.Count)
        {
            problems.Add(new SchemaProblem(
                pointer,
                "all_of",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Every requirement here has to hold, and requirement {first + 1} reported `{firstProblem!.Reason}` at `{Shown(firstProblem.Pointer)}`.")));
        }
    }

    private static void ApplyNot(
        JsonNode? argument,
        JsonNode? value,
        JsonObject root,
        string pointer,
        int depth,
        List<SchemaProblem> problems,
        HashSet<(string Target, string Pointer)> openReferences)
    {
        if (argument is not JsonObject forbidden)
        {
            problems.Add(Malformed(pointer, "not", "a schema, which is always an object"));
            return;
        }

        var branchProblems = new List<SchemaProblem>();
        Apply(value, forbidden, root, pointer, depth + 1, branchProblems, openReferences);
        if (branchProblems.Count == 0)
        {
            problems.Add(new SchemaProblem(pointer, "not", "The value here is one this schema excludes."));
        }
    }

    private static void ApplyFormat(JsonNode? argument, JsonNode? value, string pointer, List<SchemaProblem> problems)
    {
        if (!TryString(argument, out var format) || format != SupportedFormat)
        {
            problems.Add(new SchemaProblem(
                pointer,
                "format",
                $"`{SupportedFormat}` is the only format this dialect validates, because the others mean different things to different validators."));
            return;
        }

        if (TryString(value, out var text) && !IsRfc3339DateTime(text))
        {
            problems.Add(new SchemaProblem(pointer, "format", "A timestamp here is ISO-8601 with an offset, as in `2026-09-15T12:00:00Z`."));
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Checking a schema against the dialect
    // ---------------------------------------------------------------------------------------------------------

    private static void CheckSchema(JsonObject schema, JsonObject root, string pointer, int depth, List<SchemaProblem> problems)
    {
        if (depth > MaxDepth)
        {
            problems.Add(new SchemaProblem(
                pointer,
                "schema_too_deep",
                string.Create(CultureInfo.InvariantCulture, $"This dialect validates {MaxDepth} levels of nesting, and the schema goes deeper.")));
            return;
        }

        if (Unreadable(schema))
        {
            problems.Add(Duplicated(pointer));
            return;
        }

        foreach (var (keyword, argument) in schema)
        {
            var at = Child(pointer, keyword);
            switch (keyword)
            {
                case "$schema" or "$id" or "title" or "description":
                    if (!TryString(argument, out _))
                    {
                        problems.Add(Malformed(at, keyword, "a string"));
                    }

                    break;

                case "examples":
                    if (argument is not JsonArray)
                    {
                        problems.Add(Malformed(at, keyword, "an array of example values"));
                    }

                    break;

                case "$defs":
                    CheckDefinitions(argument, root, at, depth, problems);
                    break;

                case "$ref":
                    CheckRef(argument, root, at, problems);
                    break;

                case "type":
                    CheckType(argument, at, problems);
                    break;

                case "properties":
                    CheckSubSchemaMap(argument, root, at, depth, problems, "an object of property names to schemas");
                    break;

                case "required":
                    CheckStringArray(argument, at, problems, "an array of property names");
                    break;

                case "additionalProperties" or "uniqueItems":
                    if (!TryBoolean(argument, out _))
                    {
                        problems.Add(Malformed(at, keyword, "true or false"));
                    }

                    break;

                case "enum":
                    if (argument is not JsonArray { Count: > 0 })
                    {
                        problems.Add(Malformed(at, keyword, "a non-empty array of the values this place accepts"));
                    }

                    break;

                case "const":
                    break;

                case "minLength" or "maxLength" or "minItems" or "maxItems":
                    if (!TryInteger(argument, out var count) || count < 0)
                    {
                        problems.Add(Malformed(at, keyword, "a whole number, zero or more"));
                    }

                    break;

                case "pattern":
                    CheckPattern(argument, at, problems);
                    break;

                case "minimum" or "maximum" or "exclusiveMinimum" or "exclusiveMaximum":
                    if (!TryNumber(argument, out _))
                    {
                        problems.Add(Malformed(at, keyword, "a number"));
                    }

                    break;

                case "multipleOf":
                    if (!TryNumber(argument, out var divisor) || divisor <= 0)
                    {
                        problems.Add(Malformed(at, keyword, "a number greater than zero"));
                    }

                    break;

                case "items" or "not":
                    CheckSubSchema(argument, root, at, depth, problems);
                    break;

                case "anyOf" or "allOf":
                    CheckSubSchemaArray(argument, root, at, depth, problems);
                    break;

                case "format":
                    if (!TryString(argument, out var format) || format != SupportedFormat)
                    {
                        problems.Add(new SchemaProblem(
                            at,
                            "format",
                            $"`{SupportedFormat}` is the only format this dialect validates, because the others mean different things to different validators."));
                    }

                    break;

                default:
                    problems.Add(new SchemaProblem(
                        at,
                        "schema_keyword_unknown",
                        $"`{keyword}` is not part of this dialect, so the rule it states would not run."));
                    break;
            }
        }
    }

    private static void CheckDefinitions(JsonNode? argument, JsonObject root, string pointer, int depth, List<SchemaProblem> problems) =>
        CheckSubSchemaMap(argument, root, pointer, depth, problems, "an object of definition names to schemas");

    private static void CheckSubSchemaMap(JsonNode? argument, JsonObject root, string pointer, int depth, List<SchemaProblem> problems, string expected)
    {
        if (argument is not JsonObject map)
        {
            problems.Add(MalformedAt(pointer, expected));
            return;
        }

        if (Unreadable(map))
        {
            problems.Add(Duplicated(pointer));
            return;
        }

        foreach (var (name, sub) in map)
        {
            CheckSubSchema(sub, root, Child(pointer, name), depth, problems);
        }
    }

    private static void CheckSubSchemaArray(JsonNode? argument, JsonObject root, string pointer, int depth, List<SchemaProblem> problems)
    {
        if (argument is not JsonArray { Count: > 0 } branches)
        {
            problems.Add(MalformedAt(pointer, "a non-empty array of schemas"));
            return;
        }

        for (var index = 0; index < branches.Count; index++)
        {
            CheckSubSchema(branches[index], root, Child(pointer, index.ToString(CultureInfo.InvariantCulture)), depth, problems);
        }
    }

    private static void CheckSubSchema(JsonNode? argument, JsonObject root, string pointer, int depth, List<SchemaProblem> problems)
    {
        if (argument is JsonObject sub)
        {
            CheckSchema(sub, root, pointer, depth + 1, problems);
        }
        else
        {
            problems.Add(MalformedAt(pointer, "a schema, which is always an object"));
        }
    }

    private static void CheckStringArray(JsonNode? argument, string pointer, List<SchemaProblem> problems, string expected)
    {
        if (argument is not JsonArray entries)
        {
            problems.Add(MalformedAt(pointer, expected));
            return;
        }

        foreach (var entry in entries)
        {
            if (!TryString(entry, out _))
            {
                problems.Add(MalformedAt(pointer, expected));
                return;
            }
        }
    }

    private static void CheckType(JsonNode? argument, string pointer, List<SchemaProblem> problems)
    {
        if (TryString(argument, out var single))
        {
            if (!TypeNames.Contains(single))
            {
                problems.Add(MalformedAt(pointer, "one of " + string.Join(", ", TypeNames.Order(StringComparer.Ordinal))));
            }

            return;
        }

        if (argument is not JsonArray { Count: > 0 } names)
        {
            problems.Add(MalformedAt(pointer, "a type name or a non-empty list of them"));
            return;
        }

        foreach (var entry in names)
        {
            if (!TryString(entry, out var name) || !TypeNames.Contains(name))
            {
                problems.Add(MalformedAt(pointer, "one of " + string.Join(", ", TypeNames.Order(StringComparer.Ordinal))));
                return;
            }
        }
    }

    private static void CheckPattern(JsonNode? argument, string pointer, List<SchemaProblem> problems)
    {
        if (!TryString(argument, out var pattern))
        {
            problems.Add(MalformedAt(pointer, "a regular expression written as a string"));
            return;
        }

        if (Compile(pattern) is null)
        {
            problems.Add(new SchemaProblem(
                pointer,
                "schema_pattern_invalid",
                $"`{pattern}` is not a pattern this dialect can run: it must compile without backtracking, which rules out lookaround and backreferences."));
        }
    }

    private static void CheckRef(JsonNode? argument, JsonObject root, string pointer, List<SchemaProblem> problems)
    {
        if (!TryString(argument, out var target))
        {
            problems.Add(MalformedAt(pointer, "a string naming a definition in the same document"));
            return;
        }

        if (Resolve(root, target) is null)
        {
            problems.Add(new SchemaProblem(
                pointer,
                "schema_ref_unresolved",
                $"`{target}` names no definition here; this dialect resolves `{RefPrefix}<name>` and nothing else."));
            return;
        }

        // A definition is checked once, where it is defined. What is followed here is the chain of references that
        // consume nothing — the shape that would spin for ever rather than terminate on the value.
        var seen = new HashSet<string>(StringComparer.Ordinal) { target };
        var next = Resolve(root, target);
        while (next is not null && next.Count == 1 && TryString(next["$ref"], out var onwards))
        {
            if (!seen.Add(onwards))
            {
                problems.Add(new SchemaProblem(pointer, "schema_cyclic", $"`{target}` refers back to itself through a chain that consumes nothing."));
                return;
            }

            next = Resolve(root, onwards);
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Values, pointers and patterns
    // ---------------------------------------------------------------------------------------------------------

    private static SchemaProblem Malformed(string pointer, string keyword, string expected) =>
        new(pointer, "type", $"`{keyword}` must be {expected}, so the rule it states did not run.");

    /// <summary>The same refusal where the pointer already names the place: the dialect check addresses the schema.</summary>
    private static SchemaProblem MalformedAt(string pointer, string expected) =>
        new(pointer, "type", $"What stands here must be {expected}, so the rule it states would not run.");

    private static SchemaProblem Duplicated(string pointer) =>
        new(pointer, "duplicate_property", "An object here writes the same property twice, so which of the two it means is not decidable.");

    /// <summary>
    /// Whether an object cannot be read at all. A parser may accept a property written twice and build the
    /// dictionary only at the first read, which then throws — so every object neither entry point built itself is
    /// asked this before it is read. "I cannot read this" is one of the things that can be wrong with a document,
    /// and reporting what is wrong rather than throwing is the whole promise of this code.
    /// </summary>
    private static bool Unreadable(JsonObject map)
    {
        try
        {
            _ = map.Count;
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static string Shown(string pointer) => pointer.Length == 0 ? "/" : pointer;

    private static string Child(string pointer, string segment) =>
        pointer + "/" + segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static JsonObject? Resolve(JsonObject root, string target)
    {
        if (!target.StartsWith(RefPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var name = target[RefPrefix.Length..]
            .Replace("~1", "/", StringComparison.Ordinal)
            .Replace("~0", "~", StringComparison.Ordinal);

        // A set of definitions nothing can read resolves nothing: the caller reports the reference as unresolved,
        // which is what it is, and the object itself is named where the walk over the document reaches it.
        return name.Length != 0
            && !name.Contains('/', StringComparison.Ordinal)
            && root["$defs"] is JsonObject definitions
            && !Unreadable(definitions)
            && definitions.TryGetPropertyValue(name, out var definition)
                ? definition as JsonObject
                : null;
    }

    private static Regex? Compile(string pattern)
    {
        if (CompiledPatterns.TryGetValue(pattern, out var cached))
        {
            return cached;
        }

        var compiled = Build(pattern);
        if (CompiledPatterns.Count < MaxCachedPatterns)
        {
            // The cache is bounded because the patterns come from third-party manifests: a package with ten
            // thousand distinct patterns must cost memory once, not for ever.
            CompiledPatterns[pattern] = compiled;
        }

        return compiled;
    }

    private static Regex? Build(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant, PatternTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string KindOf(JsonNode? node) => node switch
    {
        null => "null",
        JsonObject => "object",
        JsonArray => "array",
        _ => node.GetValueKind() switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            _ => "null",
        },
    };

    private static bool IsOfType(string type, JsonNode? value) => type switch
    {
        "integer" => TryNumber(value, out var number) && number == decimal.Truncate(number),
        _ => KindOf(value) == type,
    };

    private static bool TryString(JsonNode? node, out string value)
    {
        if (node is JsonValue candidate && node.GetValueKind() == JsonValueKind.String && candidate.TryGetValue(out string? text) && text is not null)
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryBoolean(JsonNode? node, out bool value)
    {
        if (node is JsonValue candidate && node.GetValueKind() is JsonValueKind.True or JsonValueKind.False && candidate.TryGetValue(out bool parsed))
        {
            value = parsed;
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryInteger(JsonNode? node, out int value)
    {
        if (TryNumber(node, out var number) && number == decimal.Truncate(number) && number >= int.MinValue && number <= int.MaxValue)
        {
            value = (int)number;
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryNumber(JsonNode? node, out decimal value)
    {
        value = 0m;
        if (node is not JsonValue candidate || node.GetValueKind() != JsonValueKind.Number)
        {
            return false;
        }

        if (candidate.TryGetValue(out decimal exact))
        {
            value = exact;
            return true;
        }

        // A node parsed from text holds an element that converts to any numeric type; one built in memory — a
        // manifest's binding schema, read from YAML — holds the CLR value it was given and converts to that type
        // alone. Both are the same number, and a rule that ran for one has to run for the other.
        if (candidate.TryGetValue(out long whole))
        {
            value = whole;
            return true;
        }

        if (candidate.TryGetValue(out double real) && double.IsFinite(real) && real is >= MinDecimal and <= MaxDecimal)
        {
            value = (decimal)real;
            return true;
        }

        // A number outside decimal's range is still a number; it simply cannot take part in a numeric comparison.
        return false;
    }

    private static bool SameValue(JsonNode? left, JsonNode? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (TryNumber(left, out var leftNumber) && TryNumber(right, out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        return string.Equals(Canonical(left, 0), Canonical(right, 0), StringComparison.Ordinal);
    }

    private static string Canonical(JsonNode? node, int depth)
    {
        var builder = new StringBuilder();
        WriteCanonical(node, builder, depth);
        return builder.ToString();
    }

    private static void WriteCanonical(JsonNode? node, StringBuilder builder, int depth)
    {
        if (depth > MaxDepth)
        {
            builder.Append("\"…\"");
            return;
        }

        switch (node)
        {
            case null:
                builder.Append("null");
                break;

            // An object nothing can read has no canonical form, and this one is only ever compared or shown; the
            // entry points name such an object where they meet it.
            case JsonObject unreadable when Unreadable(unreadable):
                builder.Append("\"…\"");
                break;

            case JsonObject map:
                builder.Append('{');
                var first = true;
                foreach (var (name, child) in map.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    WriteQuoted(name, builder);
                    builder.Append(':');
                    WriteCanonical(child, builder, depth + 1);
                }

                builder.Append('}');
                break;

            case JsonArray array:
                builder.Append('[');
                for (var index = 0; index < array.Count; index++)
                {
                    if (index != 0)
                    {
                        builder.Append(',');
                    }

                    WriteCanonical(array[index], builder, depth + 1);
                }

                builder.Append(']');
                break;

            default:
                builder.Append(Scalar(node));
                break;
        }
    }

    /// <summary>
    /// One scalar as JSON, or as its own name when JSON has none for it. An infinity and a not-a-number can only
    /// reach here from a node built in memory, and this form is what <c>enum</c>, <c>const</c> and
    /// <c>uniqueItems</c> compare by: each keeps a spelling of its own, so two of them are the same value and one
    /// of each is not, which is all the comparison asks.
    /// </summary>
    private static string Scalar(JsonNode node)
    {
        try
        {
            return node.ToJsonString();
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return node is JsonValue value && value.TryGetValue(out double real)
                ? "\"" + real.ToString("R", CultureInfo.InvariantCulture) + "\""
                : "\"…\"";
        }
    }

    private static void WriteQuoted(string text, StringBuilder builder)
    {
        builder.Append('"');
        foreach (var character in text)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character < ' ')
                    {
                        builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)character:x4}");
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    /// <summary>
    /// A date and time as RFC 3339 writes it, offset included. Framework parsing is deliberately not used: it
    /// accepts prose such as "15 September" against the current year, and a contract that says `date-time` means
    /// the one unambiguous form a provider on the other side of the world will read back the same way.
    /// </summary>
    private static bool IsRfc3339DateTime(string text)
    {
        if (text.Length < 20)
        {
            return false;
        }

        if (!Digits(text, 0, 4) || text[4] != '-' || !Digits(text, 5, 2) || text[7] != '-' || !Digits(text, 8, 2))
        {
            return false;
        }

        if (text[10] is not ('T' or 't'))
        {
            return false;
        }

        if (!Digits(text, 11, 2) || text[13] != ':' || !Digits(text, 14, 2) || text[16] != ':' || !Digits(text, 17, 2))
        {
            return false;
        }

        var year = int.Parse(text.AsSpan(0, 4), CultureInfo.InvariantCulture);
        var month = int.Parse(text.AsSpan(5, 2), CultureInfo.InvariantCulture);
        var day = int.Parse(text.AsSpan(8, 2), CultureInfo.InvariantCulture);
        var hour = int.Parse(text.AsSpan(11, 2), CultureInfo.InvariantCulture);
        var minute = int.Parse(text.AsSpan(14, 2), CultureInfo.InvariantCulture);
        var second = int.Parse(text.AsSpan(17, 2), CultureInfo.InvariantCulture);

        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month) || hour > 23 || minute > 59 || second > 60)
        {
            return false;
        }

        var rest = text.AsSpan(19);
        if (rest.Length > 0 && rest[0] == '.')
        {
            var fraction = 1;
            while (fraction < rest.Length && char.IsAsciiDigit(rest[fraction]))
            {
                fraction++;
            }

            if (fraction == 1)
            {
                return false;
            }

            rest = rest[fraction..];
        }

        if (rest.Length == 1 && rest[0] is 'Z' or 'z')
        {
            return true;
        }

        return rest.Length == 6
            && rest[0] is '+' or '-'
            && char.IsAsciiDigit(rest[1]) && char.IsAsciiDigit(rest[2])
            && rest[3] == ':'
            && char.IsAsciiDigit(rest[4]) && char.IsAsciiDigit(rest[5])
            && int.Parse(rest[1..3], CultureInfo.InvariantCulture) <= 23
            && int.Parse(rest[4..6], CultureInfo.InvariantCulture) <= 59;
    }

    private static bool Digits(string text, int start, int length)
    {
        for (var index = start; index < start + length; index++)
        {
            if (!char.IsAsciiDigit(text[index]))
            {
                return false;
            }
        }

        return true;
    }
}
