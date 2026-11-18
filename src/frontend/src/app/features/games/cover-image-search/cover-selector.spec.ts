import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Session } from '@supabase/supabase-js';
import { of, Subject, throwError } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { CoverDraft } from '../covers/cover-types';
import { CoverUploadsService } from '../covers/cover-uploads.service';
import { CoverImageSearch } from './cover-image-search';
import { CoverImageSearchService } from './cover-image-search.service';
import * as preparation from './image-preparation';

vi.hoisted(() => {
  URL.createObjectURL = vi.fn(() => 'blob:selector-test');
  URL.revokeObjectURL = vi.fn();
});

describe('unified cover selector', () => {
  let fixture: ComponentFixture<CoverImageSearch>;
  const session = signal<Session | null>({ user: { id: 'user-a' } } as Session);
  let options: ReturnType<typeof vi.fn>;
  let search: ReturnType<typeof vi.fn>;
  let upload: ReturnType<typeof vi.fn>;
  let prepare: ReturnType<typeof vi.fn<typeof preparation.prepareImage>>;

  function configure(enabled = true, error?: number): void {
    session.set({ user: { id: 'user-a' } } as Session);
    options = vi.fn(() => error ? throwError(() => new HttpErrorResponse({ status: error })) : of({ enabled }));
    search = vi.fn(() => of({ candidates: [] }));
    upload = vi.fn();
    prepare = vi.fn();
    TestBed.configureTestingModule({
      imports: [CoverImageSearch],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: AuthService, useValue: { session } },
        { provide: CoverUploadsService, useValue: { options, upload } },
        { provide: CoverImageSearchService, useValue: { search } },
        { provide: preparation.PREPARE_COVER_IMAGE, useValue: prepare },
      ],
    });
    fixture = TestBed.createComponent(CoverImageSearch);
    fixture.componentRef.setInput('gameType', 'VideoGame');
    fixture.componentRef.setInput('gameName', 'Hades');
    fixture.detectChanges();
  }

  function button(text: string): HTMLButtonElement {
    return [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find((element) => element.textContent?.trim() === text)!;
  }

  function fileInput(camera = false): HTMLInputElement {
    return fixture.nativeElement.querySelector(camera ? 'input[capture]' : 'input[type="file"]:not([capture])');
  }

  async function choose(file?: File, camera = false): Promise<void> {
    const input = fileInput(camera);
    Object.defineProperty(input, 'files', { configurable: true, value: file ? [file] : [] });
    await fixture.componentInstance['chooseFile'](input);
    fixture.detectChanges();
  }

  afterEach(() => { fixture?.destroy(); vi.restoreAllMocks(); });

  it('keeps manual URL visible and uses separate ordinary and rear-camera inputs', () => {
    configure();
    const input = fixture.nativeElement.querySelector('input[type="url"]') as HTMLInputElement;
    expect(input.disabled).toBe(false);
    expect(fileInput().hasAttribute('capture')).toBe(false);
    expect(fileInput(true).getAttribute('capture')).toBe('environment');
    expect(fileInput().accept).toBe('image/jpeg,image/png,image/webp');
    expect(fileInput(true).accept).toBe(fileInput().accept);
    expect(search).not.toHaveBeenCalled();
    expect(upload).not.toHaveBeenCalled();
  });

  it('gracefully disables upload/camera but preserves manual and search when disabled or unavailable', () => {
    configure(false);
    expect(button('Upload image').disabled).toBe(true);
    expect(button('Take photo').disabled).toBe(true);
    expect(button('Search cover').disabled).toBe(false);
    expect(fixture.nativeElement.querySelector('input[type="url"]').disabled).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('uploads are disabled');
  });

  it('keeps the selector useful when options fails', () => {
    configure(true, 503);
    expect(button('Upload image').disabled).toBe(true);
    expect(button('Search cover').disabled).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('uploads are unavailable');
  });

  it('ignores an old account options 401 before Angular effect cleanup runs', () => {
    configure();
    const late = new Subject<{ enabled: boolean }>();
    options.mockReturnValue(late);
    session.set({ user: { id: 'user-b' } } as Session);
    fixture.detectChanges();
    const unauthorized = vi.fn();
    fixture.componentInstance.unauthorized.subscribe(unauthorized);
    session.set({ user: { id: 'user-c' } } as Session);
    late.error(new HttpErrorResponse({ status: 401 }));
    expect(unauthorized).not.toHaveBeenCalled();
  });

  it('preserves local drafts and preview URLs across token refresh in the same Supabase login', () => {
    configure();
    const token = (iat: number) => `header.${btoa(JSON.stringify({ session_id: '11111111-1111-4111-8111-111111111111', iat }))}.signature`;
    session.set({ user: { id: 'user-a' }, access_token: token(1) } as Session);
    fixture.detectChanges();
    fixture.componentRef.setInput('draft', { intent: 'local', blob: new Blob(['draft']) });
    fixture.detectChanges();
    const changed = vi.fn();
    fixture.componentInstance.draftChanged.subscribe(changed);
    const revoke = vi.spyOn(URL, 'revokeObjectURL');
    session.set({ user: { id: 'user-a' }, access_token: token(2) } as Session);
    fixture.detectChanges();
    expect(changed).not.toHaveBeenCalled();
    expect(revoke).not.toHaveBeenCalled();
    expect(fixture.componentInstance['localPreviewUrl']()).toBe('blob:selector-test');
  });

  it('managed saved cover starts with an empty external control and no implicit removal', () => {
    configure();
    const changed = vi.fn();
    fixture.componentInstance.draftChanged.subscribe(changed);
    fixture.componentRef.setInput('savedCover', { coverImageUrl: null, coverUploadId: 'saved-upload' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('input[type="url"]').value).toBe('');
    expect(changed).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('app-cover-image').getAttribute('ng-reflect-cover-upload-id')).not.toBe('blob:selector-test');
    button('Remove cover').click();
    expect(changed).toHaveBeenCalledWith({ intent: 'remove' });
  });

  it('emits explicit manual and removal intents without uploading, searching or equality inference', () => {
    configure();
    const drafts: CoverDraft[] = [];
    fixture.componentInstance.draftChanged.subscribe((draft) => drafts.push(draft));
    const input = fixture.nativeElement.querySelector('input[type="url"]') as HTMLInputElement;
    input.value = 'https://example.test/manual.jpg';
    input.dispatchEvent(new Event('input'));
    input.value = '';
    input.dispatchEvent(new Event('input'));
    expect(drafts).toEqual([{ intent: 'external', url: 'https://example.test/manual.jpg' }, { intent: 'remove' }]);
    expect(search).not.toHaveBeenCalled();
    expect(upload).not.toHaveBeenCalled();
  });

  it('canceled file or camera chooser retains the prior draft and does not load the editor', async () => {
    configure();
    fixture.componentRef.setInput('draft', { intent: 'external', url: 'https://example.test/previous.jpg' });
    fixture.detectChanges();
    const changed = vi.fn();
    const busy = vi.fn();
    fixture.componentInstance.draftChanged.subscribe(changed);
    fixture.componentInstance.busyChanged.subscribe(busy);
    await choose();
    await choose(undefined, true);
    expect(changed).not.toHaveBeenCalled();
    expect(busy).not.toHaveBeenCalled();
    expect(fixture.componentInstance['editorImage']()).toBeNull();
    expect(fixture.nativeElement.querySelector('input[type="url"]').value).toContain('previous.jpg');
  });

  it('opening either picker is explicit, without search or upload', () => {
    configure();
    const fileClick = vi.spyOn(fileInput(), 'click').mockImplementation(() => undefined);
    const cameraClick = vi.spyOn(fileInput(true), 'click').mockImplementation(() => undefined);
    button('Upload image').click();
    button('Take photo').click();
    expect(fileClick).toHaveBeenCalledOnce();
    expect(cameraClick).toHaveBeenCalledOnce();
    expect(upload).not.toHaveBeenCalled();
    expect(search).not.toHaveBeenCalled();
  });

  it('marks preparation/editor busy, retains the prior draft on cancel, and returns focus', async () => {
    configure();
    const prepared = { file: new File(['png'], 'working.png'), width: 600, height: 900 };
    prepare.mockResolvedValue(prepared);
    const changed = vi.fn();
    const busy: boolean[] = [];
    fixture.componentInstance.draftChanged.subscribe(changed);
    fixture.componentInstance.busyChanged.subscribe((value) => busy.push(value));
    const trigger = button('Upload image');
    const focus = vi.spyOn(trigger, 'focus');
    vi.spyOn(fileInput(), 'click').mockImplementation(() => undefined);
    trigger.click();
    await choose(new File(['source'], 'photo.png'));
    expect(fixture.componentInstance['editorImage']()).toBe(prepared);
    expect(button('Search cover').disabled).toBe(true);
    fixture.componentInstance['finishEditing']();
    fixture.detectChanges();
    expect(busy).toEqual([true, false]);
    expect(changed).not.toHaveBeenCalled();
    expect(focus).toHaveBeenCalled();
    expect(upload).not.toHaveBeenCalled();
  });

  it('Use image chooses only a local Blob draft and a volatile preview', async () => {
    configure();
    prepare.mockResolvedValue({ file: new File(['png'], 'working.png'), width: 600, height: 900 });
    const blob = new Blob(['fresh jpeg'], { type: 'image/jpeg' });
    const changed = vi.fn((draft: CoverDraft) => fixture.componentRef.setInput('draft', draft));
    fixture.componentInstance.draftChanged.subscribe(changed);
    await choose(new File(['original'], 'photo.png'));
    fixture.componentInstance['useLocalImage'](blob);
    fixture.detectChanges();
    expect(changed).toHaveBeenCalledWith({ intent: 'local', blob });
    expect(fixture.nativeElement.querySelector('input[type="url"]').value).toBe('');
    expect(fixture.nativeElement.querySelector('.cover-search__selected img').src).toBe('blob:selector-test');
    expect(fixture.nativeElement.textContent).toContain('Not saved');
    expect(upload).not.toHaveBeenCalled();
  });

  it('source or account changes invalidate late preparation without choosing a draft', async () => {
    configure();
    const changed = vi.fn();
    fixture.componentInstance.draftChanged.subscribe(changed);
    let finish!: (image: preparation.PreparedImage) => void;
    prepare.mockImplementation(() => new Promise((resolve) => finish = resolve));
    const pending = choose(new File(['source'], 'photo.png'));
    fixture.componentRef.setInput('draft', { intent: 'external', url: 'https://example.test/new.jpg' });
    fixture.detectChanges();
    finish({ file: new File(['late'], 'working.png'), width: 600, height: 900 });
    await pending;
    expect(fixture.componentInstance['editorImage']()).toBeNull();
    expect(changed).not.toHaveBeenCalled();
    const accountPending = choose(new File(['source'], 'photo.png'));
    session.set({ user: { id: 'user-b' } } as Session);
    fixture.detectChanges();
    finish({ file: new File(['late'], 'working.png'), width: 600, height: 900 });
    await accountPending;
    expect(fixture.componentInstance['editorImage']()).toBeNull();
    expect(changed).toHaveBeenCalledWith({ intent: 'keep' });
    expect(upload).not.toHaveBeenCalled();
  });

  it('shows file validation failures and releases the busy state without changing the draft', async () => {
    configure();
    prepare.mockRejectedValue(new Error('Export HEIC as JPEG or PNG first.'));
    const changed = vi.fn();
    fixture.componentInstance.draftChanged.subscribe(changed);
    await choose(new File(['heic'], 'photo.heic'), true);
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Export HEIC');
    expect(fixture.componentInstance['busy']()).toBe(false);
    expect(changed).not.toHaveBeenCalled();
  });

  it('invalidates preparation and local preview on a replacement session for the same user', async () => {
    configure();
    fixture.componentRef.setInput('draft', { intent: 'local', blob: new Blob(['old-session-photo']) });
    fixture.detectChanges();
    let finish!: (image: preparation.PreparedImage) => void;
    prepare.mockImplementation(() => new Promise((resolve) => { finish = resolve; }));
    const pending = choose(new File(['source'], 'photo.png'));
    const changed = vi.fn();
    fixture.componentInstance.draftChanged.subscribe(changed);
    const revoke = vi.spyOn(URL, 'revokeObjectURL');
    session.set({ user: { id: 'user-a' } } as Session);
    fixture.detectChanges();
    finish({ file: new File(['late'], 'working.png'), width: 600, height: 900 });
    await pending;
    expect(fixture.componentInstance['editorImage']()).toBeNull();
    expect(fixture.componentInstance['localPreviewUrl']()).toBeNull();
    expect(revoke).toHaveBeenCalledWith('blob:selector-test');
    expect(changed).toHaveBeenCalledExactlyOnceWith({ intent: 'keep' });
    expect(upload).not.toHaveBeenCalled();
  });

  it('revokes and hides a previous account local preview even before the parent replaces its draft', () => {
    configure();
    fixture.componentRef.setInput('draft', { intent: 'local', blob: new Blob(['private photo']) });
    fixture.detectChanges();
    const revoke = vi.spyOn(URL, 'revokeObjectURL');
    const changed = vi.fn();
    fixture.componentInstance.draftChanged.subscribe(changed);
    session.set(null);
    fixture.detectChanges();
    expect(revoke).toHaveBeenCalledWith('blob:selector-test');
    expect(fixture.componentInstance['localPreviewUrl']()).toBeNull();
    expect(fixture.nativeElement.querySelector('.cover-search__selected')).toBeNull();
    expect(changed).toHaveBeenCalledWith({ intent: 'keep' });
  });

  it('revokes local URLs on source change and account change, and ignores work after destruction', async () => {
    configure();
    fixture.componentRef.setInput('draft', { intent: 'local', blob: new Blob(['a']) });
    fixture.detectChanges();
    const revoke = vi.spyOn(URL, 'revokeObjectURL');
    fixture.componentRef.setInput('draft', { intent: 'external', url: 'https://example.test/a.jpg' });
    fixture.detectChanges();
    expect(revoke).toHaveBeenCalledWith('blob:selector-test');
    let finish!: (image: preparation.PreparedImage) => void;
    prepare.mockImplementation(() => new Promise((resolve) => finish = resolve));
    const input = fileInput();
    Object.defineProperty(input, 'files', { value: [new File(['source'], 'photo.png')] });
    const active = fixture.componentInstance['chooseFile'](input);
    fixture.destroy();
    finish({ file: new File(['late'], 'working.png'), width: 600, height: 900 });
    await active;
    expect(upload).not.toHaveBeenCalled();
  });
});
