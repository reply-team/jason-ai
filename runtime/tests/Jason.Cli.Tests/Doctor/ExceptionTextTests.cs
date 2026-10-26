using Jason.Cli.Doctor;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Doctor;

/// <summary>
/// An exception's text is the one string in a log that can be as long as somebody's input or as deep as the program,
/// so a bundle keeps its message lines to a length, all of them to a total, and its stack to a number of frames — and
/// says what it left out, so a reader knows the text was cut and by how much.
/// </summary>
public sealed class ExceptionTextTests
{
    private static string Frame(int number) => $"   at Jason.Probe.Type{number}.Method(Int32 value)";

    private static string Frames(int count, string newline = "\r\n") =>
        string.Concat(Enumerable.Range(1, count).Select(number => Frame(number) + newline));

    private static string Line(char fill, int length) => new(fill, length);

    /// <summary>
    /// What this runtime threw for a value it could not read as a switch, as a log file holds it: a configuration
    /// binding failure with two inner exceptions, one stack in the middle of another, and a line that is not a frame
    /// and not indented.
    /// </summary>
    private static readonly string BoundSwitch = string.Join(
        "\r\n",
        "System.InvalidOperationException: Failed to convert configuration value 'SENTINEL_D_YES' at 'Dispatcher:Enabled' to type 'System.Boolean'.",
        " ---> System.FormatException: SENTINEL_D_YES is not a valid value for Boolean.",
        " ---> System.FormatException: String 'SENTINEL_D_YES' was not recognized as a valid Boolean.",
        "   at System.Boolean.Parse(String value)",
        "   at System.ComponentModel.BooleanConverter.ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, Object value)",
        "   --- End of inner exception stack trace ---",
        "   at System.ComponentModel.BooleanConverter.ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, Object value)",
        "   at Microsoft.Extensions.Configuration.ConfigurationBinder.TryConvertValue(Type type, String value, String path, Object& result, Exception& error)",
        "   --- End of inner exception stack trace ---",
        "   at Microsoft.Extensions.Configuration.ConfigurationBinder.BindInstance(Type type, BindingPoint bindingPoint, IConfiguration config, BinderOptions options, Boolean isParentCollection)",
        "   at Microsoft.Extensions.Configuration.ConfigurationBinder.BindProperty(PropertyInfo property, Object instance, IConfiguration config, BinderOptions options)",
        "   at Microsoft.Extensions.Configuration.ConfigurationBinder.BindProperties(Object instance, IConfiguration configuration, BinderOptions options, ParameterInfo[] constructorParameters)",
        "   at Microsoft.Extensions.Configuration.ConfigurationBinder.BindInstance(Type type, BindingPoint bindingPoint, IConfiguration config, BinderOptions options, Boolean isParentCollection)",
        "   at Microsoft.Extensions.Configuration.ConfigurationBinder.Bind(IConfiguration configuration, Object instance, Action`1 configureOptions)",
        "   at Microsoft.Extensions.Options.OptionsFactory`1.Create(String name)",
        "   at System.Lazy`1.ViaFactory(LazyThreadSafetyMode mode)",
        "--- End of stack trace from previous location ---",
        "   at System.Lazy`1.CreateValue()",
        "   at Jason.Runtime.Configuration.LiveSettings`1.get_Current()",
        "   at Jason.Runtime.Dispatch.LeaseEnforcer.EnforceAsync(JasonDbContext db, CancellationToken ct)",
        "   at Jason.Runtime.Dispatch.ScanRunner.InScopeAsync[TStep,TResult](Func`3 step)",
        "   at Jason.Runtime.Dispatch.ScanRunner.InScopeAsync[TStep,TResult](Func`3 step)",
        "   at Jason.Runtime.Dispatch.ScanRunner.ScanAsync(CancellationToken ct)",
        "   at Jason.Runtime.Dispatch.ScanRunner.ScanOnceAsync(CancellationToken ct)",
        "   at Jason.Runtime.Dispatch.DispatcherService.ExecuteAsync(CancellationToken stoppingToken)");

