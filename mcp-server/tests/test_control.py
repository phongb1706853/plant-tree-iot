import json
import respx
import httpx
from tools.control import set_pump, set_light, set_mode, show_message, get_recent_commands

BASE = "http://localhost:5000"


@respx.mock
def test_set_pump_publishes_flat_key():
    route = respx.post(f"{BASE}/api/control/dev1").mock(
        return_value=httpx.Response(200, json={
            "message": "Đã gửi lệnh xuống xmini/control",
            "deviceId": "dev1",
            "published": {"pump": True},
        })
    )
    result = set_pump("dev1", True)
    assert result["published"] == {"pump": True}
    # Body gửi lên đúng khoá phẳng theo hợp đồng (không bọc command/parameters)
    assert json.loads(route.calls.last.request.content) == {"pump": True}


@respx.mock
def test_set_light_pwm_takes_precedence():
    route = respx.post(f"{BASE}/api/control/dev1").mock(
        return_value=httpx.Response(200, json={"published": {"light_pwm": 180}})
    )
    result = set_light("dev1", on=True, pwm=180)
    assert json.loads(route.calls.last.request.content) == {"light_pwm": 180}
    assert result["published"]["light_pwm"] == 180


def test_set_light_without_args_returns_error():
    result = set_light("dev1")
    assert "error" in result


@respx.mock
def test_set_mode_auto():
    route = respx.post(f"{BASE}/api/control/dev1").mock(
        return_value=httpx.Response(200, json={"published": {"mode": "auto"}})
    )
    set_mode("dev1", True)
    assert json.loads(route.calls.last.request.content) == {"mode": "auto"}


@respx.mock
def test_show_message_with_secs():
    route = respx.post(f"{BASE}/api/control/dev1").mock(
        return_value=httpx.Response(200, json={"published": {"message": "Toi khat nuoc", "message_secs": 15}})
    )
    show_message("dev1", "Toi khat nuoc", secs=15)
    assert json.loads(route.calls.last.request.content) == {"message": "Toi khat nuoc", "message_secs": 15}


@respx.mock
def test_get_recent_commands():
    respx.get(f"{BASE}/api/control/commands/dev1").mock(
        return_value=httpx.Response(200, json=[
            {"deviceId": "dev1", "payload": {"pump": True}}
        ])
    )
    result = get_recent_commands("dev1")
    assert result[0]["payload"] == {"pump": True}
