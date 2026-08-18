import { HttpInterceptorFn } from '@angular/common/http';
import { inject, InjectionToken } from '@angular/core';

import { environment } from '../../../environments/environment';

import { AuthService } from './auth.service';

export const API_BASE_URL = new InjectionToken<string>('Game Library API base URL', {
  providedIn: 'root',
  factory: () => environment.apiBaseUrl,
});

export function isApiOrigin(url: string, apiBaseUrl: string): boolean {
  if (!apiBaseUrl) {
    return false;
  }
  try {
    const base = new URL(apiBaseUrl);
    const target = new URL(url, base);
    return target.origin === base.origin;
  } catch {
    return false;
  }
}

export const apiTokenInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const apiBaseUrl = inject(API_BASE_URL);
  const token = auth.accessToken();

  if (apiBaseUrl && token && isApiOrigin(req.url, apiBaseUrl)) {
    return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
  }
  return next(req);
};