    /// <summary>The same kind of failure while the host starts: 49 frames, the deepest stack this runtime has been seen to throw.</summary>
    private static readonly string FailedToStart = string.Join(
        "\r\n",
        "System.InvalidOperationException: Failed to convert configuration value 'SENTINEL_F_START' at 'Plugins:Limits:TimeoutMs' to type 'System.Int32'.",
        " ---> System.ArgumentException: SENTINEL_F_START is not a valid value for Int32. (Parameter 'value')",
        " ---> System.FormatException: The input string 'SENTINEL_F_START' was not in a correct format.",
        "   at System.Number.ThrowFormatException[TChar](ReadOnlySpan`1 value)",
        "   at System.Int32.Parse(String s, NumberStyles style, IFormatProvider provider)",
        "   at System.ComponentModel.Int32Converter.FromString(String value, NumberFormatInfo formatInfo)",
        "   at System.ComponentModel.BaseNumberConverter.ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, Object value)",
        "   --- End of inner exception stack trace ---",
        "   at System.ComponentModel.BaseNumberConverter.ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, Object value)",
        "   at Microsoft.Extensions.Configuration.ConfigurationBinder.TryConvertValue(Type type, String value, String path, Object& result, Exception& error)",
        "   --- End of inner exception stack trace ---",
        "   at Microsoft.Extensions.Configuration.ConfigurationBinder.BindInstance(Type type, BindingPoint bindingPoint, IConfiguration config, BinderOptions options, Boolean isParentCollection)",
        "   at Microsoft.Extensions.Configuration.ConfigurationBinder.BindProperty(PropertyInfo property, Object instance, IConfiguration config, BinderOptions options)",
        "   at Microsoft.Extensions.Configuration.ConfigurationBinder.BindProperties(Object instance, IConfiguration configuration, BinderOptions options, ParameterInfo[] constructorParameters)",
        "   at Microsoft.Extensions.Configuration.ConfigurationBinder.BindInstance(Type type, BindingPoint bindingPoint, IConfiguration config, BinderOptions options, Boolean isParentCollection)",
        "   at Microsoft.Extensions.Configuration.ConfigurationBinder.BindProperty(PropertyInfo property, Object instance, IConfiguration config, BinderOptions options)",
        "   at Microsoft.Extensions.Configuration.ConfigurationBinder.BindProperties(Object instance, IConfiguration configuration, BinderOptions options, ParameterInfo[] constructorParameters)",
        "   at Microsoft.Extensions.Configuration.ConfigurationBinder.BindInstance(Type type, BindingPoint bindingPoint, IConfiguration config, BinderOptions options, Boolean isParentCollection)",
        "   at Microsoft.Extensions.Configuration.ConfigurationBinder.Bind(IConfiguration configuration, Object instance, Action`1 configureOptions)",
        "   at Microsoft.Extensions.Options.OptionsFactory`1.Create(String name)",
        "   at System.Lazy`1.ViaFactory(LazyThreadSafetyMode mode)",
        "   at System.Lazy`1.ExecutionAndPublication(LazyHelper executionAndPublication, Boolean useDefaultConstructor)",
        "   at System.Lazy`1.CreateValue()",
        "   at Microsoft.Extensions.Options.OptionsCache`1.GetOrAdd[TArg](String name, Func`3 createOptions, TArg factoryArgument)",
        "   at Microsoft.Extensions.Options.OptionsMonitor`1.Get(String name)",
        "   at Jason.Runtime.Configuration.LiveSettings`1.get_Current()",
        "   at Jason.Runtime.Configuration.ProviderOpBudgetValidator.Validate(String name, DispatcherOptions options)",
        "   at Microsoft.Extensions.Options.OptionsFactory`1.Create(String name)",
        "   at Microsoft.Extensions.Options.UnnamedOptionsManager`1.get_Value()",
        "   at Jason.Runtime.Dispatch.HandlerPool..ctor(IOptions`1 options, IServiceScopeFactory scopes, RunningAttemptRegistry registry, ILogger`1 logger)",
        "   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Constructor(Object obj, IntPtr* args)",
        "   at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span`1 copyOfArgs, BindingFlags invokeAttr)",
        "   at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)",
        "   at System.Reflection.RuntimeConstructorInfo.Invoke(BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)",
        "   at Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteRuntimeResolver.VisitConstructor(ConstructorCallSite constructorCallSite, RuntimeResolverContext context)",
        "   at Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteRuntimeResolver.VisitRootCache(ServiceCallSite callSite, RuntimeResolverContext context)",
        "   at Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteVisitor`2.VisitCallSite(ServiceCallSite callSite, TArgument argument)",
        "   at Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteRuntimeResolver.VisitConstructor(ConstructorCallSite constructorCallSite, RuntimeResolverContext context)",
        "   at Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteRuntimeResolver.VisitRootCache(ServiceCallSite callSite, RuntimeResolverContext context)",
        "   at Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteVisitor`2.VisitCallSite(ServiceCallSite callSite, TArgument argument)",
        "   at Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteRuntimeResolver.VisitConstructor(ConstructorCallSite constructorCallSite, RuntimeResolverContext context)",
        "   at Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteRuntimeResolver.VisitRootCache(ServiceCallSite callSite, RuntimeResolverContext context)",
        "   at Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteVisitor`2.VisitCallSite(ServiceCallSite callSite, TArgument argument)",
        "   at Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteRuntimeResolver.VisitIEnumerable(IEnumerableCallSite enumerableCallSite, RuntimeResolverContext context)",
        "   at Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteRuntimeResolver.VisitRootCache(ServiceCallSite callSite, RuntimeResolverContext context)",
        "   at Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteVisitor`2.VisitCallSite(ServiceCallSite callSite, TArgument argument)",
        "   at Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteRuntimeResolver.Resolve(ServiceCallSite callSite, ServiceProviderEngineScope scope)",
        "   at Microsoft.Extensions.DependencyInjection.ServiceProvider.CreateServiceAccessor(ServiceIdentifier serviceIdentifier)",
        "   at System.Collections.Concurrent.ConcurrentDictionary`2.GetOrAdd(TKey key, Func`2 valueFactory)",
        "   at Microsoft.Extensions.DependencyInjection.ServiceProvider.GetService(ServiceIdentifier serviceIdentifier, ServiceProviderEngineScope serviceProviderEngineScope)",
        "   at Microsoft.Extensions.DependencyInjection.ServiceProvider.GetService(Type serviceType)",
        "   at Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService(IServiceProvider provider, Type serviceType)",
        "   at Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService[T](IServiceProvider provider)",
        "   at Microsoft.Extensions.Hosting.Internal.Host.StartAsync(CancellationToken cancellationToken)");

