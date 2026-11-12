using System.Text.Json;
using PlantTreeIoTServer.Models;
using Xunit;

namespace PlantTreeIoTServer.Tests;

// Parse đúng các ví dụ payload firmware 2.1.0 trong backend-api.md mục 4.
public class XminiPayloadTests
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Adapter_telemetry_maps_new_fields_and_empty_battery()
    {
        const string json = """
            {"device_id":"ESP32S3_Zone1","temperature_c":28.61523,"soil_percent":44,"power_source":"adapter",
             "supply_voltage_v":9.12,"supply_current_ma":168.4,"battery_voltage_v":null,"battery_percent":-1,
             "mode":"auto","water_ok":null,"soil_ok":true,"soil_fault":false,"safe_mode":false,"doses_today":2,
             "light_min_today":0,"power_state":"awake"}
            """;

        var data = JsonSerializer.Deserialize<XminiTelemetry>(json, Opts)!.ToSensorData();

        Assert.Equal("adapter", data.PowerSource);
        Assert.Equal(9.12, data.SupplyVoltageV);
        Assert.Equal(168.4, data.SupplyCurrentMa);
        Assert.Null(data.BatteryVoltageV);
        Assert.Null(data.BatteryPercent); // -1 = không có số
        Assert.True(data.SoilOk);
        Assert.False(data.SoilFault);
        Assert.Equal(2, data.DosesToday);
        Assert.Equal(0, data.LightMinToday);
        Assert.Equal("awake", data.PowerState);
        Assert.Null(data.WaterOk);
    }

    [Fact]
    public void Lwt_status_has_only_state_and_device_id()
    {
        var status = JsonSerializer.Deserialize<XminiStatus>("""{"state":"offline","device_id":"ESP32S3_Zone1"}""", Opts)!
            .ToDeviceStatus(retained: false, Now);

        Assert.Equal("offline", status.State);
        Assert.Equal("ESP32S3_Zone1", status.DeviceId);
        Assert.Null(status.Fw);
        Assert.Equal(Now, status.ReceivedAt);
    }

    [Fact]
    public void Sleeping_status_maps_next_wake()
    {
        const string json = """
            {"state":"sleeping","device_id":"ESP32S3_Zone1","fw":"2.1.0","power_source":"battery","power_mode":"eco",
             "power_state":"eco","next_wake_s":294,"uptime_s":86523,"boot_count":37,"reset_reason":"poweron",
             "free_heap":190112,"min_free_heap":148876,"max_alloc":110580,"rssi":-67,"wifi_fail_count":0,
             "wake_ms":5840,"safe_mode":false}
            """;

        var status = JsonSerializer.Deserialize<XminiStatus>(json, Opts)!.ToDeviceStatus(retained: true, Now);

        Assert.Equal(294, status.NextWakeS);
        Assert.Equal(86523, status.UptimeS);
        Assert.Equal("2.1.0", status.Fw);
        Assert.True(status.Retained);
    }

    private const string ConfigWithRejected = """
        {"config":{"soil_on_pct":30,"soil_off_pct":60,"pump_dose_ms":2000,"soak_s":600,"light_auto":false,
                   "lux_on":50,"power_mode":"auto","plugged_on_v":8.25,"screen_timeout_s":0},
         "rejected":["pump_dose_ms","soil_on_pct","soil_off_pct"]}
        """;

    [Fact]
    public void Live_config_keeps_rejected_keys()
    {
        var envelope = JsonSerializer.Deserialize<XminiConfigEnvelope>(ConfigWithRejected, Opts)!;
        var config = envelope.ToDeviceConfig("ESP32S3_Zone1", live: true, Now);

        Assert.Equal(2000, config.PumpDoseMs);
        Assert.Equal(false, config.LightAuto);
        Assert.Equal("auto", config.PowerMode);
        Assert.Equal(new[] { "pump_dose_ms", "soil_on_pct", "soil_off_pct" }, config.Rejected);
        Assert.Equal(3, config.RejectedCount);
        Assert.Equal(Now, config.RejectedAt);
    }

    [Fact]
    public void Retained_config_ignores_old_rejected_keys()
    {
        // Bản retained có thể mang 'rejected' của một lệnh CŨ (backend-api.md mục 4.3).
        var envelope = JsonSerializer.Deserialize<XminiConfigEnvelope>(ConfigWithRejected, Opts)!;
        var config = envelope.ToDeviceConfig("ESP32S3_Zone1", live: false, Now);

        Assert.Null(config.Rejected);
        Assert.Null(config.RejectedAt);
        Assert.Equal(30, config.SoilOnPct);
    }

    [Fact]
    public void Rejected_count_alone_is_kept()
    {
        var envelope = JsonSerializer.Deserialize<XminiConfigEnvelope>("""{"config":{"soak_s":600},"rejected_count":12}""", Opts)!;
        var config = envelope.ToDeviceConfig("ESP32S3_Zone1", live: true, Now);

        Assert.Null(config.Rejected);
        Assert.Equal(12, config.RejectedCount);
    }
}
