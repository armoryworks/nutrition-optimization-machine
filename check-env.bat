@echo off
REM Environment Check Script for NOM Docker Setup
REM Validates that the environment is ready before starting services

echo ========================================
echo NOM Docker Environment Check
echo ========================================
echo.

set HAS_ERRORS=0

REM Check 1: Docker is installed and running
echo [1/5] Checking Docker...
docker --version >nul 2>&1
if %errorlevel% neq 0 (
    echo   [FAIL] Docker is not installed or not running
    echo          Download from: https://www.docker.com/products/docker-desktop/
    set HAS_ERRORS=1
) else (
    for /f "tokens=3" %%v in ('docker --version') do echo   [OK] Docker %%v installed
)
echo.

REM Check 2: Docker daemon is accessible
echo [2/5] Checking Docker daemon...
docker ps >nul 2>&1
if %errorlevel% neq 0 (
    echo   [FAIL] Docker daemon is not running
    echo          Start Docker Desktop and try again
    set HAS_ERRORS=1
) else (
    echo   [OK] Docker daemon is running
)
echo.

REM Check 3: Check for port conflicts
echo [3/5] Checking for port conflicts...
netstat -ano | findstr :5432 >nul 2>&1
if %errorlevel% equ 0 (
    echo   [WARN] Port 5432 is in use
    echo          PostgreSQL may fail to start
    echo          Check with: netstat -ano ^| findstr :5432
) else (
    echo   [OK] Port 5432 is available
)

netstat -ano | findstr :8080 >nul 2>&1
if %errorlevel% equ 0 (
    echo   [WARN] Port 8080 is in use
    echo          API may fail to start
    echo          Check with: netstat -ano ^| findstr :8080
) else (
    echo   [OK] Port 8080 is available
)

netstat -ano | findstr :4200 >nul 2>&1
if %errorlevel% equ 0 (
    echo   [WARN] Port 4200 is in use
    echo          UI may fail to start
    echo          Check with: netstat -ano ^| findstr :4200
) else (
    echo   [OK] Port 4200 is available
)
echo.

REM Check 4: Disk space
echo [4/5] Checking disk space...
docker system df >nul 2>&1
if %errorlevel% equ 0 (
    echo   [OK] Docker storage is accessible
    docker system df | findstr "RECLAIMABLE"
    echo          Run 'docker system prune' if space is low
) else (
    echo   [WARN] Could not check Docker disk usage
)
echo.

REM Check 5: Check for existing containers
echo [5/5] Checking for existing NOM containers...
docker ps -a --filter name=nom_ --format "{{.Names}}" >nul 2>&1
for /f %%i in ('docker ps -a --filter name=nom_ --format "{{.Names}}" 2^>nul ^| find /c /v ""') do set COUNT=%%i
if %COUNT% gtr 0 (
    echo   [INFO] Found %COUNT% existing NOM containers:
    docker ps -a --filter name=nom_ --format "   - {{.Names}} ({{.Status}})"
    echo          These will be reused/restarted
) else (
    echo   [OK] No existing NOM containers found
)
echo.

REM Summary
echo ========================================
echo Summary
echo ========================================
if %HAS_ERRORS% equ 0 (
    echo [SUCCESS] Environment is ready!
    echo.
    echo Next steps:
    echo   dev.bat start-full    - Start full containerized environment
    echo   dev.bat start         - Start databases only
    echo.
    exit /b 0
) else (
    echo [FAILED] Please fix the errors above before starting
    echo.
    exit /b 1
)
