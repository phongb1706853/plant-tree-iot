using System.Globalization;
using System.Text.Json;

namespace PlantTreeIoTServer.Services;

/// <summary>Kiểu giá trị của một khoá cấu hình (backend-api.md mục 6, "Cách nhận kiểu").</summary>
public enum ConfigValueKind
{
    Int,
    Float,
    Bool,
    PowerMode,
}

/// <summary>Một dòng của bảng khoá cấu hình: kiểu + miền đóng [Min, Max] + mặc định của firmware.</summary>
public sealed record ConfigKeySpec(string Key, ConfigValueKind Kind, double Min, double Max, object Default);

/// <summary>
/// Kiểm tra body cấu hình TRƯỚC khi publish {"config":{...}} xuống thiết bị — HÀM THUẦN.
///
/// Thiết bị thật (firmware 2.1.0): 25 khoá, giá trị sai kiểu / ngoài miền bị firmware TỪ CHỐI (không kẹp) và chỉ
/// báo lại qua 'rejected' trên xmini/config. Server kiểm sớm theo đúng bảng DEFS (settings.cpp) để trả 400 ngay cho
/// app thay vì để lệnh đi rồi bị từ chối im lặng. Ràng buộc cặp chỉ kiểm được khi CẢ HAI khoá có trong cùng lệnh
/// (firmware kiểm trên giá trị sau khi áp cả lệnh — khoá còn lại là giá trị đang lưu trên thiết bị).
///
/// Thiết bị ảo SIM_*: giữ hợp đồng cũ (15 khoá, chỉ cần là số; thiết bị ảo tự kẹp).
/// </summary>
public static class DeviceConfigRules
{
    /// <summary>25 khoá theo đúng thứ tự bảng §6 (cũng là thứ tự thiết bị in trong xmini/config).</summary>
    public static readonly IReadOnlyList<ConfigKeySpec> Keys = new ConfigKeySpec[]
    {
        // Tưới theo liều
        new("soil_on_pct", ConfigValueKind.Int, 0, 100, 30),
        new("soil_off_pct", ConfigValueKind.Int, 0, 100, 60),
        new("pump_dose_ms", ConfigValueKind.Int, 200, 10000, 2000),
        new("soak_s", ConfigValueKind.Int, 60, 7200, 600),
        new("max_doses_per_cycle", ConfigValueKind.Int, 1, 20, 4),
        new("max_doses_per_day", ConfigValueKind.Int, 0, 48, 8),
        new("soil_dry", ConfigValueKind.Int, 0, 4095, 3200),
        new("soil_wet", ConfigValueKind.Int, 0, 4095, 1200),
        // Đèn tự động
        new("light_auto", ConfigValueKind.Bool, 0, 1, false),
        new("lux_on", ConfigValueKind.Float, 0, 100000, 50.0),
        new("lux_off", ConfigValueKind.Float, 0, 100000, 200.0),
        new("light_auto_pwm", ConfigValueKind.Int, 1, 255, 255),
        new("light_min_batt_pct", ConfigValueKind.Int, 0, 100, 50),
        new("light_max_min_per_day", ConfigValueKind.Int, 0, 1440, 240),
        // Pin
        new("batt_warn_pct", ConfigValueKind.Int, 0, 100, 15),
        new("batt_recover_pct", ConfigValueKind.Int, 0, 100, 20),
        new("batt_full_on_v", ConfigValueKind.Float, 4.0, 9.0, 8.3),
        new("batt_full_off_v", ConfigValueKind.Float, 4.0, 9.0, 8.0),
        new("batt_crit_v", ConfigValueKind.Float, 6.0, 7.5, 6.8),
        new("batt_crit_recover_v", ConfigValueKind.Float, 6.2, 8.0, 7.2),
        // Nguồn / màn hình
        new("power_mode", ConfigValueKind.PowerMode, 0, 2, "auto"),
        new("wake_interval_s", ConfigValueKind.Int, 60, 3600, 300),
        new("plugged_on_v", ConfigValueKind.Float, 7.0, 8.6, 8.25),
        new("plugged_off_v", ConfigValueKind.Float, 7.0, 8.6, 8.1),
        new("screen_timeout_s", ConfigValueKind.Int, 0, 3600, 0),
    };

    /// <summary>Giá trị chuỗi hợp lệ của power_mode (ngoài ra firmware còn nhận số 0 / 1 / 2).</summary>
    public static readonly IReadOnlyList<string> PowerModes = new[] { "eco", "always_on", "auto" };

