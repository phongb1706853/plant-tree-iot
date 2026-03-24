#!/bin/bash

# Railway Deployment Script for Plant Tree IoT
# Railway offers a generous free tier: 512MB RAM, 1GB storage, PostgreSQL included

echo "🚂 Setting up Plant Tree IoT on Railway..."
echo "=========================================="

# Check if Railway CLI is installed
if ! command -v railway &> /dev/null; then
    echo "Installing Railway CLI..."
    curl -fsSL https://railway.app/install.sh | sh
fi

# Login to Railway
echo "🔐 Logging into Railway..."
railway login

# Create new project
echo "📁 Creating Railway project..."
railway init plant-tree-iot --name "Plant Tree IoT Server"

# Add environment variables
echo "⚙️  Configuring environment variables..."
railway variables set ASPNETCORE_ENVIRONMENT=Production
railway variables set ASPNETCORE_URLS=http://0.0.0.0:$PORT

# Railway automatically provides PostgreSQL
# But we need MongoDB, so we'll use MongoDB Atlas free tier
echo "🗄️  MongoDB Setup:"
echo "Railway doesn't include MongoDB, but you can use:"
echo "1. MongoDB Atlas Free: https://www.mongodb.com/atlas"
echo "2. Or use Railway's PostgreSQL with a MongoDB adapter"
echo ""
echo "For now, using MongoDB Atlas connection string..."
echo "Please create a free MongoDB Atlas account and get connection string"

# Build and deploy
echo "🚀 Building and deploying..."
railway up

# Get the URL
echo "🌐 Getting deployment URL..."
sleep 10
railway domain

echo ""
echo "✅ Railway deployment completed!"
echo ""
echo "📋 Next steps:"
echo "1. Set up MongoDB Atlas and update connection string"
echo "2. Update ESP32 code with Railway URL"
echo "3. Test the API endpoints"
echo ""
echo "💰 Railway Free Tier:"
echo "   - 512MB RAM"
echo "   - 1GB storage"
echo "   - PostgreSQL included"
echo "   - Custom domains"
echo "   - 14 days inactivity timeout"