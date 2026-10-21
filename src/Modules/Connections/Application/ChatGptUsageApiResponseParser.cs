using System.Globalization;
using System.Text.Json;
using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.Modules.Connections.Application;

public static class ChatGptUsageApiResponseParser
{
    public static ChatGptUsageApiReadResult Parse(JsonElement result, DateTimeOffset observedAtUtc)
    {
        if (TryGetString(result, "code", out var resultCode))
        {
            return resultCode switch
            {
                "USAGE_API_ORIGIN_MISMATCH" => Failure(resultCode, "Chỉ được gọi endpoint usage khi session đang ở https://chatgpt.com."),
                "USAGE_API_RESPONSE_TOO_LARGE" => Failure(resultCode, "ChatGPT trả về phản hồi usage vượt giới hạn an toàn."),
                "USAGE_API_RESPONSE_NOT_JSON" => Failure(resultCode, "ChatGPT không trả về JSON cho endpoint usage."),
                _ => Failure("USAGE_API_REQUEST_FAILED", "Không thể đọc usage từ session ChatGPT hiện tại; không lưu phản hồi thô.")
            };
        }

        if (!TryGetInt32(result, "status", out var statusCode))
        {
            return Failure("USAGE_API_RESPONSE_INVALID", "ChatGPT trả về phản hồi usage không hợp lệ.");
        }

        if (statusCode == 401)
        {
            return Failure("USAGE_API_UNAUTHORIZED", "Session ChatGPT chưa đăng nhập hoặc đã hết hạn; chưa lấy được usage thật.");
        }

        if (statusCode == 403)
        {
            return Failure("USAGE_API_FORBIDDEN", "ChatGPT từ chối quyền đọc usage cho session hiện tại.");
        }

        if (statusCode == 429)
        {
            return Failure("USAGE_API_RATE_LIMITED", "ChatGPT giới hạn yêu cầu usage; hãy thử lại sau.");
        }

        if (statusCode != 200)
        {
            return Failure("USAGE_API_HTTP_ERROR", "Không thể đọc usage từ ChatGPT; endpoint không trả về HTTP 200.");
        }

        if (!TryGetProperty(result, "payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
        {
            return Failure("USAGE_API_RESPONSE_INVALID", "ChatGPT trả về dữ liệu usage không đúng định dạng JSON.");
        }

        return ParsePayload(payload, observedAtUtc);
    }

    public static ChatGptUsageApiReadResult ParsePayload(JsonElement payload, DateTimeOffset observedAtUtc)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return Failure("USAGE_API_RESPONSE_INVALID", "ChatGPT trả về dữ liệu usage không đúng định dạng JSON.");
        }

        var planType = ReadPlanType(payload);
        var rateLimit = TryGetProperty(payload, "rate_limit", out var rateLimitElement)
            ? rateLimitElement
            : TryGetProperty(payload, "rate_limits", out rateLimitElement)
                ? rateLimitElement
                : default;
        var primary = ReadWindow(rateLimit, "primary_window");
        var secondary = ReadWindow(rateLimit, "secondary_window");
        if (planType is null && primary is null && secondary is null)
        {
            return Failure("USAGE_API_SCHEMA_UNSUPPORTED", "Phản hồi usage không chứa plan hoặc hạn mức được hỗ trợ; không tạo số liệu suy đoán.");
        }

        var snapshot = new ChatGptWebUsageSnapshot(planType, primary, secondary, observedAtUtc);
        return new ChatGptUsageApiReadResult(
            true,
            primary is null && secondary is null ? "PlanObserved" : "UsageObserved",
            "Đã đọc dữ liệu plan/usage trực tiếp từ session ChatGPT hiện tại.",
            snapshot,
            ReadVerifiedEmail(payload));
    }

    private static string? ReadVerifiedEmail(JsonElement payload)
    {
        if (!TryGetProperty(payload, "email", out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var email = value.GetString()?.Trim();
        return !string.IsNullOrWhiteSpace(email)
            && email.Length <= 254
            && email.Contains('@')
            && !email.Any(char.IsControl)
                ? email
                : null;
    }

    private static ChatGptUsageWindowSnapshot? ReadWindow(JsonElement rateLimit, string propertyName)
    {
        if (rateLimit.ValueKind != JsonValueKind.Object
            || !TryGetProperty(rateLimit, propertyName, out var window)
            || window.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var usedPercent = ReadDecimal(window, "used_percent");
        if (usedPercent is < 0 or > 100)
        {
            usedPercent = null;
        }

        var limitWindowSeconds = ReadPositiveInt64(window, "limit_window_seconds");
        var resetAfterSeconds = ReadPositiveInt64(window, "reset_after_seconds");
        var resetAt = ReadTimestamp(window, "reset_at");
        return usedPercent is null && limitWindowSeconds is null && resetAfterSeconds is null && resetAt is null
            ? null
            : new ChatGptUsageWindowSnapshot(usedPercent, limitWindowSeconds, resetAt, resetAfterSeconds);
    }

    private static string? ReadPlanType(JsonElement payload)
    {
        if (!TryGetProperty(payload, "plan_type", out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var plan = value.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(plan) || plan.Length > 64
            || plan.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
        {
            return null;
        }

        return plan.ToLowerInvariant();
    }

    private static decimal? ReadDecimal(JsonElement parent, string propertyName)
    {
        if (!TryGetProperty(parent, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
    }

    private static long? ReadPositiveInt64(JsonElement parent, string propertyName)
    {
        if (!TryGetProperty(parent, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number is > 0 and <= 31_536_000)
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed is > 0 and <= 31_536_000
                ? parsed
                : null;
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement parent, string propertyName)
    {
        if (!TryGetProperty(parent, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var epoch))
        {
            try
            {
                return (epoch > 10_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                    : DateTimeOffset.FromUnixTimeSeconds(epoch)).ToUniversalTime();
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        if (value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
        {
            return timestamp.ToUniversalTime();
        }

        return null;
    }

    private static bool TryGetInt32(JsonElement parent, string propertyName, out int value)
    {
        value = 0;
        return TryGetProperty(parent, propertyName, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }

    private static bool TryGetString(JsonElement parent, string propertyName, out string value)
    {
        value = string.Empty;
        if (!TryGetProperty(parent, propertyName, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetProperty(JsonElement parent, string propertyName, out JsonElement value)
    {
        value = default;
        if (parent.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in parent.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static ChatGptUsageApiReadResult Failure(string status, string message)
        => new(false, status, message);
}
