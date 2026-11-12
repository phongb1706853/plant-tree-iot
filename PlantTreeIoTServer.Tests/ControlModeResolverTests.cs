using PlantTreeIoTServer.Services;
using Xunit;

namespace PlantTreeIoTServer.Tests;

// Kiểm thử bộ xử lý mode cho payload xmini/control (hàm thuần).
//
// Bối cảnh bug: firmware ép mode="manual" khi nhận BẤT KỲ lệnh pump/light. Trước đây server auto-tưới
// gửi {"pump":true} trần -> thiết bị tự rớt sang manual -> auto chết sau 1 nhịp. Yêu cầu mới: SERVER
// làm chủ mode; lệnh actuator KHÔNG được đổi mode, chỉ lệnh mode tường minh (mode/auto) mới đổi.
// ControlModeResolver.Apply gộp mode hiện tại vào lệnh actuator để giữ nguyên chế độ, và trả về
// mode mới server nên lưu.
public class ControlModeResolverTests
{
    // ---- Lệnh mode tường minh -> cập nhật mode lưu, không gộp gì thêm ----

    [Fact]
    public void Explicit_mode_manual_updates_stored_mode()
    {
        var flat = new Dictionary<string, object?> { ["mode"] = "manual" };
        var newMode = ControlModeResolver.Apply(flat, currentMode: "auto");

        Assert.Equal("manual", newMode);
        Assert.Equal("manual", flat["mode"]); // giữ nguyên, không bị ghi đè
    }

    [Fact]
    public void Explicit_mode_auto_updates_stored_mode()
    {
        var flat = new Dictionary<string, object?> { ["mode"] = "auto" };
        var newMode = ControlModeResolver.Apply(flat, currentMode: "manual");

        Assert.Equal("auto", newMode);
    }

    [Fact]
    public void Explicit_auto_false_maps_to_manual()
    {
        var flat = new Dictionary<string, object?> { ["auto"] = false };
        var newMode = ControlModeResolver.Apply(flat, currentMode: "auto");

        Assert.Equal("manual", newMode);
    }

    [Fact]
    public void Explicit_auto_true_maps_to_auto()
    {
        var flat = new Dictionary<string, object?> { ["auto"] = true };
        var newMode = ControlModeResolver.Apply(flat, currentMode: "manual");

        Assert.Equal("auto", newMode);
    }

    // ---- Lệnh actuator (pump/light) -> GỘP mode hiện tại, KHÔNG đổi mode lưu ----

    [Fact]
    public void Pump_command_in_auto_keeps_auto_and_injects_mode()
    {
        // Đây chính là ca gây bug: bật bơm nhưng phải giữ auto.
        var flat = new Dictionary<string, object?> { ["pump"] = true };
        var newMode = ControlModeResolver.Apply(flat, currentMode: "auto");

        Assert.Equal("auto", newMode);
        Assert.Equal("auto", flat["mode"]); // đã gộp mode để firmware không rớt manual
    }

    [Fact]
    public void Pump_off_command_in_auto_keeps_auto_and_injects_mode()
    {
        var flat = new Dictionary<string, object?> { ["pump"] = false };
        var newMode = ControlModeResolver.Apply(flat, currentMode: "auto");

        Assert.Equal("auto", newMode);
        Assert.Equal("auto", flat["mode"]);
    }

    [Fact]
    public void Pump_command_in_manual_stays_manual_and_injects_manual()
    {
        var flat = new Dictionary<string, object?> { ["pump"] = true };
        var newMode = ControlModeResolver.Apply(flat, currentMode: "manual");

        Assert.Equal("manual", newMode);
        Assert.Equal("manual", flat["mode"]);
    }

    [Fact]
    public void Light_on_command_in_auto_keeps_auto_and_injects_mode()
    {
        var flat = new Dictionary<string, object?> { ["light"] = true };
        var newMode = ControlModeResolver.Apply(flat, currentMode: "auto");

        Assert.Equal("auto", newMode);
        Assert.Equal("auto", flat["mode"]);
    }

    // ---- Lệnh không đụng mode ở firmware -> KHÔNG gộp mode, KHÔNG đổi mode lưu ----

