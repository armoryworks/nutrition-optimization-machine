# NOM Production Deployment Guide

## Table of Contents
- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Production Configuration](#production-configuration)
- [Deployment Steps](#deployment-steps)
- [SSL/HTTPS Setup](#sslhttps-setup)
- [Database Management](#database-management)
- [Monitoring and Maintenance](#monitoring-and-maintenance)
- [Scaling and Performance](#scaling-and-performance)
- [Security Best Practices](#security-best-practices)
- [Troubleshooting](#troubleshooting)
- [Disaster Recovery](#disaster-recovery)

## Prerequisites

### System Requirements
- **OS**: Linux (Ubuntu 20.04+ or CentOS 8+ recommended)
- **CPU**: Minimum 2 cores, 4+ cores recommended
- **RAM**: Minimum 4GB, 8GB+ recommended
- **Storage**: 20GB+ available space
- **Docker**: Version 20.10+
- **Docker Compose**: Version 2.0+

### Software Dependencies
- Docker and Docker Compose installed
- PostgreSQL 16+ (or use included container)
- Redis 7+ (or use included container)
- Domain name configured
- SSL certificate (Let's Encrypt recommended)

## Quick Start

1. **Clone and configure:**

   ```bash
   git clone https://github.com/your-org/nutrition-optimization-machine.git
   cd nutrition-optimization-machine
   cp .env.example .env
   # Edit .env with your production values
   nano .env
   ```

2. **Build and deploy:**

   ```bash
   # Build Docker images
   docker-compose build
   
   # Start services in detached mode
   docker-compose up -d
   
   # Verify all services are running
   docker-compose ps
   ```

3. **Initialize database:**

   ```bash
   # Run database migrations
   docker-compose exec nom-api dotnet ef database update
   
   # Create initial admin user (optional)
   docker-compose exec postgres psql -U nom -d nom -f /workspace/_GrantInitialAdminClaims.sql
   ```

4. **Access:**
   - Frontend: https://your-domain.com
   - API: https://your-domain.com/api
   - Health: https://your-domain.com/health
   - Health Details: https://your-domain.com/health/detailed

## Production Configuration

### Essential Environment Variables

```bash
# Database Configuration
POSTGRES_DB=nom
POSTGRES_USER=nom
POSTGRES_PASSWORD=<strong-password>
POSTGRES_HOST=postgres
POSTGRES_PORT=5432

# JWT Configuration
JWT_SECRET_KEY=<64-character-random-string>
JWT_ISSUER=NOMApi
JWT_AUDIENCE=NOMAngular
JWT_EXPIRATION_MINUTES=1440

# Application Configuration
ASPNETCORE_ENVIRONMENT=Production
API_PORT=8080
UI_PORT=80
ALLOWED_ORIGINS=https://yourdomain.com

# Redis Configuration
REDIS_CONNECTION_STRING=redis:6379
REDIS_PASSWORD=<redis-password>

# Security Configuration
ENABLE_HTTPS_REDIRECT=true
ENABLE_HSTS=true
CORS_ENABLED=true
RATE_LIMIT_ENABLED=true
RATE_LIMIT_REQUESTS_PER_MINUTE=100
```

### Generate Secure Keys

```bash
# Generate JWT secret key
openssl rand -base64 64

# Generate database password
openssl rand -base64 32

# Generate Redis password
openssl rand -base64 32
```

## Deployment Steps

### 1. Initial Setup

```bash
# Create necessary directories
mkdir -p /opt/nom/{data,logs,backups,uploads}

# Set permissions
chmod -R 755 /opt/nom
chown -R 1000:1000 /opt/nom
```

### 2. Docker Deployment

```bash
# Pull latest images
docker-compose pull

# Build custom images
docker-compose build --no-cache

# Start services with health checks
docker-compose up -d --wait

# Verify health status
curl http://localhost:8080/health
```

### 3. Database Initialization

```bash
# Apply migrations
docker-compose exec nom-api dotnet ef database update

# Verify database connection
docker-compose exec postgres psql -U nom -d nom -c "SELECT version();"
```

## SSL/HTTPS Setup

### Using Let's Encrypt with Certbot

1. **Install Certbot:**
   ```bash
   sudo apt update
   sudo apt install certbot python3-certbot-nginx
   ```

2. **Obtain certificate:**
   ```bash
   sudo certbot --nginx -d yourdomain.com -d www.yourdomain.com
   ```

3. **Update nginx.conf for SSL:**
   ```nginx
   server {
       listen 443 ssl http2;
       server_name yourdomain.com;
       
       ssl_certificate /etc/letsencrypt/live/yourdomain.com/fullchain.pem;
       ssl_certificate_key /etc/letsencrypt/live/yourdomain.com/privkey.pem;
       ssl_protocols TLSv1.2 TLSv1.3;
       ssl_ciphers HIGH:!aNULL:!MD5;
       
       # ... rest of configuration
   }
   
   server {
       listen 80;
       server_name yourdomain.com;
       return 301 https://$server_name$request_uri;
   }
   ```

4. **Auto-renewal:**
   ```bash
   # Test renewal
   sudo certbot renew --dry-run
   
   # Add to crontab
   0 0,12 * * * certbot renew --quiet
   ```

## Database Management

### Backup Strategy

```bash
# Manual backup
docker exec nom_postgres pg_dump -U nom nom > backup_$(date +%Y%m%d_%H%M%S).sql

# Compressed backup
docker exec nom_postgres pg_dump -U nom nom | gzip > backup_$(date +%Y%m%d_%H%M%S).sql.gz

# Automated backup script
cat > /opt/nom/backup.sh << 'EOF'
#!/bin/bash
BACKUP_DIR="/opt/nom/backups"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
DB_NAME="nom"
DB_USER="nom"

# Create backup
docker exec nom_postgres pg_dump -U $DB_USER $DB_NAME | gzip > $BACKUP_DIR/backup_$TIMESTAMP.sql.gz

# Keep only last 30 days of backups
find $BACKUP_DIR -name "backup_*.sql.gz" -mtime +30 -delete

# Log backup completion
echo "Backup completed: backup_$TIMESTAMP.sql.gz"
EOF

chmod +x /opt/nom/backup.sh

# Add to crontab
0 2 * * * /opt/nom/backup.sh >> /opt/nom/logs/backup.log 2>&1
```

### Restore Process

```bash
# Restore from backup
gunzip < backup_20240101_020000.sql.gz | docker exec -i nom_postgres psql -U nom nom

# Restore specific backup
docker exec -i nom_postgres psql -U nom nom < backup.sql
```

## Monitoring and Maintenance

### Health Monitoring

```bash
# Check all services health
curl http://localhost:8080/health/detailed | jq

# Monitor specific service
watch -n 5 'curl -s http://localhost:8080/health | jq'

# Setup health check alerts
cat > /opt/nom/health-check.sh << 'EOF'
#!/bin/bash
HEALTH_URL="http://localhost:8080/health"
RESPONSE=$(curl -s -o /dev/null -w "%{http_code}" $HEALTH_URL)

if [ $RESPONSE -ne 200 ]; then
    echo "Health check failed with status: $RESPONSE"
    # Send alert (email, Slack, etc.)
fi
EOF

chmod +x /opt/nom/health-check.sh
```

### Log Management

```bash
# View all logs
docker-compose logs

# Follow specific service logs
docker-compose logs -f nom-api

# Export logs
docker-compose logs > nom_logs_$(date +%Y%m%d).txt

# Log rotation setup
cat > /etc/logrotate.d/nom << EOF
/opt/nom/logs/*.log {
    daily
    rotate 30
    compress
    delaycompress
    notifempty
    create 0644 nom nom
    sharedscripts
}
EOF
```

### Performance Monitoring

```bash
# Monitor resource usage
docker stats

# Check container resource limits
docker inspect nom_api | jq '.[0].HostConfig.Memory'

# Database performance
docker exec nom_postgres psql -U nom -d nom -c "SELECT * FROM pg_stat_activity;"
```

## Scaling and Performance

### Horizontal Scaling

```yaml
# docker-compose.yml for scaling
services:
  nom-api:
    deploy:
      replicas: 3
      resources:
        limits:
          cpus: '0.5'
          memory: 512M
```

```bash
# Scale API instances
docker-compose up -d --scale nom-api=3

# Load balancing with nginx
upstream nom_backend {
    least_conn;
    server nom-api-1:8080;
    server nom-api-2:8080;
    server nom-api-3:8080;
}
```

### Performance Optimization

```bash
# Database optimization
docker exec nom_postgres psql -U nom -d nom << EOF
-- Analyze tables
ANALYZE;

-- Reindex
REINDEX DATABASE nom;

-- Vacuum
VACUUM FULL ANALYZE;
EOF

# Redis optimization
docker exec nom_redis redis-cli CONFIG SET maxmemory 256mb
docker exec nom_redis redis-cli CONFIG SET maxmemory-policy allkeys-lru
```

## Security Best Practices

### 1. Network Security

```bash
# Create isolated network
docker network create --driver bridge --subnet=172.20.0.0/16 nom_secure

# Firewall rules
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable
```

### 2. Secret Management

```bash
# Use Docker secrets
echo "super_secret_password" | docker secret create postgres_password -
echo "jwt_secret_key" | docker secret create jwt_key -

# Reference in docker-compose
services:
  nom-api:
    secrets:
      - postgres_password
      - jwt_key
```

### 3. Security Scanning

```bash
# Scan Docker images
docker scan nom-api:latest
docker scan nom-ui:latest

# Security audit
npm audit --prefix nom-ui
dotnet list nom-api package --vulnerable
```

## Troubleshooting

### Common Issues and Solutions

1. **Container won't start**
   ```bash
   # Check logs
   docker-compose logs nom-api
   
   # Verify configuration
   docker-compose config
   
   # Reset containers
   docker-compose down -v
   docker-compose up -d
   ```

2. **Database connection issues**
   ```bash
   # Test connection
   docker exec nom_postgres pg_isready -U nom
   
   # Check network
   docker network inspect nom-network
   
   # Verify credentials
   docker exec nom_postgres psql -U nom -c "\l"
   ```

3. **High memory usage**
   ```bash
   # Check memory usage
   docker stats --no-stream
   
   # Limit container memory
   docker update --memory="1g" nom_api
   
   # Clear caches
   docker exec nom_redis redis-cli FLUSHALL
   ```

4. **API performance issues**
   ```bash
   # Check response times
   curl -w "@curl-format.txt" -o /dev/null -s http://localhost:8080/api/health
   
   # Monitor connections
   netstat -an | grep :8080 | wc -l
   
   # Check rate limiting
   curl -I http://localhost:8080/api/recipes
   ```

## Disaster Recovery

### Backup and Recovery Plan

1. **Regular Backups**
   - Database: Daily automated backups
   - Application data: Weekly full backups
   - Configuration: Version controlled in Git

2. **Recovery Time Objectives**
   - RTO: 4 hours
   - RPO: 24 hours

3. **Recovery Procedures**
   ```bash
   # Full system recovery
   ./scripts/disaster-recovery.sh
   
   # Database recovery
   docker-compose down
   docker volume rm nom_postgres_data
   docker-compose up -d postgres
   ./scripts/restore-database.sh latest
   docker-compose up -d
   ```

### Monitoring and Alerts

```bash
# Setup monitoring stack
docker-compose -f docker-compose.monitoring.yml up -d

# Prometheus configuration
global:
  scrape_interval: 15s
scrape_configs:
  - job_name: 'nom'
    static_configs:
      - targets: ['nom-api:8080']
```

## Support and Resources

- **Documentation**: [https://github.com/your-org/nom/docs](https://github.com/your-org/nom/docs)
- **Issues**: [https://github.com/your-org/nom/issues](https://github.com/your-org/nom/issues)
- **Community**: Discord/Slack channel
- **Emergency Support**: support@nom.com

---

Last Updated: January 2025
Version: 1.0.0
