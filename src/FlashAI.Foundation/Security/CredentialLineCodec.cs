using System.Text;

namespace FlashAI.Foundation.Security;

public sealed record CredentialLine(string Account, string Password, string? TotpSeed);

public static class CredentialLineCodec
{
    public static bool TryParse(string? input, out CredentialLine? credential, out string error)
    {
        credential = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input) || input.Length > 2048 || input.Contains('\r') || input.Contains('\n'))
        {
            error = "Hãy nhập một dòng credential hợp lệ.";
            return false;
        }

        var fields = new List<string>(3);
        var current = new StringBuilder();
        var inQuotes = false;
        var closedQuote = false;

        for (var index = 0; index < input.Length; index++)
        {
            var character = input[index];
            if (inQuotes)
            {
                if (character == '\\')
                {
                    if (index + 1 >= input.Length)
                    {
                        error = "Định dạng credential có escape chưa hoàn tất.";
                        return false;
                    }

                    current.Append(input[++index]);
                    continue;
                }

                if (character == '"')
                {
                    if (index + 1 < input.Length && input[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                        continue;
                    }

                    inQuotes = false;
                    closedQuote = true;
                    continue;
                }

                current.Append(character);
                continue;
            }

            if (character == '"')
            {
                if (current.Length != 0 || closedQuote)
                {
                    error = "Định dạng credential có dấu ngoặc kép không hợp lệ.";
                    return false;
                }

                inQuotes = true;
                continue;
            }

            if (character == '\\')
            {
                if (index + 1 >= input.Length || input[index + 1] is not ('|' or '\\'))
                {
                    error = "Credential chỉ hỗ trợ escape \\| và \\\\.";
                    return false;
                }

                current.Append(input[++index]);
                continue;
            }

            if (character == '|')
            {
                fields.Add(current.ToString());
                current.Clear();
                closedQuote = false;
                if (fields.Count > 2)
                {
                    error = "Credential phải có dạng account|password|totp tùy chọn.";
                    return false;
                }

                continue;
            }

            if (closedQuote)
            {
                error = "Không được có ký tự sau dấu ngoặc kép trước dấu phân cách.";
                return false;
            }

            current.Append(character);
        }

        if (inQuotes)
        {
            error = "Định dạng credential chưa đóng dấu ngoặc kép.";
            return false;
        }

        fields.Add(current.ToString());
        if (fields.Count != 3)
        {
            error = "Credential phải có dạng account|password|totp tùy chọn.";
            return false;
        }

        var account = fields[0].Trim();
        var password = fields[1];
        var totp = fields[2].Trim();
        if (account.Length is 0 or > 254 || password.Length is 0 or > 512 || totp.Length > 256)
        {
            error = "Độ dài account, password hoặc TOTP không hợp lệ.";
            return false;
        }

        if (account.Any(char.IsWhiteSpace) || account.Count(character => character == '@') != 1)
        {
            error = "Account phải là email hoặc tên đăng nhập hợp lệ.";
            return false;
        }

        if (totp.Length > 0 && totp.All(char.IsDigit) && totp.Length == 6)
        {
            error = "Không nhập mã OTP sáu chữ số; hãy nhập TOTP secret seed.";
            return false;
        }

        credential = new CredentialLine(account, password, string.IsNullOrWhiteSpace(totp) ? null : totp);
        return true;
    }

    public static string Serialize(CredentialLine credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        return string.Join('|', Escape(credential.Account), Escape(credential.Password), Escape(credential.TotpSeed ?? string.Empty));
    }

    private static string Escape(string value)
    {
        if (!value.Any(character => character is '|' or '"' or '\\'))
        {
            return value;
        }

        return '"' + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
    }
}