    [Theory]
    [InlineData("")]
    [InlineData("System.X: boom")]
    [InlineData("System.X: boom\r\n   at a\r\n   at b\r\n")]
    [InlineData("System.X: boom\n   at a\n   at b\n")]
    [InlineData("System.X: boom\r\n   at a\n   at b\r\n\r\n")]
    [InlineData("\r\n\r\n")]
    public void A_text_under_every_limit_is_returned_as_it_was_written(string text) =>
        Assert.Equal(text, ExceptionText.Cap(text));

    [Fact]
    public void The_exception_this_runtime_throws_for_a_value_it_cannot_read_passes_under_the_cap_unchanged() =>
        Assert.Equal(BoundSwitch, ExceptionText.Cap(BoundSwitch));

    [Fact]
    public void A_failure_at_start_with_a_stack_of_49_frames_passes_under_the_cap_unchanged() =>
        Assert.Equal(FailedToStart, ExceptionText.Cap(FailedToStart));

    [Fact]
    public void A_message_line_of_500_characters_is_kept_whole()
    {
        var text = "Type: " + Line('m', 494) + "\r\n" + Frames(1);

        Assert.Equal(text, ExceptionText.Cap(text));
    }

    [Fact]
    public void A_message_line_of_501_characters_is_cut_at_500_and_says_how_much_was_cut()
    {
        var text = "Type: " + Line('m', 495) + "\r\n" + Frames(1);

        Assert.Equal("Type: " + Line('m', 494) + "... [truncated 1 characters]\r\n" + Frames(1), ExceptionText.Cap(text));
    }

