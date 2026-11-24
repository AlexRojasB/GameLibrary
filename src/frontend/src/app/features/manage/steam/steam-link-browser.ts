import { DOCUMENT } from '@angular/common';
import { inject, Injectable } from '@angular/core';

export const steamLinkStorageKey = 'game-library-steam-link-v2';
export interface SteamLinkHandle { transactionId: string; initiatingUserId: string; expiresAt: string }

export function validSteamId(value: unknown): value is string {
  return typeof value === 'string' && /^[1-9][0-9]{0,19}$/.test(value) && BigInt(value) <= 18446744073709551615n;
}
export function validLinkHandle(value: unknown): value is SteamLinkHandle {
  if (!value || typeof value !== 'object') return false;
  const handle = value as SteamLinkHandle;
  return Object.keys(handle).sort().join() === 'expiresAt,initiatingUserId,transactionId'
    && typeof handle.transactionId === 'string' && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(handle.transactionId)
    && typeof handle.initiatingUserId === 'string' && handle.initiatingUserId.length > 0
    && typeof handle.expiresAt === 'string' && Number.isFinite(Date.parse(handle.expiresAt));
}
export function trustedSteamUrl(value: unknown): value is string {
  if (typeof value !== 'string' || value !== value.trim() || /[\s\\#]/.test(value)) return false;
  try {
    const url = new URL(value);
    return url.protocol === 'https:' && url.hostname === 'steamcommunity.com' && url.pathname === '/openid/login'
      && !url.port && !url.username && !url.password && !url.hash
      && /^https:\/\/steamcommunity\.com\/openid\/login\?/.test(value);
  } catch { return false; }
}

// Browser boundary is feature-local and replaceable in unit tests, not configuration.
@Injectable({ providedIn: 'root' })
export class SteamLinkBrowser {
  private readonly window = inject(DOCUMENT).defaultView!;
  read(): SteamLinkHandle | null {
    const raw = this.window.sessionStorage.getItem(steamLinkStorageKey);
    if (!raw) return null;
    try { const value: unknown = JSON.parse(raw); if (validLinkHandle(value)) return value; } catch { /* fail closed */ }
    this.clear();
    return null;
  }
  save(handle: SteamLinkHandle): void {
    if (!validLinkHandle(handle)) throw new Error('Invalid Steam link metadata.');
    this.window.sessionStorage.setItem(steamLinkStorageKey, JSON.stringify(handle));
    if (this.window.sessionStorage.getItem(steamLinkStorageKey) !== JSON.stringify(handle)) throw new Error('Steam metadata unavailable.');
  }
  clear(): void { this.window.sessionStorage.removeItem(steamLinkStorageKey); }
  navigate(url: string): void {
    if (!trustedSteamUrl(url)) throw new Error('Invalid Steam destination.');
    this.window.location.replace(url);
  }
}
