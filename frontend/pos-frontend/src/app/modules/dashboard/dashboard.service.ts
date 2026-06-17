import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { DashboardSummary } from './dashboard.model';

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/Dashboard`;

  getSummary(): Observable<DashboardSummary> {
    return this.http.get<DashboardSummary>(`${this.baseUrl}/summary`);
  }
}
