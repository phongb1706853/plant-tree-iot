#!/bin/bash

# Deploy to Render from GitHub
# This script helps set up Render deployment from GitHub

set -e

echo "🎨 Deploy Plant Tree IoT Server to Render via GitHub"
echo "=================================================="

echo "📋 To deploy to Render:"
echo ""
echo "1. Go to https://render.com"
echo "2. Sign up/Login with GitHub"
echo "3. Click 'New +' -> 'Web Service'"
echo "4. Connect your GitHub repository: plant-tree"
echo "5. Configure the service:"
echo ""

cat << 'EOF'
Service Settings:
- Name: plant-tree-iot-server
- Environment: Docker
- Build Command: docker build -t planttree-server .
- Start Command: docker run -p $PORT:80 planttree-server

Environment Variables:
- ASPNETCORE_ENVIRONMENT=Production
- ASPNETCORE_URLS=http://+:$PORT
- MONGODB_CONNECTION_STRING=[Your MongoDB Atlas connection string]

Free Instance Type:
- Free tier: 750 hours/month
EOF

echo ""
echo "6. For database, use MongoDB Atlas (free tier):"
echo "   - Go to https://cloud.mongodb.com"
echo "   - Create free cluster"
echo "   - Get connection string"
echo "   - Add to Render environment variables"
echo ""

echo "7. Deploy and get your URL"
echo ""
echo "8. Update ESP32 code with Render URL"
echo ""
echo "✅ That's it! Render will auto-deploy on Git pushes."