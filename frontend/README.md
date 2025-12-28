# Frontend Angular 20 Application

This folder contains the Angular 20 frontend application with Material Design.

## 📁 Folder Structure

```
frontend/
├── src/
│   ├── app/
│   │   ├── app.component.ts      # Main component logic
│   │   ├── app.component.html    # Main template
│   │   ├── app.component.scss    # Component styles
│   │   └── app.module.ts         # Root module
│   ├── index.html                # Entry HTML file
│   ├── main.ts                   # Application bootstrap
│   └── styles.scss               # Global styles
├── docker/
│   ├── Dockerfile                # Multi-stage Docker build
│   └── nginx.conf                # Nginx configuration
├── angular.json                  # Angular CLI configuration
├── package.json                  # Dependencies
└── tsconfig.json                 # TypeScript configuration
```

## 🎨 Features

### Single-Page UI Layout
The application displays everything on one page:

1. **Top Section (Side by side)**
   - User creation form (Name, Email)
   - File upload form

2. **Middle Section (Side by side)**
   - All users table (from Cloud SQL)
   - Latest 3 users table (from Redis cache)

3. **Bottom Section (Full width)**
   - Pub/Sub event messages (live updates every 5 seconds)

### Technologies Used
- **Angular 20**: Latest version with standalone components support
- **Angular Material**: Google's Material Design components
- **RxJS**: Reactive programming for auto-refresh
- **TypeScript**: Type-safe development

### Design System
- **Colors**: Google-style soft color palette
- **Typography**: Roboto font family
- **Components**: Material Design (cards, forms, tables, buttons)
- **Responsive**: Works on desktop, tablet, and mobile

## 🚀 Running Locally

### Prerequisites
- Node.js 20+
- npm

### Steps

```bash
cd frontend
npm install
npm start
```

The application will be available at `http://localhost:4200`

### Build for Production

```bash
npm run build
```

The production build will be in `dist/dot-net-frontend`

## 🐳 Building Docker Image

```bash
cd frontend
docker build -t dot-net-frontend:latest .
```

## 📡 API Integration

The frontend connects to the backend API at `http://localhost:8080/api` by default.

Change the `apiUrl` in `app.component.ts` to point to your deployed backend URL.

### API Endpoints Used

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/users` | GET | Get all users |
| `/api/users/latest` | GET | Get last 3 users (Redis) |
| `/api/users` | POST | Create new user |
| `/api/upload` | POST | Upload file |
| `/api/pubsub/events` | GET | Get Pub/Sub events |

## 🔄 Auto-Refresh

The application automatically refreshes data every 5 seconds using RxJS intervals:
- All users table
- Latest users table (from cache)
- Pub/Sub events

This provides a near-real-time experience without needing WebSockets.

## 📱 Responsive Design

The UI adapts to different screen sizes:
- **Desktop**: 2-column layout for forms and tables
- **Mobile**: Single column, stacked layout

## 🎯 User Experience Features

- **Loading indicators**: Spinners show when data is being fetched
- **Success/Error notifications**: Snackbar messages for user feedback
- **Form validation**: Real-time validation with error messages
- **File size display**: Shows selected file name and size
- **Color-coded events**: Different colors for SQL vs Bucket events