    [Fact]
    public void Each_message_line_is_cut_on_its_own_and_the_line_endings_are_kept()
    {
        var text = Line('a', 700) + "\n" + Line('b', 20) + "\r\n" + Line('c', 501);

        Assert.Equal(
            Line('a', 500) + "... [truncated 200 characters]\n" + Line('b', 20) + "\r\n" + Line('c', 500) + "... [truncated 1 characters]",
            ExceptionText.Cap(text));
    }

    /// <summary>A cut that fell between the two halves of one character would leave half of it, which is no character at all.</summary>
    [Fact]
    [Critical(Family.X1, "a cut never splits a character")]
    public void A_cut_never_leaves_half_of_a_character_that_takes_two_places()
    {
        var text = Line('x', 499) + "😀" + "yy";

        var capped = ExceptionText.Cap(text);

        Assert.Equal(Line('x', 499) + "... [truncated 4 characters]", capped);
        Assert.All(capped, character => Assert.False(char.IsSurrogate(character)));
    }

    [Fact]
    public void Message_lines_together_keep_2000_characters_and_a_line_after_that_is_left_out_with_a_mark()
    {
        var full = string.Join("\r\n", Line('a', 500), Line('b', 500), Line('c', 500), Line('d', 500));

        Assert.Equal(full, ExceptionText.Cap(full));
        Assert.Equal(full + "\r\n... [truncated 1 characters]", ExceptionText.Cap(full + "\r\nz"));
    }

    [Fact]
    public void The_line_that_crosses_the_total_is_cut_where_it_does()
    {
        var text = string.Join("\r\n", Line('a', 500), Line('b', 500), Line('c', 500), Line('d', 400), Line('e', 300));

        Assert.Equal(
            string.Join("\r\n", Line('a', 500), Line('b', 500), Line('c', 500), Line('d', 400), Line('e', 100) + "... [truncated 200 characters]"),
            ExceptionText.Cap(text));
    }

    [Fact]
    public void Only_what_a_message_line_keeps_counts_towards_the_total()
    {
        var text = string.Join("\r\n", Line('a', 900), Line('b', 900), Line('c', 900), Line('d', 900), Line('e', 900));

        // The four lines keep 500 each, which is the whole total: the fifth has nothing left to keep.
        Assert.Equal(
            string.Join("\r\n", Line('a', 500) + "... [truncated 400 characters]", Line('b', 500) + "... [truncated 400 characters]", Line('c', 500) + "... [truncated 400 characters]", Line('d', 500) + "... [truncated 400 characters]")
                + "\r\n... [truncated 900 characters]",
            ExceptionText.Cap(text));
    }

    [Fact]
    public void Lines_left_out_one_after_another_are_one_mark_and_a_frame_between_them_ends_the_run()
    {
        var text = string.Join("\r\n", Line('a', 500), Line('b', 500), Line('c', 500), Line('d', 500), "xxxxxxxxxx", "yyyyyyyyyy", Frame(1), "zzzzz", "");

        Assert.Equal(
            string.Join("\r\n", Line('a', 500), Line('b', 500), Line('c', 500), Line('d', 500), "... [truncated 20 characters]", Frame(1), "... [truncated 5 characters]", ""),
            ExceptionText.Cap(text));
    }

