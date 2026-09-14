using System.Security.Cryptography;

namespace Jason.Contracts.Ids;

/// <summary>
/// Minimal ULID: 48-bit millisecond timestamp + 80 random bits, Crockford base32, 26 characters,
/// lexicographically time-ordered. Deliberately dependency-free.
/// </summary>
public static class Ulid
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private static readonly Lock Gate = new();
    private static readonly byte[] LastRandom = new byte[10];
    private static long _lastMilliseconds = long.MinValue;

    /// <summary>
    /// Strictly increasing within the process: rows created in the same millisecond still sort in creation order,
    /// which is what keyset pagination over public ids relies on.
    /// </summary>
    public static string NewUlid()
    {
        Span<byte> bytes = stackalloc byte[16];
        lock (Gate)
        {
            var milliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (milliseconds <= _lastMilliseconds)
            {
                // The clock stood still or went backwards: keep the last millisecond and walk the random tail up.
                milliseconds = _lastMilliseconds;
                Increment(LastRandom);
            }
            else
            {
                _lastMilliseconds = milliseconds;
                RandomNumberGenerator.Fill(LastRandom);
            }

            WriteTimestamp(bytes, milliseconds);
            LastRandom.CopyTo(bytes[6..]);
        }

        return Encode(bytes);
    }

    /// <summary>Encodes a given instant with a random tail. For tests and for ids whose time is not "now".</summary>
    public static string NewUlid(DateTimeOffset now)
    {
        Span<byte> bytes = stackalloc byte[16];
        WriteTimestamp(bytes, now.ToUnixTimeMilliseconds());
        RandomNumberGenerator.Fill(bytes[6..]);
        return Encode(bytes);
    }

    private static void WriteTimestamp(Span<byte> bytes, long milliseconds)
    {
        bytes[0] = (byte)(milliseconds >> 40);
        bytes[1] = (byte)(milliseconds >> 32);
        bytes[2] = (byte)(milliseconds >> 24);
        bytes[3] = (byte)(milliseconds >> 16);
        bytes[4] = (byte)(milliseconds >> 8);
        bytes[5] = (byte)milliseconds;
    }

    private static void Increment(byte[] tail)
    {
        for (var i = tail.Length - 1; i >= 0; i--)
        {
            if (++tail[i] != 0)
            {
                return;
            }
        }
    }

    private static string Encode(ReadOnlySpan<byte> b)
    {
        Span<char> c = stackalloc char[26];
        c[0] = Alphabet[(b[0] & 224) >> 5];
        c[1] = Alphabet[b[0] & 31];
        c[2] = Alphabet[(b[1] & 248) >> 3];
        c[3] = Alphabet[((b[1] & 7) << 2) | ((b[2] & 192) >> 6)];
        c[4] = Alphabet[(b[2] & 62) >> 1];
        c[5] = Alphabet[((b[2] & 1) << 4) | ((b[3] & 240) >> 4)];
        c[6] = Alphabet[((b[3] & 15) << 1) | ((b[4] & 128) >> 7)];
        c[7] = Alphabet[(b[4] & 124) >> 2];
        c[8] = Alphabet[((b[4] & 3) << 3) | ((b[5] & 224) >> 5)];
        c[9] = Alphabet[b[5] & 31];
        c[10] = Alphabet[(b[6] & 248) >> 3];
        c[11] = Alphabet[((b[6] & 7) << 2) | ((b[7] & 192) >> 6)];
        c[12] = Alphabet[(b[7] & 62) >> 1];
        c[13] = Alphabet[((b[7] & 1) << 4) | ((b[8] & 240) >> 4)];
        c[14] = Alphabet[((b[8] & 15) << 1) | ((b[9] & 128) >> 7)];
        c[15] = Alphabet[(b[9] & 124) >> 2];
        c[16] = Alphabet[((b[9] & 3) << 3) | ((b[10] & 224) >> 5)];
        c[17] = Alphabet[b[10] & 31];
        c[18] = Alphabet[(b[11] & 248) >> 3];
        c[19] = Alphabet[((b[11] & 7) << 2) | ((b[12] & 192) >> 6)];
        c[20] = Alphabet[(b[12] & 62) >> 1];
        c[21] = Alphabet[((b[12] & 1) << 4) | ((b[13] & 240) >> 4)];
        c[22] = Alphabet[((b[13] & 15) << 1) | ((b[14] & 128) >> 7)];
        c[23] = Alphabet[(b[14] & 124) >> 2];
        c[24] = Alphabet[((b[14] & 3) << 3) | ((b[15] & 224) >> 5)];
        c[25] = Alphabet[b[15] & 31];
        return new string(c);
    }
}
