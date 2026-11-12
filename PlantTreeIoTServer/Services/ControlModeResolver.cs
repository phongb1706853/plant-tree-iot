namespace PlantTreeIoTServer.Services;

/// <summary>
/// Xử lý chế độ auto/manual cho một payload sắp publish xuống <c>xmini/control</c> — HÀM THUẦN.
///
/// Thiết bị THẬT (firmware 2.1.0) làm chủ auto: mọi lệnh <c>light</c> / <c>light_pwm</c> / <c>pump</c> đưa nó về
/// MANUAL, chỉ <c>{"mode":"auto"}</c> / <c>{"auto":true}</c> trả lại AUTO (backend-api.md mục 5.7). Server KHÔNG gộp
/// thêm khoá nào, chỉ tính mode thiết bị SẼ ở sau lệnh (<see cref="ExpectedMode"/>) để ghi tạm, telemetry xác nhận.
///
/// Thiết bị ẢO SIM_* giữ luật cũ: SERVER làm chủ mode, lệnh actuator được gộp mode hiện tại để không đổi chế độ
/// (<see cref="Apply"/>). Dùng <see cref="Resolve"/> để chọn đúng nhánh theo deviceId.
/// </summary>
public static class ControlModeResolver
{
    public const string Auto = "auto";
    public const string Manual = "manual";

    // Thiết bị ảo: các khoá ép MANUAL theo hợp đồng cũ (light_pwm KHÔNG đổi mode).
    private static readonly string[] SimModeForcingKeys = { "pump", "light" };

    // Firmware 2.1.0 (control.cpp, bước 3-4): có BẤT KỲ khoá nào trong đây -> chuyển MANUAL.
    private static readonly string[] ActuatorKeys = { "light_pwm", "light", "pump" };

    /// <summary>Chọn nhánh theo loại thiết bị. Trả về mode server nên lưu sau lệnh này.</summary>
    public static string Resolve(string deviceId, IDictionary<string, object?> flat, string currentMode)
        => SimTopics.IsSimDevice(deviceId) ? Apply(flat, currentMode) : ExpectedMode(flat, currentMode);

    /// <summary>
    /// Thiết bị thật: mode sau khi firmware xử lý lệnh, theo đúng thứ tự của nó — actuator -> MANUAL, rồi
    /// <c>mode</c> ("auto" -> AUTO, chuỗi khác -> MANUAL), rồi <c>auto</c> (xử lý sau nên thắng). KHÔNG sửa payload.
    /// </summary>
    public static string ExpectedMode(IDictionary<string, object?> flat, string currentMode)
    {
        var mode = currentMode;
        if (ActuatorKeys.Any(flat.ContainsKey))
            mode = Manual;
        if (flat.TryGetValue("mode", out var modeVal) && modeVal is string s)
            mode = s == Auto ? Auto : Manual;
        if (flat.TryGetValue("auto", out var autoVal) && autoVal is bool b)
            mode = b ? Auto : Manual;
        return mode;
    }

    /// <summary>
    /// Thiết bị ảo: đồng bộ mode cho <paramref name="flat"/> (có thể được SỬA: thêm khoá <c>mode</c> khi cần) và
    /// trả về mode server nên LƯU sau lệnh này.
    /// <list type="bullet">
    ///   <item>Có <c>mode</c>/<c>auto</c> tường minh -> đó là lệnh đổi mode -> lấy làm mode mới, không gộp thêm.</item>
    ///   <item>Không, nhưng có <c>pump</c>/<c>light</c> -> gộp <c>mode = currentMode</c> để KHÔNG đổi chế độ.</item>
    ///   <item>Còn lại (light_pwm/config/message) -> không đụng gì, mode giữ nguyên.</item>
    /// </list>
    /// </summary>
    public static string Apply(IDictionary<string, object?> flat, string currentMode)
    {
        // 1) Lệnh mode tường minh THẮNG: thiết bị xử lý mode/auto sau cùng nên đây là mode kết quả.
        if (flat.TryGetValue("mode", out var modeVal) && modeVal is string s && !string.IsNullOrWhiteSpace(s))
            return s;
        if (flat.TryGetValue("auto", out var autoVal) && autoVal is bool b)
            return b ? Auto : Manual;

        // 2) Lệnh actuator ép manual -> gộp mode hiện tại để giữ nguyên chế độ.
        foreach (var key in SimModeForcingKeys)
        {
            if (flat.ContainsKey(key))
            {
                flat["mode"] = currentMode;
                break;
            }
        }

        // 3) Không có gì đổi mode.
        return currentMode;
    }
}
