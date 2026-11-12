from tools.api_client import request

# Cấu hình của thiết bị (firmware 2.1.0, 25 khoá — backend-api.md mục 6). Thiết bị TỰ chạy auto theo các ngưỡng
# này (lưu trong NVS). Thay cho "moisture/light rule" phía server cũ:
#   - đọc: get_device_config  (BE nghe từ topic xmini/config; có 'rejected' nếu lệnh trước bị từ chối)
#   - đặt: set_device_config  (BE kiểm kiểu + miền rồi publish {"config":{...}} -> thiết bị lưu NVS + echo lại)


def get_device_config(device_id: str) -> dict:
    """Xem cấu hình hiện tại của thiết bị (soilOnPct, pumpDoseMs, soakS, lightAuto, luxOn, powerMode...).
    'rejected' / 'rejectedCount' = khoá bị thiết bị từ chối ở lệnh config gần nhất.
    Nếu chưa nghe được từ xmini/config, gọi refresh_device_config trước.
    """
    return request("GET", f"/api/control/{device_id}/config")


def refresh_device_config(device_id: str) -> dict:
    """Yêu cầu thiết bị gửi lại cấu hình hiện tại (publish {"config":{}}). Dùng khi BE mới kết nối."""
    return request("POST", f"/api/control/{device_id}/config/refresh")


def set_device_config(
    device_id: str,
    soil_on_pct: int | None = None,
    soil_off_pct: int | None = None,
    pump_dose_ms: int | None = None,
    soak_s: int | None = None,
    max_doses_per_cycle: int | None = None,
    max_doses_per_day: int | None = None,
    soil_dry: int | None = None,
    soil_wet: int | None = None,
    light_auto: bool | None = None,
    lux_on: float | None = None,
    lux_off: float | None = None,
    light_auto_pwm: int | None = None,
    light_min_batt_pct: int | None = None,
    light_max_min_per_day: int | None = None,
    batt_warn_pct: int | None = None,
    batt_recover_pct: int | None = None,
    batt_full_on_v: float | None = None,
    batt_full_off_v: float | None = None,
    batt_crit_v: float | None = None,
    batt_crit_recover_v: float | None = None,
    power_mode: str | None = None,
    wake_interval_s: int | None = None,
    plugged_on_v: float | None = None,
    plugged_off_v: float | None = None,
    screen_timeout_s: int | None = None,
) -> dict:
    """Đặt (một phần) cấu hình — chỉ truyền tham số muốn đổi. Giá trị sai kiểu / ngoài miền bị TỪ CHỐI (không kẹp).
    Đổi một cặp ngưỡng thì truyền CẢ HAI khoá trong cùng lệnh.
    Tưới theo liều:
      soil_on_pct (0–100): đất < % này -> mở đợt tưới | soil_off_pct (0–100): đất ≥ % này -> đóng đợt.
        Cần soil_on_pct + 5 ≤ soil_off_pct.
      pump_dose_ms (200–10000): thời lượng 1 liều (ms) | soak_s (60–7200): chờ ngấm giữa 2 liều (s)
      max_doses_per_cycle (1–20) | max_doses_per_day (0–48; 0 = tắt tự tưới)
      soil_dry / soil_wet (0–4095): hiệu chuẩn ADC đất khô / ẩm, cách nhau ≥ 300
    Đèn tự động (chỉ ở AUTO):
      light_auto (bool, mặc định false): cho phép tự bật đèn
      lux_on / lux_off (0–100000, lux_on < lux_off): lux < lux_on -> bật, lux > lux_off -> tắt
      light_auto_pwm (1–255) | light_min_batt_pct (0–100, chỉ bản pin) | light_max_min_per_day (0–1440 phút)
    Pin (chỉ bản pin): batt_warn_pct < batt_recover_pct (0–100); batt_full_off_v < batt_full_on_v (4–9 V);
      batt_crit_v (6.0–7.5), batt_crit_recover_v (6.2–8.0) ≥ batt_crit_v + 0.2
    Nguồn / màn hình: power_mode ("eco" | "always_on" | "auto"), wake_interval_s (60–3600),
      plugged_off_v < plugged_on_v (7.0–8.6 V), screen_timeout_s (0–3600; 0 = màn luôn sáng)
    """
    body = {k: v for k, v in {
        "soil_on_pct": soil_on_pct,
        "soil_off_pct": soil_off_pct,
        "pump_dose_ms": pump_dose_ms,
        "soak_s": soak_s,
        "max_doses_per_cycle": max_doses_per_cycle,
        "max_doses_per_day": max_doses_per_day,
        "soil_dry": soil_dry,
        "soil_wet": soil_wet,
        "light_auto": light_auto,
        "lux_on": lux_on,
        "lux_off": lux_off,
        "light_auto_pwm": light_auto_pwm,
        "light_min_batt_pct": light_min_batt_pct,
        "light_max_min_per_day": light_max_min_per_day,
        "batt_warn_pct": batt_warn_pct,
        "batt_recover_pct": batt_recover_pct,
        "batt_full_on_v": batt_full_on_v,
        "batt_full_off_v": batt_full_off_v,
        "batt_crit_v": batt_crit_v,
        "batt_crit_recover_v": batt_crit_recover_v,
        "power_mode": power_mode,
        "wake_interval_s": wake_interval_s,
        "plugged_on_v": plugged_on_v,
        "plugged_off_v": plugged_off_v,
        "screen_timeout_s": screen_timeout_s,
    }.items() if v is not None}

    if not body:
        return {"error": "Cần truyền ít nhất một khoá cấu hình để đổi."}
    return request("PUT", f"/api/control/{device_id}/config", json=body)
