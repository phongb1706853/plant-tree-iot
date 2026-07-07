"""HTTP client dùng chung cho mọi tool: tự đăng nhập lấy JWT, gắn Bearer,
và tự login lại một lần nếu gặp 401 (token hết hạn)."""
import httpx
from config import API_BASE_URL, REQUEST_TIMEOUT, MCP_USER_EMAIL, MCP_USER_PASSWORD

# Token được cache ở cấp module. Reset về None để buộc login lại.
_token = None


def _login() -> str:
    resp = httpx.post(
        f"{API_BASE_URL}/api/auth/login",
        json={"email": MCP_USER_EMAIL, "password": MCP_USER_PASSWORD},
        timeout=REQUEST_TIMEOUT,
    )
    resp.raise_for_status()
    return resp.json()["token"]


def _headers() -> dict:
    global _token
    if _token is None:
        _token = _login()
    return {"Authorization": f"Bearer {_token}"}


def request(method: str, path: str, **kwargs):
    """Gọi API kèm Bearer. Nếu 401 -> login lại 1 lần rồi thử lại."""
    global _token
    url = f"{API_BASE_URL}{path}"
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        resp = client.request(method, url, headers=_headers(), **kwargs)
        if resp.status_code == 401:
            _token = None
            resp = client.request(method, url, headers=_headers(), **kwargs)
        resp.raise_for_status()
        return resp.json()
