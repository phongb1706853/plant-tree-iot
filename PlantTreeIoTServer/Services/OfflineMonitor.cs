namespace PlantTreeIoTServer.Services;

/// <summary>
/// Phát hiện mất kết nối — HÀM THUẦN theo thời gian truyền vào (dễ test).
/// device.offline bắn khi khoảng lặng >= ngưỡng VÀ chưa báo offline trước đó (alreadyOffline chặn báo lặp)
/// VÀ đã hết "cửa sổ im lặng có kế hoạch" (quietUntilUtc — thiết bị bản pin báo "sleeping" kèm next_wake_s).
/// Trạng thái alreadyOffline + lastSeen + quietUntil do caller giữ.
/// </summary>
public static class OfflineMonitor
{
    public const int DefaultThresholdSeconds = 240;

    public static bool ShouldFireOffline(
        System.DateTime lastSeenUtc,
        System.DateTime nowUtc,
        bool alreadyOffline,
        int thresholdSeconds,
        System.DateTime? quietUntilUtc = null)
    {
        if (alreadyOffline) return false; // đã báo rồi, không lặp
        if (quietUntilUtc.HasValue && nowUtc <= quietUntilUtc.Value) return false; // đang ngủ đúng lịch
        return (nowUtc - lastSeenUtc).TotalSeconds >= thresholdSeconds;
    }

    /// <summary>
    /// Ngưỡng im lặng (giây) để coi là mất kết nối: env NOTIFY_OFFLINE_SECONDS / config Notify:OfflineSeconds,
    /// mặc định 240 s (telemetry ~10 s, LWT tới sau ~45 s).
    /// </summary>
    public static int ResolveThresholdSeconds(IConfiguration configuration)
        => int.TryParse(
            System.Environment.GetEnvironmentVariable("NOTIFY_OFFLINE_SECONDS") ?? configuration["Notify:OfflineSeconds"],
            out var s) && s > 0 ? s : DefaultThresholdSeconds;
}
