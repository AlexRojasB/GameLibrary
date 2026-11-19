import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../../environments/environment';

export interface SteamStatusResponse {
  enabled: boolean;
  linkAvailable: boolean;
  importAvailable: boolean;
  linked: boolean;
  steamId64: string | null;
  linkedAt: string | null;
  message: string | null;
}

export interface SteamLinkRequestResponse {
  startUrl: string;
  expiresAt: string;
}

export interface SteamPreviewCandidate {
  steamAppId: number;
  name: string;
  playtimeForeverMinutes: number | null;
  alreadyImported: boolean;
}

export interface SteamLibraryPreviewResponse {
  state: string;
  candidates: SteamPreviewCandidate[];
  message: string | null;
}

export interface SteamImportResponse {
  imported: SteamImportedGame[];
  alreadyImported: number[];
  unavailable: number[];
  invalid: number[];
}

export interface SteamImportedGame {
  steamAppId: number;
  videoGameId: string;
  name: string;
}

@Injectable({ providedIn: 'root' })
export class SteamService {
  private readonly http = inject(HttpClient);

  getStatus(): Observable<SteamStatusResponse> {
    return this.http.get<SteamStatusResponse>(`${environment.apiBaseUrl}/steam/status`);
  }

  createLinkRequest(): Observable<SteamLinkRequestResponse> {
    return this.http.post<SteamLinkRequestResponse>(`${environment.apiBaseUrl}/steam/link-requests`, {}, { withCredentials: true });
  }

  unlink(): Observable<void> {
    return this.http.delete<void>(`${environment.apiBaseUrl}/steam/link`);
  }

  preview(): Observable<SteamLibraryPreviewResponse> {
    return this.http.post<SteamLibraryPreviewResponse>(`${environment.apiBaseUrl}/steam/library/preview`, {});
  }

  import(steamAppIds: number[]): Observable<SteamImportResponse> {
    return this.http.post<SteamImportResponse>(`${environment.apiBaseUrl}/steam/library/import`, { steamAppIds });
  }
}
