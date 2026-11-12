from tools.api_client import request


def list_devices() -> list:
    """Liệt kê tất cả thiết bị IoT của tài khoản"""
    return request("GET", "/api/devices")


def get_device_info(device_id: str) -> dict:
    """Lấy thông tin chi tiết một thiết bị: status, LastSeen, location, plantType"""
    return request("GET", f"/api/devices/{device_id}")


def get_device_status(device_id: str) -> dict:
    """Trạng thái kết nối + sức khoẻ thiết bị: connection (online | sleeping | late | offline | unknown), mode
    (auto | manual), và bản xmini/status gần nhất (fw, powerSource, powerMode, powerState, uptimeS, bootCount,
    resetReason, freeHeap, rssi, safeMode...). Dùng để biết thiết bị còn sống không trước khi ra lệnh.
    """
    return request("GET", f"/api/devices/{device_id}/status")
