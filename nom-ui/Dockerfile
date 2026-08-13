# =================================================================
# Dockerfile for NOM Angular Frontend
# =================================================================

# Stage 1: Build
FROM node:20-alpine AS build
WORKDIR /app

# Copy package files and install dependencies
COPY package.json package-lock.json ./
RUN npm ci

# Copy source code and build the app plus the embeddable login element
# (a small web-component bundle the marketing site mounts in its static page)
COPY . .
RUN npx ng build nom-ui --configuration=production \
    && npx ng build nom-login-element --configuration=production

# Stage 2: Serve with nginx
FROM nginx:alpine AS runtime

# Remove default nginx config
RUN rm /etc/nginx/conf.d/default.conf

# Copy nginx config template (uses ${API_URL} placeholder)
COPY nginx.conf /etc/nginx/conf.d/default.conf.template

# Copy built Angular app and the login element bundle
COPY --from=build /app/dist/nom-ui/browser /usr/share/nginx/html
COPY --from=build /app/dist/nom-login-element/browser /usr/share/nginx/html/elements

# Expose port
EXPOSE 80

# Health check — probe 127.0.0.1 explicitly: in-container "localhost" resolves
# to ::1 first, and nginx here only listens on IPv4, so the check would always
# report unhealthy while the site serves fine.
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD wget -qO- http://127.0.0.1/ || exit 1

# Substitute only API_URL (preserves nginx $uri, $host, etc.) then start nginx
CMD ["/bin/sh", "-c", "envsubst '$API_URL' < /etc/nginx/conf.d/default.conf.template > /etc/nginx/conf.d/default.conf && nginx -g 'daemon off;'"]
