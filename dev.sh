#!/bin/bash

# Development Helper Script for NOM
# Provides easy commands for managing development environment

set -e

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

print_help() {
    echo -e "${BLUE}NOM Development Helper${NC}"
    echo ""
    echo "Usage: ./dev.sh [command]"
    echo ""
    echo "Commands:"
    echo "  start           Start development databases (PostgreSQL + Redis)"
    echo "  start-tools     Start databases + pgAdmin"
    echo "  stop            Stop development databases"
    echo "  restart         Restart development databases"
    echo "  logs            Show database logs"
    echo "  clean           Remove all development containers and volumes"
    echo "  db-shell        Open PostgreSQL shell"
    echo "  db-reset        Reset development database (WARNING: deletes all data)"
    echo "  test-start      Start test environment"
    echo "  test-stop       Stop test environment"
    echo "  test-run        Run Cypress e2e tests"
    echo "  test-clean      Clean test environment"
    echo "  status          Show running containers"
    echo "  help            Show this help message"
}

start_dev() {
    echo -e "${GREEN}Starting development databases...${NC}"
    docker-compose -f docker-compose.dev.yml up -d
    echo -e "${GREEN}Waiting for PostgreSQL to be ready...${NC}"
    sleep 3
    docker-compose -f docker-compose.dev.yml ps
    echo -e "${GREEN}✓ Development environment ready!${NC}"
    echo -e "${YELLOW}PostgreSQL:${NC} localhost:5432 (user: nom, db: nom_dev)"
    echo -e "${YELLOW}Redis:${NC} localhost:6379"
}

start_dev_tools() {
    echo -e "${GREEN}Starting development databases + tools...${NC}"
    docker-compose -f docker-compose.dev.yml --profile tools up -d
    echo -e "${GREEN}Waiting for services to be ready...${NC}"
    sleep 3
    docker-compose -f docker-compose.dev.yml ps
    echo -e "${GREEN}✓ Development environment ready!${NC}"
    echo -e "${YELLOW}PostgreSQL:${NC} localhost:5432 (user: nom, db: nom_dev)"
    echo -e "${YELLOW}Redis:${NC} localhost:6379"
    echo -e "${YELLOW}pgAdmin:${NC} http://localhost:5050 (admin@nom.local / admin)"
}

stop_dev() {
    echo -e "${YELLOW}Stopping development databases...${NC}"
    docker-compose -f docker-compose.dev.yml down
    echo -e "${GREEN}✓ Development environment stopped${NC}"
}

restart_dev() {
    stop_dev
    start_dev
}

show_logs() {
    docker-compose -f docker-compose.dev.yml logs -f
}

clean_dev() {
    echo -e "${RED}WARNING: This will delete all development data!${NC}"
    read -p "Are you sure? (y/N) " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        echo -e "${YELLOW}Cleaning development environment...${NC}"
        docker-compose -f docker-compose.dev.yml down -v
        echo -e "${GREEN}✓ Development environment cleaned${NC}"
    fi
}

db_shell() {
    echo -e "${BLUE}Opening PostgreSQL shell...${NC}"
    docker exec -it nom_postgres_dev psql -U nom -d nom_dev
}

db_reset() {
    echo -e "${RED}WARNING: This will delete all data in the development database!${NC}"
    read -p "Are you sure? (y/N) " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        echo -e "${YELLOW}Resetting database...${NC}"
        docker exec nom_postgres_dev psql -U nom -d nom_dev -c "DROP SCHEMA public CASCADE; CREATE SCHEMA public;"
        echo -e "${GREEN}✓ Database reset${NC}"
        echo -e "${YELLOW}Run ./db/apply.sh to recreate the schema${NC}"
    fi
}

start_test() {
    echo -e "${GREEN}Starting test environment...${NC}"
    docker-compose -f docker-compose.test.yml up -d
    echo -e "${GREEN}Waiting for services to be ready...${NC}"
    sleep 5
    docker-compose -f docker-compose.test.yml ps
    echo -e "${GREEN}✓ Test environment ready!${NC}"
}

stop_test() {
    echo -e "${YELLOW}Stopping test environment...${NC}"
    docker-compose -f docker-compose.test.yml down
    echo -e "${GREEN}✓ Test environment stopped${NC}"
}

run_tests() {
    echo -e "${GREEN}Running Cypress tests...${NC}"
    cd nom-test
    npm run test:e2e
    cd ..
}

clean_test() {
    echo -e "${YELLOW}Cleaning test environment...${NC}"
    docker-compose -f docker-compose.test.yml down -v
    echo -e "${GREEN}✓ Test environment cleaned${NC}"
}

show_status() {
    echo -e "${BLUE}Development Environment:${NC}"
    docker-compose -f docker-compose.dev.yml ps
    echo ""
    echo -e "${BLUE}Test Environment:${NC}"
    docker-compose -f docker-compose.test.yml ps 2>/dev/null || echo "Not running"
}

# Main script logic
case "${1}" in
    start)
        start_dev
        ;;
    start-tools)
        start_dev_tools
        ;;
    stop)
        stop_dev
        ;;
    restart)
        restart_dev
        ;;
    logs)
        show_logs
        ;;
    clean)
        clean_dev
        ;;
    db-shell)
        db_shell
        ;;
    db-reset)
        db_reset
        ;;
    test-start)
        start_test
        ;;
    test-stop)
        stop_test
        ;;
    test-run)
        run_tests
        ;;
    test-clean)
        clean_test
        ;;
    status)
        show_status
        ;;
    help|--help|-h|"")
        print_help
        ;;
    *)
        echo -e "${RED}Unknown command: ${1}${NC}"
        echo ""
        print_help
        exit 1
        ;;
esac
