using Jason.Cli.Doctor;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Doctor;

/// <summary>
/// A path written for a person from the home directory: <c>~</c> and the rest where it is inside it, by the same rule the
/// diagnostic bundle's rewriter decides that by, and the path as it is anywhere else. Every path here is made up, so what
/// the tests say does not depend on whose machine runs them.
/// </summary>
public sealed class FromHomeTests
{
    [Theory]
    [InlineData(@"C:\Users\Alex\.jason\bin\jason.exe", @"C:\Users\Alex", @"~\.jason\bin\jason.exe")]
    [InlineData(@"C:\Users\Alex", @"C:\Users\Alex", "~")]
    [InlineData(@"C:\Users\Alex\.jason", @"C:\Users\Alex\", @"~\.jason")]
    [InlineData("C:/Users/Alex/.jason/logs", @"C:\Users\Alex", "~/.jason/logs")]
    [InlineData("/home/ada/.jason/bin/jason", "/home/ada", "~/.jason/bin/jason")]
    [InlineData("/home/ada//.jason/x", "/home/ada", "~//.jason/x")]
    public void A_path_inside_the_home_directory_is_written_from_it_with_the_rest_as_it_was(string path, string home, string written) =>
        Assert.Equal(written, IdentityRewriter.FromHome(path, home));

    /// <summary>
    /// The boundary a prefix comparison gets wrong: a home directory whose name is the start of another's. A path in the
    /// other one is not in this one.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Users\Alex\.jason\bin\jason.exe", @"C:\Users\Al")]
    [InlineData(@"C:\Users\Al2\x", @"C:\Users\Al")]
    [InlineData("/home/adam/x", "/home/ada")]
    [InlineData(@"D:\jason\bin\jason.exe", @"C:\Users\Alex")]
    [InlineData(@"C:\Users", @"C:\Users\Alex")]
    public void A_path_outside_the_home_directory_is_left_as_it_is(string path, string home) =>
        Assert.Equal(path, IdentityRewriter.FromHome(path, home));

    /// <summary>A home directory that is only a root holds nothing, as the bundle's rewriter holds it: every path would be in it.</summary>
    [Theory]
    [InlineData(@"C:\jason\bin\jason.exe", @"C:\")]
    [InlineData("/opt/jason/jason", "/")]
    public void A_home_directory_that_is_only_a_root_holds_nothing(string path, string home) =>
        Assert.Equal(path, IdentityRewriter.FromHome(path, home));

    [Fact]
    [Critical(Family.S4, "case-insensitive home on Windows")]
    public void On_Windows_a_path_in_another_case_is_the_same_path()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere a path in another case is another path.");

        Assert.Equal(@"~\.jason\x", IdentityRewriter.FromHome(@"c:\users\ALEX\.jason\x", @"C:\Users\Alex"));
    }

    [Fact]
    public void Elsewhere_a_path_in_another_case_is_another_path()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "On Windows a path in another case is the same path.");

        Assert.Equal("/home/ADA/x", IdentityRewriter.FromHome("/home/ADA/x", "/home/ada"));
    }
}
