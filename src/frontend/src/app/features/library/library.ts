import type { AcquisitionStatus } from '../games/acquisition-status';
import type { InteractionType } from '../games/board-game';
import type { GameStatus } from '../games/video-game';

export type LibraryGameType = 'VideoGame' | 'BoardGame';

export type LibrarySort = 'NameAsc' | 'NameDesc' | 'RatingDesc' | 'RatingAsc' | 'RecentlyAdded';

export interface LibraryItem {
  id: string;
  gameType: LibraryGameType;
  name: string;
  coverImageUrl: string | null;
  createdAt: string;
  acquisitionStatus: AcquisitionStatus;
  rating: number | null;
  notes: string | null;
  platformIds: string[];
  genreIds: string[];
  gameStatus: GameStatus | null;
  progressPercentage: number | null;
  minimumPlayers: number | null;
  maximumPlayers: number | null;
  approximateDuration: number | null;
  interactionType: InteractionType | null;
}

export interface LibraryFilterState {
  search: string;
  gameType: LibraryGameType | null;
  acquisitionStatuses: AcquisitionStatus[];
  platformIds: string[];
  genreIds: string[];
  ratingMin: number | null;
  playerCount: number | null;
  interactionTypes: InteractionType[];
  gameStatuses: GameStatus[];
}
