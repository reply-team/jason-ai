using System.Text;

namespace Jason.Contracts.Plugins;

/// <summary>
/// Reads a child's output without ever letting it fill memory or block: the first bytes up to a cap are kept,
/// the crossing is flagged once, and the rest of the stream is still read to the end — a pipe nobody drains is
/// how a child process hangs forever. Both sides of the protocol capture output this way: the plugin host for
/// <c>host.exec</c>, the runtime for the plugin host itself.
/// </summary>
public sealed class BoundedCapture
{
    private readonly int _maxBytes;
    private readonly Action? _onTruncated;
    private readonly byte[] _kept;
    private int _keptCount;

    public BoundedCapture(int maxBytes, Action? onTruncated = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        _maxBytes = maxBytes;
        _onTruncated = onTruncated;
        _kept = new byte[maxBytes];
    }

    /// <summary>What was captured, decoded as UTF-8; a sequence cut by the cap decodes to a replacement character.</summary>
    public string Text => Encoding.UTF8.GetString(_kept, 0, _keptCount);

    /// <summary>Whether the stream held more than the cap — the caller says so rather than pretending it did not.</summary>
    public bool Truncated { get; private set; }

    /// <summary>Everything the stream held, including what was read past the cap and dropped.</summary>
    public long TotalBytes { get; private set; }

    public async Task DrainAsync(Stream source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            TotalBytes += read;

            var room = _maxBytes - _keptCount;
            if (room > 0)
            {
                var take = Math.Min(room, read);
                Array.Copy(buffer, 0, _kept, _keptCount, take);
                _keptCount += take;
            }

            if (!Truncated && TotalBytes > _maxBytes)
            {
                Truncated = true;
                _onTruncated?.Invoke();
            }
        }
    }
}
