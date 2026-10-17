using System.Security.Cryptography;

namespace FlashAI.Foundation.Security;

public static class Base32SecretCodec
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

    public static bool IsValid(string value, int minimumLength = 8)
    {
        try
        {
            if (value.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).Length < minimumLength)
            {
                return false;
            }

            var decoded = Decode(value);
            CryptographicOperations.ZeroMemory(decoded);
            return true;
        }
        catch (FormatException)
        {
            return false;
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
