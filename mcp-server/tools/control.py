import httpx
from config import API_BASE_URL, REQUEST_TIMEOUT

VALID_COMMANDS = ["WATER_ON", "WATER_OFF", "LIGHT_ON", "LIGHT_OFF", "FAN_ON", "FAN_OFF"]

def send_command(device_id: str, command: str, duration: int = 0) -> dict:
    """Gửi lệnh điều khiển đến thiết bị.
    command: WATER_ON, WATER_OFF, LIGHT_ON, LIGHT_OFF, FAN_ON, FAN_OFF
    duration: thời gian (ms), chỉ dùng cho WATER_ON
    """
    if command not in VALID_COMMANDS:
        return {"error": f"Lệnh không hợp lệ. Các lệnh hợp lệ: {', '.join(VALID_COMMANDS)}"}
    payload = {"deviceId": device_id, "command": command}
    if duration > 0:
        payload["parameters"] = {"duration": duration}
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.post(f"{API_BASE_URL}/api/control/commands", json=payload)
        response.raise_for_status()
        return response.json()

def get_pending_commands(device_id: str) -> list:
    """Xem danh sách lệnh đang chờ thiết bị thực thi"""
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.get(f"{API_BASE_URL}/api/control/commands/{device_id}")
        response.raise_for_status()
        return response.json()

def auto_water(device_id: str, threshold: float = 30.0) -> dict:
    """Tưới tự động nếu độ ẩm đất < threshold (mặc định 30%)"""
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.post(
            f"{API_BASE_URL}/api/control/auto-water/{device_id}",
            params={"threshold": threshold}
        )
        response.raise_for_status()
        return response.json()

def auto_light(device_id: str, threshold: float = 200.0) -> dict:
    """Tự động bật đèn nếu ánh sáng < threshold, tắt nếu >= threshold (mặc định 200 lux)"""
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.post(
            f"{API_BASE_URL}/api/control/auto-light/{device_id}",
            params={"threshold": threshold}
        )
        response.raise_for_status()
        return response.json()
