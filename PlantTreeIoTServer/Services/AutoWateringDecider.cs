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
///   - bồn cạn (water_ok=false): không bật; đang bật thì TurnOff (tránh bơm chạy khô). Pin cạn (batt_cut): không bật.
/// soil null (thiếu dữ liệu telemetry) -> None. Đổ nước lại thì xoá cooldown (ShouldClearCooldown).
/// </summary>
public static class AutoWateringDecider
{
    // Fallback khi DeviceConfig chưa có ngưỡng (khớp hợp đồng firmware).
    public const int DefaultSoilOnPct = 30;
    public const int DefaultSoilOffPct = 60;
    public const int DefaultPumpCooldownS = 300;

    /// <param name="waterOk">water_ok telemetry: false = bồn cạn -> không bật (đang chạy thì tắt, tránh bơm chạy khô);
    /// null = cảm biến nước tắt -> không chặn.</param>
    /// <param name="battCut">batt_cut: thiết bị đã cắt tải -> không gửi lệnh bật (vô ích).</param>
    public static PumpAction Decide(
        string? mode,
        int? soilPercent,
        bool? pumpOn,
        int soilOnPct,
        int soilOffPct,
        bool cooldownActive,
        bool? waterOk = null,
        bool? battCut = null)
    {
        if (mode != "auto") return PumpAction.None;   // tôn trọng chế độ tay

        // Bồn cạn mà bơm đang chạy -> tắt ngay (bơm chạy khô hại bơm, đất cũng không ẩm lên).
        if (pumpOn == true && waterOk == false) return PumpAction.TurnOff;

        if (soilPercent is null) return PumpAction.None;  // thiếu dữ liệu telemetry -> không quyết định

        if (pumpOn == true)
            return soilPercent > soilOffPct ? PumpAction.TurnOff : PumpAction.None;

        // Bồn cạn / pin cạn: bật cũng vô ích — và một lần bơm khô sẽ mở cooldown, làm lỡ lượt tưới sau khi đổ nước.
        if (waterOk == false || battCut == true) return PumpAction.None;

        // Bơm đang tắt: chỉ bật khi đất khô dưới ngưỡng VÀ đã hết cooldown
        if (soilPercent < soilOnPct && !cooldownActive)
            return PumpAction.TurnOn;

        return PumpAction.None;
    }

    /// <summary>
    /// Vừa ĐỔ NƯỚC lại (water_ok false -> true): xoá cooldown để đất đang khô được tưới ngay — lần bơm trước (nếu
    /// có) chạy khô, không tính là đã tưới. Lần đầu thấy thiết bị (prev null) không tính.
    /// </summary>
    public static bool ShouldClearCooldown(bool? prevWaterOk, bool? waterOk)
        => prevWaterOk == false && waterOk == true;

    /// <summary>
    /// Mở cooldown khi bơm chuyển BẬT -> TẮT — nhưng CHỈ khi lượt bơm đó có nước (water_ok không false ở cả telemetry
    /// trước lẫn hiện tại). Lượt chạy khô không tính là đã tưới, để đổ nước xong là tưới được ngay.
    /// </summary>
    public static bool ShouldStartCooldown(bool? prevPumpOn, bool? pumpOn, bool? prevWaterOk, bool? waterOk)
        => prevPumpOn == true && pumpOn == false && prevWaterOk != false && waterOk != false;
}
