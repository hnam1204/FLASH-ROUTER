using System.Text.Json;
using FlashAI.Modules.Connections.Application;

namespace FlashAI.IntegrationTests;

public sealed class ChatGptUsageApiResponseParserTests
{
    [Fact]
    public void ParsesFreePlanAndBothRateLimitWindows()
    {
        var resetAt = DateTimeOffset.UtcNow.AddHours(3).ToUnixTimeSeconds();
        using var response = JsonDocument.Parse($$"""
            {
              "status": 200,
              "payload": {
                "plan_type": "free",
                "rate_limit": {
                  "primary_window": { "used_percent": 27.5, "limit_window_seconds": 18000, "reset_at": {{resetAt}}, "reset_after_seconds": 10800 },
                  "secondary_window": { "used_percent": 42, "limit_window_seconds": 604800, "reset_after_seconds": 500000 }
                }
              }
            }
            """);
        var observedAt = DateTimeOffset.UtcNow;

        var result = ChatGptUsageApiResponseParser.Parse(response.RootElement, observedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal("UsageObserved", result.Status);
        Assert.Equal("free", result.Snapshot!.PlanType);
        Assert.Equal(27.5m, result.Snapshot.PrimaryWindow!.UsedPercent);
        Assert.Equal(18_000, result.Snapshot.PrimaryWindow.LimitWindowSeconds);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(resetAt), result.Snapshot.PrimaryWindow.ResetAtUtc);
        Assert.Equal(42m, result.Snapshot.SecondaryWindow!.UsedPercent);
        Assert.Equal(604_800, result.Snapshot.SecondaryWindow.LimitWindowSeconds);
        Assert.Equal(observedAt, result.Snapshot.ObservedAtUtc);
    }

    [Fact]
    public void MapsUnauthorizedResponseWithoutExposingProviderBody()
    {
        using var response = JsonDocument.Parse("""{"status":401}""");

        var result = ChatGptUsageApiResponseParser.Parse(response.RootElement, DateTimeOffset.UtcNow);

        Assert.False(result.IsSuccess);
        Assert.Equal("USAGE_API_UNAUTHORIZED", result.Status);
        Assert.DoesNotContain("cookie", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void DoesNotAcceptOutOfRangePercentagesAsQuotaData()
    {
        using var response = JsonDocument.Parse("""
            { "status": 200, "payload": { "plan_type": "free", "rate_limit": {
              "primary_window": { "used_percent": 120 }
            } } }
            """);

        var result = ChatGptUsageApiResponseParser.Parse(response.RootElement, DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal("free", result.Snapshot!.PlanType);
        Assert.Null(result.Snapshot.PrimaryWindow);
    }

    [Fact]
    public void RejectsUnknownSchemaInsteadOfInventingValues()
    {
        using var response = JsonDocument.Parse("""{"status":200,"payload":{"account_id":"opaque"}}""");

        var result = ChatGptUsageApiResponseParser.Parse(response.RootElement, DateTimeOffset.UtcNow);

        Assert.False(result.IsSuccess);
        Assert.Equal("USAGE_API_SCHEMA_UNSUPPORTED", result.Status);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void ParsesCodexPayloadAndReturnsEmailForAccountBinding()
    {
        using var response = JsonDocument.Parse("""
            {
              "email": "codex.owner@example.test",
              "plan_type": "free",
              "rate_limit": {
                "primary_window": { "used_percent": 25, "limit_window_seconds": 18000 },
                "secondary_window": { "used_percent": 40, "limit_window_seconds": 604800 }
              }
            }
            """);
        var observedAt = DateTimeOffset.UtcNow;

        var result = ChatGptUsageApiResponseParser.ParsePayload(response.RootElement, observedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal("codex.owner@example.test", result.VerifiedEmail);
        Assert.Equal("free", result.Snapshot!.PlanType);
        Assert.Equal(25m, result.Snapshot.PrimaryWindow!.UsedPercent);
        Assert.Equal(40m, result.Snapshot.SecondaryWindow!.UsedPercent);
        Assert.Equal(observedAt, result.Snapshot.ObservedAtUtc);
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("100", "100")]
    [InlineData("null", null)]
    [InlineData("\"not-a-number\"", null)]
    public void KeepsOnlyFinitePercentageValuesWithinRange(string usedPercentJson, string? expectedText)
    {
        using var payload = JsonDocument.Parse($$"""
            { "plan_type": "free", "rate_limit": { "primary_window": {
              "used_percent": {{usedPercentJson}}, "limit_window_seconds": 18000
            } } }
            """);

        var result = ChatGptUsageApiResponseParser.ParsePayload(payload.RootElement, DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        decimal? expected = expectedText is null ? null : decimal.Parse(expectedText, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, result.Snapshot!.PrimaryWindow!.UsedPercent);
        Assert.Equal(18_000, result.Snapshot.PrimaryWindow.LimitWindowSeconds);
    }
}
