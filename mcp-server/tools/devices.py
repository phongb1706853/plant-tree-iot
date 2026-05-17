import httpx
from config import API_BASE_URL, REQUEST_TIMEOUT

def list_devices() -> list:
    """Liệt kê tất cả thiết bị IoT đã đăng ký"""
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.get(f"{API_BASE_URL}/api/devices")
        response.raise_for_status()
        return response.json()

def get_device_info(device_id: str) -> dict:
    """Lấy thông tin chi tiết một thiết bị: status, LastSeen, location, plantType"""
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.get(f"{API_BASE_URL}/api/devices/{device_id}")
        response.raise_for_status()
        return response.json()
