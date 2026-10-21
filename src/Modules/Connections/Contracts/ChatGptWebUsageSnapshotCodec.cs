using System.Text.Json;

namespace FlashAI.Modules.Connections.Contracts;

public static class ChatGptWebUsageSnapshotCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize(ChatGptWebUsageSnapshot snapshot)
        => JsonSerializer.Serialize(snapshot, JsonOptions);

    public static bool TryDeserialize(string? value, out ChatGptWebUsageSnapshot? snapshot)
    {
        snapshot = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4_096)
        {
            return false;
        }

        try
        {
            snapshot = JsonSerializer.Deserialize<ChatGptWebUsageSnapshot>(value, JsonOptions);
            return snapshot is not null
                && snapshot.ObservedAtUtc != default
                && (snapshot.PlanType is not null
                    || snapshot.PrimaryWindow is not null
                    || snapshot.SecondaryWindow is not null);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
