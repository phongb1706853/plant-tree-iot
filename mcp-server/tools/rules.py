import httpx
from config import API_BASE_URL, REQUEST_TIMEOUT

def get_moisture_rule(device_id: str) -> list:
    """Xem danh sách rule tưới nước tự động của thiết bị"""
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.get(f"{API_BASE_URL}/api/rules/moisture/{device_id}")
        response.raise_for_status()
        return response.json()

def set_moisture_rule(
    device_id: str,
    name: str,
    min_moisture: float,
    max_moisture: float,
    water_duration_ms: int = 5000,
    cooldown_minutes: int = 30
) -> dict:
    """Tạo rule tưới tự động.
    Tưới khi độ ẩm đất < min_moisture, dừng khi > max_moisture.
    water_duration_ms: thời gian bơm (ms). cooldown_minutes: thời gian chờ giữa các lần tưới.
    """
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.post(f"{API_BASE_URL}/api/rules/moisture", json={
            "deviceId": device_id,
            "name": name,
            "minMoisture": min_moisture,
            "maxMoisture": max_moisture,
            "waterDurationMs": water_duration_ms,
            "isEnabled": True,
            "cooldownMinutes": cooldown_minutes
        })
        response.raise_for_status()
        return response.json()

def get_light_rule(device_id: str) -> list:
    """Xem danh sách rule đèn tự động của thiết bị"""
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.get(f"{API_BASE_URL}/api/rules/light/{device_id}")
        response.raise_for_status()
        return response.json()

def set_light_rule(
    device_id: str,
    name: str,
    min_light: float,
    max_light: float,
    cooldown_minutes: int = 10
) -> dict:
    """Tạo rule đèn tự động.
    Bật đèn khi ánh sáng < min_light, tắt khi > max_light.
    """
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.post(f"{API_BASE_URL}/api/rules/light", json={
            "deviceId": device_id,
            "name": name,
            "minLight": min_light,
            "maxLight": max_light,
            "isEnabled": True,
            "cooldownMinutes": cooldown_minutes
        })
        response.raise_for_status()
        return response.json()
