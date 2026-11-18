import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

import { environment } from '../../../../environments/environment';

import { CoverSaveResult, CoverUpdate, CoverUploadOptions, CoverUploadStatus } from './cover-types';

@Injectable({ providedIn: 'root' })
export class CoverUploadsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/cover-uploads`;

  options() {
    return this.http.get<CoverUploadOptions>(`${this.base}/options`);
  }

  upload(blob: Blob) {
    const body = new FormData();
    body.append('file', blob, 'cover.jpg');
    return this.http.post<CoverUploadStatus>(this.base, body);
  }

  status(id: string) {
    return this.http.get<CoverUploadStatus>(`${this.base}/${encodeURIComponent(id)}`);
  }

  cancel(id: string) {
    return this.http.delete<void>(`${this.base}/${encodeURIComponent(id)}`);
  }

  update(gameId: string, coverUpdate: CoverUpdate) {
    return this.http.put<CoverSaveResult>(`${environment.apiBaseUrl}/games/${encodeURIComponent(gameId)}/cover`, { coverUpdate });
  }
}
