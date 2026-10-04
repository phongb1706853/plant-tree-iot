namespace PlantTreeIoTServer.Services;

/// <summary>Hành động với đèn mà bộ quyết định đèn auto đề nghị.</summary>
public enum LightAction
{
    None,
    TurnOn,
    TurnOff,
}

/// <summary>Bối cảnh để biết khi nào phải xét lại đèn theo MỨC lux (so với telemetry trước của cùng thiết bị).</summary>
public readonly record struct LightContext(string? Mode, bool? BattCut, double LuxOn, double LuxOff);

/// <summary>
/// Bộ quyết định đèn auto — HÀM THUẦN, không I/O, dễ test (cùng kiểu AutoWateringDecider). SERVER ra lệnh đèn,
/// thiết bị chỉ làm theo (logic quyết định nằm ở server, không ở firmware).
///   - Chỉ khi mode server giữ là "auto"; thiếu lux hoặc đang batt_cut (đã cắt tải) -> không ra lệnh.
///   - Tick thường: chỉ ra lệnh lúc lux VƯỢT ngưỡng so với telemetry trước (xuống dưới lux_on -> bật, lên trên
///     lux_off -> tắt) — lệnh tắt đèn tay ban đêm được giữ tới lần vượt ngưỡng kế tiếp.
///   - recheckLevel (xem ShouldRecheckLevel): xét theo MỨC lux hiện tại — tối mà đèn tắt -> bật, sáng mà đèn bật -> tắt.
///   - Nằm giữa 2 ngưỡng: giữ nguyên.
/// </summary>
public static class AutoLightDecider
{
    // Fallback khi DeviceConfig chưa có ngưỡng (khớp mặc định firmware).
    public const double DefaultLuxOn = 120;
    public const double DefaultLuxOff = 300;
    public const int DefaultLightAutoPwm = 180;

    public static LightAction Decide(
        string? mode,
        double? lux,
        double? prevLux,
        bool? lightOn,
        bool? battCut,
        double luxOn,
        double luxOff,
        bool recheckLevel)
    {
        if (mode != "auto" || lux is null || battCut == true) return LightAction.None;

        var dark = lux < luxOn;
        var bright = lux > luxOff;

        if (recheckLevel)
        {
            if (dark && lightOn != true) return LightAction.TurnOn;
            if (bright && lightOn == true) return LightAction.TurnOff;
            return LightAction.None;
        }

        if (prevLux is null) return LightAction.None;
        if (prevLux >= luxOn && dark && lightOn != true) return LightAction.TurnOn;
        if (prevLux <= luxOff && bright && lightOn == true) return LightAction.TurnOff;
        return LightAction.None;
    }

    /// <summary>
    /// Xét lại theo MỨC lux khi "vào lại auto": lần đầu thấy thiết bị, server mode chuyển sang auto, hết batt_cut,
    /// hoặc ngưỡng lux đổi — nếu chỉ xét vượt ngưỡng thì bật auto giữa đêm sẽ không bao giờ bật đèn.
    /// </summary>
    public static bool ShouldRecheckLevel(LightContext? previous, LightContext current)
    {
        if (previous is null) return true;
        var p = previous.Value;
        return (p.Mode != "auto" && current.Mode == "auto")
            || (p.BattCut == true && current.BattCut != true)
            || p.LuxOn != current.LuxOn
            || p.LuxOff != current.LuxOff;
    }
}
