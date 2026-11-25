import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';

import { VideoGame, VideoGameInput } from './video-game';
import { LibraryService } from '../library/library.service';

@Injectable({ providedIn: 'root' })
export class VideoGamesService {
  private readonly http = inject(HttpClient);
  private readonly library = inject(LibraryService);

  list(search = ''): Observable<VideoGame[]> {
    if (search.trim().length > 0) {
      // Library's read model includes every edit field and uses the same name
      // ordering. Reuse its owner-scoped server search, never filter loaded cards.
      return this.library.browse({
        search, gameType: 'VideoGame', acquisitionStatuses: [], platformIds: [],
        genreIds: [], ratingMin: null, playerCount: null, interactionTypes: [],
        gameStatuses: [], familyFriendlyOnly: false,
      }, 'NameAsc');
    }
    return this.http.get<VideoGame[]>(`${environment.apiBaseUrl}/video-games`);
  }

  create(input: VideoGameInput): Observable<VideoGame> {
    return this.http.post<VideoGame>(`${environment.apiBaseUrl}/video-games`, this.writeInput(input, true));
  }

  update(id: string, input: VideoGameInput): Observable<VideoGame> {
    return this.http.put<VideoGame>(`${environment.apiBaseUrl}/video-games/${id}`, this.writeInput(input, false));
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${environment.apiBaseUrl}/video-games/${id}`);
  }

  private writeInput(input: VideoGameInput, create: boolean): VideoGameInput {
    const { name, isFamilyFriendly, acquisitionStatus, platformIds, genreIds, gameStatus,
      progressPercentage, rating, notes, minimumPlayers, maximumPlayers } = input;
    const coverUpdate = input.coverUpdate === undefined ? undefined : { ...input.coverUpdate };
    if (create && coverUpdate !== undefined && coverUpdate.intent !== 'keep') delete coverUpdate.expectedCover;
    return { name, isFamilyFriendly, acquisitionStatus, platformIds, genreIds, gameStatus,
      progressPercentage, rating, notes, minimumPlayers, maximumPlayers,
      ...(coverUpdate === undefined ? {} : { coverUpdate }) };
  }
}
