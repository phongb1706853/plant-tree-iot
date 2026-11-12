using System.Text.Json;
using PlantTreeIoTServer.Services;
using Xunit;

namespace PlantTreeIoTServer.Tests;

// Kiểm tra body cấu hình trước khi publish (hàm thuần). Bám bảng DEFS của firmware 2.1.0 (backend-api.md mục 6):
// giá trị sai bị firmware TỪ CHỐI im lặng (chỉ báo qua 'rejected'), nên server phải chặn sớm và trả 400 rõ ràng.
public class DeviceConfigRulesTests
{
    private static (Dictionary<string, object?> Config, List<string> Errors) Validate(string json, bool isSim = false)
    {
        using var doc = JsonDocument.Parse(json);
        return DeviceConfigRules.Validate(doc.RootElement.Clone(), isSim);
    }

    [Fact]
    public void Table_has_the_25_firmware_keys_in_order()
    {
        Assert.Equal(25, DeviceConfigRules.Keys.Count);
        Assert.Equal("soil_on_pct", DeviceConfigRules.Keys[0].Key);
        Assert.Equal("screen_timeout_s", DeviceConfigRules.Keys[^1].Key);
    }

    [Fact]
    public void Valid_keys_pass_with_normalized_types()
    {
        var (cfg, errors) = Validate("""
            {"soil_on_pct":35,"soil_off_pct":65,"pump_dose_ms":2500,"light_auto":true,"lux_on":40.5,
             "power_mode":"always_on","screen_timeout_s":0}
            """);

        Assert.Empty(errors);
        Assert.Equal(35, cfg["soil_on_pct"]);
        Assert.Equal(2500, cfg["pump_dose_ms"]);
        Assert.Equal(true, cfg["light_auto"]);
        Assert.Equal(40.5, cfg["lux_on"]);
        Assert.Equal("always_on", cfg["power_mode"]);
    }

    [Fact]
    public void Empty_object_is_valid_and_means_ask_for_config()
    {
        var (cfg, errors) = Validate("{}");
        Assert.Empty(errors);
        Assert.Empty(cfg);
    }

    [Fact]
    public void Int_key_accepts_a_float_and_rounds_it_after_the_range_check()
    {
        var (cfg, errors) = Validate("""{"soak_s":600.6}""");
        Assert.Empty(errors);
        Assert.Equal(601, cfg["soak_s"]);
    }

    [Fact]
    public void Out_of_range_value_is_rejected_not_clamped()
    {
        // Ví dụ trong tài liệu: pump_dose_ms = 50 nằm ngoài 200–10000.
        var (cfg, errors) = Validate("""{"pump_dose_ms":50}""");
        Assert.Single(errors);
        Assert.Contains("pump_dose_ms", errors[0]);
        Assert.False(cfg.ContainsKey("pump_dose_ms"));
    }