    [Fact]
    public void Sixty_frames_are_kept()
    {
        var text = "System.X: boom\r\n" + Frames(60);

        Assert.Equal(text, ExceptionText.Cap(text));
    }

    [Fact]
    [Critical(Family.X1, "the stack cap edge")]
    public void Sixty_one_frames_are_cut_to_sixty_and_the_mark_says_how_many_more_there_were()
    {
        var text = "System.X: boom\r\n" + Frames(61);

        Assert.Equal("System.X: boom\r\n" + Frames(60) + "... [1 more frames]\r\n", ExceptionText.Cap(text));
    }

    [Fact]
    public void Every_frame_over_sixty_is_counted_in_the_mark()
    {
        var text = "System.X: boom\r\n" + Frames(100);

        Assert.Equal("System.X: boom\r\n" + Frames(60) + "... [40 more frames]\r\n", ExceptionText.Cap(text));
    }

    /// <summary>The sixty are for the whole text: an exception with inner exceptions has one stack for each, and a cap per stack would be a cap of sixty times as many.</summary>
    [Fact]
    public void The_sixty_frames_are_counted_across_every_stack_in_the_text_and_each_run_left_out_has_its_own_mark()
    {
        const string end = "   --- End of inner exception stack trace ---";
        var text = "System.X: boom\r\n ---> System.Y: inner\r\n" + Frames(40) + end + "\r\n" + Frames(40) + end + "\r\n" + Frames(5);

        var expected = "System.X: boom\r\n ---> System.Y: inner\r\n" + Frames(40) + end + "\r\n" + Frames(20)
            + "... [20 more frames]\r\n" + end + "\r\n" + "... [5 more frames]\r\n";

        Assert.Equal(expected, ExceptionText.Cap(text));
    }

    [Fact]
    public void The_mark_for_frames_ends_the_text_the_way_the_frames_it_replaced_did()
    {
        var withoutNewline = string.Concat(Enumerable.Range(1, 61).Select(Frame).Select((frame, index) => index == 0 ? frame : "\n" + frame));
        var withNewline = withoutNewline + "\n";

        Assert.EndsWith("\n... [1 more frames]", ExceptionText.Cap(withoutNewline), StringComparison.Ordinal);
        Assert.EndsWith("\n... [1 more frames]\n", ExceptionText.Cap(withNewline), StringComparison.Ordinal);
    }

    /// <summary>A frame is a line that is indented and says <c>at</c>; a line that says it at the margin is somebody's message.</summary>
    [Fact]
    public void A_line_that_begins_with_at_but_is_not_indented_is_a_message_and_not_a_frame()
    {
        var text = string.Concat(Enumerable.Range(1, 70).Select(_ => "at the margin\r\n"));

        Assert.Equal(text, ExceptionText.Cap(text));
    }

    [Fact]
    public void An_indented_line_that_begins_with_at_is_a_frame_whatever_the_indent_is()
    {
        var text = string.Concat(Enumerable.Range(1, 61).Select(_ => "\tat Tab.Frame()\r\n"));

        Assert.Equal(string.Concat(Enumerable.Range(1, 60).Select(_ => "\tat Tab.Frame()\r\n")) + "... [1 more frames]\r\n", ExceptionText.Cap(text));
    }

