import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../../environments/environment';

export type CoverImageSearchGameType = 'VideoGame' | 'BoardGame';

export interface CoverImageSearchRequest {
  gameType: CoverImageSearchGameType;
  name: string;
  platformName?: string;
}

export interface CoverImageSearchResponse {
  candidates: CoverImageCandidate[];
}

export interface CoverImageCandidate {
  imageUrl: string;
  thumbnailUrl: string | null;
  sourcePageUrl: string | null;
  sourceName: string | null;
  width: number | null;
  height: number | null;
}

@Injectable({ providedIn: 'root' })
export class CoverImageSearchService {
  private readonly http = inject(HttpClient);

  search(request: CoverImageSearchRequest): Observable<CoverImageSearchResponse> {
    return this.http.post<CoverImageSearchResponse>(`${environment.apiBaseUrl}/cover-images/search`, request);
  }
}
