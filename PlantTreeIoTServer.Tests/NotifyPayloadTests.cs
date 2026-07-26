using PlantTreeIoTServer.Services;
using Xunit;

namespace PlantTreeIoTServer.Tests;

// Dựng payload gửi Notify (hàm thuần). Khóa các key hợp đồng: deviceId, event, severity, occurredAt, id, data.
public class NotifyPayloadTests
{
    [Fact]
    public void Build_maps_contract_fields()
    {
        var data = new Dictionary<string, object?> { ["soilPercent"] = 18, ["waterOk"] = false };
        var evt = new NotifyEvent("ESP32S3_Zone1", "water.empty", NotifySeverity.Critical, data);
        var at = new System.DateTime(2026, 7, 26, 9, 15, 0, System.DateTimeKind.Utc);

        var p = NotifyPayload.Build(evt, at);

        Assert.Equal("ESP32S3_Zone1", p["deviceId"]);
        Assert.Equal("water.empty", p["event"]);
        Assert.Equal("critical", p["severity"]);
        Assert.Equal("2026-07-26T09:15:00Z", p["occurredAt"]);
        Assert.Same(data, p["data"]);
    }

    [Fact]
    public void Build_id_is_stable_deviceId_event_unixSeconds()
    {
        var evt = new NotifyEvent("dev1", "battery.low", NotifySeverity.Warning,
            new Dictionary<string, object?>());
        var at = new System.DateTime(2026, 7, 26, 9, 15, 0, System.DateTimeKind.Utc);
        var unix = ((System.DateTimeOffset)at).ToUnixTimeSeconds();

        var p = NotifyPayload.Build(evt, at);

        Assert.Equal($"dev1:battery.low:{unix}", p["id"]);
    }

    [Fact]
    public void Build_severity_serialized_lowercase()
    {
        var evt = new NotifyEvent("dev1", "battery.low", NotifySeverity.Info,
            new Dictionary<string, object?>());

        var p = NotifyPayload.Build(evt, new System.DateTime(2026, 7, 26, 0, 0, 0, System.DateTimeKind.Utc));

        Assert.Equal("info", p["severity"]);
    }
}
