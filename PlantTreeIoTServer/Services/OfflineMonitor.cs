namespace PlantTreeIoTServer.Services;

/// <summary>
/// Phát hiện mất kết nối — HÀM THUẦN theo thời gian truyền vào (dễ test).
/// device.offline bắn khi khoảng lặng telemetry >= ngưỡng VÀ chưa báo offline trước đó
/// (alreadyOffline chặn báo lặp). Trạng thái alreadyOffline + lastSeen do caller giữ.
/// </summary>
public static class OfflineMonitor
{
    public static bool ShouldFireOffline(
        System.DateTime lastSeenUtc,
        System.DateTime nowUtc,
        bool alreadyOffline,
        int thresholdSeconds)
    {
        if (alreadyOffline) return false; // đã báo rồi, không lặp
        return (nowUtc - lastSeenUtc).TotalSeconds >= thresholdSeconds;
    }
}
