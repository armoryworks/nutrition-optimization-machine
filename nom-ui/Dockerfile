# =================================================================
# Dockerfile for NOM Angular Frontend
# =================================================================

# Stage 1: Build
FROM node:20-alpine AS build
WORKDIR /app

# Copy package files
COPY package*.json ./
COPY angular.json ./
COPY tsconfig*.json ./

# Install dependencies
RUN npm ci

# Copy source code
COPY src ./src/
COPY public ./public/

# Build for production
RUN npm run build -- --configuration production

# Stage 2: Production
FROM nginx:alpine
COPY --from=build /app/dist/nom-ui/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/nginx.conf

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]