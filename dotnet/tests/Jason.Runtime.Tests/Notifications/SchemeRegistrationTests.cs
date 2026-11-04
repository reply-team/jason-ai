using Jason.Contracts.Notifications;
using Jason.Runtime.Notifications.Desktop;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// The per-user <c>jason:</c> link registration a toast's click starts this executable through: written beside the
/// AppUserModelID key, only where it is absent or ours and saying something else, never over another program's, and a
/// write the account refuses is an answer rather than an exception. Every row runs through a fake registry.
/// </summary>
public sealed class SchemeRegistrationTests
{
    private static readonly string[] Self = [@"C:\Users\someone\AppData\Local\Programs\Jason\jason.exe"];

    private const string SelfCommand = "\"C:\\Users\\someone\\AppData\\Local\\Programs\\Jason\\jason.exe\" \"%1\"";

    private static void Ours(FakeUserRegistry registry, string command = SelfCommand)
    {
        registry.Values[(LinkRegistration.Key, LinkRegistration.MarkerValue)] = LinkRegistration.Marker;
        registry.Values[(LinkRegistration.Key, string.Empty)] = LinkRegistration.Description;
        registry.Values[(LinkRegistration.Key, LinkRegistration.ProtocolValue)] = string.Empty;
        registry.Values[(LinkRegistration.CommandKey, string.Empty)] = command;
    }

    [Fact]
    public void An_absent_key_is_written_with_its_four_values_and_the_marker_first()
    {
        var registry = new FakeUserRegistry();

        Assert.Equal(("written", (string?)null), SchemeRegistration.Ensure(registry, Self));
        Assert.Equal(
            [
                (@"Software\Classes\jason", "AppUserModelID", "ai.jason.runtime"),
                (@"Software\Classes\jason", string.Empty, "URL:Jason"),
                (@"Software\Classes\jason", "URL Protocol", string.Empty),
                (@"Software\Classes\jason\shell\open\command", string.Empty, SelfCommand),
            ],
            registry.Writes);
    }

    /// <summary>The marker is the AppUserModelID a toast is shown under: one text, and the value Windows documents on a protocol key.</summary>
    [Fact]
    public void The_marker_is_the_toast_s_own_app_user_model_id() =>
        Assert.Equal(ToastRegistration.Aumid, LinkRegistration.Marker);

    [Fact]
    public void A_key_of_ours_that_says_the_same_is_left_alone()
    {
        var registry = new FakeUserRegistry();
        Ours(registry);

        Assert.Equal(("present", (string?)null), SchemeRegistration.Ensure(registry, Self));
        Assert.Empty(registry.Writes);
    }

