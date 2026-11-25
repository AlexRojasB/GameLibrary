import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';

export interface CurrentlyPlayingItem {
  gameId: string;
  libraryEntryId: string;
  gameType: 'VideoGame' | 'BoardGame';
  name: string;
  coverImageUrl: string | null;
  coverUploadId: string | null;
}

export interface CurrentlyPlaying {
  videoGame: CurrentlyPlayingItem | null;
  boardGame: CurrentlyPlayingItem | null;
}

@Injectable({ providedIn: 'root' })
export class CurrentlyPlayingService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiBaseUrl}/currently-playing`;

  get(): Observable<CurrentlyPlaying> {
    return this.http.get<CurrentlyPlaying>(this.url);
  }

  select(gameId: string): Observable<CurrentlyPlaying> {
    return this.http.put<CurrentlyPlaying>(this.url, { gameId });
  }
}
