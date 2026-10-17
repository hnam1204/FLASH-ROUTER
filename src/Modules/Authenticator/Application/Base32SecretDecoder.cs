using System.Collections.Generic;
using System.Security.Cryptography;

namespace FlashAI.Modules.Authenticator.Application;

public static class Base32SecretDecoder
{
    public static byte[] Decode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = new string(value
            .Where(character => !char.IsWhiteSpace(character) && character != '-')
            .ToArray())
            .TrimEnd('=');

        if (normalized.Length == 0)
        {
            throw new FormatException("The TOTP secret is empty.");
        }

        var output = new List<byte>(normalized.Length * 5 / 8);
        var buffer = 0;
        var bits = 0;

        foreach (var character in normalized)
        {
            var digit = DecodeDigit(character);
            buffer = (buffer << 5) | digit;
            bits += 5;

            while (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)((buffer >> bits) & 0xff));
            }
        }

        if (bits >= 5 || (bits > 0 && (buffer & ((1 << bits) - 1)) != 0))
        {
            throw new FormatException("The TOTP secret has invalid Base32 padding.");
        }

        return output.ToArray();
    }

    public static string Normalize(string value)
    {
        var decoded = Decode(value);
        try
        {
            return Convert.ToBase64String(decoded);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
        }
    }

    private static int DecodeDigit(char character)
    {
        character = char.ToUpperInvariant(character);
        return character switch
        {
            >= 'A' and <= 'Z' => character - 'A',
            >= '2' and <= '7' => character - '2' + 26,
            _ => throw new FormatException("The TOTP secret contains an invalid Base32 character.")
        };
    }
}
