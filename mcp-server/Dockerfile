FROM python:3.12-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY . .

# Chạy dạng HTTP (không phải stdio) để AI server ở stack khác gọi qua http://host.docker.internal:8100/mcp
ENV MCP_TRANSPORT=streamable-http \
    MCP_HOST=0.0.0.0 \
    MCP_PORT=8100 \
    PYTHONUNBUFFERED=1

EXPOSE 8100

CMD ["python", "server.py"]
