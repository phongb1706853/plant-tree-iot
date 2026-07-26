using PlantTreeIoTServer.Services;
using Xunit;

namespace PlantTreeIoTServer.Tests;

// Phát hiện sự kiện thông báo (hàm thuần) từ 2 ảnh chụp sức khỏe liên tiếp.
// Edge-triggered: chỉ bắn khi cờ CHUYỂN trạng thái, không lặp mỗi telemetry (~10s).
public class NotifyEventDetectorTests
{
    private static DeviceHealthSnapshot Snap(
        bool? waterOk = null, bool? battCut = null, bool? lowBatt = null,
        int? soil = null, int? batt = null, double? volt = null)
        => new(waterOk, battCut, lowBatt, soil, batt, volt);

    [Fact]
    public void Water_ok_true_to_false_fires_water_empty_critical()
    {
        var events = NotifyEventDetector.Detect(
            previous: Snap(waterOk: true),
            current: Snap(waterOk: false, soil: 18),
            deviceId: "dev1");

        var e = Assert.Single(events);
        Assert.Equal("water.empty", e.EventCode);
        Assert.Equal(NotifySeverity.Critical, e.Severity);
    }

    [Fact]
    public void Batt_cut_false_to_true_fires_battery_cut_critical()
    {
        var events = NotifyEventDetector.Detect(
            Snap(battCut: false), Snap(battCut: true, batt: 5, volt: 6.1), "dev1");

        var e = Assert.Single(events);
        Assert.Equal("battery.cut", e.EventCode);
        Assert.Equal(NotifySeverity.Critical, e.Severity);
    }

    [Fact]
    public void Low_batt_false_to_true_fires_battery_low_warning()
    {
        var events = NotifyEventDetector.Detect(
            Snap(lowBatt: false), Snap(lowBatt: true, batt: 18), "dev1");

        var e = Assert.Single(events);
        Assert.Equal("battery.low", e.EventCode);
        Assert.Equal(NotifySeverity.Warning, e.Severity);
    }

    [Fact]
    public void No_previous_snapshot_establishes_baseline_and_fires_nothing()
    {
        // Lần telemetry đầu (prev null): chỉ lập mốc, không bắn (tránh spam khi server khởi động).
        var events = NotifyEventDetector.Detect(
            previous: null,
            current: Snap(waterOk: false, battCut: true, lowBatt: true),
            deviceId: "dev1");

        Assert.Empty(events);
    }

    [Fact]
    public void Flag_unchanged_fires_nothing()
    {
        var events = NotifyEventDetector.Detect(
            Snap(waterOk: false), Snap(waterOk: false), "dev1");

        Assert.Empty(events);
    }

    [Fact]
    public void Water_recovered_false_to_true_fires_nothing()
    {
        // Chỉ báo khi HẾT nước; có nước lại thì thôi (Pha 1 không có event phục hồi).
        var events = NotifyEventDetector.Detect(
            Snap(waterOk: false), Snap(waterOk: true), "dev1");

        Assert.Empty(events);
    }

    [Fact]
    public void Multiple_edges_in_one_telemetry_fire_all()
    {
        var events = NotifyEventDetector.Detect(
            Snap(battCut: false, lowBatt: false),
            Snap(battCut: true, lowBatt: true, batt: 3),
            "dev1");

        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.EventCode == "battery.cut");
        Assert.Contains(events, e => e.EventCode == "battery.low");
    }

    [Fact]
    public void Null_flag_on_either_side_fires_nothing()
    {
        // water_ok null = tính năng cảm biến tắt -> không đủ căn cứ để báo.
        var events = NotifyEventDetector.Detect(
            Snap(waterOk: null), Snap(waterOk: false), "dev1");

        Assert.Empty(events);
    }
}
