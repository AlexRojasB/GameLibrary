export type PlayLogGameType = 'VideoGame' | 'BoardGame';

export interface PlayLogEntry {
  id: string;
  libraryEntryId: string;
  gameId: string;
  gameType: PlayLogGameType;
  gameName: string;
  coverImageUrl: string | null;
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
