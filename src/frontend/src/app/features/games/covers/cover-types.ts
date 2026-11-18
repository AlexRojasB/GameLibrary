export interface CoverReference {
  coverImageUrl: string | null;
  coverUploadId: string | null;
}

export type CoverDraft =
  | { intent: 'keep' }
  | { intent: 'remove' }
  | { intent: 'external'; url: string }
  | { intent: 'local'; blob: Blob };

export type CoverUpdate =
  | { intent: 'keep' }
  | { intent: 'remove'; expectedCover?: CoverReference }
  | { intent: 'external'; url: string; expectedCover?: CoverReference }
  | { intent: 'upload'; uploadId: string; expectedCover?: CoverReference };

export interface CoverUploadStatus {
  id: string;
  state: 'Uploading' | 'Ready' | 'Attached' | 'DeletePending';
  gameId: string | null;
  gameExists: boolean | null;
  expiresAt: string | null;
}

export interface CoverUploadOptions {
  enabled: boolean;
  maxUploadBytes: number;
  acceptedMediaTypes: string[];
  maxOutputBytes: number;
  maxOutputWidth: number;
  maxOutputHeight: number;
  maxLibraryBytes: number;
  maxUnclaimedUploads: number;
}

export interface CoverSaveResult extends CoverReference {
  gameId: string;
}
