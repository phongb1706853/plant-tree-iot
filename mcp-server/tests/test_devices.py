import respx
import httpx
from tools.devices import list_devices, get_device_info

BASE = "http://localhost:5000"

@respx.mock
def test_list_devices_returns_list():
    respx.get(f"{BASE}/api/devices").mock(
        return_value=httpx.Response(200, json=[
            {"deviceId": "dev1", "name": "Phòng khách", "isActive": True}
        ])
    )
    result = list_devices()
    assert isinstance(result, list)
    assert result[0]["deviceId"] == "dev1"

@respx.mock
def test_get_device_info_returns_device():
    respx.get(f"{BASE}/api/devices/dev1").mock(
        return_value=httpx.Response(200, json={
            "deviceId": "dev1", "name": "Phòng khách", "isActive": True
        })
    )
    result = get_device_info("dev1")
    assert result["deviceId"] == "dev1"
    assert result["isActive"] is True
