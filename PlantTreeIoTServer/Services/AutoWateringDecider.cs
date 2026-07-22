namespace PlantTreeIoTServer.Services;

/// <summary>Hành động với bơm mà bộ quyết định auto tưới đề nghị.</summary>
public enum PumpAction
{
    None,
    TurnOn,
    TurnOff,
}

/// <summary>
/// Bộ quyết định auto tưới — HÀM THUẦN, không I/O, dễ test.
/// Chỉ can thiệp khi thiết bị báo mode="auto". Hysteresis theo soil_percent:
///   - đang tắt + 0 &lt; soil &lt; soilOnPct + hết cooldown  -> TurnOn
///   - đang bật + soil &gt; soilOffPct                        -> TurnOff (cooldown KHÔNG chặn tắt)
/// soil &lt;= 0 (hoặc null) coi như cảm biến lỗi/chưa cắm -> None.
/// </summary>
public static class AutoWateringDecider
{
    // Fallback khi DeviceConfig chưa có ngưỡng (khớp hợp đồng firmware).
    public const int DefaultSoilOnPct = 30;
    public const int DefaultSoilOffPct = 60;
    public const int DefaultPumpCooldownS = 300;

    public static PumpAction Decide(
        string? mode,
        int? soilPercent,
        bool? pumpOn,
        int soilOnPct,
        int soilOffPct,
        bool cooldownActive)
    {
        if (mode != "auto") return PumpAction.None;               // tôn trọng chế độ tay
        if (soilPercent is null or <= 0) return PumpAction.None;  // nghi cảm biến lỗi/chưa cắm

        if (pumpOn == true)
            return soilPercent > soilOffPct ? PumpAction.TurnOff : PumpAction.None;

        // Bơm đang tắt: chỉ bật khi đất khô dưới ngưỡng VÀ đã hết cooldown
        if (soilPercent < soilOnPct && !cooldownActive)
            return PumpAction.TurnOn;

        return PumpAction.None;
    }
}
