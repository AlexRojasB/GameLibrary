import type { AcquisitionStatus } from './acquisition-status';
import type { CoverUpdate } from './covers/cover-types';

export type { AcquisitionStatus };

export type GameStatus = 'Backlog' | 'Playing' | 'Completed' | 'Abandoned' | 'WantToPlay';

export interface VideoGame {
  id: string;
  name: string;
  coverImageUrl: string | null;
  coverUploadId: string | null;
  isFamilyFriendly: boolean;
  acquisitionStatus: AcquisitionStatus;
  platformIds: string[];
  genreIds: string[];
  gameStatus: GameStatus | null;
  progressPercentage: number | null;
  rating: number | null;
  notes: string | null;
  createdAt: string;
  minimumPlayers: number | null;
  maximumPlayers: number | null;
}

export interface VideoGameInput {
  name: string;
  coverUpdate?: CoverUpdate;
  isFamilyFriendly: boolean;
  acquisitionStatus: AcquisitionStatus;
  platformIds: string[];
  genreIds: string[];
  gameStatus: GameStatus | null;
  progressPercentage: number | null;
  rating: number | null;
  notes: string | null;
  minimumPlayers: number | null;
  maximumPlayers: number | null;
}
