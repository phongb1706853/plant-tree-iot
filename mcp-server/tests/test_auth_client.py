import respx
import httpx

import tools.api_client as api_client
from tools.devices import list_devices

BASE = "http://localhost:5000"


@respx.mock
def test_login_and_bearer_header_sent():
    """Khi chưa có token, client tự login rồi gắn Authorization: Bearer."""
    api_client._token = None  # buộc login thật (ghi đè preset của conftest)

    login = respx.post(f"{BASE}/api/auth/login").mock(
        return_value=httpx.Response(200, json={"token": "real-jwt-token"}))
    devices = respx.get(f"{BASE}/api/devices").mock(
        return_value=httpx.Response(200, json=[{"deviceId": "dev1"}]))

    result = list_devices()

    assert result[0]["deviceId"] == "dev1"
    assert login.called
    assert devices.calls.last.request.headers["Authorization"] == "Bearer real-jwt-token"


@respx.mock
def test_relogin_on_401():
    """Gặp 401 -> xoá token, login lại 1 lần rồi thử lại thành công."""
    api_client._token = None

    login = respx.post(f"{BASE}/api/auth/login").mock(
        return_value=httpx.Response(200, json={"token": "t"}))
    respx.get(f"{BASE}/api/devices").mock(
        side_effect=[httpx.Response(401), httpx.Response(200, json=[{"deviceId": "dev1"}])])

    result = list_devices()

    assert result[0]["deviceId"] == "dev1"
    assert login.call_count == 2  # login lần đầu + login lại sau 401
