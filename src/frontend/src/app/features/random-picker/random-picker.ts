import type { InteractionType } from '../games/board-game';
import type { GameStatus } from '../games/video-game';
import type { CoverReference } from '../games/covers/cover-types';

export type RandomPickerMode = 'VideoGames' | 'BoardGames' | 'All';
export type RandomPickerState = 'SUCCESS' | 'NO_CANDIDATES' | 'ALL_ALREADY_SHOWN';
export type RandomPickerGameType = 'VideoGame' | 'BoardGame';

export interface RandomPickerFilters {
  platformIds: string[];
  genreIds: string[];
  gameStatuses: GameStatus[];
  playerCount: number | null;
  availableDuration: number | null;
  interactionTypes: InteractionType[];
  ratingMin: number | null;
  familyFriendlyOnly: boolean;
}

export interface RandomPickerPickRequest extends RandomPickerFilters {
  mode: RandomPickerMode;
  shownLibraryEntryIds: string[];
}

export interface RandomPickerPickResponse {
  state: RandomPickerState;
  result: RandomPickerItem | null;
}

export interface RandomPickerItem extends CoverReference {
  libraryEntryId: string;
  gameId: string;
  gameType: RandomPickerGameType;
  name: string;
  isFamilyFriendly: boolean;
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
