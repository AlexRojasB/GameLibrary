import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';

import { VideoGame, VideoGameInput } from './video-game';

@Injectable({ providedIn: 'root' })
export class VideoGamesService {
  private readonly http = inject(HttpClient);

  list(): Observable<VideoGame[]> {
    return this.http.get<VideoGame[]>(`${environment.apiBaseUrl}/video-games`);
  }

  create(input: VideoGameInput): Observable<VideoGame> {
    return this.http.post<VideoGame>(`${environment.apiBaseUrl}/video-games`, input);
  }

  update(id: string, input: VideoGameInput): Observable<VideoGame> {
    return this.http.put<VideoGame>(`${environment.apiBaseUrl}/video-games/${id}`, input);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${environment.apiBaseUrl}/video-games/${id}`);
  }
}