using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Authenticator.Integration;

namespace FlashAI.Modules.Authenticator.Application;

public sealed class Rfc6238TotpGenerator : ITotpGenerator, ILocalTotpCodeGenerator
{
    public string Generate(ReadOnlySpan<byte> secret, DateTimeOffset utcNow, int digits, int period)
        => Generate(secret, utcNow, digits, period, TotpAlgorithm.Sha1);

    public string Generate(
        ReadOnlySpan<byte> secret,
        DateTimeOffset utcNow,
        int digits,
        int period,
        TotpAlgorithm algorithm)
    {
        if (secret.IsEmpty)
        {
            throw new ArgumentException("The TOTP secret cannot be empty.", nameof(secret));
        }

        if (digits is < 6 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(digits), "TOTP digits must be between 6 and 8.");
        }

        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "TOTP period must be positive.");
        }

        var unixSeconds = utcNow.ToUnixTimeSeconds();
        if (unixSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(utcNow), "TOTP time cannot be before the Unix epoch.");
        }

        var counter = checked(unixSeconds / period);
        Span<byte> counterBytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        var secretBytes = secret.ToArray();
        byte[]? hash = null;
        try
        {
            using HMAC hmac = algorithm switch
            {
#pragma warning disable CA5350 // RFC 6238 explicitly supports HMAC-SHA1.
                TotpAlgorithm.Sha1 => new HMACSHA1(secretBytes),
#pragma warning restore CA5350
                TotpAlgorithm.Sha256 => new HMACSHA256(secretBytes),
                TotpAlgorithm.Sha512 => new HMACSHA512(secretBytes),
                _ => throw new ArgumentOutOfRangeException(nameof(algorithm), "The TOTP algorithm is not supported.")
            };
            hash = hmac.ComputeHash(counterBytes.ToArray());
            var offset = hash[^1] & 0x0f;
            var binary = ((hash[offset] & 0x7f) << 24)
                | (hash[offset + 1] << 16)
                | (hash[offset + 2] << 8)
                | hash[offset + 3];
            var divisor = digits switch
            {
                6 => 1_000_000,
                7 => 10_000_000,
                _ => 100_000_000
            };

            return (binary % divisor).ToString($"D{digits}", CultureInfo.InvariantCulture);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
            if (hash is not null)
            {
                CryptographicOperations.ZeroMemory(hash);
            }
        }
    }
}
