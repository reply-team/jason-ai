using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Logging;

/// <summary>
/// Every message template the runtime writes names only properties whose values carry nothing a person typed. A hole
/// in a template is a place for a value, and the log is what a diagnostic bundle carries off the machine: so each
/// hole's name is held to <see cref="ReviewedLogProperties"/>, and a template that opens a new one fails here, naming
/// itself and its file, until somebody has decided what that value can be.
/// </summary>
/// <remarks>
/// <para>
/// Its reach is the source text of every production project under <c>runtime/src</c> whose compiled assembly
/// references a logging library — Microsoft's logging abstractions or Serilog. The projects are found by looking, not
/// from a list, so a project that starts to log is read from its first line; one whose assembly is not beside this test
/// fails the scan. What an assembly references, and the constant strings it declares, are read from its metadata, so
/// looking loads nothing into the process that runs these tests.
/// </para>
/// <para>
/// The reader takes a <c>LoggerMessage.Define</c> or <c>DefineScope</c>, a <c>[LoggerMessage]</c> attribute, a call
/// of a logger's own methods — <c>LogTrace</c> to <c>LogCritical</c>, Serilog's <c>Verbose</c> to <c>Fatal</c>,
/// <c>BeginScope</c>, and <c>Log</c> or <c>Write</c> where a level comes first or a string follows a level held in a
/// variable, each with type arguments or without — and a <c>LogEventProperty</c> made by name. The template is a string literal,
/// quoted, verbatim or raw, or the name of a constant string, which is read from the compiled assembly; a scope made of
/// key-value pairs is read for its keys, and a property for its name, each as a hole of its own. A call whose template
/// is anything else — interpolated, joined, handed in, or behind an exception argument — is not passed over: it fails
/// the scan, naming its file and line, so the reader is taught that shape before the template is trusted.
/// </para>
/// <para>
/// What the source cannot always say is which calls are a logger's: with the level and the template both held in
/// variables a logger's <c>Log</c> reads like any other method of that name, and a method group has no call to read at
/// all. The compiled assembly can. Every call it makes to a logging library's template or property methods is found in
/// its IL and given a file and line by its symbols, and has to be one the reader found there; a call it did not find
/// fails the scan by that file and line, never a template passed over. A file a source generator wrote, which no source
/// file holds, is left to the attribute the generator read it from.
/// </para>
/// <para>
/// A marker named in a comment is read as a marker, as the pattern guard reads its own. Properties added any other
/// way — an enricher, a log context — are beyond a template, and are held by the run of the real executable that reads
/// its log back (<c>LogInvariantTests</c>, in the application's tests).
/// </para>
/// </remarks>
public sealed class LogTemplateInventoryTests
{
    /// <summary>
    /// How many templates the scan finds. Exact on purpose: a template added, removed or no longer read changes it,
    /// and the failure lists every template found so the change can be seen and the number moved knowingly.
    /// </summary>
    private const int TemplateCount = 69;