    [Fact]
    public void String_number_is_rejected()
    {
        var (_, errors) = Validate("""{"soil_on_pct":"30"}""");
        Assert.Single(errors);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void Bool_key_accepts_0_and_1(string raw, bool expected)
    {
        var (cfg, errors) = Validate($$"""{"light_auto":{{raw}}}""");
        Assert.Empty(errors);
        Assert.Equal(expected, cfg["light_auto"]);
    }

    [Fact]
    public void Bool_key_rejects_other_numbers()
    {
        var (_, errors) = Validate("""{"light_auto":2}""");
        Assert.Single(errors);
    }

    [Fact]
    public void Power_mode_rejects_unknown_string_and_wrong_case()
    {
        Assert.Single(Validate("""{"power_mode":"turbo"}""").Errors);
        Assert.Single(Validate("""{"power_mode":"AUTO"}""").Errors);
    }

    [Fact]
    public void Power_mode_accepts_number_0_to_2()
    {
        var (cfg, errors) = Validate("""{"power_mode":2}""");
        Assert.Empty(errors);
        Assert.Equal(2, cfg["power_mode"]);
    }

    [Fact]
    public void Null_value_is_skipped_like_firmware()
    {
        var (cfg, errors) = Validate("""{"soil_on_pct":null,"soak_s":900}""");
        Assert.Empty(errors);
        Assert.False(cfg.ContainsKey("soil_on_pct"));
        Assert.Equal(900, cfg["soak_s"]);
    }

    [Theory]
    [InlineData("pump_max_run_s")]
    [InlineData("pump_cooldown_s")]
    public void Removed_1x_keys_are_rejected_for_real_device(string key)
    {
        var (_, errors) = Validate($$"""{"{{key}}":20}""");
        Assert.Single(errors);
        Assert.Contains("firmware 2.x", errors[0]);
    }

    [Fact]
    public void Unknown_key_is_rejected_for_real_device()
    {
        var (_, errors) = Validate("""{"soil_on":30}""");
        Assert.Single(errors);
    }

    // ---- Ràng buộc cặp: chỉ kiểm khi cả hai khoá có trong cùng lệnh ----

    [Fact]
    public void Soil_pair_needs_a_gap_of_5()
    {
        // Ví dụ trong tài liệu: 25 + 5 > 28.
        var (_, errors) = Validate("""{"soil_on_pct":25,"soil_off_pct":28}""");
        Assert.Single(errors);
        Assert.Empty(Validate("""{"soil_on_pct":25,"soil_off_pct":30}""").Errors);
    }

    [Fact]
    public void Pair_with_only_one_key_is_not_checked()
    {
        // Khoá còn lại là giá trị đang lưu trên thiết bị — server không biết chắc, để firmware kiểm.
        Assert.Empty(Validate("""{"soil_off_pct":32}""").Errors);
    }

    [Theory]
    [InlineData("""{"lux_on":200,"lux_off":200}""")]
    [InlineData("""{"batt_warn_pct":20,"batt_recover_pct":20}""")]
    [InlineData("""{"batt_full_on_v":8.0,"batt_full_off_v":8.0}""")]
    [InlineData("""{"batt_crit_v":6.8,"batt_crit_recover_v":6.9}""")]
    [InlineData("""{"soil_dry":1400,"soil_wet":1200}""")]
    [InlineData("""{"plugged_on_v":8.1,"plugged_off_v":8.1}""")]
    public void Broken_pairs_are_rejected(string json)
    {
        Assert.Single(Validate(json).Errors);
    }

    [Fact]
    public void Crit_recover_with_a_small_margin_passes()
    {
        Assert.Empty(Validate("""{"batt_crit_v":6.8,"batt_crit_recover_v":7.01}""").Errors);
    }

    [Fact]
    public void Soil_calibration_works_in_either_direction()
    {
        Assert.Empty(Validate("""{"soil_dry":1200,"soil_wet":3200}""").Errors);
    }

    // ---- Thiết bị ảo SIM_*: hợp đồng cũ 15 khoá ----

    [Fact]
    public void Sim_device_keeps_legacy_keys_and_ignores_new_ones()
    {
        var (cfg, errors) = Validate("""{"pump_max_run_s":20,"pump_cooldown_s":300,"pump_dose_ms":2000}""", isSim: true);
        Assert.Empty(errors);
        Assert.Equal(20, cfg["pump_max_run_s"]);
        Assert.Equal(300, cfg["pump_cooldown_s"]);
        Assert.False(cfg.ContainsKey("pump_dose_ms"));
    }

    [Fact]
    public void Sim_device_does_not_range_check()
    {
        // Thiết bị ảo tự kẹp theo dải cũ (vd lux 0–65535, light_auto_pwm 0–255).
        var (cfg, errors) = Validate("""{"light_auto_pwm":0,"lux_on":120}""", isSim: true);
        Assert.Empty(errors);
        Assert.Equal(0, cfg["light_auto_pwm"]);
    }

    [Fact]
    public void Sim_device_still_needs_numbers()
    {
        Assert.Single(Validate("""{"soil_on_pct":"30"}""", isSim: true).Errors);
    }
}
