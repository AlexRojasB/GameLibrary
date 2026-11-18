import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CoverCropper } from './cover-cropper';

vi.hoisted(() => {
  URL.createObjectURL = vi.fn(() => 'blob:owned-cropper');
  URL.revokeObjectURL = vi.fn();
});

describe('local cropper interaction and lifetime', () => {
  let fixture: ComponentFixture<CoverCropper>;

  function configure(): CoverCropper {
    vi.stubGlobal('createImageBitmap', vi.fn(() => new Promise<ImageBitmap>(() => undefined)));
    TestBed.configureTestingModule({ imports: [CoverCropper] });
    fixture = TestBed.createComponent(CoverCropper);
    fixture.componentRef.setInput('localFile', new File(['working'], 'working.png', { type: 'image/png' }));
    fixture.componentRef.setInput('aspectRatio', 2 / 3);
    fixture.componentRef.setInput('maintainAspectRatio', true);
    fixture.componentRef.setInput('autoCrop', false);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  function seedFrame(component: CoverCropper): HTMLElement {
    component['state'].setMaxSize(200, 300);
    component['state'].cropper.set({ x1: 20, y1: 30, x2: 180, y2: 270 });
    component.imageVisible = true;
    component['frameReady'].set(true);
    fixture.detectChanges();
    const frame = fixture.nativeElement.querySelector('.crop-frame') as HTMLElement;
    frame.setPointerCapture = vi.fn();
    return frame;
  }

  function pointer(frame: HTMLElement, x: number, y: number): PointerEvent {
    return { currentTarget: frame, pointerId: 1, clientX: x, clientY: y, preventDefault: vi.fn(), stopPropagation: vi.fn() } as unknown as PointerEvent;
  }

  afterEach(() => { fixture?.destroy(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });

  it('keeps the crop keyboard-operable using the pinned ngx bounds engine', () => {
    const component = configure();
    const frame = seedFrame(component);
    const changed = vi.fn();
    component.cropperChange.subscribe(changed);
    frame.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    expect(component['state'].cropper().x1).toBe(23);
    expect(changed).toHaveBeenCalled();
    const previousWidth = component['state'].cropper().x2 - component['state'].cropper().x1;
    frame.querySelector('button')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    const crop = component['state'].cropper();
    expect(crop.x2 - crop.x1).toBeGreaterThan(previousWidth);
    expect((crop.x2 - crop.x1) / (crop.y2 - crop.y1)).toBeCloseTo(2 / 3);
  });

  it('captures a pointer, repositions within bounds and stops on pointer cancellation', () => {
    const component = configure();
    const frame = seedFrame(component);
    component['beginPointer'](pointer(frame, 100, 100), false);
    expect(frame.setPointerCapture).toHaveBeenCalledWith(1);
    component['movePointer'](pointer(frame, 500, 500));
    expect(component['state'].cropper()).toEqual({ x1: 40, y1: 60, x2: 200, y2: 300 });
    frame.dispatchEvent(new Event('pointercancel'));
    component['movePointer'](pointer(frame, 0, 0));
    expect(component['state'].cropper()).toEqual({ x1: 40, y1: 60, x2: 200, y2: 300 });
  });

  it('resizes with a 44px pointer target and preserves the 2:3 ratio', () => {
    const component = configure();
    const frame = seedFrame(component);
    const handle = frame.querySelector('button')!;
    component['beginPointer'](pointer(handle, 100, 100), true);
    component['movePointer'](pointer(frame, 80, 70));
    const crop = component['state'].cropper();
    expect(crop.x2 - crop.x1).toBe(140);
    expect(crop.y2 - crop.y1).toBe(210);
    component['endPointer']();
  });

  it('closes an original bitmap that completes after the editor is destroyed', async () => {
    configure();
    let resolve!: (bitmap: ImageBitmap) => void;
    vi.mocked(createImageBitmap).mockReturnValueOnce(new Promise((finish) => resolve = finish));
    fixture.componentRef.setInput('localFile', new File(['next'], 'next.png', { type: 'image/png' }));
    fixture.detectChanges();
    const failed = vi.fn();
    fixture.componentInstance.loadImageFailed.subscribe(failed);
    fixture.destroy();
    const close = vi.fn();
    resolve({ width: 600, height: 900, close } as unknown as ImageBitmap);
    await Promise.resolve();
    await Promise.resolve();
    expect(close).toHaveBeenCalledOnce();
    expect(failed).not.toHaveBeenCalled();
  });

  it('does not let a stale rotation/source decoder replace the current image', async () => {
    configure();
    let resolve!: (bitmap: ImageBitmap) => void;
    vi.mocked(createImageBitmap).mockReturnValueOnce(new Promise((finish) => resolve = finish));
    fixture.componentRef.setInput('turns', 1);
    fixture.detectChanges();
    fixture.componentRef.setInput('turns', 2);
    fixture.detectChanges();
    const close = vi.fn();
    resolve({ width: 600, height: 900, close } as unknown as ImageBitmap);
    await Promise.resolve();
    await Promise.resolve();
    expect(close).toHaveBeenCalledOnce();
    expect(fixture.componentInstance['state'].loadedImage).toBeUndefined();
  });
});
