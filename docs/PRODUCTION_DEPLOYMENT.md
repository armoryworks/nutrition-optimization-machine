# NOM Production Deployment Guide

## Prerequisites

- Docker and Docker Compose installed
- PostgreSQL 16+ (or use included container)
- Redis 7+ (or use included container)
- Domain name and SSL certificate (for production)

## Quick Start

1. **Clone and configure:**

   ```bash
   git clone https://github.com/your-org/nutrition-optimization-machine.git
   cd nutrition-optimization-machine
   cp .env.example .env
   # Edit .env with your production values
   ```

2. **Deploy:**

   ```bash
   docker-compose up -d
   ```

3. **Access:**
   - Frontend: http://your-domain.com
   - API: http://your-domain.com/api
   - Health: http://your-domain.com/health

## Production Configuration

### Environment Variables

| Variable                 | Description       | Example                  |
| ------------------------ | ----------------- | ------------------------ |
| `POSTGRES_PASSWORD`      | Database password | `secure_password_123`    |
| `JWT_SECRET_KEY`         | JWT signing key   | `64_char_random_string`  |
| `ALLOWED_ORIGINS`        | CORS origins      | `https://yourdomain.com` |
| `ASPNETCORE_ENVIRONMENT` | Environment       | `Production`             |

### SSL/HTTPS Setup

1. **Obtain SSL certificate** (Let's Encrypt recommended)
2. **Update nginx.conf** with SSL configuration
3. **Redirect HTTP to HTTPS**

### Database Backup

```bash
# Create backup script
docker exec nom_postgres pg_dump -U nom nom > backup_$(date +%Y%m%d_%H%M%S).sql

# Automated backup with cron
0 2 * * * docker exec nom_postgres pg_dump -U nom nom > /backups/nom_$(date +\%Y\%m\%d).sql
```

## Monitoring and Maintenance

### Health Checks

- **Application Health**: `/health`
- **Database Health**: `/health` (includes DB check)
- **Redis Health**: `/health` (includes Redis check)

### Logs

```bash
# View application logs
docker-compose logs -f nom-api

# View nginx logs
docker-compose logs -f nom-ui

# View database logs
docker-compose logs -f postgres
```

### Updates

```bash
# Pull latest changes
git pull origin main

# Rebuild and restart
docker-compose down
docker-compose build --no-cache
docker-compose up -d
```

## Troubleshooting

### Common Issues

1. **Database connection failed**

   - Check PostgreSQL container status
   - Verify connection string in .env

2. **Frontend not loading**

   - Check nginx container status
   - Verify nginx configuration

3. **API endpoints failing**
   - Check API container status
   - Verify health check endpoint

### Performance Tuning

1. **Database optimization**

   - Enable connection pooling
   - Configure appropriate memory limits

2. **Caching**

   - Redis for session storage
   - Response caching for static content

3. **Load balancing**
   - Multiple API instances
   - Nginx upstream configuration
