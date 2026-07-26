namespace PlantTreeIoTServer.Services;

/// <summary>Mức độ nghiêm trọng của thông báo (khớp hợp đồng Notify: info | warning | critical).</summary>
public enum NotifySeverity
{
    Info,
    Warning,
    Critical,
}

/// <summary>
/// Ảnh chụp các cờ + số liệu sức khỏe thiết bị lấy từ telemetry, dùng để phát hiện edge.
/// Cờ null = cảm biến/tính năng đang tắt -> không đủ căn cứ để báo.
/// </summary>
public readonly record struct DeviceHealthSnapshot(
    bool? WaterOk,
    bool? BattCut,
    bool? LowBatt,
    int? SoilPercent,
    int? BatteryPercent,
    double? BatteryVoltageV);

/// <summary>
/// Một sự kiện thông báo đã sẵn sàng gửi sang Notify: mã sự kiện cố định + severity + data thô.
/// .NET quyết định KHI NÀO/GÌ; Notify lo câu chữ/UI. Xem NOTIFY-INTEGRATION-GUIDE.md.
/// </summary>
public sealed record NotifyEvent(
    string DeviceId,
    string EventCode,
    NotifySeverity Severity,
    IReadOnlyDictionary<string, object?> Data);
