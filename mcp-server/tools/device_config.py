from tools.api_client import request

# Ngưỡng auto của thiết bị (mqtt-api.md mục 3 / 4.3). Thiết bị TỰ chạy auto theo các ngưỡng này
# (lưu trong NVS). Thay cho "moisture/light rule" phía server cũ:
#   - đọc: get_device_config  (BE nghe từ topic xmini/config)
#   - đặt: set_device_config  (BE publish {"config":{...}} -> thiết bị clamp + lưu NVS + echo lại)


def get_device_config(device_id: str) -> dict:
    """Xem ngưỡng auto hiện tại của thiết bị (soil_on_pct, lux_on, pump_max_run_s, ngưỡng pin...).
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
    pump_max_run_s: int | None = None,
    pump_cooldown_s: int | None = None,
    lux_on: float | None = None,
    lux_off: float | None = None,
    light_auto_pwm: int | None = None,
    batt_warn_pct: int | None = None,
    batt_recover_pct: int | None = None,
    soil_dry: int | None = None,
    soil_wet: int | None = None,
    batt_full_on_v: float | None = None,
    batt_full_off_v: float | None = None,
    batt_crit_v: float | None = None,
    batt_crit_recover_v: float | None = None,
) -> dict:
    """Đặt (một phần) ngưỡng auto — chỉ truyền tham số muốn đổi. Thiết bị clamp về dải hợp lệ, lưu NVS.
    Ý nghĩa chính:
      soil_on_pct: đất < % này -> auto bật bơm | soil_off_pct: đất > % này -> auto tắt bơm
      pump_max_run_s: thời gian tưới tối đa mỗi lần (s) | pump_cooldown_s: nghỉ giữa 2 lần tưới (s)
      lux_on: lux < ngưỡng -> auto bật đèn | lux_off: lux > ngưỡng -> auto tắt đèn | light_auto_pwm: 0–255
      batt_warn_pct / batt_recover_pct: cảnh báo + gỡ cảnh báo pin yếu (%)
      batt_full_on_v/off_v, batt_crit_v/recover_v: ngưỡng pin đầy / cắt xả cạn (V)
      soil_dry / soil_wet: hiệu chuẩn ADC đất khô / ẩm (0–4095)
    """
    body = {k: v for k, v in {
        "soil_on_pct": soil_on_pct,
        "soil_off_pct": soil_off_pct,
        "pump_max_run_s": pump_max_run_s,
        "pump_cooldown_s": pump_cooldown_s,
        "lux_on": lux_on,
        "lux_off": lux_off,
        "light_auto_pwm": light_auto_pwm,
        "batt_warn_pct": batt_warn_pct,
        "batt_recover_pct": batt_recover_pct,
        "soil_dry": soil_dry,
        "soil_wet": soil_wet,
        "batt_full_on_v": batt_full_on_v,
        "batt_full_off_v": batt_full_off_v,
        "batt_crit_v": batt_crit_v,
        "batt_crit_recover_v": batt_crit_recover_v,
    }.items() if v is not None}

    if not body:
        return {"error": "Cần truyền ít nhất một ngưỡng để đổi."}
    return request("PUT", f"/api/control/{device_id}/config", json=body)