    /// <summary>
    /// The real registry's merged view shows this account's own key too, so a key of ours is seen in both places; the
    /// merged view decides only where our marker is absent, and a key of ours that says the same is still left alone.
    /// </summary>
    [Fact]
    public void A_key_of_ours_the_merged_view_also_shows_is_present_and_nothing_is_written()
    {
        var registry = new FakeUserRegistry();
        Ours(registry);
        foreach (var ((key, name), value) in registry.Values.ToList())
        {
            registry.Classes[(key[@"Software\Classes\".Length..], name)] = value;
        }

        Assert.Equal(SelfCommand, registry.Classes[(LinkRegistration.ClassesCommandKey, string.Empty)]);
        Assert.Equal(("present", (string?)null), SchemeRegistration.Ensure(registry, Self));
        Assert.Empty(registry.Writes);
    }

    /// <summary>A second installation, or a development build, re-points the link to itself at its first toast.</summary>
    [Fact]
    public void A_key_of_ours_naming_another_executable_is_written_again()
    {
        var registry = new FakeUserRegistry();
        Ours(registry, "\"D:\\elsewhere\\jason.exe\" \"%1\"");

        Assert.Equal("written", SchemeRegistration.Ensure(registry, Self).State);
        Assert.Equal(SelfCommand, registry.Values[(LinkRegistration.CommandKey, string.Empty)]);
    }

    [Fact]
    public void A_key_of_ours_missing_a_value_is_written_again()
    {
        var registry = new FakeUserRegistry();
        Ours(registry);
        registry.Values.Remove((LinkRegistration.Key, LinkRegistration.ProtocolValue));

        Assert.Equal("written", SchemeRegistration.Ensure(registry, Self).State);
        Assert.Equal(string.Empty, registry.Values[(LinkRegistration.Key, LinkRegistration.ProtocolValue)]);
    }

    /// <summary>Another program's per-user key, which carries no marker of ours, is never written over.</summary>
    [Theory]
    [InlineData(@"Software\Classes\jason", "URL Protocol")]
    [InlineData(@"Software\Classes\jason", "")]
    [InlineData(@"Software\Classes\jason\shell\open\command", "")]
    public void Another_program_s_key_is_foreign_and_nothing_is_written(string key, string name)
    {
        var registry = new FakeUserRegistry();
        registry.Values[(key, name)] = "theirs";

        Assert.Equal(("foreign", (string?)null), SchemeRegistration.Ensure(registry, Self));
        Assert.Empty(registry.Writes);
    }

    [Fact]
    public void A_key_whose_marker_is_another_s_is_foreign()
    {
        var registry = new FakeUserRegistry();
        Ours(registry);
        registry.Values[(LinkRegistration.Key, LinkRegistration.MarkerValue)] = "some.other.app";

        Assert.Equal("foreign", SchemeRegistration.Ensure(registry, Self).State);
        Assert.Empty(registry.Writes);
    }

    /// <summary>A machine-wide <c>jason</c> of another program shows in the merged view, and a per-user key would shadow it.</summary>
    [Theory]
    [InlineData("jason", "URL Protocol")]
    [InlineData("jason", "")]
    [InlineData(@"jason\shell\open\command", "")]
    public void A_machine_wide_key_of_another_program_is_foreign_and_never_shadowed(string key, string name)
    {
        var registry = new FakeUserRegistry();
        registry.Classes[(key, name)] = "theirs";

        Assert.Equal(("foreign", (string?)null), SchemeRegistration.Ensure(registry, Self));
        Assert.Empty(registry.Writes);
    }

    /// <summary>
    /// A program the person chose for <c>jason:</c> links routes them past any class key, so a choice that is not Jason's
    /// own class is foreign, whether or not the key carries our marker.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_protocol_choice_of_another_program_is_foreign(bool marked)
    {
        var registry = new FakeUserRegistry();
        if (marked)
        {
            Ours(registry);
        }

        registry.Values[(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\jason\UserChoice", "ProgId")] = "Some.Other.App";

        Assert.Equal(("foreign", (string?)null), SchemeRegistration.Ensure(registry, Self));
        Assert.Empty(registry.Writes);
    }

    [Fact]
    public void A_protocol_choice_of_jason_s_own_class_is_ours()
    {
        var registry = new FakeUserRegistry();
        registry.Values[(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\jason\UserChoice", "ProgId")] = "jason";

        Assert.Equal("written", SchemeRegistration.Ensure(registry, Self).State);
    }

    /// <summary>Windows paths ignore case, so a command that differs from ours only in case names this executable.</summary>
    [Fact]
    public void A_key_of_ours_whose_command_differs_only_in_case_is_left_alone()
    {
        var registry = new FakeUserRegistry();
        Ours(registry, SelfCommand.ToUpperInvariant());

        Assert.Equal(("present", (string?)null), SchemeRegistration.Ensure(registry, Self));
        Assert.Empty(registry.Writes);
    }

    /// <summary>
    /// The shell substitutes its <c>%</c> tokens anywhere in a command, so a path holding one is a command Jason will not
    /// write: the registration fails, and the toast is shown without a link.
    /// </summary>
    [Fact]
    public void A_percent_in_the_executable_s_path_is_refused_and_nothing_is_written()
    {
        var registry = new FakeUserRegistry();

        Assert.Equal(("failed", "0x80070057"), SchemeRegistration.Ensure(registry, [@"C:\Users\Ann%1x\AppData\Local\Programs\Jason\jason.exe"]));
        Assert.Empty(registry.Writes);
    }

    /// <summary>A command that cannot be composed is a failed registration, never an exception that would fail the toast.</summary>
    [Fact]
    public void A_command_that_cannot_be_composed_is_an_answer_not_an_exception()
    {
        var registry = new FakeUserRegistry();

        Assert.Equal(("failed", "0x80070057"), SchemeRegistration.Ensure(registry, ["C:\\a\"b\\jason.exe"]));
        Assert.Empty(registry.Writes);
    }

    [Fact]
    public void A_write_the_account_refuses_is_an_answer_with_its_hresult()
    {
        var registry = new FakeUserRegistry { Refuse = new UnauthorizedAccessException("denied") };

        Assert.Equal(("failed", "0x80070005"), SchemeRegistration.Ensure(registry, Self));
    }

    /// <summary>A write refused part-way leaves the marker, so the next toast finds the key ours and finishes it.</summary>
    [Fact]
    public void A_key_left_part_way_is_ours_and_finished_at_the_next_toast()
    {
        var first = new FakeUserRegistry { Refuse = new IOException("busy"), RefuseAfter = 1 };
        Assert.Equal("failed", SchemeRegistration.Ensure(first, Self).State);

        var next = new FakeUserRegistry();
        foreach (var (key, value) in first.Values)
        {
            next.Values[key] = value;
        }

        Assert.Equal("written", SchemeRegistration.Ensure(next, Self).State);
        Assert.Equal(SelfCommand, next.Values[(LinkRegistration.CommandKey, string.Empty)]);
    }

    /// <summary>A build run through the muxer names the muxer and the assembly, each quoted, so the link starts this very build.</summary>
    [Fact]
    public void A_muxed_build_s_command_names_the_muxer_and_the_assembly()
    {
        var registry = new FakeUserRegistry();

        SchemeRegistration.Ensure(registry, [@"C:\Program Files\dotnet\dotnet.exe", @"C:\src\jason\bin\jason.dll"]);

        Assert.Equal(
            "\"C:\\Program Files\\dotnet\\dotnet.exe\" \"C:\\src\\jason\\bin\\jason.dll\" \"%1\"",
            registry.Values[(LinkRegistration.CommandKey, string.Empty)]);
    }
}