    private static readonly Regex Marker = new(
        @"LoggerMessage\.Define(?<scope>Scope)?(?:<[^<>()]*>)?\s*\(|\[\s*LoggerMessage\s*\(|new\s+(?:Serilog\.Events\.)?(?<property>LogEventProperty)\s*\(|\??\.\s*(?<method>LogTrace|LogDebug|LogInformation|LogWarning|LogError|LogCritical|Log|Verbose|Debug|Information|Warning|Error|Fatal|Write|BeginScope)\s*(?:<(?:[^<>()]|<[^<>()]*>)*>\s*)?\(",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

    private static readonly Regex ConstantReference = new(
        @"^(?:[A-Za-z_][A-Za-z0-9_]*\.)*[A-Za-z_][A-Za-z0-9_]*\z",
        RegexOptions.CultureInvariant);

    /// <summary>An attribute's named argument, <c>Name = value</c>, which a positional argument comparing with <c>==</c> is not.</summary>
    private static readonly Regex Named = new(
        @"^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=(?!=)(?<value>.*)\z",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.Singleline);

    /// <summary>
    /// A list of type arguments after a name, as far as a scope's pairs are read: its commas are not the ones that
    /// separate arguments, so it is taken out before they are split.
    /// </summary>
    private static readonly Regex TypeArguments = new(
        @"(?<=[A-Za-z0-9_])\s*<(?:[^<>()]|<[^<>()]*>)*>(?=\s*[\[({])",
        RegexOptions.CultureInvariant);

    /// <summary>The opening of a scope's pairs, its type arguments taken out: an array of key-value pairs, or a collection expression.</summary>
    private static readonly Regex PairsOpening = new(
        @"^(?:new\s*(?:KeyValuePair\s*)?\[\s*\]\s*)?(?=[\[{])",
        RegexOptions.CultureInvariant);

    /// <summary>One pair of a scope, its type arguments taken out: <c>new(key, value)</c> or <c>new KeyValuePair(key, value)</c>.</summary>
    private static readonly Regex PairOpening = new(
        @"^new\s*(?:KeyValuePair\s*)?\(",
        RegexOptions.CultureInvariant);

    /// <summary>A project's own name for its assembly, where it gives one.</summary>
    private static readonly Regex AssemblyNameElement = new(
        @"<AssemblyName>(?<name>[^<]+)</AssemblyName>",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

    /// <summary>The methods of Microsoft's logging abstractions a template or a scope's state goes through.</summary>
    private static readonly HashSet<string> TemplateMethods = new(StringComparer.Ordinal)
    {
        "LogTrace", "LogDebug", "LogInformation", "LogWarning", "LogError", "LogCritical", "Log", "BeginScope", "Define", "DefineScope",
    };

    /// <summary>The methods of Serilog's loggers a template goes through.</summary>
    private static readonly HashSet<string> SerilogMethods = new(StringComparer.Ordinal)
    {
        "Write", "Verbose", "Debug", "Information", "Warning", "Error", "Fatal",
    };

    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    /// <summary>Every production project, with what its compiled assembly references.</summary>
    private static readonly Lazy<IReadOnlyList<Project>> Production = new(() => Projects(RepositoryRoot()));

    /// <summary>Every constant string the production assemblies declare, by the name of its type and its own.</summary>
    private static readonly Lazy<ILookup<(string Type, string Field), string>> Constants = new(() => ConstantsOf(Production.Value));

    [Fact]
    public void Every_template_of_the_runtime_names_only_properties_whose_values_nobody_typed()
    {
        var root = RepositoryRoot();
        var scanned = Production.Value.Where(Logs).ToList();
        var sites = scanned
            .SelectMany(project => SourceFiles(project.Directory))
            .SelectMany(file => SitesIn(File.ReadAllText(file), Relative(root, file)))
            .ToList();
        var problems = new List<string>();

        // Each project that logs is read, and the one that holds the runtime is among them.
        Assert.Contains(scanned, project => project.Name == "Jason.Runtime");

        var unread = sites.Where(site => site.Template is null).Select(site => $"{site.File}({site.Line})").ToList();
        if (unread.Count > 0)
        {
            problems.Add($"The template of {unread.Count} logging call(s) could not be read: the scan reads a string literal or a constant string, and a call written another way is taught to it before its template is trusted:{Environment.NewLine}{string.Join(Environment.NewLine, unread)}");
        }

        var compiled = scanned
            .Select(project => Assembly.Load(new AssemblyName(project.Name)))
            .SelectMany(assembly => CompiledCalls(assembly, Methods(assembly)))
            .ToList();
        var unfound = Unfound(compiled, sites);
        if (unfound.Count > 0)
        {
            problems.Add($"{unfound.Count} logging call(s) of the compiled runtime are not calls the reader found in its source; each is a shape it is taught before its template is trusted:{Environment.NewLine}{string.Join(Environment.NewLine, unfound)}");
        }

        // Each kind of call the compiled side looks for has to have been seen in the tree itself, or its silence about
        // the rest would prove nothing: one in a static initializer, one in an async method, one on a Serilog logger,
        // a scope's and a property's.
        problems.AddRange(Unseen(compiled, "OperationEndpoints.cs", "LoggerMessage.Define"));
        problems.AddRange(Unseen(compiled, "AiRoleCommand.cs", "LoggerExtensions.LogInformation"));
        problems.AddRange(Unseen(compiled, "RunningRuntime.cs", "Logger.Information"));
        problems.AddRange(Unseen(compiled, "AttemptLogScope.cs", "ILogger.BeginScope"));
        problems.AddRange(Unseen(compiled, "RequestLine.cs", "LogEventProperty.new"));

        if (sites.Count != TemplateCount)
        {
            var found = sites.Select(site => $"{site.File}({site.Line}): {site.Template}");
            problems.Add($"Expected {TemplateCount} templates and found {sites.Count}:{Environment.NewLine}{string.Join(Environment.NewLine, found)}");
        }

        // Each shape the scan reads has to have been read in the tree itself, not only in the theories below.
        problems.AddRange(Missing(sites, "OperationEndpoints.cs", "Operation {Operation} failed: {Failure}"));
        problems.AddRange(Missing(sites, "NotificationSender.cs", "Notification channel {Channel} failed ({Code}, {Class})"));
        problems.AddRange(Missing(sites, "AiRoleCommand.cs", "Attempt {AttemptId} of work item {WorkItemId} started as pid {Pid}"));
        problems.AddRange(Missing(sites, "RunningRuntime.cs", "Runtime {InstanceId} stopped"));
        problems.AddRange(Missing(sites, "RuntimeHost.cs", "Console at start: {Console}"));
        problems.AddRange(Missing(sites, "RuntimeHost.cs", "The settings file could not be read ({ExceptionType} at line {LineNumber}, byte {BytePositionInLine}); the runtime keeps the settings it had"));
        problems.AddRange(Missing(sites, "RuntimeHost.cs", "The settings file could not be read ({ExceptionType} at line {LineNumber}, byte {BytePositionInLine}) while the runtime was starting, so any setting not yet in force starts on its default; restart it once the file can be read"));
        problems.AddRange(Missing(sites, "AttemptLogScope.cs", "{CampaignId} {WorkItemId} {AttemptId}"));
        problems.AddRange(Missing(sites, "RequestLine.cs", "{RequestPath}"));

        var offences = sites
            .Where(site => site.Template is not null)
            .SelectMany(site => Holes(site.Template!)
                .Where(name => !ReviewedLogProperties.Names.Contains(name))
                .Select(name => $"{site.File}({site.Line}): \"{site.Template}\" carries {name}, which is not a reviewed property"))
            .ToList();
        if (offences.Count > 0)
        {
            problems.Add($"{offences.Count} template hole(s) are not on the reviewed list; a value goes into a log only once what it can be has been decided:{Environment.NewLine}{string.Join(Environment.NewLine, offences)}");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine + Environment.NewLine, problems));
    }

    /// <summary>A reload's reason is free text a person or an agent typed, and the journal is where it is kept.</summary>
    [Fact]
    public void A_reason_is_not_a_property_any_log_line_may_carry()
    {
        Assert.DoesNotContain("Reason", ReviewedLogProperties.Names);
        Assert.Contains("Reason", ReviewedLogProperties.Refused);
        Assert.Empty(ReviewedLogProperties.Names.Intersect(ReviewedLogProperties.Refused));
    }

    /// <summary>
    /// The scan is only worth what its reader is worth. This is the reader held to every shape a template is written
    /// in; the theories after it hold it to the calls it must report rather than skip, and to the calls that are not
    /// log calls at all.
    /// </summary>
    [Theory]
    [InlineData("LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, nameof(Started)), \"Started {A}\");", "Started {A}")]
    [InlineData("LoggerMessage.Define(\n    LogLevel.Error,\n    new EventId(3, nameof(Failed)),\n    \"Failed\");", "Failed")]
    [InlineData("LoggerMessage.Define<string, int>(LogLevel.Warning, Event, @\"Verbatim \"\"{Q}\"\"\");", "Verbatim \"{Q}\"")]
    [InlineData("LoggerMessage.Define<string>(LogLevel.Information, Event, \"\"\"Raw {R}\"\"\");", "Raw {R}")]
    [InlineData("LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, \"a, (b\"), \"Comma {C}\");", "Comma {C}")]
    [InlineData("LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, ','), \"Character {C}\");", "Character {C}")]
    [InlineData("LoggerMessage.Define<string>(LogLevel.Information, /* a, b */ Event, // c, d\n \"Commented {C}\");", "Commented {C}")]
    [InlineData("LoggerMessage.DefineScope<string>(\"Scope {S}\");", "Scope {S}")]
    [InlineData("[LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = \"Attribute {A}\")]", "Attribute {A}")]
    [InlineData("[LoggerMessage(2, LogLevel.Debug, \"Positional = {P}\")]", "Positional = {P}")]
    [InlineData("logger.LogWarning(\"Direct {D}\", value);", "Direct {D}")]
    [InlineData("logger?.Warning(\n    \"Conditional {C}\",\n    value);", "Conditional {C}")]
    [InlineData("_logger.Information(\"Serilog {S}\", value);", "Serilog {S}")]
    [InlineData("logger.Log(LogLevel.Information, \"Leveled {L}\", value);", "Leveled {L}")]
    [InlineData("logger.Log(level, \"Leveled by a variable {L}\", value);", "Leveled by a variable {L}")]
    [InlineData("logger.Write(LogEventLevel.Debug, \"Written {W}\", value);", "Written {W}")]
    [InlineData("logger.Write(severity, @\"Written by a variable {W}\", value);", "Written by a variable {W}")]
    [InlineData("logger.Information<string>(\"Generic {G}\", value);", "Generic {G}")]
    [InlineData("logger.Write<int, Dictionary<string, int>>(LogEventLevel.Debug, \"Generic written {W}\", 1, map);", "Generic written {W}")]
    [InlineData("logger.LogInformation(\"Joined {J}\", $\"{one}, {two}\", 'x');", "Joined {J}")]
    [InlineData("logger.Information(ProcessDetacher.LogTemplate, word);", "Console at start: {Console}")]
    [InlineData("LoggerMessage.Define<string, string, string>(LogLevel.Warning, ChannelFailedEvent, ChannelFailedTemplate);", "Notification channel {Channel} failed ({Code}, {Class})")]
    [InlineData("using var scope = logger.BeginScope(\"Scope {S}\", value);", "Scope {S}")]
    [InlineData("logger.BeginScope(new KeyValuePair<string, object>[]\n{\n    new(\"CampaignId\", campaign),\n    new KeyValuePair<string, object>(AttemptLogScope.WorkItemKey, item),\n});", "{CampaignId} {WorkItemId}")]
    [InlineData("logger.BeginScope([new(\"AttemptId\", attempt)]);", "{AttemptId}")]
    [InlineData("new LogEventProperty(\"RequestMethod\", new ScalarValue(Method(context.Request.Method)))", "{RequestMethod}")]
    [InlineData("new Serilog.Events.LogEventProperty(RequestLine.Unmatched, new ScalarValue(0))", "{unmatched}")]
    public void The_reader_finds_the_template_in_every_shape_it_is_written_in(string source, string template)
    {
        var site = Assert.Single(SitesIn(source, "NotificationSender.cs"));

        Assert.Equal(template, site.Template);
    }

    [Theory]
    [InlineData("logger.LogError(exception, \"After an exception {E}\");")]
    [InlineData("logger.LogInformation($\"Interpolated {value}\");")]
    [InlineData("logger.LogInformation(\"Joined \" + value);")]
    [InlineData("logger.Information(template, value);")]
    [InlineData("logger.Log(LogLevel.Information, new EventId(1), \"After an event {E}\");")]
    [InlineData("logger.Log(level, $\"Interpolated {value}\");")]
    [InlineData("logger.LogInformation(\"Never closed {N}\"")]
    [InlineData("logger.Information<string>(message: \"Named {N}\", value);")]
    [InlineData("logger.BeginScope(state);")]
    [InlineData("logger.BeginScope(new Dictionary<string, object> { [\"CampaignId\"] = campaign });")]
    [InlineData("logger.BeginScope(new KeyValuePair<string, object>[] { new(key, value) });")]
    [InlineData("logger.BeginScope(new KeyValuePair<string, object>[] { KeyValuePair.Create(\"CampaignId\", (object)campaign) });")]
    [InlineData("new LogEventProperty(name, new ScalarValue(value))")]
    public void A_call_whose_template_the_reader_cannot_read_is_reported_and_not_skipped(string source)
    {
        var site = Assert.Single(SitesIn(source, "Anywhere.cs"));

        Assert.Null(site.Template);
    }

    /// <summary>
    /// Calls by these names that are not a logger's. A <c>Log</c> or <c>Write</c> whose level and template are both
    /// held in variables reads the same as these, and is left to the compiled assembly, which knows whose method it is.
    /// </summary>
    [Theory]
    [InlineData("writer.Write(\"not a log line\");")]
    [InlineData("writer.Write(buffer, 0, count);")]
    [InlineData("host.Log(engine, args);")]
    [InlineData("Volatile.Write(ref settingsRead, true);")]
    public void A_call_that_is_not_a_log_call_is_not_read_as_one(string source) =>
        Assert.Empty(SitesIn(source, "Anywhere.cs"));

    [Fact]
    public void The_reader_gives_the_line_each_call_starts_on()
    {
        var source = "class A\n{\n    void M()\n    {\n        logger.LogInformation(\"One {A}\");\n\n        logger?.Warning(\n            \"Two {B}\");\n    }\n}";

        Assert.Equal([(5, "One {A}"), (7, "Two {B}")], SitesIn(source, "A.cs").Select(site => (site.Line, site.Template)));
    }

    /// <summary>
    /// What the reader passes over, the compiled assembly still holds. This file's own assembly makes two calls the
    /// reader does not find — a <c>Log</c> whose level and template are both variables, and a method group handed out
    /// as a delegate — and the check finds both, by this file and the line each is on.
    /// </summary>
    [Fact]
    public void A_logging_call_the_reader_passes_over_is_found_in_the_compiled_assembly_by_its_file_and_line()
    {
        var root = RepositoryRoot();
        var path = Path.Combine(root, "runtime", "tests", "Jason.Runtime.Tests", "Logging", "LogTemplateInventoryTests.cs");
        var source = File.ReadAllText(path);
        var file = Relative(root, path);
        var calls = CompiledCalls(typeof(LogTemplateInventoryTests).Assembly, Methods(typeof(LogTemplateInventoryTests)));

        var unfound = Unfound(calls, SitesIn(source, file).ToList());

        // Each declaration is looked for in pieces, so the text looked for is not also the text of this lookup.
        Assert.Equal(
            [
                $"{file}({LineOf(source, source.IndexOf("void " + nameof(PassedOver) + "(", StringComparison.Ordinal))}): LoggerExtensions.Log",
                $"{file}({LineOf(source, source.IndexOf("> " + nameof(Grouped) + "(", StringComparison.Ordinal))}): ILogger.Information",
            ],
            unfound);
    }

    /// <summary>The holes as the logger reads them: escaped braces are text, and a format, an alignment or a capture prefix is not part of a name.</summary>
    [Theory]
    [InlineData("Plain", "")]
    [InlineData("{A} and {B}", "A B")]
    [InlineData("{{Escaped}} {Real}", "Real")]
    [InlineData("}} {{", "")]
    [InlineData("{@Structured} {$Stringified}", "Structured Stringified")]
    [InlineData("{Elapsed:0.0000} ms, {Count,5}, {Padded,-3:x}", "Elapsed Count Padded")]
    [InlineData("{0}", "0")]
    public void The_holes_of_a_template_are_read_as_the_logger_reads_them(string template, string names) =>
        Assert.Equal(names.Split(' ', StringSplitOptions.RemoveEmptyEntries), Holes(template));

    /// <summary>A logger's <c>Log</c> with nothing the reader can take: its level and its template are both handed in.</summary>
    private static void PassedOver(ILogger logger, LogLevel level, string template) => logger.Log(level, template);

    /// <summary>A logger's method handed out as a delegate, which no call in the source shows.</summary>
    private static Action<string, object?[]?> Grouped(Serilog.ILogger logger) => logger.Information;

    private static IEnumerable<string> Missing(List<TemplateSite> sites, string file, string template)
    {
        if (!sites.Any(site => Path.GetFileName(site.File) == file && site.Template == template))
        {
            yield return $"The scan did not find the template \"{template}\" in {file}.";
        }
    }

    private static IEnumerable<string> Unseen(List<CompiledCall> calls, string file, string member)
    {
        if (!calls.Any(call => Path.GetFileName(call.File) == file && call.Member == member))
        {
            yield return $"The compiled runtime was not seen to call {member} in {file}.";
        }
    }

    /// <summary>
    /// The property names a template's holes give its values: what is between single braces, without a capture prefix
    /// and without what follows a comma or a colon. A doubled brace is a brace.
    /// </summary>
    private static List<string> Holes(string template)
    {
        var names = new List<string>();
        for (var at = 0; at < template.Length; at++)
        {
            if (template[at] is '{')
            {
                if (at + 1 < template.Length && template[at + 1] is '{')
                {
                    at++;
                    continue;
                }

                var close = template.IndexOf('}', at + 1);
                if (close < 0)
                {
                    break;
                }

                var hole = template[(at + 1)..close].TrimStart('@', '$');
                var end = hole.IndexOfAny([',', ':']);
                names.Add((end < 0 ? hole : hole[..end]).Trim());
                at = close;
            }
            else if (template[at] is '}' && at + 1 < template.Length && template[at + 1] is '}')
            {
                at++;
            }
        }

        return names;
    }

    /// <summary>Every logging call in one source text, with the line it starts on and its template, or null where it cannot be read.</summary>
    private static IEnumerable<TemplateSite> SitesIn(string source, string file)
    {
        foreach (var marker in Marker.Matches(source).Cast<Match>())
        {
            var line = LineOf(source, marker.Index);
            if (Arguments(source, marker.Index + marker.Length - 1) is not { } arguments)
            {
                yield return new TemplateSite(file, line, null);
                continue;
            }

            string? template;
            if (marker.Value.StartsWith("LoggerMessage", StringComparison.Ordinal))
            {
                template = Template(arguments.ElementAtOrDefault(marker.Groups["scope"].Success ? 0 : 2), file);
            }
            else if (marker.Value.StartsWith('['))
            {
                // Named, or the last positional argument: every constructor of the attribute takes the message last.
                template = Template(
                    arguments.Select(each => Named.Match(each)).FirstOrDefault(named => named.Success && named.Groups["name"].Value == "Message") is { } message
                        ? message.Groups["value"].Value
                        : arguments.LastOrDefault(positional => !Named.IsMatch(positional)),
                    file);
            }
            else if (marker.Groups["property"].Success)
            {
                // A property made by name carries that name into the line, as a hole of the template would.
                template = Template(arguments[0], file) is { } name ? "{" + name + "}" : null;
            }
            else if (marker.Groups["method"].Value is "Log" or "Write")
            {
                // A log call where a level comes first, or where a string follows whatever comes first; anything else by
                // these names is not read as a logger's, and the compiled assembly answers for the calls that are.
                var first = arguments[0].Trim();
                var second = arguments.ElementAtOrDefault(1)?.Trim();
                if (first.StartsWith("LogLevel.", StringComparison.Ordinal) || first.StartsWith("LogEventLevel.", StringComparison.Ordinal))
                {
                    template = Template(second, file);
                }
                else if (second is { Length: > 0 } && second[0] is '"' or '@' or '$')
                {
                    template = Template(second, file);
                }
                else
                {
                    continue;
                }
            }
            else if (marker.Groups["method"].Value is "BeginScope")
            {
                // A scope's state is its one argument, which a comma between type arguments may have split.
                template = Template(arguments[0], file) ?? Pairs(string.Join(',', arguments), file);
            }
            else
            {
                template = Template(arguments[0], file);
            }

            yield return new TemplateSite(file, line, template);
        }
    }

    /// <summary>The template one argument holds: a literal on its own, or a constant read from the compiled assembly.</summary>
    private static string? Template(string? argument, string file)
    {
        if (argument is null)
        {
            return null;
        }

        var text = argument.Trim();
        if (text.Length > 0 && text[0] is '"' or '@')
        {
            return Literal(text, 0, out var end) is { } literal && end == text.Length ? literal : null;
        }

        return ConstantReference.IsMatch(text) ? Constant(text, file) : null;
    }

    /// <summary>
    /// A scope made of key-value pairs, as the holes its keys make: <c>{CampaignId} {WorkItemId}</c>. Each pair is
    /// <c>new(key, value)</c> or <c>new KeyValuePair&lt;…&gt;(key, value)</c> in an array or a collection expression,
    /// and each key a literal or a constant; anything else is not a scope this reader reads, and null.
    /// </summary>
    private static string? Pairs(string argument, string file)
    {
        var text = TypeArguments.Replace(argument, string.Empty).Trim();
        if (PairsOpening.Match(text) is not { Success: true } opening
            || Arguments(text, opening.Length) is not { } elements
            || !elements.Any(element => element.Trim().Length > 0))
        {
            return null;
        }

        var keys = new List<string>();
        foreach (var element in elements.Select(each => each.Trim()).Where(each => each.Length > 0))
        {
            if (PairOpening.Match(element) is not { Success: true } pair
                || Arguments(element, pair.Length - 1) is not [var key, _]
                || Template(key, file) is not { } name)
            {
                return null;
            }

            keys.Add("{" + name + "}");
        }

        return string.Join(' ', keys);
    }

    /// <summary>
    /// A constant string, named as <c>Type.Field</c> or, inside its own type, as <c>Field</c> — the type then being
    /// the one its file is named after. One match or none: a name that could mean two constants is not a template.
    /// </summary>
    private static string? Constant(string reference, string file)
    {
        var parts = reference.Split('.');
        var typeName = parts.Length > 1 ? parts[^2] : Path.GetFileNameWithoutExtension(file);
        var values = Constants.Value[(typeName, parts[^1])].Distinct(StringComparer.Ordinal).ToList();
        return values.Count == 1 ? values[0] : null;
    }

    /// <summary>
    /// Every constant string the projects' assemblies declare, read from their metadata rather than by loading them:
    /// an assembly loaded into this process to be looked at would bring what it references along, and one test of this
    /// project holds that the process running the runtime never loads the engine the plugin host does.
    /// </summary>
    private static ILookup<(string Type, string Field), string> ConstantsOf(IEnumerable<Project> projects)
    {
        var constants = new List<((string Type, string Field) Name, string Value)>();
        foreach (var project in projects)
        {
            using var image = new PEReader(File.OpenRead(project.AssemblyFile));
            var metadata = image.GetMetadataReader();
            foreach (var type in metadata.TypeDefinitions.Select(metadata.GetTypeDefinition))
            {
                var typeName = metadata.GetString(type.Name).Split('`')[0];
                foreach (var field in type.GetFields().Select(metadata.GetFieldDefinition))
                {
                    if ((field.Attributes & FieldAttributes.Literal) == 0 || field.GetDefaultValue().IsNil)
                    {
                        continue;
                    }

                    var constant = metadata.GetConstant(field.GetDefaultValue());
                    if (constant.TypeCode == ConstantTypeCode.String)
                    {
                        var value = metadata.GetBlobReader(constant.Value);
                        constants.Add(((typeName, metadata.GetString(field.Name)), value.ReadUTF16(value.Length)));
                    }
                }
            }
        }

        return constants.ToLookup(constant => constant.Name, constant => constant.Value);
    }

    /// <summary>
    /// The arguments of the call whose opening parenthesis is at the index, split at the commas that separate them
    /// and nowhere inside a literal, a comment or a nested bracket, each without its comments; null when the call
    /// never closes.
    /// </summary>
    private static List<string>? Arguments(string source, int open)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        for (var at = open + 1; at < source.Length; at++)
        {
            var character = source[at];
            if (character is '"' or '@' or '$' or '\'' && Skip(source, at) is { } after)
            {
                current.Append(source, at, after - at);
                at = after - 1;
                continue;
            }

            if (character is '/' && at + 1 < source.Length && source[at + 1] is '/' or '*')
            {
                var close = source[at + 1] is '/' ? source.IndexOf('\n', at) : source.IndexOf("*/", at + 2, StringComparison.Ordinal) + 1;
                if (close <= 0)
                {
                    return null;
                }

                current.Append(' ');
                at = close;
                continue;
            }

            switch (character)
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}' when depth == 0:
                    arguments.Add(current.ToString());
                    return arguments;
                case ')' or ']' or '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    arguments.Add(current.ToString());
                    current.Clear();
                    continue;
            }

            current.Append(character);
        }

        return null;
    }

