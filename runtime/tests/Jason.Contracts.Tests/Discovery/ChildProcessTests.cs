using System.Text;
using Jason.Contracts.Discovery;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests.Discovery;

/// <summary>
/// The one door every child process comes out of. What it sets is the whole of its job, and
/// <c>NoChildGetsAWindowTests</c> in the application's tests holds the rest of the product to it.
/// </summary>
public class ChildProcessTests
{
    [Fact]
    [Critical(Family.B1, "the door's two settings, observed on a real child")]
    public void A_child_is_started_with_no_window_and_without_the_shell()
    {
        var start = ChildProcess.StartInfo("program");

        Assert.Equal("program", start.FileName);
        Assert.True(start.CreateNoWindow);
        Assert.False(start.UseShellExecute);
        Assert.Equal((false, false, false), (start.RedirectStandardInput, start.RedirectStandardOutput, start.RedirectStandardError));
    }

    /// <summary>
    /// A child that speaks to Jason has all three streams, each UTF-8 without a byte-order mark: written at the start of
    /// a pipe, a preamble is three bytes a node program's JSON.parse refuses.
    /// </summary>
    [Fact]
    [Critical(Family.F4, "stdin encoding of a Jason child")]
    public void A_child_that_speaks_to_jason_is_spoken_to_in_utf_8_without_a_byte_order_mark()
    {
        var start = ChildProcess.StartInfo("program", ChildKind.SpeaksToJason);

        Assert.True(start.CreateNoWindow);
        Assert.False(start.UseShellExecute);
        Assert.Equal((true, true, true), (start.RedirectStandardInput, start.RedirectStandardOutput, start.RedirectStandardError));
        foreach (var encoding in new[] { start.StandardInputEncoding, start.StandardOutputEncoding, start.StandardErrorEncoding })
        {
            Assert.IsType<UTF8Encoding>(encoding);
            Assert.True(encoding!.GetPreamble().Length == 0, $"The child's stream is {encoding.EncodingName} with a {encoding.GetPreamble().Length}-byte preamble.");
        }
    }

    /// <summary>
    /// A system tool writes in the console's code page, so its output is read in that page: no encoding is chosen,
    /// which is how .NET reads it. Its input is this process's own unless the caller asks for one of its own to close.
    /// </summary>
    [Theory]
    [Critical(Family.F4, "a system tool's output decoding")]
    [InlineData(ChildKind.SystemTool, false)]
    [InlineData(ChildKind.SystemToolWithInput, true)]
    public void A_system_tool_is_read_in_the_console_s_code_page(ChildKind kind, bool input)
    {
        var start = ChildProcess.StartInfo("program", kind);

        Assert.True(start.CreateNoWindow);
        Assert.False(start.UseShellExecute);
        Assert.Equal((input, true, true), (start.RedirectStandardInput, start.RedirectStandardOutput, start.RedirectStandardError));
        Assert.Equal<Encoding?>([null, null, null], [start.StandardInputEncoding, start.StandardOutputEncoding, start.StandardErrorEncoding]);
    }

    [Fact]
    public void A_kind_the_door_does_not_know_is_refused()
    {
        var refused = Assert.Throws<ArgumentOutOfRangeException>(() => ChildProcess.StartInfo("program", (ChildKind)99));

        Assert.Equal("kind", refused.ParamName);
    }
}
