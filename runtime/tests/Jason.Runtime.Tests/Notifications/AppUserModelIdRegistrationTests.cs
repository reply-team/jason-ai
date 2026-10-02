using Jason.Contracts.Notifications;
using Jason.Runtime.Notifications.Desktop;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// The per-user key a toast is shown under: written only where it is absent or says something else, with a display
/// name and nothing more, and a write the account refuses is an answer rather than an exception.
/// </summary>
public sealed class AppUserModelIdRegistrationTests
{
    private sealed class RecordingRegistry : IUserRegistry
    {
        public Dictionary<(string Key, string Name), string> Values { get; } = [];

        public List<(string Key, string Name, string Value)> Writes { get; } = [];

        public Exception? Refuse { get; init; }

        public string? ReadString(string key, string name) => Values.GetValueOrDefault((key, name));

        public void WriteString(string key, string name, string value)
        {
            if (Refuse is not null)
            {
                throw Refuse;
            }

            Writes.Add((key, name, value));
            Values[(key, name)] = value;
        }
    }

    [Fact]
    public void An_absent_key_is_written_with_the_display_name_alone()
    {
        var registry = new RecordingRegistry();

        Assert.Equal(("written", (string?)null), AppUserModelIdRegistration.Ensure(registry));
        Assert.Equal([(ToastRegistration.Key, "DisplayName", "Jason")], registry.Writes);
    }

    [Fact]
    public void A_key_that_says_the_same_is_left_alone()
    {
        var registry = new RecordingRegistry();
        registry.Values[(ToastRegistration.Key, "DisplayName")] = "Jason";

        Assert.Equal(("present", (string?)null), AppUserModelIdRegistration.Ensure(registry));
        Assert.Empty(registry.Writes);
    }

    [Fact]
    public void A_key_that_says_something_else_is_written_again()
    {
        var registry = new RecordingRegistry();
        registry.Values[(ToastRegistration.Key, "DisplayName")] = "Someone";

        Assert.Equal("written", AppUserModelIdRegistration.Ensure(registry).State);
        Assert.Single(registry.Writes);
    }

    [Fact]
    public void A_write_the_account_refuses_is_an_answer_with_its_hresult()
    {
        var registry = new RecordingRegistry { Refuse = new UnauthorizedAccessException("denied") };

        var (state, hr) = AppUserModelIdRegistration.Ensure(registry);

        Assert.Equal("failed", state);
        Assert.Equal("0x80070005", hr);
    }

    /// <summary>A pointer needs no activation and no icon: nothing but the display name is ever written.</summary>
    [Fact]
    public void Nothing_but_the_display_name_is_ever_written()
    {
        var registry = new RecordingRegistry();
        AppUserModelIdRegistration.Ensure(registry);

        Assert.DoesNotContain(registry.Writes, write => write.Name is "IconUri" or "CustomActivator");
        Assert.All(registry.Writes, write => Assert.Equal(ToastRegistration.Key, write.Key));
    }
}
