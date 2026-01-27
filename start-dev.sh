#!/bin/bash

# ==============================================================================
# NOM - Full Stack Development Environment Starter
# ==============================================================================
# Description:
# This script automates the setup and launch of the entire NOM application,
# including the .NET backend API and the Angular frontend UI. It performs the
# following steps:
#   1. Installs frontend npm dependencies.
#   2. Restores backend .NET dependencies.
#   3. Applies Entity Framework database migrations.
#   4. Starts both the backend API and frontend dev server concurrently.
#
# Usage:
# Run this script from the root directory of the project:
#   ./start-dev.sh
#
# To stop both servers, press Ctrl+C in the terminal where the script is running.
# ==============================================================================

# --- Cleanup Function ---
# This function is triggered when the script is terminated (e.g., with Ctrl+C).
# It ensures that any background processes (like the dotnet and ng servers) are killed.
cleanup() {
    echo ""
    echo "Shutting down development servers..."
    # Kill all child processes of this script
    pkill -P $$
    echo "All processes stopped. Exiting."
    exit 0
}

# Trap the EXIT signal to run the cleanup function
trap cleanup INT TERM EXIT

# --- Step 1: Frontend Setup ---
echo "FRONTEND: Navigating to nom-ui directory..."
cd nom-ui || { echo "ERROR: nom-ui directory not found."; exit 1; }

echo "FRONTEND: Installing npm dependencies..."
npm install

echo "FRONTEND: Setup complete. Navigating back to root..."
cd ..

# --- Step 2: Backend Setup ---
echo "BACKEND: Navigating to nom-api directory..."
cd nom-api || { echo "ERROR: nom-api directory not found."; exit 1; }

echo "BACKEND: Restoring .NET dependencies..."
dotnet restore

echo "BACKEND: Applying database migrations..."
dotnet ef database update

echo "BACKEND: Setup complete. Navigating back to root..."
cd ..

# --- Step 3: Launch Servers ---
echo "LAUNCH: Starting backend and frontend servers in the background..."

# Start the .NET backend API
(cd nom-api && dotnet run) &
API_PID=$!
echo "BACKEND API started with PID: $API_PID"

# Start the Angular frontend server
(cd nom-ui && ng serve --open) &
UI_PID=$!
echo "FRONTEND UI started with PID: $UI_PID"

echo ""
echo "========================================================"
echo "Both servers are running. Press Ctrl+C to shut down."
echo "========================================================"
echo ""

# Wait for any background jobs to complete. This keeps the script alive.
# The `trap` command will handle cleanup when this is interrupted.
wait