    /// <summary>The index just past the string or character literal that begins at the index, or null where none begins there.</summary>
    private static int? Skip(string source, int index)
    {
        if (source[index] is '\'')
        {
            var close = index + 1 < source.Length && source[index + 1] is '\\' ? source.IndexOf('\'', index + 3) : source.IndexOf('\'', index + 2);
            return close < 0 ? null : close + 1;
        }

        if (Literal(source, index, out var end) is not null)
        {
            return end;
        }

        // An interpolated literal is never a template, but a call can carry one in another argument.
        var quote = index;
        while (quote < source.Length && source[quote] is '$' or '@')
        {
            quote++;
        }

        if (quote == index || quote >= source.Length || source[quote] is not '"')
        {
            return null;
        }

        for (var at = quote + 1; at < source.Length; at++)
        {
            if (source[at] is '\\')
            {
                at++;
            }
            else if (source[at] is '"')
            {
                return at + 1;
            }
        }

        return null;
    }

    /// <summary>
    /// The text of the literal that begins at the index — raw, verbatim or quoted — and the index just past it; null
    /// where no literal this reader reads begins there, an interpolated one included. A quoted literal's escapes are
    /// undone only for a backslash and a quote, which is all a hole's name is read through.
    /// </summary>
    private static string? Literal(string source, int start, out int end)
    {
        end = start;
        var verbatim = source.AsSpan(start).StartsWith("@\"", StringComparison.Ordinal);
        var quote = verbatim ? start + 1 : start;
        if (quote >= source.Length || source[quote] is not '"')
        {
            return null;
        }

        var quotes = 0;
        while (quote + quotes < source.Length && source[quote + quotes] is '"')
        {
            quotes++;
        }

        if (quotes >= 3)
        {
            var close = source.IndexOf(new string('"', quotes), quote + quotes, StringComparison.Ordinal);
            if (close < 0)
            {
                return null;
            }

            end = close + quotes;
            return source[(quote + quotes)..close].Trim();
        }

        if (quotes == 2 && !verbatim)
        {
            end = quote + 2;
            return string.Empty;
        }

        var text = new StringBuilder();
        for (var at = quote + 1; at < source.Length; at++)
        {
            var character = source[at];
            if (verbatim && character is '"')
            {
                if (at + 1 < source.Length && source[at + 1] is '"')
                {
                    text.Append('"');
                    at++;
                    continue;
                }

                end = at + 1;
                return text.ToString();
            }

            if (!verbatim && character is '"')
            {
                end = at + 1;
                return text.ToString();
            }

            if (!verbatim && character is '\\' && at + 1 < source.Length)
            {
                text.Append(source[at + 1] is '\\' or '"' ? source.AsSpan(at + 1, 1) : source.AsSpan(at, 2));
                at++;
                continue;
            }

            if (!verbatim && character is '\n')
            {
                return null;
            }

            text.Append(character);
        }

        return null;
    }

