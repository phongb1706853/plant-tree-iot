import json
import respx
import httpx
from tools.device_config import get_device_config, set_device_config, refresh_device_config

BASE = "http://localhost:5000"


@respx.mock
def test_get_device_config():
    respx.get(f"{BASE}/api/control/dev1/config").mock(
        return_value=httpx.Response(200, json={
            "deviceId": "dev1", "soilOnPct": 30, "soilOffPct": 60, "luxOn": 50.0
        })
    )
    result = get_device_config("dev1")
    assert result["soilOnPct"] == 30


@respx.mock
def test_set_device_config_sends_only_given_keys():
    route = respx.put(f"{BASE}/api/control/dev1/config").mock(
        return_value=httpx.Response(200, json={"published": {"config": {"soil_on_pct": 25, "lux_on": 40.0}}})
    )
    result = set_device_config("dev1", soil_on_pct=25, lux_on=40.0)
    # Chỉ gửi các khoá được truyền (snake_case đúng hợp đồng)
    assert json.loads(route.calls.last.request.content) == {"soil_on_pct": 25, "lux_on": 40.0}
    assert result["published"]["config"]["soil_on_pct"] == 25


def test_set_device_config_no_args_returns_error():
    result = set_device_config("dev1")
    assert "error" in result


@respx.mock
def test_refresh_device_config():
    route = respx.post(f"{BASE}/api/control/dev1/config/refresh").mock(
        return_value=httpx.Response(200, json={"published": {"config": {}}})
    )
    result = refresh_device_config("dev1")
    assert route.calls.call_count == 1
    assert result["published"]["config"] == {}
