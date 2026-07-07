import os

API_BASE_URL = os.getenv("PLANT_API_URL", "http://localhost:5000")
REQUEST_TIMEOUT = 10
MCP_SERVER_NAME = "plant-tree-mcp"

# Service account của MCP — tạo trước bằng POST /api/auth/register.
# Đặt qua biến môi trường trên máy chạy MCP, đừng hard-code secret thật.
MCP_USER_EMAIL = os.getenv("PLANT_MCP_EMAIL", "mcp@plant-tree.local")
MCP_USER_PASSWORD = os.getenv("PLANT_MCP_PASSWORD", "change-me-mcp-password")
