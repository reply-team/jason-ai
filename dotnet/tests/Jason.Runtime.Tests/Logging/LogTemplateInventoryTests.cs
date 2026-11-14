using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Jason.Cli.Doctor;
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
/// Its reach is the source text of every production project under <c>dotnet/src</c> whose compiled assembly
/// references a logging library — Microsoft's logging abstractions or Serilog. The projects are found by looking, not
/// from a list, so a project that starts to log is read from its first line; one whose assembly is not beside this test
/// fails the scan. What an assembly references, the constant strings it declares and the calls its code makes are read
/// from its file — its metadata and its symbols — so looking loads nothing into the process that runs these tests: not
/// the assembly, and not what its calls go into, the plugin host's engine among them.
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
/// A marker named in a comment is read as a marker, as the pattern guard reads its own. A property added to every line
/// written while it is in force — Serilog's <c>ForContext</c>, <c>LogContext.PushProperty</c> and a diagnostic
/// context's <c>Set</c> — names no hole any template has, so no template can be held to the list for it: none is used,
/// and a call to one fails the scan by its file and line until what it adds has been reviewed. Properties added at
/// configuration, by an enricher, are held by the run of the real executable that reads its log back
/// (<c>LogInvariantTests</c>, in the application's tests).
/// </para>
/// </remarks>
public sealed class LogTemplateInventoryTests
{
    /// <summary>
    /// How many templates the scan finds. Exact on purpose: a template added, removed or no longer read changes it,
    /// and the failure lists every template found so the change can be seen and the number moved knowingly.
    /// </summary>
    private const int TemplateCount = 140;

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

    /// <summary>Every production project, with what its compiled assembly references.</summary>
    private static readonly Lazy<IReadOnlyList<Project>> Production = new(() => Projects(RepositoryRoot()));

    /// <summary>Every constant string the production assemblies declare, by the name of its type and its own.</summary>
    private static readonly Lazy<ILookup<(string Type, string Field), string>> Constants = new(() => ConstantsOf(Production.Value));

    [Fact]
    [Critical(Family.B3, "the log-template guard")]
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

        var all = scanned.SelectMany(project => CompiledCalls.In(project.AssemblyFile)).ToList();
        var compiled = LoggingCalls(all);

        var enriching = Enriching(all);
        if (enriching.Count > 0)
        {
            problems.Add($"{enriching.Count} call(s) add a property to every line written while they are in force, which names no hole of any template, so its value is held to nothing; review what each adds before it is let through:{Environment.NewLine}{string.Join(Environment.NewLine, enriching)}");
        }
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
        problems.AddRange(Missing(sites, "UnhandledExceptionLine.cs", "The runtime is ending on an unhandled {ExceptionType}, wrapping {InnerExceptionTypes}; its stack, innermost frame first: {Frames}"));
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

