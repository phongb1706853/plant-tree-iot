#!/bin/bash

# Deploy to Vercel from GitHub
# This script helps set up Vercel deployment from GitHub

set -e

echo "▲ Deploy Plant Tree IoT Server to Vercel via GitHub"
echo "=================================================="

# Check if Vercel CLI is installed
if ! command -v vercel &> /dev/null; then
    echo "📦 Installing Vercel CLI..."
    npm install -g vercel
fi

# Login to Vercel
echo "🔐 Logging into Vercel..."
vercel login

# Initialize Vercel project
echo "🚀 Initializing Vercel project..."
vercel

# Vercel will ask for configuration:
echo ""
echo "📋 Vercel Configuration (when prompted):"
echo ""
echo "Which scope do you want to deploy to? [your-account]"
echo "Link to existing project? [y/N] N"
echo "What's your project's name? plant-tree-iot-server"
echo "In which directory is your code located? ./"
echo ""
echo "⚙️  Environment Variables to set in Vercel dashboard:"
echo "- ASPNETCORE_ENVIRONMENT=Production"
echo "- ASPNETCORE_URLS=https://\$VERCEL_URL"
echo "- MONGODB_CONNECTION_STRING=[Your MongoDB Atlas string]"
echo ""

# Create vercel.json for .NET configuration
cat > vercel.json << 'EOF'
{
  "version": 2,
  "builds": [
    {
      "src": "PlantTreeIoTServer/PlantTreeIoTServer.csproj",
      "use": "@vercel/dotnet"
    }
  ],
  "routes": [
    {
      "src": "/(.*)",
      "dest": "PlantTreeIoTServer/bin/Release/net10.0/publish/PlantTreeIoTServer.dll"
    }
  ],
  "env": {
    "ASPNETCORE_ENVIRONMENT": "Production",
    "ASPNETCORE_URLS": "https://$VERCEL_URL"
  }
}
EOF

echo "📄 Created vercel.json configuration file"
echo ""

# Deploy
echo "🚀 Deploying to Vercel..."
vercel --prod

echo ""
echo "✅ Deployment completed!"
echo "🌐 Your app will be available at the Vercel URL shown above"
echo ""
echo "📝 Next steps:"
echo "1. Set environment variables in Vercel dashboard"
echo "2. Update ESP32 code with Vercel URL"
echo "3. Test API endpoints"