    /// <summary>
    /// A frame is <c>at</c>, a dotted method name and an opening bracket, as the runtime writes one. Any other indented
    /// line that begins with <c>at</c> is a line of somebody's message — a sentence, or a stack of another language — and
    /// is held to the length of one.
    /// </summary>
    [Theory]
    [InlineData("   at the start of a long sentence ", false)]
    [InlineData("   at Object.<anonymous> (/work/index.js:1:1)", false)]
    [InlineData("   at async Promise.all (index 0)", false)]
    [InlineData("   at Method()", false)]
    [InlineData("   at ", false)]
    [InlineData("   at System.Boolean.Parse(String value)", true)]
    [InlineData("   at System.Lazy`1.ViaFactory(LazyThreadSafetyMode mode)", true)]
    [InlineData("   at Jason.Runtime.Dispatch.ScanRunner.InScopeAsync[TStep,TResult](Func`3 step)", true)]
    [InlineData("   at Program.<Main>$(String[] args)", true)]
    [InlineData("   at X.<>c__DisplayClass1_0.<M>b__0() in /build/x.cs:line 12", true)]
    public void Only_a_line_shaped_like_a_frame_of_the_runtime_is_a_frame(string line, bool isFrame)
    {
        // 61 of them: 60 frames are kept and the 61st is marked as left out; a message line is not counted as a frame.
        var text = string.Concat(Enumerable.Repeat(line + "\r\n", 61));

        var capped = ExceptionText.Cap(text);

        Assert.Equal(isFrame, capped.EndsWith("... [1 more frames]\r\n", StringComparison.Ordinal));
        Assert.Equal(isFrame, capped.Contains("more frames]", StringComparison.Ordinal));
    }

    [Fact]
    public void A_message_line_that_is_shaped_like_an_indented_sentence_is_cut_at_500_like_any_other()
    {
        var message = "   at the start of a very long message " + Line('m', 2000);

        var capped = ExceptionText.Cap(message + "\r\n");

        Assert.Equal(message[..500] + $"... [truncated {message.Length - 500} characters]\r\n", capped);
    }

    [Fact]
    public void A_stack_of_another_language_inside_a_message_is_held_to_the_message_total()
    {
        var text = "Error: plugin failed\r\n" + string.Concat(Enumerable.Range(1, 200).Select(number => $"    at Object.method{number} (/work/plugin/index.js:{number}:1)\r\n"));

        var capped = ExceptionText.Cap(text);

        Assert.True(capped.Length < ExceptionText.MaxMessageCharacters + 200, $"{capped.Length} characters were kept.");
        Assert.Contains("... [truncated ", capped, StringComparison.Ordinal);
    }

    /// <summary>
    /// A text that has been capped is capped again by whoever receives it, and says the same. The mark is the cap's own
    /// words, so it is read as one: not as a line of the text to be cut, which would count the mark's own characters as
    /// what was left out.
    /// </summary>
    [Fact]
    public void A_text_the_cap_has_already_cut_is_not_cut_again()
    {
        var texts = new[]
        {
            Line('a', 700),
            "Type: " + Line('m', 900) + "\r\n   at Jason.Probe.Type1.Method()\r\n",
            string.Join("\r\n", Line('a', 500), Line('b', 500), Line('c', 500), Line('d', 500), "xxxxxxxxxx", "yyyyyyyyyy", Frame(1), "zzzzz", ""),
            "System.X: boom\r\n" + Frames(100),
            "System.X: boom\r\n ---> System.Y: inner\r\n" + Frames(40) + "   --- End of inner exception stack trace ---\r\n" + Frames(40) + "   --- End of inner exception stack trace ---\r\n" + Frames(5),
            string.Concat(Enumerable.Repeat(Line('m', 100) + "\r\n", 50)),
            Line('x', 499) + "😀" + "yy",
            BoundSwitch,
            FailedToStart,
        };

        foreach (var text in texts)
        {
            var once = ExceptionText.Cap(text);

            Assert.Equal(once, ExceptionText.Cap(once));
        }
    }

    [Fact]
    public void A_line_the_cap_has_cut_is_cut_further_only_where_the_total_now_requires_it_and_says_so_honestly()
    {
        // 1,900 characters of message, then a line that was already cut at 500 and says 200 more were cut before: only 100 fit.
        var text = string.Join("\r\n", Line('a', 500), Line('b', 500), Line('c', 500), Line('d', 400), Line('e', 500) + "... [truncated 200 characters]");

        Assert.Equal(
            string.Join("\r\n", Line('a', 500), Line('b', 500), Line('c', 500), Line('d', 400), Line('e', 100) + "... [truncated 600 characters]"),
            ExceptionText.Cap(text));
    }

