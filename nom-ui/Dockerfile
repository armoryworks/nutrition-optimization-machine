# =================================================================
# Dockerfile for NOM Angular Frontend
# =================================================================

# Stage 1: Build
FROM node:20-alpine AS build
WORKDIR /app

# Copy package files and install dependencies
COPY package.json package-lock.json ./
RUN npm ci

# Copy source code and build
COPY . .
RUN npx ng build --configuration=production

# Stage 2: Serve with nginx
FROM nginx:alpine AS runtime

# Remove default nginx config
RUN rm /etc/nginx/conf.d/default.conf

# Copy nginx config template (uses ${API_URL} placeholder)
COPY nginx.conf /etc/nginx/conf.d/default.conf.template

# Copy built Angular app
COPY --from=build /app/dist/nom-ui/browser /usr/share/nginx/html

# Expose port
EXPOSE 80

# Health check
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD wget -qO- http://localhost/ || exit 1

# Substitute only API_URL (preserves nginx $uri, $host, etc.) then start nginx
CMD ["/bin/sh", "-c", "envsubst '$API_URL' < /etc/nginx/conf.d/default.conf.template > /etc/nginx/conf.d/default.conf && nginx -g 'daemon off;'"]
