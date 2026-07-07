from tools.api_client import request


def list_devices() -> list:
    """Liệt kê tất cả thiết bị IoT của tài khoản"""
    return request("GET", "/api/devices")


def get_device_info(device_id: str) -> dict:
    """Lấy thông tin chi tiết một thiết bị: status, LastSeen, location, plantType"""
    return request("GET", f"/api/devices/{device_id}")
