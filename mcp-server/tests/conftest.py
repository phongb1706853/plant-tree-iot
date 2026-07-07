import pytest
import tools.api_client as api_client


@pytest.fixture(autouse=True)
def _preset_token():
    """Mặc định pre-seed token để các test tool không phải mock endpoint login.
    Test nào muốn kiểm tra luồng login thật thì tự set api_client._token = None."""
    api_client._token = "faketoken"
    yield
    api_client._token = None