    [Fact]
    public void Light_pwm_only_does_not_inject_mode()
    {
        // light_pwm chỉ đổi độ sáng, firmware KHÔNG đổi mode -> không cần gộp.
        var flat = new Dictionary<string, object?> { ["light_pwm"] = 180 };
        var newMode = ControlModeResolver.Apply(flat, currentMode: "auto");

        Assert.Equal("auto", newMode);
        Assert.False(flat.ContainsKey("mode"));
    }

    [Fact]
    public void Config_only_does_not_inject_mode()
    {
        var flat = new Dictionary<string, object?> { ["config"] = new Dictionary<string, object?>() };
        var newMode = ControlModeResolver.Apply(flat, currentMode: "manual");

        Assert.Equal("manual", newMode);
        Assert.False(flat.ContainsKey("mode"));
    }

    [Fact]
    public void Message_only_does_not_inject_mode()
    {
        var flat = new Dictionary<string, object?> { ["message"] = "hi", ["message_secs"] = 5 };
        var newMode = ControlModeResolver.Apply(flat, currentMode: "auto");

        Assert.Equal("auto", newMode);
        Assert.False(flat.ContainsKey("mode"));
    }

    // ---- Kết hợp: mode tường minh THẮNG lệnh actuator trong cùng payload ----

    [Fact]
    public void Explicit_mode_wins_over_actuator_in_same_payload()
    {
        // {"pump":true,"mode":"manual"}: người dùng chủ động chuyển manual + bật bơm.
        var flat = new Dictionary<string, object?> { ["pump"] = true, ["mode"] = "manual" };
        var newMode = ControlModeResolver.Apply(flat, currentMode: "auto");

        Assert.Equal("manual", newMode);
        Assert.Equal("manual", flat["mode"]); // không bị gộp về currentMode="auto"
    }
}

// Thiết bị THẬT (firmware 2.1.0) làm chủ auto: light / light_pwm / pump -> MANUAL, chỉ mode:"auto" / auto:true trả
// lại AUTO; thứ tự xử lý actuator -> mode -> auto (backend-api.md mục 5.3). Server KHÔNG gộp khoá nào vào payload.
public class ControlModeResolverRealDeviceTests
{
    [Theory]
    [InlineData("pump")]
    [InlineData("light")]
    [InlineData("light_pwm")]
    public void Actuator_moves_real_device_to_manual_without_touching_payload(string key)
    {
        var flat = new Dictionary<string, object?> { [key] = key == "light_pwm" ? 180 : true };
        var mode = ControlModeResolver.Resolve("ESP32S3_Zone1", flat, currentMode: "auto");

        Assert.Equal("manual", mode);
        Assert.False(flat.ContainsKey("mode"));
    }

    [Fact]
    public void Pump_with_mode_auto_ends_in_auto()
    {
        // Ví dụ tài liệu: {"pump":true,"mode":"auto"} -> 1 liều, MANUAL ở bước 4, về AUTO ở bước 5.
        var flat = new Dictionary<string, object?> { ["pump"] = true, ["mode"] = "auto" };
        Assert.Equal("auto", ControlModeResolver.ExpectedMode(flat, "manual"));
    }

    [Fact]
    public void Auto_key_wins_over_mode_key()
    {
        var flat = new Dictionary<string, object?> { ["mode"] = "auto", ["auto"] = false };
        Assert.Equal("manual", ControlModeResolver.ExpectedMode(flat, "auto"));
    }

    [Fact]
    public void Config_message_qr_keep_the_current_mode()
    {
        var flat = new Dictionary<string, object?>
        {
            ["config"] = new Dictionary<string, object?>(), ["message"] = "hi", ["show_qr"] = 300, ["stay_awake_s"] = 600,
        };
        Assert.Equal("manual", ControlModeResolver.ExpectedMode(flat, "manual"));
        Assert.Equal("auto", ControlModeResolver.ExpectedMode(flat, "auto"));
    }

    [Fact]
    public void Sim_device_keeps_the_old_merge()
    {
        var flat = new Dictionary<string, object?> { ["pump"] = true };
        var mode = ControlModeResolver.Resolve("SIM_Zone1", flat, currentMode: "auto");

        Assert.Equal("auto", mode);
        Assert.Equal("auto", flat["mode"]);
    }
}
