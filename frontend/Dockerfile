# ================================================
# Multi-stage Dockerfile for Angular 20 Frontend
# ================================================
# Stage 1: Build the Angular application
# Stage 2: Serve with Nginx

# ==========================================
# STAGE 1: BUILD
# ==========================================
FROM node:20-alpine AS build
WORKDIR /app

# Copy package files and install dependencies
# This layer is cached unless package.json changes
COPY package*.json ./
RUN npm ci

# Copy all source files
COPY . ./

# Build Angular application for production
# Output goes to dist/dot-net-frontend
# IMPORTANT: Use --configuration production to use environment.prod.ts
RUN npm run build -- --configuration production

# ==========================================
# STAGE 2: SERVE WITH NGINX
# ==========================================
FROM nginx:alpine AS runtime

# Copy the built Angular app from build stage
COPY --from=build /app/dist/dot-net-frontend /usr/share/nginx/html

# Copy custom nginx configuration
COPY nginx.conf /etc/nginx/nginx.conf               

# Expose port 80 (standard HTTP port)
EXPOSE 80

# Start nginx server
CMD ["nginx", "-g", "daemon off;"]
