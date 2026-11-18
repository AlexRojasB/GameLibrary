import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CoverEditor } from './cover-editor';
import * as preparation from './image-preparation';

vi.hoisted(() => {
  URL.createObjectURL = vi.fn(() => 'blob:cropper-test');
  URL.revokeObjectURL = vi.fn();
});

describe('CoverEditor', () => {
  let fixture: ComponentFixture<CoverEditor>;
  let exportImage: ReturnType<typeof vi.fn<typeof preparation.exportCover>>;

  function configure(fit = false): CoverEditor {
    exportImage = vi.fn();
    TestBed.configureTestingModule({ imports: [CoverEditor], providers: [{ provide: preparation.EXPORT_COVER_IMAGE, useValue: exportImage }] });
    // The ngx UI is exercised separately; these tests isolate async editor intent
    // and the native controls without jsdom pretending to decode image pixels.
    const file = new File(['working pixels'], 'working.png', { type: 'image/png' });
    fixture = TestBed.createComponent(CoverEditor);
    fixture.componentRef.setInput('image', { file, width: 600, height: 600 });
    fixture.componentRef.setInput('initialFit', fit);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  function button(text: string): HTMLButtonElement {
    return [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find((element) => element.textContent?.trim() === text)!;
  }

  afterEach(() => { fixture?.destroy(); vi.restoreAllMocks(); });

  it('starts BoardGames in Fit, provides an explicit Crop alternative and keeps controls native', () => {
    configure(true);
    expect(button('Fit whole image').getAttribute('aria-pressed')).toBe('true');
    expect(fixture.nativeElement.querySelector('.cover-editor__fit img')).not.toBeNull();
    expect(button('Crop 2:3').type).toBe('button');
    expect(fixture.nativeElement.querySelector('dialog')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Not saved');
  });

  it('defaults VideoGames to Crop and exposes zoom, position, rotation, reset and keyboard instructions', () => {
    // Avoid rendering an unknown cropper element in this control-only test.
    TestBed.configureTestingModule({ imports: [CoverEditor] });
    TestBed.overrideComponent(CoverEditor, { set: { template: '' } });
    const component = configure();
    expect(component['mode']()).toBe('crop');
    component['zoom'](2);
    component['move'](-5, 5);
    expect(component['transform']()).toMatchObject({ scale: 2, translateH: -5, translateV: 5 });
    component['rotate']();
    expect(component['rotation']()).toBe(1);
    expect(component['transform']().scale).toBe(1);
    component['reset']();
    expect(component['rotation']()).toBe(0);
    expect(component['mode']()).toBe('crop');
  });

  it('Fit ignores crop zoom/reposition and exports the whole working source', async () => {
    const component = configure(true);
    const blob = new Blob(['jpeg'], { type: 'image/jpeg' });
    exportImage.mockResolvedValue(blob);
    const chosen = vi.fn();
    component.chosen.subscribe(chosen);
    component['rotate']();
    await component['useImage']();
    expect(exportImage).toHaveBeenCalledWith(component.image().file, 'fit', 1, expect.any(AbortSignal));
    expect(chosen).toHaveBeenCalledWith(blob);
  });

  it('resetting an already loaded, unrotated Crop does not leave Use image disabled', () => {
    TestBed.configureTestingModule({ imports: [CoverEditor] });
    TestBed.overrideComponent(CoverEditor, { set: { template: '' } });
    const component = configure();
    component['ready'].set(true);
    component['zoom'](2);
    component['reset']();
    expect(component['mode']()).toBe('crop');
    expect(component['ready']()).toBe(true);
    expect(component['transform']().scale).toBe(1);
  });

  it('prevents duplicate exports and ignores results after Cancel', async () => {
    const component = configure(true);
    let resolve!: (blob: Blob) => void;
    exportImage.mockReturnValue(new Promise((finish) => resolve = finish));
    const chosen = vi.fn();
    const canceled = vi.fn();
    component.chosen.subscribe(chosen);
    component.canceled.subscribe(canceled);
    const pending = component['useImage']();
    await component['useImage']();
    expect(exportImage).toHaveBeenCalledOnce();
    button('Cancel editing').click();
    resolve(new Blob(['late']));
    await pending;
    expect(canceled).toHaveBeenCalledOnce();
    expect(chosen).not.toHaveBeenCalled();
  });

  it('invalidates exports on rotation, mode change, new source and destruction', async () => {
    const component = configure(true);
    const chosen = vi.fn();
    component.chosen.subscribe(chosen);
    let resolve!: (blob: Blob) => void;
    exportImage.mockImplementation(() => new Promise((finish) => resolve = finish));
    const pending = component['useImage']();
    component['rotate']();
    resolve(new Blob(['late rotation']));
    await pending;
    expect(chosen).not.toHaveBeenCalled();
    const next = component['useImage']();
    fixture.componentRef.setInput('image', { file: new File(['next'], 'next.png'), width: 200, height: 300 });
    fixture.detectChanges();
    resolve(new Blob(['old source']));
    await next;
    expect(chosen).not.toHaveBeenCalled();
    const destroyed = component['useImage']();
    fixture.destroy();
    resolve(new Blob(['destroyed']));
    await destroyed;
    expect(chosen).not.toHaveBeenCalled();
  });

  it('shows actionable export failures without choosing a draft', async () => {
    const component = configure(true);
    exportImage.mockRejectedValue(new Error('Selection too small. Choose a larger image.'));
    const chosen = vi.fn();
    component.chosen.subscribe(chosen);
    await component['useImage']();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Choose a larger image');
    expect(chosen).not.toHaveBeenCalled();
    expect(button('Use image').disabled).toBe(false);
  });

  it('revokes Fit preview URLs on replacement and destruction', () => {
    configure(true);
    const revoke = vi.spyOn(URL, 'revokeObjectURL');
    revoke.mockClear();
    fixture.componentRef.setInput('image', { file: new File(['next'], 'next.png'), width: 200, height: 300 });
    fixture.detectChanges();
    expect(revoke).toHaveBeenCalledWith('blob:cropper-test');
    fixture.destroy();
    expect(revoke).toHaveBeenCalledTimes(2);
  });
});
