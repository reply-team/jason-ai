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

    /// <summary>Held over what was kept and how much of it, which are written and read on different threads.</summary>
    private readonly Lock _keptGate = new();

    private int _keptCount;
    private bool _frozen;

    public BoundedCapture(int maxBytes, Action? onTruncated = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        _maxBytes = maxBytes;
        _onTruncated = onTruncated;
        _kept = new byte[maxBytes];
    }

    /// <summary>What was captured, decoded as UTF-8; a sequence cut by the cap decodes to a replacement character.</summary>
    public string Text
    {
        get
        {
            lock (_keptGate)
            {
                return Encoding.UTF8.GetString(_kept, 0, _keptCount);
            }
        }
    }

    /// <summary>Whether the stream held more than the cap — the caller says so rather than pretending it did not.</summary>
    public bool Truncated { get; private set; }

    /// <summary>Everything the stream held, including what was read past the cap and dropped.</summary>
    public long TotalBytes { get; private set; }

    /// <summary>
    /// Says that the caller has let go: nothing more is kept, and every later read answers with what stood here
    /// at this moment. Both callers wait for a drain only as long as a kill grace and then abandon it, because a
    /// program can leave something behind holding the pipe open — so without this the answer a caller already
    /// acted on would go on changing under it.
    /// </summary>
    public void Freeze()
    {
        lock (_keptGate)
        {
            _frozen = true;
        }
    }

    public async Task DrainAsync(Stream source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            bool crossed;
            lock (_keptGate)
            {
                if (_frozen)
                {
                    // Still read to the end: a pipe nobody drains is how a child hangs forever. Nothing more is
                    // kept, because whoever asked for it has already been answered.
                    continue;
                }

                TotalBytes += read;

                var room = _maxBytes - _keptCount;
                if (room > 0)
                {
                    var take = Math.Min(room, read);
                    Array.Copy(buffer, 0, _kept, _keptCount, take);
                    _keptCount += take;
                }

                crossed = !Truncated && TotalBytes > _maxBytes;
                if (crossed)
                {
                    Truncated = true;
                }
            }

            // Outside the gate: the caller's reaction to a flood is to end the process, which is not work to do
            // while a reader of this capture is waiting on it.
            if (crossed)
            {
                _onTruncated?.Invoke();
            }
        }
    }
}
