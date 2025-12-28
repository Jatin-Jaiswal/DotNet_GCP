import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatSnackBar } from '@angular/material/snack-bar';
import { interval } from 'rxjs';
import { environment } from '../environments/environment';

/**
 * User interface matching the backend User model
 */
interface User {
  id: number;
  name: string;
  email: string;
  createdAt: string;
}

/**
 * Pub/Sub event interface
 */
interface PubSubEvent {
  source: string;
  message: string;
  timestamp: string;
}

/**
 * Main application component
 * Single-page UI that displays:
 * - User form (create new users)
 * - File upload
 * - All users table (from SQL)
 * - Latest users table (from Redis cache)
 * - Pub/Sub events list
 */
@Component({
  selector: 'app-root',
  standalone: false,  // Explicitly set to false for NgModule-based app
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss']
})
export class AppComponent implements OnInit {
  title = 'DotNet GCP Application';
  
  // Backend API URL from environment configuration
  private apiUrl = environment.apiUrl;
  
  // Form for creating new users
  userForm: FormGroup;
  
  // Data arrays
  allUsers: User[] = [];
  latestUsers: User[] = [];
  pubSubEvents: PubSubEvent[] = [];
  
  // Loading states
  loadingUsers = false;
  loadingLatest = false;
  loadingEvents = false;
  creatingUser = false;
  uploadingFile = false;
  
  // Table columns configuration
  userColumns: string[] = ['id', 'name', 'email', 'createdAt'];
  latestUserColumns: string[] = ['id', 'name', 'email'];
  
  // File upload
  selectedFile: File | null = null;

  constructor(
    private fb: FormBuilder,
    private http: HttpClient,
    private snackBar: MatSnackBar
  ) {
    // Initialize the user form with validation rules
    this.userForm = this.fb.group({
      name: ['', [Validators.required, Validators.minLength(2)]],
      email: ['', [Validators.required, Validators.email]]
    });
  }

  ngOnInit(): void {
    // Load initial data when component starts
    this.loadAllUsers();
    this.loadLatestUsers();
    this.loadPubSubEvents();
    
    // Auto-refresh data in background without showing loading indicators
    interval(environment.refreshInterval).subscribe(() => {
      this.loadAllUsers(true); // true = silent refresh
      this.loadLatestUsers(true);
      this.loadPubSubEvents(true);
    });
  }

  /**
   * Loads all users from the database via backend API
   * Endpoint: GET /api/users
   * @param silent - if true, don't show loading indicator
   */
  loadAllUsers(silent: boolean = false): void {
    if (!silent) {
      this.loadingUsers = true;
    }
    
    this.http.get<any>(`${this.apiUrl}/users`).subscribe({
      next: (response) => {
        if (response.success) {
          this.allUsers = response.data;
        }
        this.loadingUsers = false;
      },
      error: (error) => {
        console.error('Error loading users:', error);
        this.loadingUsers = false;
      }
    });
  }

  /**
   * Loads the latest 3 users from Redis cache
   * Endpoint: GET /api/users/latest
   * Shows cache source (redis-cache or database)
   * @param silent - if true, don't show loading indicator
   */
  loadLatestUsers(silent: boolean = false): void {
    if (!silent) {
      this.loadingLatest = true;
    }
    
    this.http.get<any>(`${this.apiUrl}/users/latest`).subscribe({
      next: (response) => {
        if (response.success) {
          this.latestUsers = response.data;
          
          // Show notification about cache source
          if (response.source === 'redis-cache') {
            console.log('Latest users loaded from Redis cache');
          } else {
            console.log('Latest users loaded from database (cache miss)');
          }
        }
        this.loadingLatest = false;
      },
      error: (error) => {
        console.error('Error loading latest users:', error);
        this.loadingLatest = false;
      }
    });
  }