    /// <summary>
    /// A diagnostic bundle writes a plugin's id as its alias wherever a line of the log names one: under
    /// <see cref="LoggedNames.PluginIdProperty"/> on any line, and under another property on the lines the client lists
    /// by their templates, since it cannot read the runtime's code. That list is held to the runtime's templates here:
    /// each is a template the runtime writes, with its property as a hole; every template the runtime writes with a hole
    /// of one of those names is on it; a value that names no plugin is kept to a template on it, never to every line; and
    /// no template names a role or a skill pack, which the client would not read.
    /// </summary>
    [Fact]
    public void The_clients_list_of_the_lines_that_name_a_plugin_is_the_runtimes_own()
    {
        var root = RepositoryRoot();
        var templates = Production.Value.Where(Logs)
            .SelectMany(project => SourceFiles(project.Directory))
            .SelectMany(file => SitesIn(File.ReadAllText(file), Relative(root, file)))
            .Select(site => site.Template)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var listed = LoggedNames.PluginTemplates;

        // The properties a line of the runtime's names a plugin under besides PluginId, as its code fills them: a loader's
        // candidate by its folder, a plugin's grants by its id, a notification channel by its name.
        string[] naming = ["Directory", "Id", "Channel"];

        Assert.All(listed, entry =>
        {
            Assert.Contains(entry.Key, templates);
            Assert.Contains(entry.Value, Holes(entry.Key));
            Assert.Contains(entry.Value, naming);
        });
        Assert.Equal(
            templates.Where(template => Holes(template).Any(naming.Contains)).Order(StringComparer.Ordinal),
            listed.Keys.Order(StringComparer.Ordinal));
        Assert.All(LoggedNames.NotAPlugin.Keys, template => Assert.Contains(template, listed.Keys));
        Assert.Contains(LoggedNames.PluginIdProperty, ReviewedLogProperties.Names);
        Assert.Contains(templates, template => Holes(template).Contains(LoggedNames.PluginIdProperty));
        Assert.DoesNotContain(
            templates.SelectMany(Holes),
            hole => hole.Contains("Role", StringComparison.OrdinalIgnoreCase) || hole.Contains("Pack", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A reload's reason is free text a person or an agent typed, and the journal is where it is kept.</summary>
    [Fact]
    [Critical(Family.B3, "a typed reason never reaches a log")]
    public void A_reason_is_not_a_property_any_log_line_may_carry()
    {
        Assert.DoesNotContain("Reason", ReviewedLogProperties.Names);
        Assert.Contains("Reason", ReviewedLogProperties.Refused);
        Assert.Empty(ReviewedLogProperties.Names.Intersect(ReviewedLogProperties.Refused));
    }

    /// <summary>A doubt's sentence about a send is free text at rest on the work item, read only onto the approval's preview.</summary>
    [Fact]
    public void A_doubt_s_sentence_is_not_a_property_any_log_line_may_carry()
    {
        string[] sentence = ["Note", "AskPersonNote", "ReviewNote", "Doubt"];

        Assert.All(sentence, name => Assert.Contains(name, ReviewedLogProperties.Refused));
        Assert.Empty(ReviewedLogProperties.Names.Intersect(sentence));
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
    [Critical(Family.B3, "the guard cannot be bypassed by shape")]
    public void A_logging_call_the_reader_passes_over_is_found_in_the_compiled_assembly_by_its_file_and_line()
    {
        var root = RepositoryRoot();
        var path = Path.Combine(root, "dotnet", "tests", "Jason.Runtime.Tests", "Logging", "LogTemplateInventoryTests.cs");
        var source = File.ReadAllText(path);
        var file = Relative(root, path);
        var calls = LoggingCalls(CompiledCalls.In(typeof(LogTemplateInventoryTests).Assembly.Location, ThisClass));

        var unfound = Unfound(calls, SitesIn(source, file).ToList());

        // Each declaration is looked for in pieces, so the text looked for is not also the text of this lookup.
        Assert.Equal(
            [
                $"{file}({CSharpSource.LineOf(source, source.IndexOf("void " + nameof(PassedOver) + "(", StringComparison.Ordinal))}): LoggerExtensions.Log",
                $"{file}({CSharpSource.LineOf(source, source.IndexOf("> " + nameof(Grouped) + "(", StringComparison.Ordinal))}): ILogger.Information",
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

    /// <summary>
    /// The calls that add a property beside every line rather than through a template are found in the compiled
    /// assembly too: this file's own assembly makes one of each, and the check names each by this file and its line.
    /// </summary>
    [Fact]
    public void A_call_that_adds_a_property_beside_every_line_is_found_in_the_compiled_assembly_by_its_file_and_line()
    {
        var root = RepositoryRoot();
        var path = Path.Combine(root, "dotnet", "tests", "Jason.Runtime.Tests", "Logging", "LogTemplateInventoryTests.cs");
        var source = File.ReadAllText(path);
        var file = Relative(root, path);

        var enriching = Enriching(CompiledCalls.In(typeof(LogTemplateInventoryTests).Assembly.Location, ThisClass));

        // Each declaration is looked for in pieces, so the text looked for is not also the text of this lookup.
        Assert.Equal(
            [
                $"{file}({CSharpSource.LineOf(source, source.IndexOf("IDisposable " + nameof(Pushed) + "(", StringComparison.Ordinal))}): LogContext.PushProperty",
                $"{file}({CSharpSource.LineOf(source, source.IndexOf("ILogger " + nameof(Enriched) + "(", StringComparison.Ordinal))}): ILogger.ForContext",
                $"{file}({CSharpSource.LineOf(source, source.IndexOf("void " + nameof(Diagnosed) + "(", StringComparison.Ordinal))}): IDiagnosticContext.Set",
            ],
            enriching.Order(StringComparer.Ordinal).ToList());
    }

    /// <summary>A property pushed onto the log context, which every line written while it is open carries.</summary>
    private static IDisposable Pushed(string value) => Serilog.Context.LogContext.PushProperty("Pushed", value);

    /// <summary>A logger that adds a property to every line written through it.</summary>
    private static Serilog.ILogger Enriched(Serilog.ILogger logger, string value) => logger.ForContext("Enriched", value);

    /// <summary>A property set on the request's diagnostic context, which its request line carries.</summary>
    private static void Diagnosed(Serilog.IDiagnosticContext context, string value) => context.Set("Diagnosed", value);

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

    private static IEnumerable<string> Unseen(List<LogCall> calls, string file, string member)
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
            var line = CSharpSource.LineOf(source, marker.Index);
            if (CSharpSource.Arguments(source, marker.Index + marker.Length - 1) is not { } arguments)
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
            return CSharpSource.Literal(text, 0, out var end) is { } literal && end == text.Length ? literal : null;
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
            || CSharpSource.Arguments(text, opening.Length) is not { } elements
            || !elements.Any(element => element.Trim().Length > 0))
        {
            return null;
        }

        var keys = new List<string>();
        foreach (var element in elements.Select(each => each.Trim()).Where(each => each.Length > 0))
        {
            if (PairOpening.Match(element) is not { Success: true } pair
                || CSharpSource.Arguments(element, pair.Length - 1) is not [var key, _]
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
    /// The compiled calls the reader did not find: each call's statement, by the lines the symbols give it, has to hold
    /// as many sites the reader found as it holds calls. A call the symbols give no line is never found.
    /// </summary>
    private static List<string> Unfound(IEnumerable<LogCall> calls, List<TemplateSite> sites) =>
        calls
            .GroupBy(call => (call.File, call.StartLine, call.EndLine))
            .Where(statement => statement.Key.File is null
                || sites.Count(site => string.Equals(site.File, statement.Key.File, StringComparison.OrdinalIgnoreCase)
                    && site.Line >= statement.Key.StartLine
                    && site.Line <= statement.Key.EndLine) < statement.Count())
            .SelectMany(statement => statement.Select(call => $"{call.File ?? call.Method}({call.StartLine}): {call.Member}"))
            .ToList();

    /// <summary>
    /// The calls of these to a logging library's template or property methods — a call, a constructor, or a method
    /// handed out as a delegate — each named by its type and member as a failure names it.
    /// </summary>
    private static List<LogCall> LoggingCalls(IEnumerable<CompiledCall> calls) =>
        [.. calls
            .Where(call => IsLogging(call.Target))
            .Select(call => new LogCall(call.File, call.StartLine, call.EndLine, $"{call.Target.Type}.{(call.Target.Member == ".ctor" ? "new" : call.Target.Member)}", call.Method))];

    /// <summary>
    /// Whether a call names one a template, a scope's state or a property's name goes through: Microsoft's logging
    /// methods and <c>LoggerMessage</c>, Serilog's write methods, and the making of a <c>LogEventProperty</c>.
    /// </summary>
    private static bool IsLogging(CallTarget target) =>
        (target.Namespace == "Microsoft.Extensions.Logging" && TemplateMethods.Contains(target.Member))
        || (target.Namespace is "Serilog" or "Serilog.Core" && SerilogMethods.Contains(target.Member))
        || (target.Namespace == "Serilog.Events" && target.Type == "LogEventProperty" && target.Member == ".ctor");

    /// <summary>
    /// The calls of these that add a property beside every line written while they are in force, each named by its file
    /// and line, or the method it is in where the symbols give none.
    /// </summary>
    private static List<string> Enriching(IEnumerable<CompiledCall> calls) =>
        [.. calls
            .Where(call => AddsProperties(call.Target))
            .Select(call => $"{call.File ?? call.Method}({call.StartLine}): {call.Target.Type}.{call.Target.Member}")];

    /// <summary>Serilog's <c>ForContext</c>, on a logger or on the static one, <c>LogContext.PushProperty</c>, and a diagnostic context's <c>Set</c>.</summary>
    private static bool AddsProperties(CallTarget target) =>
        (target.Namespace is "Serilog" or "Serilog.Core" && target.Type is "ILogger" or "Logger" or "Log" && target.Member == "ForContext")
        || (target.Namespace == "Serilog.Context" && target.Type == "LogContext" && target.Member == "PushProperty")
        || (target.Namespace == "Serilog" && target.Type == "IDiagnosticContext" && target.Member == "Set");

    /// <summary>This class and every type nested in it, where the compiler writes its lambdas.</summary>
    private static bool ThisClass(string type) =>
        type == typeof(LogTemplateInventoryTests).FullName || type.StartsWith(typeof(LogTemplateInventoryTests).FullName + "/", StringComparison.Ordinal);

    /// <summary>
    /// Every production project under <c>dotnet/src</c>: a directory holding a project file, whose assembly is the one
    /// the file names or else the one named after the file, and the assemblies that assembly references, read from its
    /// metadata beside this test. A project whose assembly is not there fails the scan, because whether it logs is what
    /// that assembly says.
    /// </summary>
    private static List<Project> Projects(string root)
    {
        var projects = new List<Project>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "dotnet", "src"), "*.csproj", SearchOption.AllDirectories))
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

    /// <summary>A source file as the scan names it: from <c>dotnet/</c> on, with forward slashes.</summary>
    private static string Relative(string root, string file) => Path.GetRelativePath(root, file).Replace('\\', '/');

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
    private sealed record LogCall(string? File, int StartLine, int EndLine, string Member, string Method);

    /// <summary>A production project: where its source is, its assembly's name and file, and what that assembly references.</summary>
    private sealed record Project(string Directory, string Name, string AssemblyFile, IReadOnlyList<string> References);
}
