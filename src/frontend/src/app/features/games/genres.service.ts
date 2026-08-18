import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';

import { Genre } from './genres';

@Injectable({ providedIn: 'root' })
export class GenresService {
  private readonly http = inject(HttpClient);

  list(): Observable<Genre[]> {
    return this.http.get<Genre[]>(`${environment.apiBaseUrl}/genres`);
  }
}