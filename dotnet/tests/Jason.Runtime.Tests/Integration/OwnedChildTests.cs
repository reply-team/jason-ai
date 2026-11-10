using System.Diagnostics;
using Jason.Runtime.Tests.Plugins;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// A test ends only a child it can prove is its own. A pid that was written down may, by the time it is used,
/// belong to somebody else, and the reuse itself cannot be summoned on demand; what these hold is the rule that
/// keeps a reused pid from being ended: the process that answers to it must have started when the child did.
/// An access refusal while reading a process is treated as "gone or not ours" in the same place, and cannot be
/// summoned on demand either, so no row pretends to hold it.
/// </summary>
public class OwnedChildTests
{
    private const string HostName = "dotnet";

    /// <summary>How long to watch a process that must not die. Waiting for something that should not happen.</summary>
    private const int Watch = 1_000;

    [Fact]
    public void A_process_that_started_long_after_the_child_recorded_is_left_alone()
    {
        using var bystander = StartBystander();
        try
        {
            // The pid was recorded a minute before this process began: the child that had it is long gone and
            // this is whoever the machine gave the number to next.
            OwnedChild.End(bystander.Id, DateTimeOffset.UtcNow.AddMinutes(-1), HostName);

            Assert.False(bystander.WaitForExit(Watch), $"Process {bystander.Id} started just now and was ended as the child recorded a minute ago.");
        }
        finally
        {
            OwnedChild.End(bystander);
        }
    }

    [Fact]
    public void A_process_that_started_when_the_child_did_is_ended()
    {
        using var bystander = StartBystander();
        try
        {
            OwnedChild.End(bystander.Id, DateTimeOffset.UtcNow, HostName);

            Assert.True(bystander.WaitForExit(30_000), $"Process {bystander.Id} started when the child recorded and was not ended.");
        }
        finally
        {
            OwnedChild.End(bystander);
        }
    }

    [Fact]
    public void A_process_with_another_name_is_left_alone_even_when_it_started_in_time()
    {
        using var bystander = StartBystander();
        try
        {
            OwnedChild.End(bystander.Id, DateTimeOffset.UtcNow, "Jason.FakeAgentHost");

            Assert.False(bystander.WaitForExit(Watch), $"Process {bystander.Id} is named {HostName} and was ended as Jason.FakeAgentHost.");
        }
        finally
        {
            OwnedChild.End(bystander);
        }
    }

    [Fact]
    public void A_process_that_began_before_the_moment_the_child_said_it_began_is_left_alone()
    {
        using var bystander = StartBystander();
        try
        {
            // The child said it began a minute from now. A record taken around a start admits a process that began
            // before it; the child's own word does not, because the number was the child's from the moment it began.
            using (var admitted = OwnedChild.Open(bystander.Id, DateTimeOffset.UtcNow.AddMinutes(1), HostName))
            {
                Assert.NotNull(admitted);
            }

            using var held = OwnedChild.OpenKnown(bystander.Id, DateTimeOffset.UtcNow.AddMinutes(1), HostName);

            Assert.True(held is null, $"Process {bystander.Id} began a minute before the child said it began and was taken for the child.");
        }
        finally
        {
            OwnedChild.End(bystander);
        }
    }

    [Fact]
    public void A_process_that_began_when_the_child_said_it_began_is_ended()
    {
        using var bystander = StartBystander();
        try
        {
            OwnedChild.EndKnown(bystander.Id, new DateTimeOffset(bystander.StartTime.ToUniversalTime(), TimeSpan.Zero), HostName);

            Assert.True(bystander.WaitForExit(30_000), $"Process {bystander.Id} began when the child said it began and was not ended.");
        }
        finally
        {
            OwnedChild.End(bystander);
        }
    }

