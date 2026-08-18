import type { AcquisitionStatus } from './acquisition-status';

export type InteractionType = 'Cooperative' | 'Competitive';

export interface BoardGame {
  id: string;
  name: string;
  coverImageUrl: string | null;
  minimumPlayers: number;
  maximumPlayers: number;
  approximateDuration: number | null;
  interactionType: InteractionType | null;
  acquisitionStatus: AcquisitionStatus;
  rating: number | null;
  notes: string | null;
  createdAt: string;
}

export interface BoardGameInput {
  name: string;
  minimumPlayers: number | null;
  maximumPlayers: number | null;
  approximateDuration: number | null;
  interactionType: InteractionType | null;
  acquisitionStatus: AcquisitionStatus;
  rating: number | null;
  notes: string | null;
  coverImageUrl: string | null;
}