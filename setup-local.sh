#!/bin/bash

# Local Development Setup (Completely Free)
# This script sets up everything locally using Docker

echo "🏠 Setting up Plant Tree IoT for Local Development..."
echo "===================================================="

# Check if Docker is installed
if ! command -v docker &> /dev/null; then
    echo "❌ Docker is not installed. Please install Docker first:"
    echo "https://docs.docker.com/get-docker/"
    exit 1
fi

# Check if Docker Compose is available
if ! command -v docker-compose &> /dev/null && ! docker compose version &> /dev/null; then
    echo "❌ Docker Compose is not available. Please install Docker Compose."
    exit 1
fi

# Create .env file for local development
echo "⚙️  Creating environment configuration..."
cat > .env << EOF
# Local Development Configuration
COMPOSE_PROJECT_NAME=planttree-local
HTTP_PORT=5000
HTTPS_PORT=5001
MONGO_PORT=27017
MONGO_INITDB_DATABASE=PlantTreeIoT
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://+:80;https://+:443
EOF

# Start services
echo "🐳 Starting Docker services..."
if command -v docker-compose &> /dev/null; then
    docker-compose up -d
else
    docker compose up -d
fi

# Wait for services to start
echo "⏳ Waiting for services to start..."
sleep 10

# Check if services are running
echo "🔍 Checking service status..."
if command -v docker-compose &> /dev/null; then
    docker-compose ps
else
    docker compose ps
fi

# Test the API
echo "🧪 Testing API endpoints..."
sleep 5

# Test health check
echo "Testing API availability..."
if curl -s http://localhost:5000/api/devices > /dev/null 2>&1; then
    echo "✅ API is running on http://localhost:5000"
else
    echo "⚠️  API might still be starting up..."
    echo "   Check logs with: docker-compose logs -f planttreeiotserver"
fi

# Test MongoDB
echo "Testing MongoDB connection..."
if docker exec planttree-mongodb-1 mongo --eval "db.stats()" > /dev/null 2>&1; then
    echo "✅ MongoDB is running on localhost:27017"
else
    echo "⚠️  MongoDB might still be starting up..."
fi

echo ""
echo "🎉 Local development environment is ready!"
echo ""
echo "🌐 Access points:"
echo "   - API Server: http://localhost:5000"
echo "   - MongoDB: localhost:27017"
echo "   - Swagger UI: http://localhost:5000/swagger"
echo ""
echo "🛠️  Management commands:"
echo "   - View logs: docker-compose logs -f"
echo "   - Stop services: docker-compose down"
echo "   - Restart: docker-compose restart"
echo "   - Rebuild: docker-compose build --no-cache"
echo ""
echo "📝 Next steps:"
echo "1. Test API with: curl http://localhost:5000/api/devices"
echo "2. Update ESP32 code to use: http://your-local-ip:5000"
echo "3. View database: use MongoDB Compass to connect to localhost:27017"
echo ""
echo "💰 This setup is completely FREE!"
echo "   - Docker Desktop (free for personal use)"
echo "   - MongoDB (free community edition)"
echo "   - .NET SDK (free)"
echo "   - Your development machine"