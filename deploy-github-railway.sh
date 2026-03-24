#!/bin/bash

# Deploy to Railway from GitHub
# This script helps set up Railway deployment from GitHub

set -e

echo "🚂 Deploy Plant Tree IoT Server to Railway via GitHub"
echo "===================================================="

# Check if Railway CLI is installed
if ! command -v railway &> /dev/null; then
    echo "📦 Installing Railway CLI..."
    npm install -g @railway/cli
fi

# Login to Railway
echo "🔐 Logging into Railway..."
railway login

# Initialize Railway project
echo "🚀 Initializing Railway project..."
railway init

# Set up environment variables
echo "⚙️  Setting up environment variables..."
railway variables set ASPNETCORE_ENVIRONMENT=Production
railway variables set ASPNETCORE_URLS=http://+:\$PORT

# For Railway, we'll use Railway's PostgreSQL
# But our app is designed for MongoDB, so we need to adjust
echo "🗄️  Note: Railway provides PostgreSQL by default."
echo "   Our app uses MongoDB. You can either:"
echo "   1. Use MongoDB Atlas (free tier)"
echo "   2. Modify app to use PostgreSQL"
echo ""

# Deploy
echo "🚀 Deploying to Railway..."
railway up

# Get the URL
echo "🌐 Getting deployment URL..."
railway domain

echo ""
echo "✅ Deployment completed!"
echo "📝 Next steps:"
echo "1. Update ESP32 code with the new Railway URL"
echo "2. Set up MongoDB Atlas if using MongoDB"
echo "3. Test the API endpoints"