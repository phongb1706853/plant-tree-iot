import os
from mcp.server.fastmcp import FastMCP
from config import MCP_SERVER_NAME
from tools.devices import list_devices, get_device_info
from tools.sensors import get_latest_sensor, get_sensor_history
from tools.control import set_pump, set_light, set_mode, show_message, get_recent_commands
from tools.device_config import get_device_config, set_device_config, refresh_device_config

# Transport:
#   - "stdio" (mặc định): dùng khi client tự spawn MCP làm subprocess.
#   - "streamable-http": expose HTTP tại http://<host>:<port>/mcp — dùng cho
#     AI server (tree-grow-helper) kết nối qua mạng.
# Port mặc định 8100 để KHÔNG đụng .NET API (thường 8000/80).
MCP_TRANSPORT = os.getenv("MCP_TRANSPORT", "stdio")
MCP_HOST = os.getenv("MCP_HOST", "127.0.0.1")
MCP_PORT = int(os.getenv("MCP_PORT", "8100"))

mcp = FastMCP(MCP_SERVER_NAME, host=MCP_HOST, port=MCP_PORT)

# Devices + telemetry
mcp.tool()(list_devices)
mcp.tool()(get_device_info)
mcp.tool()(get_latest_sensor)
mcp.tool()(get_sensor_history)

# Điều khiển thiết bị (khoá phẳng xmini/control) — thay cho send_command/auto_water/auto_light cũ
mcp.tool()(set_pump)
mcp.tool()(set_light)
mcp.tool()(set_mode)
mcp.tool()(show_message)
mcp.tool()(get_recent_commands)

# Ngưỡng auto của thiết bị (xmini/config) — thay cho moisture/light rule cũ
mcp.tool()(get_device_config)
mcp.tool()(set_device_config)
mcp.tool()(refresh_device_config)

if __name__ == "__main__":
    if MCP_TRANSPORT == "streamable-http":
        # Phục vụ tại http://<MCP_HOST>:<MCP_PORT>/mcp.
        # Bọc CORS để demo-dashboard (chạy ở origin khác) gọi được từ browser, và
        # expose Mcp-Session-Id để client đọc session id trả về sau initialize.
        import uvicorn
        from starlette.middleware.cors import CORSMiddleware

        app = mcp.streamable_http_app()
        app.add_middleware(
            CORSMiddleware,
            allow_origins=["*"],
            allow_methods=["*"],
            allow_headers=["*"],
            expose_headers=["Mcp-Session-Id"],
        )
        uvicorn.run(app, host=MCP_HOST, port=MCP_PORT)
    else:
        mcp.run()
