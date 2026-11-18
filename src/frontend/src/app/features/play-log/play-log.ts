export type PlayLogGameType = 'VideoGame' | 'BoardGame';

export interface PlayLogEntry extends CoverReference {
  id: string;
  libraryEntryId: string;
  gameId: string;
  gameType: PlayLogGameType;
  gameName: string;
  playedAt: string;
  durationMinutes: number | null;
  createdAt: string;
}

export interface CreatePlayLogEntryRequest {
  gameId: string;
  playedAt: string;
  durationMinutes: number | null;
}

export interface UpdatePlayLogEntryRequest {
  playedAt: string;
  durationMinutes: number | null;
}
import type { CoverReference } from '../games/covers/cover-types';