    /// <summary>Khoá đã bỏ ở firmware 2.x (firmware bỏ qua im lặng) — báo rõ cho người gọi thay vì để lệnh vô tác dụng.</summary>
    public static readonly IReadOnlyDictionary<string, string> RemovedKeys = new Dictionary<string, string>
    {
        ["pump_max_run_s"] = "đã bỏ ở firmware 2.x: bơm chạy theo liều, dùng pump_dose_ms / max_doses_per_cycle",
        ["pump_cooldown_s"] = "đã bỏ ở firmware 2.x: dùng soak_s (chờ ngấm giữa 2 liều)",
    };

    /// <summary>15 khoá của hợp đồng cũ mà thiết bị ảo SIM_* hiểu.</summary>
    public static readonly IReadOnlySet<string> SimKeys = new HashSet<string>
    {
        "soil_on_pct", "soil_off_pct", "pump_max_run_s", "pump_cooldown_s",
        "lux_on", "lux_off", "light_auto_pwm", "batt_warn_pct", "batt_recover_pct",
        "soil_dry", "soil_wet", "batt_full_on_v", "batt_full_off_v", "batt_crit_v", "batt_crit_recover_v",
    };

    private static readonly Dictionary<string, ConfigKeySpec> ByKey = Keys.ToDictionary(k => k.Key);

    /// <summary>
    /// Lọc + kiểm object "config". Trả về dict đã chuẩn hoá (int / double / bool / chuỗi power_mode) và danh sách lỗi
    /// (rỗng = hợp lệ). <c>{}</c> hợp lệ: nghĩa là "hỏi lại cấu hình".
    /// </summary>
    public static (Dictionary<string, object?> Config, List<string> Errors) Validate(JsonElement configEl, bool isSim)
        => isSim ? ValidateSim(configEl) : ValidateDevice(configEl);

    private static (Dictionary<string, object?> Config, List<string> Errors) ValidateDevice(JsonElement configEl)
    {
        var config = new Dictionary<string, object?>();
        var errors = new List<string>();

        foreach (var prop in configEl.EnumerateObject())
        {
            if (!ByKey.TryGetValue(prop.Name, out var spec))
            {
                errors.Add(RemovedKeys.TryGetValue(prop.Name, out var why)
                    ? $"'{prop.Name}' {why}."
                    : $"'{prop.Name}' không phải khoá cấu hình. Khoá hợp lệ: {string.Join(", ", Keys.Select(k => k.Key))}.");
                continue;
            }

            // Firmware bỏ qua null (không đổi, không báo) -> không gửi xuống.
            if (prop.Value.ValueKind == JsonValueKind.Null)
                continue;

            var (value, error) = ParseValue(spec, prop.Value);
            if (error != null)
                errors.Add(error);
            else
                config[spec.Key] = value;
        }

        errors.AddRange(CheckPairs(config));
        return (config, errors);
    }

