using System.Text;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Tests.Plugins;

public class BoundedCaptureTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Everything_under_the_cap_is_kept_whole()
    {
        var capture = new BoundedCapture(1024);

        await capture.DrainAsync(Stream("hello, plugin"), Ct);

        Assert.Equal("hello, plugin", capture.Text);
        Assert.False(capture.Truncated);
        Assert.Equal(13, capture.TotalBytes);
    }

    [Fact]
    public async Task An_empty_stream_captures_nothing()
    {
        var capture = new BoundedCapture(1024);

        await capture.DrainAsync(Stream(string.Empty), Ct);

        Assert.Equal(string.Empty, capture.Text);
        Assert.False(capture.Truncated);
        Assert.Equal(0, capture.TotalBytes);
    }

    [Fact]
    public async Task Beyond_the_cap_the_first_bytes_are_kept_and_the_rest_is_still_read()
    {
        var truncations = 0;
        var capture = new BoundedCapture(8, () => truncations++);

        await capture.DrainAsync(Stream(new string('a', 8) + new string('b', 5000)), Ct);

        Assert.Equal(new string('a', 8), capture.Text);
        Assert.True(capture.Truncated);
        Assert.Equal(5008, capture.TotalBytes);
        Assert.Equal(1, truncations);
    }

    [Fact]
    public async Task Text_that_is_not_ascii_is_counted_in_bytes_and_read_back_as_it_was_written()
    {
        var capture = new BoundedCapture(1024);

        await capture.DrainAsync(Stream("naïve — ok"), Ct);

        Assert.Equal("naïve — ok", capture.Text);
        Assert.Equal(Encoding.UTF8.GetByteCount("naïve — ok"), capture.TotalBytes);
    }

    [Fact]
    public async Task A_capture_that_was_never_drained_is_empty()
    {
        var capture = new BoundedCapture(16);

        Assert.Equal(string.Empty, capture.Text);
        Assert.False(capture.Truncated);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Both callers wait for a drain only as long as a kill grace and then abandon it, because a program can
    /// leave something behind that inherited its pipes. What the caller was told is therefore what stood here
    /// when it let go, and the stream is still read to the end — a pipe nobody drains is how a child hangs.
    /// </summary>
    [Fact]
    public async Task A_capture_the_caller_has_let_go_of_keeps_what_it_had()
    {
        var capture = new BoundedCapture(1024);
        await capture.DrainAsync(Stream("what the caller was told"), Ct);

        capture.Freeze();
        var afterwards = Stream(" and what the helper wrote afterwards");
        await capture.DrainAsync(afterwards, Ct);

        Assert.Equal("what the caller was told", capture.Text);
        Assert.Equal(24, capture.TotalBytes);
        Assert.Equal(afterwards.Length, afterwards.Position);
    }

    [Fact]
    public void A_cap_that_is_not_a_size_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedCapture(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedCapture(-1));
    }
}
