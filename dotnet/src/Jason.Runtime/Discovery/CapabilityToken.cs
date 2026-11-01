using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Jason.Runtime.Discovery;

/// <summary>
/// The per-run bearer token clients present as <c>Authorization: Bearer …</c>. 256 random bits,
/// base64url, compared in constant time, never logged, never passed on a command line.
/// </summary>
public static class CapabilityToken
{
    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static bool Matches(string presented, string expected)
    {
        ArgumentNullException.ThrowIfNull(presented);
        ArgumentNullException.ThrowIfNull(expected);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(expected));
    }
}