    private static (object? Value, string? Error) ParseValue(ConfigKeySpec spec, JsonElement el)
    {
        switch (spec.Kind)
        {
            case ConfigValueKind.Int:
            {
                if (el.ValueKind != JsonValueKind.Number)
                    return (null, $"'{spec.Key}' phải là số nguyên {Range(spec)}.");
                var v = el.GetDouble();
                // Firmware kiểm miền TRƯỚC khi làm tròn.
                if (v < spec.Min || v > spec.Max)
                    return (null, $"'{spec.Key}' = {Fmt(v)} nằm ngoài miền {Range(spec)}.");
                return ((int)Math.Round(v, MidpointRounding.AwayFromZero), null);
            }

            case ConfigValueKind.Float:
            {
                if (el.ValueKind != JsonValueKind.Number)
                    return (null, $"'{spec.Key}' phải là số {Range(spec)}.");
                var v = el.GetDouble();
                if (v < spec.Min || v > spec.Max)
                    return (null, $"'{spec.Key}' = {Fmt(v)} nằm ngoài miền {Range(spec)}.");
                return (v, null);
            }

            case ConfigValueKind.Bool:
            {
                if (el.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    return (el.GetBoolean(), null);
                if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var n) && n is 0 or 1)
                    return (n == 1, null);
                return (null, $"'{spec.Key}' phải là true / false (hoặc 0 / 1).");
            }

            case ConfigValueKind.PowerMode:
            {
                if (el.ValueKind == JsonValueKind.String && PowerModes.Contains(el.GetString()))
                    return (el.GetString(), null);
                // Firmware nhận cả số 0 / 1 / 2: gửi nguyên số, thiết bị tự hiểu và echo lại dạng chuỗi.
                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i) && i is >= 0 and <= 2)
                    return (i, null);
                return (null, $"'{spec.Key}' phải là \"eco\", \"always_on\" hoặc \"auto\" (hoặc 0 / 1 / 2).");
            }

            default:
                return (null, $"'{spec.Key}': kiểu không hỗ trợ.");
        }
    }

    // Ràng buộc cặp (settings.cpp:89-97). Chỉ kiểm khi cả hai khoá có trong lệnh.
    private static IEnumerable<string> CheckPairs(Dictionary<string, object?> c)
    {
        if (TryPair(c, "soil_on_pct", "soil_off_pct", out var on, out var off) && !(on + 5 <= off))
            yield return $"Cần soil_on_pct + 5 ≤ soil_off_pct (đang gửi {Fmt(on)} và {Fmt(off)}).";
        if (TryPair(c, "lux_on", "lux_off", out var luxOn, out var luxOff) && !(luxOn < luxOff))
            yield return $"Cần lux_on < lux_off (đang gửi {Fmt(luxOn)} và {Fmt(luxOff)}).";
        if (TryPair(c, "batt_warn_pct", "batt_recover_pct", out var warn, out var recover) && !(warn < recover))
            yield return $"Cần batt_warn_pct < batt_recover_pct (đang gửi {Fmt(warn)} và {Fmt(recover)}).";
        if (TryPair(c, "batt_full_off_v", "batt_full_on_v", out var fullOff, out var fullOn) && !(fullOff < fullOn))
            yield return $"Cần batt_full_off_v < batt_full_on_v (đang gửi {Fmt(fullOff)} và {Fmt(fullOn)}).";
        // So sánh float như firmware (float 32-bit): để dư một chút, ví dụ 0.21 V.
        if (TryPair(c, "batt_crit_v", "batt_crit_recover_v", out var crit, out var critRecover)
            && !((float)critRecover >= (float)crit + 0.2f))
            yield return $"Cần batt_crit_recover_v ≥ batt_crit_v + 0.2 (đang gửi {Fmt(crit)} và {Fmt(critRecover)}; nên dư một chút, vd +0.21).";
        if (TryPair(c, "soil_dry", "soil_wet", out var dry, out var wet) && !(Math.Abs(dry - wet) >= 300))
            yield return $"Cần |soil_dry − soil_wet| ≥ 300 (đang gửi {Fmt(dry)} và {Fmt(wet)}).";
        if (TryPair(c, "plugged_off_v", "plugged_on_v", out var plugOff, out var plugOn) && !(plugOff < plugOn))
            yield return $"Cần plugged_off_v < plugged_on_v (đang gửi {Fmt(plugOff)} và {Fmt(plugOn)}).";
    }

    private static bool TryPair(Dictionary<string, object?> c, string a, string b, out double va, out double vb)
    {
        va = vb = 0;
        if (!c.TryGetValue(a, out var oa) || !c.TryGetValue(b, out var ob)) return false;
        va = Convert.ToDouble(oa, CultureInfo.InvariantCulture);
        vb = Convert.ToDouble(ob, CultureInfo.InvariantCulture);
        return true;
    }

    // Hợp đồng cũ cho thiết bị ảo: chỉ giữ 15 khoá, khoá lạ bỏ qua, giá trị phải là số (thiết bị ảo tự kẹp).
    private static (Dictionary<string, object?> Config, List<string> Errors) ValidateSim(JsonElement configEl)
    {
        var config = new Dictionary<string, object?>();
        var errors = new List<string>();

        foreach (var prop in configEl.EnumerateObject())
        {
            if (!SimKeys.Contains(prop.Name)) continue;
            if (prop.Value.ValueKind != JsonValueKind.Number)
            {
                errors.Add($"Ngưỡng '{prop.Name}' phải là số.");
                continue;
            }

            // int nếu là số nguyên, ngược lại double — giữ đúng kiểu để serialize sạch.
            config[prop.Name] = prop.Value.TryGetInt32(out var i) ? (object)i : prop.Value.GetDouble();
        }

        return (config, errors);
    }

    private static string Range(ConfigKeySpec spec) => $"{Fmt(spec.Min)}–{Fmt(spec.Max)}";

    private static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