    [Fact]
    public void A_held_process_that_started_at_another_time_than_it_was_opened_at_is_left_alone()
    {
        using var bystander = StartBystander();
        try
        {
            // The number changed hands while the handle was held: the start time kept at the opening is no longer
            // the start time of the process that answers to it.
            using var held = new OwnedChild.Held(bystander, DateTimeOffset.UtcNow.AddMinutes(-1));

            OwnedChild.End(held);

            Assert.False(bystander.WaitForExit(Watch), $"Process {bystander.Id} started just now and was ended as the process opened a minute ago.");
        }
        finally
        {
            OwnedChild.End(bystander);
        }
    }

    [Fact]
    public void A_held_process_that_started_when_it_was_opened_is_ended()
    {
        using var bystander = StartBystander();
        try
        {
            using var held = OwnedChild.Open(bystander.Id, DateTimeOffset.UtcNow, HostName)
                ?? throw new InvalidOperationException($"Process {bystander.Id} was not taken for the child it is.");

            OwnedChild.End(held);

            Assert.True(bystander.WaitForExit(30_000), $"Process {bystander.Id} was held from its start and was not ended.");
        }
        finally
        {
            OwnedChild.End(bystander);
        }
    }

    [Theory]
    [InlineData("dotnet", "dotnet", true)]
    [InlineData("DOTNET", "dotnet", true)]
    [InlineData("Jason.FakeAgentHost", "Jason.FakeAgentHost", true)]
    [InlineData("Jason.FakeAgent", "Jason.FakeAgentHost", true)]
    [InlineData("Jason.FakeAgen", "Jason.FakeAgentHost", false)]
    [InlineData("dot", "dotnet", false)]
    [InlineData("pwsh", "dotnet", false)]
    public void A_name_is_the_one_written_down_when_it_is_that_name_or_the_part_of_it_a_platform_keeps(string seen, string written, bool fits)
    {
        Assert.True(
            OwnedChild.NameFits(seen, written) == fits,
            $"A process named '{seen}' was {(fits ? "refused" : "taken")} as '{written}'.");
    }

    [Fact]
    public void A_child_is_ended_through_the_handle_that_was_opened_while_it_lived()
    {
        using var child = StartBystander();

        OwnedChild.End(child);

        Assert.True(child.WaitForExit(30_000), $"Process {child.Id} was not ended through its own handle.");
    }

    [Fact]
    public void A_pid_nobody_holds_is_nothing_to_end()
    {
        using var finished = StartBystander();
        var pid = finished.Id;
        OwnedChild.End(finished);
        Assert.True(finished.WaitForExit(30_000));

        // Recorded a minute ago, so that even a program that took the number in the instant between the exit and
        // this line started too late to be the child.
        Assert.Null(OwnedChild.Open(pid, DateTimeOffset.UtcNow.AddMinutes(-1), HostName));
        Assert.False(OwnedChild.IsRunning(pid));
    }

    /// <summary>
    /// A long-lived child this test owns: the stand-in vendor program, holding for half a minute, handed over once it
    /// answers to its own name. On Linux <see cref="Process.Start(ProcessStartInfo)"/> can return before it does, and a
    /// name read in that instant is the name of the thread that started it ('.NET TP Worker'), so the name rule would
    /// leave the test's own child alone.
    /// </summary>
    private static Process StartBystander()
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(FakeProviderCli.Dll);
        info.ArgumentList.Add("hold");
        info.ArgumentList.Add("30000");
        var bystander = Process.Start(info)!;
        var waited = Stopwatch.StartNew();
        while (!AnswersTo(bystander, HostName))
        {
            if (bystander.HasExited || waited.Elapsed > TimeSpan.FromSeconds(30))
            {
                OwnedChild.End(bystander);
                bystander.Dispose();
                Assert.Fail($"The bystander never answered to the name {HostName}.");
            }

            Thread.Sleep(10);
        }

        return bystander;
    }

    private static bool AnswersTo(Process process, string name)
    {
        try
        {
            using var seen = Process.GetProcessById(process.Id);
            return OwnedChild.NameFits(seen.ProcessName, name);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
