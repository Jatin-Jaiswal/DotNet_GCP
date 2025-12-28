/**
 * Environment configuration for the Angular frontend application
 * This file contains all API URLs and configuration for different environments
 */

export const environment = {
  /**
   * Set to false for production builds
   */
  production: false,

  /**
   * Backend API base URL
   * Use a relative path so the same build works locally with a dev proxy and
   * in GCP behind nginx (which forwards /api to the backend).
   */
  apiUrl: '/api',

  /**
   * API endpoints
   * These are appended to the apiUrl
   */
  endpoints: {
    users: '/users',
    usersLatest: '/users/latest',
    upload: '/upload',
    uploadFiles: '/upload/files',
    pubsubEvents: '/pubsub/events',
    health: '/health'
  },

  /**
   * Auto-refresh interval in milliseconds
   * Data tables will refresh every 5 seconds by default
   */
  refreshInterval: 5000,

  /**
   * Maximum file upload size in bytes (10MB)
   */
  maxFileSize: 10 * 1024 * 1024
};