    private static int LineOf(string source, int index) => 1 + source.AsSpan(0, index).Count('\n');

    /// <summary>
    /// The compiled calls the reader did not find: each call's statement, by the lines the symbols give it, has to hold
    /// as many sites the reader found as it holds calls. A call the symbols give no line is never found.
    /// </summary>
    private static List<string> Unfound(IEnumerable<CompiledCall> calls, List<TemplateSite> sites) =>
        calls
            .GroupBy(call => (call.File, call.StartLine, call.EndLine))
            .Where(statement => statement.Key.File is null
                || sites.Count(site => string.Equals(site.File, statement.Key.File, StringComparison.OrdinalIgnoreCase)
                    && site.Line >= statement.Key.StartLine
                    && site.Line <= statement.Key.EndLine) < statement.Count())
            .SelectMany(statement => statement.Select(call => $"{call.File ?? call.Method}({call.StartLine}): {call.Member}"))
            .ToList();

    /// <summary>
    /// Every call the IL of these methods of an assembly makes to a logging library's template or property methods — a
    /// call, a constructor, or a method handed out as a delegate — with the file and lines the assembly's symbols give
    /// the statement it is in. A file a source generator wrote is left out: no source file holds it, and the generator
    /// wrote it from an attribute the reader reads.
    /// </summary>
    private static List<CompiledCall> CompiledCalls(Assembly assembly, IEnumerable<MethodBase> methods)
    {
        using var image = new PEReader(File.OpenRead(assembly.Location));
        Assert.True(
            image.TryOpenAssociatedPortablePdb(assembly.Location, path => File.Exists(path) ? File.OpenRead(path) : null, out var provider, out _) && provider is not null,
            $"The symbols of {assembly.GetName().Name} were not found beside it, so its logging calls cannot be given a file and a line.");

        using (provider)
        {
            var symbols = provider.GetMetadataReader();
            var calls = new List<CompiledCall>();
            foreach (var method in methods)
            {
                foreach (var (offset, target) in Calls(method))
                {
                    if (!IsLogging(target))
                    {
                        continue;
                    }

                    var member = $"{target.DeclaringType!.Name.Split('`')[0]}.{(target is ConstructorInfo ? "new" : target.Name)}";
                    var point = symbols
                        .GetMethodDebugInformation(MetadataTokens.MethodDefinitionHandle(method.MetadataToken & 0x00FFFFFF))
                        .GetSequencePoints()
                        .LastOrDefault(each => !each.IsHidden && each.Offset <= offset);
                    if (point.Document.IsNil)
                    {
                        calls.Add(new CompiledCall(null, 0, 0, member, $"{method.DeclaringType?.FullName}.{method.Name}"));
                        continue;
                    }

                    var document = symbols.GetString(symbols.GetDocument(point.Document).Name);
                    if (!document.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
                    {
                        calls.Add(new CompiledCall(Relative(document), point.StartLine, point.EndLine, member, method.Name));
                    }
                }
            }

            return calls;
        }
    }

    /// <summary>
    /// Whether a method is one a template, a scope's state or a property's name goes through: Microsoft's logging
    /// methods and <c>LoggerMessage</c>, Serilog's write methods, and the making of a <c>LogEventProperty</c>.
    /// </summary>
    private static bool IsLogging(MethodBase target) =>
        target.DeclaringType is { } type
        && ((type.Namespace == "Microsoft.Extensions.Logging" && TemplateMethods.Contains(target.Name))
            || (type.Namespace is "Serilog" or "Serilog.Core" && SerilogMethods.Contains(target.Name))
            || (type.FullName == "Serilog.Events.LogEventProperty" && target is ConstructorInfo));

    /// <summary>
    /// Every method and constructor with a body in an assembly, compiler-made ones included: a lambda, a local
    /// function and an async method's state machine are methods of nested types the compiler wrote, and
    /// <see cref="Assembly.GetTypes"/> lists nested types already.
    /// </summary>
    private static IEnumerable<MethodBase> Methods(Assembly assembly) => assembly.GetTypes().SelectMany(Declared);

    /// <summary>Every method and constructor with a body in one type and in every type nested inside it.</summary>
    private static IEnumerable<MethodBase> Methods(Type type) =>
        Declared(type).Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(Methods));

