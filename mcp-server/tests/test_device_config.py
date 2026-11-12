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


@respx.mock
def test_set_device_config_sends_new_firmware_keys():
    route = respx.put(f"{BASE}/api/control/dev1/config").mock(
        return_value=httpx.Response(200, json={"published": {"config": {}}})
    )
    set_device_config("dev1", pump_dose_ms=2500, soak_s=900, light_auto=True, power_mode="always_on")
    assert json.loads(route.calls.last.request.content) == {
        "pump_dose_ms": 2500, "soak_s": 900, "light_auto": True, "power_mode": "always_on"
    }


def test_set_device_config_has_no_removed_keys():
    # Firmware 2.x bỏ pump_max_run_s / pump_cooldown_s: tool không còn nhận 2 khoá này.
    import inspect
    params = inspect.signature(set_device_config).parameters
    assert "pump_max_run_s" not in params
    assert "pump_cooldown_s" not in params
    assert len(params) == 26  # device_id + 25 khoá
