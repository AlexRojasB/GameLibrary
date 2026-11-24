import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../../environments/environment';

export interface SteamStatusResponse {
  enabled: boolean;
  linkAvailable: boolean;
  linkProtocolVersion?: number;
  importAvailable: boolean;
  linked: boolean;
  steamId64: string | null;
  linkedAt: string | null;
  message: string | null;
}

export interface SteamPreviewCandidate {
  steamAppId: number;
  name: string;
  playtimeForeverMinutes: number | null;
  alreadyImported: boolean;
}

export interface SteamLinkCreated {
  protocolVersion: 2;
  transactionId: string;
  authorizationUrl: string;
  expiresAt: string;
}
export interface SteamLinkReview {
  state: 'Pending' | 'AwaitingConfirmation' | 'Linked';
  steamId64: string | null;
  expiresAt: string;
}
export interface SteamLinkConfirmed {
  state: 'Linked';
  steamId64: string;
  linkedAt: string;
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

  createLinkRequest(): Observable<SteamLinkCreated> {
    return this.http.post<SteamLinkCreated>(`${environment.apiBaseUrl}/steam/link/v2/requests`, {}, { withCredentials: true });
  }

  reviewLink(transactionId: string): Observable<SteamLinkReview> {
    return this.http.post<SteamLinkReview>(`${environment.apiBaseUrl}/steam/link/v2/review`, { transactionId }, { withCredentials: true });
  }

  confirmLink(transactionId: string, expectedSteamId64: string): Observable<SteamLinkConfirmed> {
    return this.http.post<SteamLinkConfirmed>(`${environment.apiBaseUrl}/steam/link/v2/confirm`, { transactionId, expectedSteamId64 }, { withCredentials: true });
  }

  cancelLink(transactionId: string): Observable<void> {
    return this.http.post<void>(`${environment.apiBaseUrl}/steam/link/v2/cancel`, { transactionId }, { withCredentials: true });
  }

  getStatus(): Observable<SteamStatusResponse> {
    return this.http.get<SteamStatusResponse>(`${environment.apiBaseUrl}/steam/status`);
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
