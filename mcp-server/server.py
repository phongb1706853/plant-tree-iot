import os
from mcp.server.fastmcp import FastMCP
from config import MCP_SERVER_NAME
from tools.devices import list_devices, get_device_info
from tools.sensors import get_latest_sensor, get_sensor_history
from tools.control import send_command, get_pending_commands, auto_water, auto_light
from tools.rules import get_moisture_rule, set_moisture_rule, get_light_rule, set_light_rule

# Transport:
#   - "stdio" (mặc định): dùng khi client tự spawn MCP làm subprocess.
#   - "streamable-http": expose HTTP tại http://<host>:<port>/mcp — dùng cho
#     AI server (tree-grow-helper) kết nối qua mạng.
# Port mặc định 8100 để KHÔNG đụng .NET API (thường 8000/80).
MCP_TRANSPORT = os.getenv("MCP_TRANSPORT", "stdio")
MCP_HOST = os.getenv("MCP_HOST", "127.0.0.1")
MCP_PORT = int(os.getenv("MCP_PORT", "8100"))

mcp = FastMCP(MCP_SERVER_NAME, host=MCP_HOST, port=MCP_PORT)

mcp.tool()(list_devices)
mcp.tool()(get_device_info)
mcp.tool()(get_latest_sensor)
mcp.tool()(get_sensor_history)
mcp.tool()(send_command)
mcp.tool()(get_pending_commands)
mcp.tool()(auto_water)
mcp.tool()(auto_light)
mcp.tool()(get_moisture_rule)
mcp.tool()(set_moisture_rule)
mcp.tool()(get_light_rule)
mcp.tool()(set_light_rule)

if __name__ == "__main__":
    if MCP_TRANSPORT == "streamable-http":
        # Phục vụ tại http://<MCP_HOST>:<MCP_PORT>/mcp
        mcp.run(transport="streamable-http")
    else:
        mcp.run()
