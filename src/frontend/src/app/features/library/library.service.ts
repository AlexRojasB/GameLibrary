import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';

import { LibraryFilterState, LibraryItem, LibrarySort } from './library';

@Injectable({ providedIn: 'root' })
export class LibraryService {
  private readonly http = inject(HttpClient);

  browse(filters: LibraryFilterState, sort: LibrarySort): Observable<LibraryItem[]> {
    return this.http.get<LibraryItem[]>(`${environment.apiBaseUrl}/library`, {
      params: toParams(filters, sort),
    });
  }
}

function toParams(filters: LibraryFilterState, sort: LibrarySort): HttpParams {
  let params = new HttpParams().set('sort', sort);

  const search = filters.search.trim();
  if (search.length > 0) {
    params = params.set('search', search);
  }
  if (filters.gameType !== null) {
    params = params.set('gameType', filters.gameType);
  }
  if (filters.ratingMin !== null) {
    params = params.set('ratingMin', String(filters.ratingMin));
  }
  if (filters.playerCount !== null) {
    params = params.set('playerCount', String(filters.playerCount));
  }

  params = appendAll(params, 'acquisitionStatuses', filters.acquisitionStatuses);
  params = appendAll(params, 'platformIds', filters.platformIds);
  params = appendAll(params, 'genreIds', filters.genreIds);
  params = appendAll(params, 'interactionTypes', filters.interactionTypes);
  params = appendAll(params, 'gameStatuses', filters.gameStatuses);

  return params;
}

function appendAll(params: HttpParams, key: string, values: readonly string[]): HttpParams {
  return values.reduce((current, value) => current.append(key, value), params);
}
