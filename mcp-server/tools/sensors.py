import httpx
from config import API_BASE_URL, REQUEST_TIMEOUT

def get_latest_sensor(device_id: str) -> dict:
    """Lấy dữ liệu cảm biến mới nhất: nhiệt độ, độ ẩm không khí, độ ẩm đất, ánh sáng, pH, mực nước"""
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.get(f"{API_BASE_URL}/api/sensordata/latest/{device_id}")
        response.raise_for_status()
        return response.json()

def get_sensor_history(device_id: str, limit: int = 10) -> list:
    """Lấy N bản ghi cảm biến gần nhất của thiết bị"""
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.get(
            f"{API_BASE_URL}/api/sensordata/history/{device_id}",
            params={"limit": limit}
        )
        response.raise_for_status()
        return response.json()
