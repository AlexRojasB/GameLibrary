import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';

import { BoardGame, BoardGameInput } from './board-game';

@Injectable({ providedIn: 'root' })
export class BoardGamesService {
  private readonly http = inject(HttpClient);

  list(): Observable<BoardGame[]> {
    return this.http.get<BoardGame[]>(`${environment.apiBaseUrl}/board-games`);
  }

  create(input: BoardGameInput): Observable<BoardGame> {
    return this.http.post<BoardGame>(`${environment.apiBaseUrl}/board-games`, input);
  }

  update(id: string, input: BoardGameInput): Observable<BoardGame> {
    return this.http.put<BoardGame>(`${environment.apiBaseUrl}/board-games/${id}`, input);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${environment.apiBaseUrl}/board-games/${id}`);
  }
}