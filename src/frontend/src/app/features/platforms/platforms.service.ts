import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';

import { Platform } from './platform';

@Injectable({ providedIn: 'root' })
export class PlatformsService {
  private readonly http = inject(HttpClient);

  list(): Observable<Platform[]> {
    return this.http.get<Platform[]>(`${environment.apiBaseUrl}/platforms`);
  }

  create(name: string): Observable<Platform> {
    return this.http.post<Platform>(`${environment.apiBaseUrl}/platforms`, { name });
  }

  update(id: string, name: string): Observable<Platform> {
    return this.http.put<Platform>(`${environment.apiBaseUrl}/platforms/${id}`, { name });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${environment.apiBaseUrl}/platforms/${id}`);
  }
}