  /**
   * Loads recent Pub/Sub events
   * Endpoint: GET /api/pubsub/events
   * @param silent - if true, don't show loading indicator
   */
  loadPubSubEvents(silent: boolean = false): void {
    if (!silent) {
      this.loadingEvents = true;
    }
    
    this.http.get<any>(`${this.apiUrl}/pubsub/events`).subscribe({
      next: (response) => {
        if (response.success) {
          this.pubSubEvents = response.data;
        }
        this.loadingEvents = false;
      },
      error: (error) => {
        console.error('Error loading Pub/Sub events:', error);
        this.loadingEvents = false;
      }
    });
  }

  /**
   * Creates a new user
   * Steps:
   * 1. Validates form
   * 2. Sends POST request to backend
   * 3. Backend inserts into SQL, publishes to Pub/Sub, updates Redis cache
   * 4. Refreshes all tables
   */
  createUser(): void {
    if (this.userForm.invalid) {
      this.showMessage('Please fill in all required fields correctly', 'error');
      return;
    }

    this.creatingUser = true;
    const userData = this.userForm.value;

    this.http.post<any>(`${this.apiUrl}/users`, userData).subscribe({
      next: (response) => {
        if (response.success) {
          this.showMessage('User created successfully!', 'success');
          this.userForm.reset();
          
          // Refresh all tables to show the new user
          this.loadAllUsers();
          this.loadLatestUsers();
          this.loadPubSubEvents();
        }
        this.creatingUser = false;
      },
      error: (error) => {
        console.error('Error creating user:', error);
        
        if (error.status === 409) {
          this.showMessage('Email already exists', 'error');
        } else {
          this.showMessage('Error creating user', 'error');
        }
        
        this.creatingUser = false;
      }
    });
  }

  /**
   * Handles file selection for upload
   */
  onFileSelected(event: any): void {
    const file = event.target.files[0];
    if (file) {
      // Validate file size using environment configuration
      if (file.size > environment.maxFileSize) {
        const maxSizeMB = environment.maxFileSize / (1024 * 1024);
        this.showMessage(`File size exceeds ${maxSizeMB}MB limit`, 'error');
        
        // Reset file input
        const fileInput = document.getElementById('fileInput') as HTMLInputElement;
        if (fileInput) {
          fileInput.value = '';
        }
        return;
      }
      
      this.selectedFile = file;
    }
  }

  /**
   * Uploads selected file to Google Cloud Storage
   * Steps:
   * 1. Creates FormData with file
   * 2. Sends POST request to backend
   * 3. Backend uploads to GCS and publishes Pub/Sub event
   * 4. Refreshes Pub/Sub events table
   */
  uploadFile(): void {
    if (!this.selectedFile) {
      this.showMessage('Please select a file first', 'error');
      return;
    }

    this.uploadingFile = true;
    const formData = new FormData();
    formData.append('file', this.selectedFile);

    this.http.post<any>(`${this.apiUrl}/upload`, formData).subscribe({
      next: (response) => {
        if (response.success) {
          this.showMessage(`File uploaded: ${response.data.fileName}`, 'success');
          this.selectedFile = null;
          
          // Reset file input
          const fileInput = document.getElementById('fileInput') as HTMLInputElement;
          if (fileInput) {
            fileInput.value = '';
          }
          
          // Refresh Pub/Sub events to show the upload event
          this.loadPubSubEvents();
        }
        this.uploadingFile = false;
      },
      error: (error) => {
        console.error('Error uploading file:', error);
        this.showMessage('Error uploading file', 'error');
        this.uploadingFile = false;
      }
    });
  }

  /**
   * Triggers the file input click programmatically
   */
  triggerFileInput(): void {
    const fileInput = document.getElementById('fileInput') as HTMLInputElement;
    if (fileInput) {
      fileInput.click();
    }
  }

  /**
   * Shows a snackbar notification message
   * Used for success/error feedback to the user
   */
  private showMessage(message: string, type: 'success' | 'error'): void {
    this.snackBar.open(message, 'Close', {
      duration: 3000,
      horizontalPosition: 'end',
      verticalPosition: 'top',
      panelClass: type === 'success' ? ['success-snackbar'] : ['error-snackbar']
    });
  }

  /**
   * Helper method to get CSS class based on Pub/Sub event source
   */
  getEventChipClass(source: string): string {
    return source === 'sql' ? 'chip-sql' : 'chip-bucket';
  }
}
