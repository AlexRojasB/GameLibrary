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
    return this.http.post<BoardGame>(`${environment.apiBaseUrl}/board-games`, this.writeInput(input, true));
  }

  update(id: string, input: BoardGameInput): Observable<BoardGame> {
    return this.http.put<BoardGame>(`${environment.apiBaseUrl}/board-games/${id}`, this.writeInput(input, false));
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${environment.apiBaseUrl}/board-games/${id}`);
  }

  private writeInput(input: BoardGameInput, create: boolean): BoardGameInput {
    const { name, isFamilyFriendly, minimumPlayers, maximumPlayers, approximateDuration,
      interactionType, acquisitionStatus, rating, notes } = input;
    const coverUpdate = input.coverUpdate === undefined ? undefined : { ...input.coverUpdate };
    if (create && coverUpdate !== undefined && coverUpdate.intent !== 'keep') delete coverUpdate.expectedCover;
    return { name, isFamilyFriendly, minimumPlayers, maximumPlayers, approximateDuration,
      interactionType, acquisitionStatus, rating, notes,
      ...(coverUpdate === undefined ? {} : { coverUpdate }) };
  }
}
