using System.Security.Cryptography;
using System.Text;

namespace FlashAI.Modules.Connections.Features.ConnectProvider.Application;

public static class OAuthSecurityPrimitives
{
    public static string CreateRandomBase64Url(int byteCount = 32)
    {
        if (byteCount < 16)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        }

        return Base64UrlEncode(RandomNumberGenerator.GetBytes(byteCount));
    }

    public static string CreatePkceChallenge(string verifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifier);
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(digest);
    }

    public static string Base64UrlEncode(ReadOnlySpan<byte> value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Base64UrlDecode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    public static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        try
        {
            return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedBytes);
            CryptographicOperations.ZeroMemory(actualBytes);
        }
    }
}
