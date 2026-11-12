from tools.api_client import request

# Điều khiển bám hợp đồng firmware Xmini 2.1.0 (backend-api.md mục 5): BE publish KHOÁ PHẲNG xuống
# xmini/control (QoS 1, server tự thêm "id"). Thiết bị KHÔNG hiểu WATER_ON/LIGHT_ON/FAN_* — chỉ hiểu pump/light/
# light_pwm/mode/auto/config/message/show_qr/stay_awake_s. Mọi lệnh gửi qua POST /api/control/{deviceId}.
# Thiết bị tự chạy AUTO; mọi lệnh pump/light/light_pwm đưa nó về MANUAL cho tới khi set_mode(auto=True).


def set_pump(device_id: str, on: bool) -> dict:
    """Tưới tay. on=True tưới ĐÚNG 1 liều (pump_dose_ms, mặc định 2 s) rồi bơm tự tắt; on=False dừng liều đang chạy.
    ⚠ Lệnh chấp hành khiến thiết bị chuyển sang MANUAL (tự tưới/tự đèn đứng yên) cho tới khi gọi set_mode(auto=True).
    Liều bị bỏ qua nếu liều trước chưa xong + 3 s. Ở AUTO thiết bị tự tưới theo ngưỡng: dùng set_device_config để chỉnh
    ngưỡng thay vì bơm tay. Xác nhận: doses_today tăng ở telemetry kế tiếp.
    """
    return request("POST", f"/api/control/{device_id}", json={"pump": bool(on)})


def set_light(device_id: str, on: bool | None = None, pwm: int | None = None) -> dict:
    """Điều khiển đèn (thủ công). Truyền `pwm` (0–255; > 0 bật ở độ sáng đó, 0 tắt), hoặc `on` để bật/tắt ở độ sáng
    hiện tại.
    ⚠ Lệnh chấp hành khiến thiết bị chuyển sang MANUAL cho tới khi gọi set_mode(auto=True). Ở MANUAL không có gì tự
    tắt đèn — nhớ tắt khi xong.
    """
    if pwm is not None:
        return request("POST", f"/api/control/{device_id}", json={"light_pwm": int(pwm)})
    if on is not None:
        return request("POST", f"/api/control/{device_id}", json={"light": bool(on)})
    return {"error": "Cần truyền `on` (bool) hoặc `pwm` (0–255)."}


def set_mode(device_id: str, auto: bool) -> dict:
    """Đổi chế độ: auto=True -> AUTO (thiết bị tự tưới theo liều / tự đèn theo ngưỡng); auto=False -> MANUAL.
    Gọi auto=True để trả thiết bị về tự động sau khi đã can thiệp tay (cũng gỡ soil_fault / safe_mode). Về AUTO với
    light_auto=false thì đèn đang bật tay sẽ tắt.
    """
    return request("POST", f"/api/control/{device_id}", json={"mode": "auto" if auto else "manual"})


def show_message(device_id: str, text: str, secs: int = 0) -> dict:
    """Hiện chữ lên màn hình TFT của chậu cây ("chậu cây nói với người").
    text: nội dung; "" để xoá. Màn chỉ hiện ASCII: server tự bỏ dấu tiếng Việt / emoji và cắt còn ≤ 90 ký tự.
    secs: tự ẩn sau N giây; 0 = giữ tới khi thay/xoá. KHÔNG đổi chế độ AUTO/MANUAL.
    """
    payload: dict = {"message": text}
    if secs and secs > 0:
        payload["message_secs"] = int(secs)
    return request("POST", f"/api/control/{device_id}", json=payload)


def show_qr(device_id: str, seconds: int = 300) -> dict:
    """Hiện mã QR trang cấu hình của thiết bị trên màn hình trong `seconds` giây (0–3600; 0 = tắt QR).
    Dùng khi người dùng cần đổi WiFi / broker tại chỗ (màn không cảm ứng). KHÔNG đổi chế độ AUTO/MANUAL.
    """
    return request("POST", f"/api/control/{device_id}/show-qr", json={"seconds": int(seconds)})


def stay_awake(device_id: str, seconds: int) -> dict:
    """Giữ thiết bị thức + online liên tục `seconds` giây (0–3600; 0 = huỷ) để điều khiển tức thời.
    Chỉ cần với bản chạy PIN đang ở eco (power_state = "eco"); bản adapter vốn luôn thức.
    """
    return request("POST", f"/api/control/{device_id}", json={"stay_awake_s": int(seconds)})


def get_recent_commands(device_id: str) -> list:
    """Xem nhật ký các lệnh gần nhất đã publish xuống thiết bị (mới nhất trước, mỗi lệnh có id)."""
    return request("GET", f"/api/control/commands/{device_id}")
