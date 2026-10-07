using System.Text;
using Jason.Contracts.Configuration;
using Jason.Runtime.Configuration;
using Microsoft.Extensions.Configuration;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// What the binding holds a section to, by the type each path has to be, over the shapes the option classes do not use
/// today and a later one easily could: an enum and a struct of the settings' own, a number that may be absent, a span of
/// time, and lists of those. Each is read from text the way the binder reads it, so the comparison has to accept what
/// the binder accepts and refuse only what it passed over — a guard on the decisions themselves, so a new kind of
/// setting is not refused for being written correctly.
/// </summary>
public sealed class SafeBindingShapeTests
{
    [Fact]
    public void Each_shape_written_correctly_is_bound_and_refused_for_nothing()
    {
        var shapes = Bound("""
            {"Shapes":{"Mode":"Loud","Window":{"Seconds":5},"Limit":"7","Wait":"00:05:00",
                       "Waits":["00:05:00","00:00:30"],"Counts":["1","2"],"Modes":{"first":"Quiet"}}}
            """, out var failures);

        Assert.Null(failures);
        Assert.Equal(Mode.Loud, shapes.Mode);
        Assert.Equal(7, shapes.Limit);
        Assert.Equal(TimeSpan.FromMinutes(5), shapes.Wait);
        Assert.Equal([TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30)], shapes.Waits);
        Assert.Equal([1, 2], shapes.Counts);
        Assert.Equal(Mode.Quiet, shapes.Modes["first"]);
    }

    [Fact]
    public void A_number_that_may_be_absent_is_absent_when_written_as_nothing()
    {
        var shapes = Bound("""{"Shapes":{"Limit":null}}""", out var failures);

        Assert.Null(failures);
        Assert.Null(shapes.Limit);
    }

    /// <summary>
    /// And each of them written as an object or a list where text belongs, or as text the binder cannot read in a list
    /// of numbers, is refused by its path and its type. None of these names is a settings name, so every key is written
    /// as a placeholder; the index of a list shows only under a list the settings declare.
    /// </summary>
    [Theory]
    [InlineData("""{"Shapes":{"Mode":{"x":"SENTINEL"}}}""", "<key>:<key> could not be read as Jason.Runtime.Tests.Configuration.SafeBindingShapeTests+Mode.")]
    [InlineData("""{"Shapes":{"Limit":["SENTINEL"]}}""", "<key>:<key> could not be read as System.Int32.")]
    [InlineData("""{"Shapes":{"Wait":{"x":"SENTINEL"}}}""", "<key>:<key> could not be read as System.TimeSpan.")]
    [InlineData("""{"Shapes":{"Waits":[{"x":"SENTINEL"}]}}""", "<key>:<key>:<key> could not be read as System.TimeSpan.")]
    [InlineData("""{"Shapes":{"Counts":["1","SENTINEL","3"]}}""", "<key>:<key>:<key> could not be read as System.Int32.")]
    [InlineData("""{"Shapes":{"Modes":{"first":["SENTINEL"]}}}""", "<key>:<key>:<key> could not be read as Jason.Runtime.Tests.Configuration.SafeBindingShapeTests+Mode.")]
    public void Each_shape_written_as_something_the_binder_passes_over_is_refused_by_path_and_type(string settings, string expected)
    {
        Bound(settings, out var failures);

        Assert.Equal([expected], failures);
    }

    /// <summary>
    /// A class that holds one of its own: the walk follows what the file wrote and nothing it did not, so a section left
    /// out ends it rather than a chain of absent sections walked without end, and a value written at any depth of the
    /// chain is still read by its path and its type.
    /// </summary>
    [Fact]
    public void A_class_that_holds_one_of_its_own_is_walked_as_deep_as_the_file_wrote_it_and_no_deeper()
    {
        var shallow = Bound("""{"Shapes":{"Limit":"7","Head":{"Value":1}}}""", out var none);
        Assert.Null(none);
        Assert.Equal(1, shallow.Head!.Value);
        Assert.Null(shallow.Head.Next);

        Bound("""{"Shapes":{"Head":{"Value":1,"Next":{"Value":2,"Next":{"Value":["SENTINEL"]}}}}}""", out var failures);

        Assert.Equal([SettingKey.ShownPath("Shapes:Head:Next:Next:Value", new HashSet<string>()) + " could not be read as System.Int32."], failures);
    }

    /// <summary>A value computed from the others is no setting, and whatever is written where its name would be is not refused as one.</summary>
    [Fact]
    public void Whatever_is_written_onto_a_computed_value_is_not_refused_as_a_setting()
    {
        var shapes = Bound("""{"Shapes":{"Limit":"7","Summary":{"x":"SENTINEL"}}}""", out var failures);

        Assert.Null(failures);
        Assert.Equal("limit 7", shapes.Summary);
    }

    private static Shapes Bound(string settings, out IReadOnlyList<string>? failures)
    {
        var configuration = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(settings))).Build();
        var shapes = new Shapes();
        SafeBinding.Bind(configuration.GetSection("Shapes"), shapes, SafeBinding.NoInstalledPlugins);
        failures = SafeBinding.TryGetFailures(shapes, out var refused) ? refused : null;
        Assert.DoesNotContain(failures ?? [], failure => failure.Contains("SENTINEL", StringComparison.Ordinal));
        return shapes;
    }

    public enum Mode
    {
        Quiet,
        Loud,
    }

    /// <summary>A struct of the settings' own, with no converter from text.</summary>
    public struct Window
    {
        public int Seconds { get; set; }
    }

    public sealed class Shapes
    {
        public Mode Mode { get; set; }

        public Window Window { get; set; }

        public int? Limit { get; set; }

        public TimeSpan Wait { get; set; }

        public List<TimeSpan> Waits { get; set; } = [];

        public List<int> Counts { get; set; } = [];

        public Dictionary<string, Mode> Modes { get; set; } = new(StringComparer.Ordinal);

        public Node? Head { get; set; }

        /// <summary>Computed from the others: there is nothing to set.</summary>
        public string Summary => $"limit {Limit}";
    }

    /// <summary>A class that holds one of its own.</summary>
    public sealed class Node
    {
        public int Value { get; set; }

        public Node? Next { get; set; }
    }
}