    private static IEnumerable<MethodBase> Declared(Type type)
    {
        const BindingFlags Own = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        return type.GetMethods(Own).Cast<MethodBase>()
            .Concat(type.GetConstructors(Own))
            .Where(method => method.GetMethodBody() is not null);
    }

    /// <summary>Every method a body names through a method token, with the offset of the instruction that names it.</summary>
    private static IEnumerable<(int Offset, MethodBase Target)> Calls(MethodBase method)
    {
        var il = method.GetMethodBody()!.GetILAsByteArray()!;
        var typeArguments = method.DeclaringType is { IsGenericType: true } generic ? generic.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var at = 0;
        while (at < il.Length)
        {
            var start = at;
            var twoBytes = il[at] == 0xFE;
            var code = Codes[twoBytes ? unchecked((short)(0xFE00 | il[at + 1])) : il[at]];
            at += twoBytes ? 2 : 1;

            if (code.OperandType == OperandType.InlineMethod)
            {
                yield return (start, method.Module.ResolveMethod(BitConverter.ToInt32(il, at), typeArguments, methodArguments)!);
            }

            at += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, at)),
                _ => 4,
            };
        }
    }

    /// <summary>
    /// Every production project under <c>runtime/src</c>: a directory holding a project file, whose assembly is the one
    /// the file names or else the one named after the file, and the assemblies that assembly references, read from its
    /// metadata beside this test. A project whose assembly is not there fails the scan, because whether it logs is what
    /// that assembly says.
    /// </summary>
    private static List<Project> Projects(string root)
    {
        var projects = new List<Project>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "runtime", "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var name = AssemblyNameElement.Match(File.ReadAllText(file)) is { Success: true } named
                ? named.Groups["name"].Value.Trim()
                : Path.GetFileNameWithoutExtension(file);
            var assembly = Path.Combine(AppContext.BaseDirectory, name + ".dll");
            Assert.True(
                File.Exists(assembly),
                $"The production project {Path.GetRelativePath(root, file)} builds {name}, which is not beside this test, so whether it writes to a log cannot be told: reference the project from this test project.");

            using var image = new PEReader(File.OpenRead(assembly));
            var metadata = image.GetMetadataReader();
            var references = metadata.AssemblyReferences.Select(reference => metadata.GetString(metadata.GetAssemblyReference(reference).Name)).ToList();
            projects.Add(new Project(Path.GetDirectoryName(file)!, name, assembly, references));
        }

        return projects;
    }

    /// <summary>Whether a project's compiled assembly references a logging library: Microsoft's logging abstractions, or Serilog.</summary>
    private static bool Logs(Project project) =>
        project.References.Any(reference =>
            reference == "Microsoft.Extensions.Logging.Abstractions" || reference.StartsWith("Serilog", StringComparison.Ordinal));

    /// <summary>The C# files of one project, without what the build writes under it.</summary>
    private static IEnumerable<string> SourceFiles(string project) =>
        Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>A source file as the scan names it: from <c>runtime/</c> on, with forward slashes.</summary>
    private static string Relative(string root, string file) => Path.GetRelativePath(root, file).Replace('\\', '/');

    /// <summary>
    /// A document the symbols name, as the scan names a source file. The symbols hold the path the build saw — the
    /// repository's own, or under the root a deterministic build maps it to — so it is read from the tree it is in.
    /// </summary>
    private static string Relative(string document)
    {
        var path = document.Replace('\\', '/');
        foreach (var tree in new[] { "/runtime/src/", "/runtime/tests/" })
        {
            var at = path.LastIndexOf(tree, StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                return path[(at + 1)..];
            }
        }

        return path;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    private sealed record TemplateSite(string File, int Line, string? Template);

    /// <summary>A compiled call to a logging method: the file and lines of its statement, or the method it is in where the symbols give none.</summary>
    private sealed record CompiledCall(string? File, int StartLine, int EndLine, string Member, string Method);

    /// <summary>A production project: where its source is, its assembly's name and file, and what that assembly references.</summary>
    private sealed record Project(string Directory, string Name, string AssemblyFile, IReadOnlyList<string> References);
}
