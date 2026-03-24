#!/bin/bash

# Plant Tree IoT Docker Management Script
# Usage: ./docker-manage.sh [command]

COMMAND=${1:-"help"}

case $COMMAND in
    "start"|"up")
        echo "🚀 Starting Plant Tree IoT containers..."
        docker-compose up -d
        echo "✅ Containers started!"
        echo "🌐 API Server: http://localhost:8080"
        echo "📊 MongoDB: localhost:27017"
        ;;

    "stop"|"down")
        echo "🛑 Stopping Plant Tree IoT containers..."
        docker-compose down
        echo "✅ Containers stopped!"
        ;;

    "restart")
        echo "🔄 Restarting Plant Tree IoT containers..."
        docker-compose restart
        echo "✅ Containers restarted!"
        ;;

    "logs")
        SERVICE=${2:-"planttreeiotserver"}
        echo "📋 Showing logs for $SERVICE..."
        docker-compose logs -f $SERVICE
        ;;

    "build")
        echo "🔨 Building Plant Tree IoT containers..."
        docker-compose build --no-cache
        echo "✅ Build completed!"
        ;;

    "clean")
        echo "🧹 Cleaning up Docker resources..."
        docker-compose down -v
        docker system prune -f
        echo "✅ Cleanup completed!"
        ;;

    "status"|"ps")
        echo "📊 Container status:"
        docker-compose ps
        ;;

    "prod")
        echo "🏭 Starting in production mode with Nginx..."
        docker-compose -f docker-compose.yml -f docker-compose.prod.yml up -d
        echo "✅ Production containers started!"
        echo "🌐 API Server: http://localhost (via Nginx)"
        echo "🔒 HTTPS: https://localhost (if SSL configured)"
        ;;

    "help"|*)
        echo "Plant Tree IoT Docker Management Script"
        echo ""
        echo "Usage: ./docker-manage.sh [command]"
        echo ""
        echo "Commands:"
        echo "  start|up     Start containers in production mode"
        echo "  stop|down    Stop containers"
        echo "  restart      Restart containers"
        echo "  logs [svc]   Show logs (default: planttreeiotserver)"
        echo "  build        Rebuild containers"
        echo "  clean        Remove containers and volumes"
        echo "  status|ps    Show container status"
        echo "  dev          Start in development mode"
        echo "  prod         Start in production mode with Nginx"
        echo "  help         Show this help"
        ;;
esac