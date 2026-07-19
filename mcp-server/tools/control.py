from tools.api_client import request

# Điều khiển bám hợp đồng firmware Xmini (mqtt-api.md mục 4): BE publish KHOÁ PHẲNG xuống
# xmini/control. Thiết bị KHÔNG hiểu WATER_ON/LIGHT_ON/FAN_* — chỉ hiểu pump/light/light_pwm/
# mode/auto/config/message. Mọi lệnh gửi qua POST /api/control/{deviceId}.


def set_pump(device_id: str, on: bool) -> dict:
    """Bật/tắt máy bơm (thủ công).
    ⚠ Lệnh chấp hành khiến thiết bị chuyển sang MANUAL cho tới khi gọi set_mode(auto=True).
    Ở AUTO, thiết bị tự tưới theo ngưỡng; dùng set_device_config để chỉnh ngưỡng thay vì bơm tay.
    """
    return request("POST", f"/api/control/{device_id}", json={"pump": bool(on)})


def set_light(device_id: str, on: bool | None = None, pwm: int | None = None) -> dict:
    """Điều khiển đèn (thủ công). Truyền `pwm` (0–255) để đặt độ sáng, hoặc `on` để bật/tắt.
    ⚠ Lệnh chấp hành khiến thiết bị chuyển sang MANUAL cho tới khi gọi set_mode(auto=True).
    """
    if pwm is not None:
        return request("POST", f"/api/control/{device_id}", json={"light_pwm": int(pwm)})
    if on is not None:
        return request("POST", f"/api/control/{device_id}", json={"light": bool(on)})
    return {"error": "Cần truyền `on` (bool) hoặc `pwm` (0–255)."}


def set_mode(device_id: str, auto: bool) -> dict:
    """Đổi chế độ: auto=True -> AUTO (thiết bị tự tưới/đèn theo ngưỡng); auto=False -> MANUAL.
    Gọi auto=True để trả thiết bị về tự động sau khi đã can thiệp tay.
    """
    return request("POST", f"/api/control/{device_id}", json={"mode": "auto" if auto else "manual"})


def show_message(device_id: str, text: str, secs: int = 0) -> dict:
    """Hiện chữ lên màn hình TFT của chậu cây ("chậu cây nói với người").
    text: nội dung — CHỈ ASCII không dấu (tiếng Việt có dấu / emoji sẽ hiện sai); "" để xoá.
    secs: tự ẩn sau N giây; 0 = giữ tới khi thay/xoá. KHÔNG đổi chế độ AUTO/MANUAL.
    """
    payload: dict = {"message": text}
    if secs and secs > 0:
        payload["message_secs"] = int(secs)
    return request("POST", f"/api/control/{device_id}", json=payload)


def get_recent_commands(device_id: str) -> list:
    """Xem nhật ký các lệnh gần nhất đã publish xuống thiết bị (mới nhất trước)."""
    return request("GET", f"/api/control/commands/{device_id}")
