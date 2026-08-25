import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';

import { CreatePlayLogEntryRequest, PlayLogEntry, UpdatePlayLogEntryRequest } from './play-log';

@Injectable({ providedIn: 'root' })
export class PlayLogService {
  private readonly http = inject(HttpClient);

  list(): Observable<PlayLogEntry[]> {
    return this.http.get<PlayLogEntry[]>(`${environment.apiBaseUrl}/play-log`);
  }

  logPlay(request: CreatePlayLogEntryRequest): Observable<PlayLogEntry> {
    return this.http.post<PlayLogEntry>(`${environment.apiBaseUrl}/play-log`, request);
  }

  update(id: string, request: UpdatePlayLogEntryRequest): Observable<PlayLogEntry> {
    return this.http.put<PlayLogEntry>(`${environment.apiBaseUrl}/play-log/${id}`, request);
  }
}
