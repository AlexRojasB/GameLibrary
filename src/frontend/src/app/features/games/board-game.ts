import type { AcquisitionStatus } from './acquisition-status';
import type { CoverUpdate } from './covers/cover-types';

export type InteractionType = 'Cooperative' | 'Competitive';

export interface BoardGame {
  id: string;
  name: string;
  coverImageUrl: string | null;
  coverUploadId: string | null;
  isFamilyFriendly: boolean;
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
  isFamilyFriendly: boolean;
  minimumPlayers: number | null;
  maximumPlayers: number | null;
  approximateDuration: number | null;
  interactionType: InteractionType | null;
  acquisitionStatus: AcquisitionStatus;
  rating: number | null;
  notes: string | null;
  coverUpdate?: CoverUpdate;
}
