namespace PlantTreeIoTServer.Services;

/// <summary>
/// Xử lý chế độ auto/manual cho một payload sắp publish xuống <c>xmini/control</c> — HÀM THUẦN.
///
/// Bối cảnh: firmware ép <c>mode="manual"</c> khi nhận BẤT KỲ lệnh <c>pump</c> hoặc <c>light</c>
/// (xem <c>esp32-mqtt-client.ino</c> khối <c>onControlReceived</c>). Nếu để nguyên, mọi lệnh tưới/đèn
/// — kể cả auto-tưới do server sinh — sẽ khiến thiết bị rớt khỏi auto. Yêu cầu sản phẩm: SERVER làm
/// chủ mode; chỉ lệnh mode TƯỜNG MINH mới đổi mode, lệnh actuator thì giữ nguyên mode hiện tại.
///
/// Firmware xử lý khoá theo thứ tự cố định: pump/light TRƯỚC, rồi mới tới mode/auto. Nhờ vậy chỉ cần
/// GỘP <c>mode</c> hiện tại vào cùng payload là mode được "sửa lại" ngay sau khi actuator ép manual.
/// </summary>
public static class ControlModeResolver
{
    // Các khoá khiến firmware ép sang MANUAL khi xuất hiện. (light_pwm KHÔNG đổi mode.)
    private static readonly string[] ModeForcingKeys = { "pump", "light" };

    /// <summary>
    /// Đồng bộ mode cho <paramref name="flat"/> (có thể được SỬA: thêm khoá <c>mode</c> khi cần) và
    /// trả về mode server nên LƯU sau lệnh này.
    /// <list type="bullet">
    ///   <item>Có <c>mode</c>/<c>auto</c> tường minh -> đó là lệnh đổi mode -> lấy làm mode mới, không gộp thêm.</item>
    ///   <item>Không, nhưng có <c>pump</c>/<c>light</c> -> gộp <c>mode = currentMode</c> để KHÔNG đổi chế độ.</item>
    ///   <item>Còn lại (light_pwm/config/message) -> không đụng gì, mode giữ nguyên.</item>
    /// </list>
    /// </summary>
    public static string Apply(IDictionary<string, object?> flat, string currentMode)
    {
        // 1) Lệnh mode tường minh THẮNG: firmware xử lý mode/auto sau cùng nên đây là mode kết quả.
        if (flat.TryGetValue("mode", out var modeVal) && modeVal is string s && !string.IsNullOrWhiteSpace(s))
            return s;
        if (flat.TryGetValue("auto", out var autoVal) && autoVal is bool b)
            return b ? "auto" : "manual";

        // 2) Lệnh actuator ép manual -> gộp mode hiện tại để giữ nguyên chế độ.
        foreach (var key in ModeForcingKeys)
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
