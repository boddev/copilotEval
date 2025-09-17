#!/bin/bash

# CopilotEval Development Startup Script
# This script starts all services needed for local development

set -e  # Exit on any error

echo "🚀 Starting CopilotEval Development Environment..."

# Function to check if a port is in use
check_port() {
    local port=$1
    if lsof -Pi :$port -sTCP:LISTEN -t >/dev/null ; then
        echo "⚠️  Port $port is already in use"
        return 1
    fi
    return 0
}

# Function to start a service in background
start_service() {
    local name=$1
    local command=$2
    local directory=$3
    local log_file="logs/${name}.log"
    
    echo "📦 Starting $name..."
    mkdir -p logs
    
    if [ -n "$directory" ]; then
        cd "$directory"
    fi
    
    # Start the service in background and redirect output to log file
    eval "$command" > "../$log_file" 2>&1 &
    local pid=$!
    echo $pid > "../logs/${name}.pid"
    
    echo "✅ $name started (PID: $pid, Log: $log_file)"
    
    if [ -n "$directory" ]; then
        cd - > /dev/null
    fi
}

# Function to stop all services
cleanup() {
    echo "🛑 Stopping all services..."
    
    if [ -d "logs" ]; then
        for pid_file in logs/*.pid; do
            if [ -f "$pid_file" ]; then
                local pid=$(cat "$pid_file")
                local service=$(basename "$pid_file" .pid)
                echo "🔪 Stopping $service (PID: $pid)"
                kill $pid 2>/dev/null || true
                rm "$pid_file"
            fi
        done
    fi
    
    echo "✅ All services stopped"
    exit 0
}

# Set up cleanup on script exit
trap cleanup SIGINT SIGTERM EXIT

# Check prerequisites
echo "🔍 Checking prerequisites..."

# Check .NET
if ! command -v dotnet &> /dev/null; then
    echo "❌ .NET SDK not found. Please install .NET 9 SDK"
    exit 1
fi

# Check Node.js
if ! command -v node &> /dev/null; then
    echo "❌ Node.js not found. Please install Node.js 18+"
    exit 1
fi

# Check npm
if ! command -v npm &> /dev/null; then
    echo "❌ npm not found. Please install npm"
    exit 1
fi

echo "✅ Prerequisites check passed"

# Check ports
echo "🔍 Checking ports..."
check_port 5000 || (echo "❌ Backend port 5000 is in use" && exit 1)
check_port 3000 || echo "⚠️  Frontend port 3000 is in use, will try 5173"

# Ensure we're in the project root
if [ ! -f "copilotEval.sln" ]; then
    echo "❌ Please run this script from the project root directory"
    exit 1
fi

# Build the solution first
echo "🏗️  Building solution..."
dotnet build --configuration Debug
if [ $? -ne 0 ]; then
    echo "❌ Build failed"
    exit 1
fi

# Install frontend dependencies if needed
echo "📦 Installing frontend dependencies..."
cd frontend
if [ ! -d "node_modules" ] || [ package.json -nt node_modules ]; then
    npm install
fi
cd ..

# Create logs directory
mkdir -p logs

# Start backend API
start_service "backend" "dotnet watch run --no-hot-reload --urls http://localhost:5000" "backend"

# Wait a moment for backend to start
sleep 3

# Start frontend
start_service "frontend" "npm run dev -- --host 0.0.0.0 --port 3000" "frontend"

# Wait a moment for frontend to start
sleep 3

# Check if services are responding
echo "🔍 Verifying services..."

# Check backend health
if curl -s http://localhost:5000/api/health > /dev/null; then
    echo "✅ Backend API is responding"
else
    echo "⚠️  Backend API is not responding yet"
fi

# Display service status
echo ""
echo "🎉 Development environment started successfully!"
echo ""
echo "📍 Services:"
echo "   🔧 Backend API:  http://localhost:5000"
echo "   📊 Swagger UI:   http://localhost:5000/swagger"
echo "   🌐 Frontend:     http://localhost:3000"
echo ""
echo "📝 Logs:"
echo "   📋 Backend:      logs/backend.log"
echo "   📋 Frontend:     logs/frontend.log"
echo ""
echo "🔧 Commands:"
echo "   📊 View logs:    tail -f logs/backend.log"
echo "   🔄 Restart:      pkill -f 'dotnet watch' && ./scripts/start-dev.sh"
echo "   🛑 Stop:         Ctrl+C"
echo ""
echo "⏳ Services are starting... Frontend may take a moment to be available."
echo "💡 Press Ctrl+C to stop all services"

# Keep script running and show logs
echo "📋 Showing backend logs (Ctrl+C to stop):"
tail -f logs/backend.log