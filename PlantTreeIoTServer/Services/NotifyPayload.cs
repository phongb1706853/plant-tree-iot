namespace PlantTreeIoTServer.Services;

/// <summary>
/// Dựng payload JSON gửi Notify — HÀM THUẦN (dễ test). Khóa các key hợp đồng:
/// deviceId, event, severity, occurredAt, id, data. Xem NOTIFY-INTEGRATION-GUIDE.md mục 2.
/// id ổn định theo đợt: "{deviceId}:{event}:{unixSeconds}" -> Notify chống trùng qua eventId.
/// </summary>
public static class NotifyPayload
{
    public static IReadOnlyDictionary<string, object?> Build(NotifyEvent evt, System.DateTime occurredAtUtc)
    {
        var utc = System.DateTime.SpecifyKind(occurredAtUtc, System.DateTimeKind.Utc);
        var unix = new System.DateTimeOffset(utc).ToUnixTimeSeconds();

        return new Dictionary<string, object?>
        {
            ["deviceId"] = evt.DeviceId,
            ["event"] = evt.EventCode,
            ["severity"] = evt.Severity.ToString().ToLowerInvariant(),
            ["occurredAt"] = utc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["id"] = $"{evt.DeviceId}:{evt.EventCode}:{unix}",
            ["data"] = evt.Data,
        };
    }
}
