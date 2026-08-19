import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';

import { RandomPickerPickRequest, RandomPickerPickResponse } from './random-picker';

@Injectable({ providedIn: 'root' })
export class RandomPickerService {
  private readonly http = inject(HttpClient);

  pick(request: RandomPickerPickRequest): Observable<RandomPickerPickResponse> {
    return this.http.post<RandomPickerPickResponse>(`${environment.apiBaseUrl}/random-picker/pick`, request);
  }
}
