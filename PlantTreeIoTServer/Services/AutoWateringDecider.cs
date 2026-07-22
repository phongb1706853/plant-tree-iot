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
///   - đang tắt + soil &lt; soilOnPct + hết cooldown  -> TurnOn (soil=0 coi là rất khô, vẫn tưới)
///   - đang bật + soil &gt; soilOffPct                 -> TurnOff (cooldown KHÔNG chặn tắt)
/// soil null (thiếu dữ liệu telemetry) -> None.
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
        if (mode != "auto") return PumpAction.None;   // tôn trọng chế độ tay
        if (soilPercent is null) return PumpAction.None;  // thiếu dữ liệu telemetry -> không quyết định

        if (pumpOn == true)
            return soilPercent > soilOffPct ? PumpAction.TurnOff : PumpAction.None;

        // Bơm đang tắt: chỉ bật khi đất khô dưới ngưỡng VÀ đã hết cooldown
        if (soilPercent < soilOnPct && !cooldownActive)
            return PumpAction.TurnOn;

        return PumpAction.None;
    }
}
