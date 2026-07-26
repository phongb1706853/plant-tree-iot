namespace PlantTreeIoTServer.Services;

/// <summary>
/// Bộ phát hiện sự kiện thông báo — HÀM THUẦN, không I/O, dễ test (giống AutoWateringDecider).
/// So 2 ảnh chụp sức khỏe liên tiếp và sinh sự kiện theo EDGE (cờ CHUYỂN trạng thái), không lặp
/// mỗi telemetry. Lần đầu (previous null) chỉ lập mốc, không bắn — tránh spam khi server khởi động.
///
/// Pha 1: water.empty (hết nước), battery.cut (pin cạn ngắt tải), battery.low (pin yếu).
/// device.offline KHÔNG ở đây (dựa trên khoảng lặng telemetry, xem OfflineMonitor).
/// </summary>
public static class NotifyEventDetector
{
    public static IReadOnlyList<NotifyEvent> Detect(
        DeviceHealthSnapshot? previous,
        DeviceHealthSnapshot current,
        string deviceId)
    {
        var events = new List<NotifyEvent>();

        // Lần đầu thấy thiết bị: chỉ lập mốc, chưa có gì để so edge.
        if (previous is null) return events;
        var prev = previous.Value;

        // water_ok true -> false: bình chứa vừa cạn.
        if (prev.WaterOk == true && current.WaterOk == false)
            events.Add(new NotifyEvent(deviceId, "water.empty", NotifySeverity.Critical,
                new Dictionary<string, object?>
                {
                    ["soilPercent"] = current.SoilPercent,
                    ["waterOk"] = false,
                }));

        // batt_cut false -> true: điện áp chạm mức bảo vệ, firmware ngắt tải.
        if (prev.BattCut == false && current.BattCut == true)
            events.Add(new NotifyEvent(deviceId, "battery.cut", NotifySeverity.Critical,
                new Dictionary<string, object?>
                {
                    ["batteryPercent"] = current.BatteryPercent,
                    ["batteryVoltageV"] = current.BatteryVoltageV,
                }));

        // low_batt false -> true: pin xuống mức cảnh báo theo %.
        if (prev.LowBatt == false && current.LowBatt == true)
            events.Add(new NotifyEvent(deviceId, "battery.low", NotifySeverity.Warning,
                new Dictionary<string, object?>
                {
                    ["batteryPercent"] = current.BatteryPercent,
                }));

        return events;
    }
}
