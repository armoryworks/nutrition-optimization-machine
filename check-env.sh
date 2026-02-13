#!/bin/bash

# Environment Check Script for NOM Docker Setup
# Validates that the environment is ready before starting services

set -e

echo "========================================"
echo "NOM Docker Environment Check"
echo "========================================"
echo ""

HAS_ERRORS=0

# Check 1: Docker is installed and running
echo "[1/5] Checking Docker..."
if command -v docker &> /dev/null; then
    DOCKER_VERSION=$(docker --version | awk '{print $3}' | sed 's/,//')
    echo "  [OK] Docker $DOCKER_VERSION installed"
else
    echo "  [FAIL] Docker is not installed"
    echo "         Download from: https://www.docker.com/products/docker-desktop/"
    HAS_ERRORS=1
fi
echo ""

# Check 2: Docker daemon is accessible
echo "[2/5] Checking Docker daemon..."
if docker ps &> /dev/null; then
    echo "  [OK] Docker daemon is running"
else
    echo "  [FAIL] Docker daemon is not running"
    echo "         Start Docker Desktop and try again"
    HAS_ERRORS=1
fi
echo ""

# Check 3: Check for port conflicts
echo "[3/5] Checking for port conflicts..."

check_port() {
    PORT=$1
    if lsof -Pi :$PORT -sTCP:LISTEN -t &> /dev/null; then
        echo "  [WARN] Port $PORT is in use"
        echo "         Service may fail to start"
        return 1
    else
        echo "  [OK] Port $PORT is available"
        return 0
    fi
}

check_port 5432
check_port 8080
check_port 4200
echo ""

# Check 4: Disk space
echo "[4/5] Checking disk space..."
if docker system df &> /dev/null; then
    echo "  [OK] Docker storage is accessible"
    docker system df | grep -E "TYPE|RECLAIMABLE" | head -2
    echo "         Run 'docker system prune' if space is low"
else
    echo "  [WARN] Could not check Docker disk usage"
fi
echo ""

# Check 5: Check for existing containers
echo "[5/5] Checking for existing NOM containers..."
COUNT=$(docker ps -a --filter name=nom_ --format "{{.Names}}" 2>/dev/null | wc -l)
if [ "$COUNT" -gt 0 ]; then
    echo "  [INFO] Found $COUNT existing NOM containers:"
    docker ps -a --filter name=nom_ --format "   - {{.Names}} ({{.Status}})"
    echo "         These will be reused/restarted"
else
    echo "  [OK] No existing NOM containers found"
fi
echo ""

# Summary
echo "========================================"
echo "Summary"
echo "========================================"
if [ $HAS_ERRORS -eq 0 ]; then
    echo "[SUCCESS] Environment is ready!"
    echo ""
    echo "Next steps:"
    echo "  ./dev.sh start-full    - Start full containerized environment"
    echo "  ./dev.sh start         - Start databases only"
    echo ""
    exit 0
else
    echo "[FAILED] Please fix the errors above before starting"
    echo ""
    exit 1
fi
