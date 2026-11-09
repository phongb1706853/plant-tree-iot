using PlantTreeIoTServer.Services;
using Xunit;

namespace PlantTreeIoTServer.Tests;

// Kiểm thử bộ quyết định đèn auto (hàm thuần) — SERVER ra lệnh đèn như tự tưới, thiết bị chỉ làm theo.
//
// Quy tắc: chỉ ở mode auto (server giữ). Tick thường chỉ ra lệnh lúc lux VƯỢT ngưỡng so với telemetry trước, để
// lệnh tắt đèn tay ban đêm không bị bật lại ngay. Lúc "vào lại auto" (lần đầu thấy thiết bị, chuyển sang auto,
// hết batt_cut, đổi ngưỡng lux) thì xét theo MỨC lux hiện tại. Đang batt_cut: không ra lệnh (gửi cũng vô ích).
public class AutoLightDeciderTests
{
    private const double On = 120, Off = 300;

    private static LightAction Decide(string? mode, double? lux, double? prevLux, bool? lightOn,
        bool? battCut = false, bool recheck = false)
        => AutoLightDecider.Decide(mode, lux, prevLux, lightOn, battCut, On, Off, recheck);

    [Fact]
    public void Lux_falling_below_lux_on_turns_light_on_in_auto()
        => Assert.Equal(LightAction.TurnOn, Decide("auto", lux: 5, prevLux: 600, lightOn: false));

    [Fact]
    public void Lux_rising_above_lux_off_turns_light_off_in_auto()
        => Assert.Equal(LightAction.TurnOff, Decide("auto", lux: 600, prevLux: 5, lightOn: true));

    [Fact]
    public void Manual_mode_never_touches_the_light()
    {
        Assert.Equal(LightAction.None, Decide("manual", lux: 5, prevLux: 600, lightOn: false));
        Assert.Equal(LightAction.None, Decide("manual", lux: 5, prevLux: null, lightOn: false, recheck: true));
    }

    [Fact]
    public void Staying_dark_keeps_a_manual_light_off()
        => Assert.Equal(LightAction.None, Decide("auto", lux: 5, prevLux: 5, lightOn: false));

    [Fact]
    public void No_command_when_light_already_in_wanted_state()
    {
        Assert.Equal(LightAction.None, Decide("auto", lux: 5, prevLux: 600, lightOn: true));
        Assert.Equal(LightAction.None, Decide("auto", lux: 600, prevLux: 5, lightOn: false));
    }

    [Fact]
    public void Between_thresholds_does_nothing()
        => Assert.Equal(LightAction.None, Decide("auto", lux: 200, prevLux: 600, lightOn: false));

    [Fact]
    public void Recheck_acts_on_the_current_level()
    {
        Assert.Equal(LightAction.TurnOn, Decide("auto", lux: 5, prevLux: 5, lightOn: false, recheck: true));
        Assert.Equal(LightAction.TurnOff, Decide("auto", lux: 600, prevLux: 600, lightOn: true, recheck: true));
        Assert.Equal(LightAction.None, Decide("auto", lux: 200, prevLux: 200, lightOn: false, recheck: true));
    }

    [Fact]
    public void Batt_cut_or_missing_lux_gives_no_command()
    {
        Assert.Equal(LightAction.None, Decide("auto", lux: 5, prevLux: 600, lightOn: false, battCut: true));
        Assert.Equal(LightAction.None, Decide("auto", lux: null, prevLux: 600, lightOn: false, recheck: true));
        Assert.Equal(LightAction.None, Decide("auto", lux: 5, prevLux: null, lightOn: false)); // không có mốc trước, không recheck
    }

    // ---- Khi nào xét lại theo MỨC lux ----

    private static LightContext Ctx(string mode = "auto", bool? cut = false, double on = On, double off = Off)
        => new(mode, cut, on, off);

    [Fact]
    public void First_telemetry_of_a_device_rechecks()
        => Assert.True(AutoLightDecider.ShouldRecheckLevel(null, Ctx()));

    [Fact]
    public void Entering_auto_rechecks_but_staying_in_auto_does_not()
    {
        Assert.True(AutoLightDecider.ShouldRecheckLevel(Ctx(mode: "manual"), Ctx(mode: "auto")));
        Assert.False(AutoLightDecider.ShouldRecheckLevel(Ctx(), Ctx()));
    }

    [Fact]
    public void Battery_recovering_from_batt_cut_rechecks()
        => Assert.True(AutoLightDecider.ShouldRecheckLevel(Ctx(cut: true), Ctx(cut: false)));

    [Fact]
    public void Changing_lux_thresholds_rechecks()
    {
        Assert.True(AutoLightDecider.ShouldRecheckLevel(Ctx(), Ctx(on: 700)));
        Assert.True(AutoLightDecider.ShouldRecheckLevel(Ctx(), Ctx(off: 900)));
    }
}
