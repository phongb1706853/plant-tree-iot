from tools.api_client import request


def get_moisture_rule(device_id: str) -> list:
    """Xem danh sách rule tưới nước tự động của thiết bị"""
    return request("GET", f"/api/rules/moisture/{device_id}")


def set_moisture_rule(
    device_id: str,
    name: str,
    min_moisture: float,
    max_moisture: float,
    water_duration_ms: int = 5000,
    cooldown_ms: int = 1800000
) -> dict:
    """Tạo rule tưới tự động.
    Tưới khi độ ẩm đất < min_moisture, dừng khi > max_moisture.
    water_duration_ms: thời gian bơm (ms). cooldown_ms: thời gian chờ giữa các lần tưới (ms).
    """
    return request("POST", "/api/rules/moisture", json={
        "deviceId": device_id,
        "name": name,
        "minMoisture": min_moisture,
        "maxMoisture": max_moisture,
        "waterDurationMs": water_duration_ms,
        "isEnabled": True,
        "cooldownMs": cooldown_ms
    })


def get_light_rule(device_id: str) -> list:
    """Xem danh sách rule đèn tự động của thiết bị"""
    return request("GET", f"/api/rules/light/{device_id}")


def set_light_rule(
    device_id: str,
    name: str,
    min_light: float,
    max_light: float,
    cooldown_ms: int = 600000
) -> dict:
    """Tạo rule đèn tự động.
    Bật đèn khi ánh sáng < min_light, tắt khi > max_light.
    cooldown_ms: thời gian chờ giữa 2 lần đổi trạng thái đèn (ms).
    """
    return request("POST", "/api/rules/light", json={
        "deviceId": device_id,
        "name": name,
        "minLight": min_light,
        "maxLight": max_light,
        "isEnabled": True,
        "cooldownMs": cooldown_ms
    })
