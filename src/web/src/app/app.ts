import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';

type BackendStatus = 'Connecting' | 'Available' | 'Unavailable';

@Component({
  imports: [RouterLink, RouterOutlet],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  private readonly http = inject(HttpClient);

  protected readonly backendStatus = signal<BackendStatus>('Connecting');

  constructor() {
    this.http.get('/api/health').subscribe({
      next: () => this.backendStatus.set('Available'),
      error: () => this.backendStatus.set('Unavailable'),
    });
  }
}