    [Fact]
    public void A_mark_that_stands_on_a_line_of_its_own_is_kept_and_what_the_total_has_to_leave_out_is_marked_beside_it()
    {
        var text = string.Join("\r\n", Line('a', 500), Line('b', 500), Line('c', 500), Line('d', 500), "xxxxx", "... [truncated 7 characters]", "yy", "");

        Assert.Equal(
            string.Join("\r\n", Line('a', 500), Line('b', 500), Line('c', 500), Line('d', 500), "... [truncated 5 characters]", "... [truncated 7 characters]", "... [truncated 2 characters]", ""),
            ExceptionText.Cap(text));
    }

    /// <summary>A mark somebody wrote with the largest count there is must not make a count that goes below zero, nor make the text change when it is capped again.</summary>
    [Fact]
    public void A_mark_with_the_largest_count_there_is_stays_the_largest_and_never_wraps_round()
    {
        const string largest = "... [truncated 9223372036854775807 characters]";

        var cutLine = ExceptionText.Cap(Line('a', 600) + largest);
        var run = ExceptionText.Cap(string.Join("\r\n", Line('a', 500), Line('b', 500), Line('c', 500), Line('d', 500), "x" + largest, "y" + largest, "z" + largest));

        Assert.Equal(Line('a', 500) + largest, cutLine);
        Assert.Equal(ExceptionText.Cap(cutLine), cutLine);
        Assert.Equal(string.Join("\r\n", Line('a', 500), Line('b', 500), Line('c', 500), Line('d', 500), largest), run);
        Assert.Equal(ExceptionText.Cap(run), run);
    }

    /// <summary>A count that is not a number a line can have been cut by is not the cap's mark: it is a line of the text, and is cut like one.</summary>
    [Fact]
    public void A_count_too_large_to_be_a_count_is_not_a_mark()
    {
        var text = "... [truncated 99999999999999999999 characters]\r\n" + Line('a', 10) + "... [truncated 99999999999999999999 characters]\r\n" + Line('b', 600) + "... [truncated 99999999999999999999 characters]";

        var capped = ExceptionText.Cap(text);

        Assert.StartsWith("... [truncated 99999999999999999999 characters]\r\n" + Line('a', 10) + "... [truncated 99999999999999999999 characters]\r\n" + Line('b', 500), capped, StringComparison.Ordinal);
        Assert.EndsWith("... [truncated " + (600 + "... [truncated 99999999999999999999 characters]".Length - 500) + " characters]", capped, StringComparison.Ordinal);
    }

    /// <summary>However much is thrown at it, what is kept is a size that does not depend on the size of the text: a run of lines is one mark.</summary>
    [Fact]
    public void What_is_kept_does_not_grow_with_the_text()
    {
        var messages = string.Concat(Enumerable.Repeat(Line('m', 100) + "\r\n", 50_000));
        var stack = string.Concat(Enumerable.Repeat("   at Jason.Probe.Deep.Method()\r\n", 50_000));

        Assert.Equal(
            string.Concat(Enumerable.Repeat(Line('m', 100) + "\r\n", 20)) + "... [truncated 4998000 characters]\r\n",
            ExceptionText.Cap(messages));
        Assert.Equal(
            string.Concat(Enumerable.Repeat("   at Jason.Probe.Deep.Method()\r\n", 60)) + "... [49940 more frames]\r\n",
            ExceptionText.Cap(stack));
    }

    [Fact]
    public void The_cap_is_described_by_the_three_numbers_it_holds_to()
    {
        Assert.Equal((500, 2000, 60), (ExceptionText.MaxLineCharacters, ExceptionText.MaxMessageCharacters, ExceptionText.MaxFrames));
    }
}
