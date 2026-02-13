@echo off
REM Development Helper Script for NOM (Windows)
REM Provides easy commands for managing development environment

setlocal enabledelayedexpansion

if "%1"=="" goto help
if "%1"=="help" goto help
if "%1"=="start" goto start_dev
if "%1"=="start-tools" goto start_dev_tools
if "%1"=="start-full" goto start_dev_full
if "%1"=="stop" goto stop_dev
if "%1"=="stop-full" goto stop_dev_full
if "%1"=="restart" goto restart_dev
if "%1"=="restart-full" goto restart_dev_full
if "%1"=="logs" goto show_logs
if "%1"=="logs-full" goto show_logs_full
if "%1"=="clean" goto clean_dev
if "%1"=="clean-full" goto clean_dev_full
if "%1"=="db-shell" goto db_shell
if "%1"=="db-reset" goto db_reset
if "%1"=="test-start" goto start_test
if "%1"=="test-stop" goto stop_test
if "%1"=="test-run" goto run_tests
if "%1"=="test-clean" goto clean_test
if "%1"=="status" goto show_status

echo Unknown command: %1
echo.
goto help

:help
echo NOM Development Helper
echo.
echo Usage: dev.bat [command]
echo.
echo Commands:
echo   HYBRID MODE (databases only, run API/UI natively):
echo     start           Start development databases (PostgreSQL + Redis)
echo     start-tools     Start databases + pgAdmin
echo     stop            Stop development databases
echo     restart         Restart development databases
echo     logs            Show database logs
echo     clean           Remove all development containers and volumes
echo.
echo   FULLY CONTAINERIZED MODE (everything in Docker):
echo     start-full      Start full stack with hot reload (API + UI + DB + Redis)
echo     stop-full       Stop full stack
echo     restart-full    Restart full stack
echo     logs-full       Show all container logs
echo     clean-full      Clean full stack containers and volumes
echo.
echo   DATABASE MANAGEMENT:
echo     db-shell        Open PostgreSQL shell
echo     db-reset        Reset development database (WARNING: deletes all data)
echo.
echo   TESTING:
echo     test-start      Start test environment
echo     test-stop       Stop test environment
echo     test-run        Run Cypress e2e tests
echo     test-clean      Clean test environment
echo.
echo   STATUS:
echo     status          Show running containers
echo     help            Show this help message
goto end

:start_dev
echo Starting development databases...
docker-compose -f docker-compose.dev.yml up -d
timeout /t 3 /nobreak >nul
docker-compose -f docker-compose.dev.yml ps
echo.
echo Development environment ready!
echo PostgreSQL: localhost:5432 (user: nom, db: nom_dev)
echo Redis: localhost:6379
goto end

:start_dev_tools
echo Starting development databases + tools...
docker-compose -f docker-compose.dev.yml --profile tools up -d
timeout /t 3 /nobreak >nul
docker-compose -f docker-compose.dev.yml ps
echo.
echo Development environment ready!
echo PostgreSQL: localhost:5432 (user: nom, db: nom_dev)
echo Redis: localhost:6379
echo pgAdmin: http://localhost:5050 (admin@nom.local / admin)
goto end

:stop_dev
echo Stopping development databases...
docker-compose -f docker-compose.dev.yml down
echo Development environment stopped
goto end

:restart_dev
call :stop_dev
call :start_dev
goto end

:show_logs
docker-compose -f docker-compose.dev.yml logs -f
goto end

:clean_dev
echo WARNING: This will delete all development data!
set /p confirm="Are you sure? (y/N): "
if /i "%confirm%"=="y" (
    echo Cleaning development environment...
    docker-compose -f docker-compose.dev.yml down -v
    echo Development environment cleaned
)
goto end

:db_shell
echo Opening PostgreSQL shell...
docker exec -it nom_postgres_dev psql -U nom -d nom_dev
goto end

:db_reset
echo WARNING: This will delete all data in the development database!
set /p confirm="Are you sure? (y/N): "
if /i "%confirm%"=="y" (
    echo Resetting database...
    docker exec nom_postgres_dev psql -U nom -d nom_dev -c "DROP SCHEMA public CASCADE; CREATE SCHEMA public;"
    echo Database reset
    echo Run migrations from your API to recreate schema
)
goto end

:start_test
echo Starting test environment...
docker-compose -f docker-compose.test.yml up -d
timeout /t 5 /nobreak >nul
docker-compose -f docker-compose.test.yml ps
echo Test environment ready!
goto end

:stop_test
echo Stopping test environment...
docker-compose -f docker-compose.test.yml down
echo Test environment stopped
goto end

:run_tests
echo Running Cypress tests...
cd nom-test
call npm run test:e2e
cd ..
goto end

:clean_test
echo Cleaning test environment...
docker-compose -f docker-compose.test.yml down -v
echo Test environment cleaned
goto end

:start_dev_full
echo Starting full containerized development stack...
docker-compose -f docker-compose.dev.full.yml up -d
timeout /t 5 /nobreak >nul
docker-compose -f docker-compose.dev.full.yml ps
echo.
echo Full development environment ready!
echo UI: http://localhost:4200
echo API: http://localhost:8080
echo PostgreSQL: localhost:5432
echo Redis: localhost:6379
echo.
echo Code changes will auto-reload!
goto end

:stop_dev_full
echo Stopping full development stack...
docker-compose -f docker-compose.dev.full.yml down
echo Full development environment stopped
goto end

:restart_dev_full
call :stop_dev_full
call :start_dev_full
goto end

:show_logs_full
docker-compose -f docker-compose.dev.full.yml logs -f
goto end

:clean_dev_full
echo WARNING: This will delete all development data and containers!
set /p confirm="Are you sure? (y/N): "
if /i "%confirm%"=="y" (
    echo Cleaning full development environment...
    docker-compose -f docker-compose.dev.full.yml down -v
    echo Full development environment cleaned
)
goto end

:show_status
echo Hybrid Development Environment (databases only):
docker-compose -f docker-compose.dev.yml ps
echo.
echo Full Development Environment (everything):
docker-compose -f docker-compose.dev.full.yml ps 2>nul || echo Not running
echo.
echo Test Environment:
docker-compose -f docker-compose.test.yml ps 2>nul || echo Not running
goto end

:end
endlocal
