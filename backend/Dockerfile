# ============================================
# Multi-stage Dockerfile for .NET 6 Web API
# ============================================
# Stage 1: Build the application
# Stage 2: Create minimal runtime image
# This approach reduces image size and improves security

# ==========================================
# STAGE 1: BUILD
# ==========================================
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /app

# Copy project file and restore dependencies
# This layer is cached unless the .csproj file changes
COPY src/*.csproj ./
RUN dotnet restore

# Copy all source files
COPY src/ ./

# Build the application in Release mode
# Output goes to /app/out directory
RUN dotnet publish -c Release -o out

# ==========================================
# STAGE 2: RUNTIME
# ==========================================
FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS runtime
WORKDIR /app

# Copy the built application from the build stage
COPY --from=build /app/out .

# Expose port 8080 (Cloud Run and GKE standard)
EXPOSE 8080

# Set environment variable to listen on all interfaces
ENV ASPNETCORE_URLS=http://+:8080

# Run the application
# Use the DLL name from the .csproj file
ENTRYPOINT ["dotnet", "DotNetGcpApp.dll"]
