using PlantTreeIoTServer.Services;
using Xunit;

namespace PlantTreeIoTServer.Tests;

// Kiểm thử bộ quyết định auto tưới (hàm thuần). Quyết định dựa trên telemetry + ngưỡng + cooldown.
// Hysteresis: bật khi 0 < soil < onPct (và hết cooldown), tắt khi soil > offPct. Chỉ khi mode="auto".
public class AutoWateringDeciderTests
{
    private const int OnPct = 30;
    private const int OffPct = 60;

    [Fact]
    public void Auto_pump_off_soil_dry_no_cooldown_turns_on()
    {
        var action = AutoWateringDecider.Decide(
            mode: "auto", soilPercent: 10, pumpOn: false,
            soilOnPct: OnPct, soilOffPct: OffPct, cooldownActive: false);

        Assert.Equal(PumpAction.TurnOn, action);
    }

    [Fact]
    public void Auto_pump_off_soil_dry_but_cooldown_active_does_nothing()
    {
        var action = AutoWateringDecider.Decide(
            mode: "auto", soilPercent: 10, pumpOn: false,
            soilOnPct: OnPct, soilOffPct: OffPct, cooldownActive: true);

        Assert.Equal(PumpAction.None, action);
    }

    [Fact]
    public void Auto_pump_on_soil_wet_turns_off()
    {
        var action = AutoWateringDecider.Decide(
            mode: "auto", soilPercent: 70, pumpOn: true,
            soilOnPct: OnPct, soilOffPct: OffPct, cooldownActive: false);

        Assert.Equal(PumpAction.TurnOff, action);
    }

    [Fact]
    public void Auto_pump_on_soil_wet_turns_off_even_during_cooldown()
    {
        // Cooldown chỉ chặn việc BẬT lại, không được chặn việc TẮT khi đã đủ ẩm.
        var action = AutoWateringDecider.Decide(
            mode: "auto", soilPercent: 70, pumpOn: true,
            soilOnPct: OnPct, soilOffPct: OffPct, cooldownActive: true);

        Assert.Equal(PumpAction.TurnOff, action);
    }

    [Fact]
    public void Auto_pump_on_soil_in_deadband_keeps_running()
    {
        // Đang bơm, đất chưa vượt offPct -> tiếp tục bơm (không tắt sớm).
        var action = AutoWateringDecider.Decide(
            mode: "auto", soilPercent: 45, pumpOn: true,
            soilOnPct: OnPct, soilOffPct: OffPct, cooldownActive: false);

        Assert.Equal(PumpAction.None, action);
    }

    [Fact]
    public void Auto_pump_off_soil_in_deadband_does_nothing()
    {
        // Bơm tắt, đất trong vùng đệm (giữa on/off) -> chưa bật (hysteresis).
        var action = AutoWateringDecider.Decide(
            mode: "auto", soilPercent: 45, pumpOn: false,
            soilOnPct: OnPct, soilOffPct: OffPct, cooldownActive: false);

        Assert.Equal(PumpAction.None, action);
    }

    [Fact]
    public void Manual_mode_never_acts()
    {
        var action = AutoWateringDecider.Decide(
            mode: "manual", soilPercent: 5, pumpOn: false,
            soilOnPct: OnPct, soilOffPct: OffPct, cooldownActive: false);

        Assert.Equal(PumpAction.None, action);
    }

    [Fact]
    public void Soil_zero_is_very_dry_and_turns_on()
    {
        // soil=0 coi là RẤT KHÔ -> vẫn tưới (yêu cầu: 0 thì cũng bật bơm).
        var action = AutoWateringDecider.Decide(
            mode: "auto", soilPercent: 0, pumpOn: false,
            soilOnPct: OnPct, soilOffPct: OffPct, cooldownActive: false);

        Assert.Equal(PumpAction.TurnOn, action);
    }

    [Fact]
    public void Soil_null_does_nothing()
    {
        var action = AutoWateringDecider.Decide(
            mode: "auto", soilPercent: null, pumpOn: false,
            soilOnPct: OnPct, soilOffPct: OffPct, cooldownActive: false);

        Assert.Equal(PumpAction.None, action);
    }

    [Fact]
    public void Boundary_soil_equal_on_pct_does_not_turn_on()
    {
        // Ngưỡng bật là "< onPct", nên đúng bằng onPct thì chưa bật.
        var action = AutoWateringDecider.Decide(
            mode: "auto", soilPercent: OnPct, pumpOn: false,
            soilOnPct: OnPct, soilOffPct: OffPct, cooldownActive: false);

        Assert.Equal(PumpAction.None, action);
    }

    [Fact]
    public void Boundary_soil_equal_off_pct_does_not_turn_off()
    {
        // Ngưỡng tắt là "> offPct", nên đúng bằng offPct thì chưa tắt.
        var action = AutoWateringDecider.Decide(
            mode: "auto", soilPercent: OffPct, pumpOn: true,
            soilOnPct: OnPct, soilOffPct: OffPct, cooldownActive: false);

        Assert.Equal(PumpAction.None, action);
    }
}
