/**
 * Production environment configuration
 * Used when building with 'npm run build' or 'ng build --configuration production'
 */

export const environment = {
  production: true,

  /**
   * Backend API URL for production
   * Using relative URL - requests will go through bastion nginx reverse proxy
   * Bastion nginx routes /api/* to backend Internal Load Balancer (10.0.1.6)
   */
  apiUrl: '/api',  // Relative URL - proxied by bastion nginx

  endpoints: {
    users: '/users',
    usersLatest: '/users/latest',
    upload: '/upload',
    uploadFiles: '/upload/files',
    pubsubEvents: '/pubsub/events',
    health: '/health'
  },

  refreshInterval: 5000,
  maxFileSize: 10 * 1024 * 1024
};
