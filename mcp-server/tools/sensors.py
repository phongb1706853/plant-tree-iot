from tools.api_client import request


def get_latest_sensor(device_id: str) -> dict:
    """Lấy dữ liệu cảm biến mới nhất: nhiệt độ, độ ẩm không khí, độ ẩm đất, ánh sáng, pH, mực nước"""
    return request("GET", f"/api/sensordata/latest/{device_id}")


def get_sensor_history(device_id: str, limit: int = 10) -> list:
    """Lấy N bản ghi cảm biến gần nhất của thiết bị"""
    return request("GET", f"/api/sensordata/history/{device_id}", params={"limit": limit})
