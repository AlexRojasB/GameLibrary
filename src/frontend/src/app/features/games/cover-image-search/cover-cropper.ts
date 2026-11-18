import { Component, effect, inject, input, OnDestroy, signal } from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';
import { ImageCropperComponent } from 'ngx-image-cropper';

import { canvasBlob, decodeLocalImage } from './image-preparation';

// The pinned cropper supplies crop bounds, keyboard interaction and Blob export.
// Its stock loader has no cancellation/URL disposal API. Own only local raster
// loading and pointer capture here instead of patching its private loader/global URL APIs.
@Component({
  selector: 'app-cover-cropper',
  template: `
    <div #wrapper class="crop-surface">
        <img #sourceImage [src]="safeImgDataUrl()" [hidden]="!safeImgDataUrl()" alt="Local cover artwork" draggable="false"
          [style.transform]="safeTransformStyle()" (load)="imageLoadedInView()" />
        @if (frameReady()) {
          <div class="crop-frame" tabindex="0" role="group"
            aria-label="Cover crop. Arrow keys move; Shift and arrow keys resize."
            [style.left.px]="state.cropper().x1" [style.top.px]="state.cropper().y1"
            [style.width.px]="state.cropper().x2 - state.cropper().x1"
            [style.height.px]="state.cropper().y2 - state.cropper().y1"
            (keydown)="keyboardAccess($event)" (pointerdown)="beginPointer($event, false)"
            (pointermove)="movePointer($event)" (pointerup)="endPointer()" (pointercancel)="endPointer()" (lostpointercapture)="endPointer()">
            <button type="button" class="crop-handle" aria-label="Resize cover crop. Use Shift and arrow keys."
              (keydown)="resizeKey($event)" (pointerdown)="beginPointer($event, true)">Resize</button>
          </div>
        }
    </div>
  `,
  styles: `
    :host { display:block; width:100%; min-width:0; }
    .crop-surface { position:relative; max-width:100%; width:fit-content; margin:auto; overflow:hidden; background:#fff; }
    img { display:block; max-width:100%; max-height:60vh; width:auto; height:auto; transform-origin:center; }
    .crop-frame { position:absolute; box-sizing:border-box; border:2px solid #fff; box-shadow:0 0 0 9999px #0006; touch-action:none; cursor:move; }
    .crop-frame:focus-visible { outline:3px solid var(--color-accent-strong); outline-offset:-5px; }
    .crop-handle { position:absolute; right:0; bottom:0; min-width:44px; min-height:44px; touch-action:none; font-size:0.75rem; padding:0.2rem; cursor:se-resize; }
  `,
})
export class CoverCropper extends ImageCropperComponent implements OnDestroy {
  readonly localFile = input.required<File>();
  readonly turns = input(0);
  protected readonly frameReady = signal(false);
  private drag: { x: number; y: number; resize: boolean; pointer: number } | null = null;

  constructor() {
    super(inject(DomSanitizer));
    effect((onCleanup) => {
      const file = this.localFile();
      const rotation = this.turns();
      const controller = new AbortController();
      const image = new Image();
      let url: string | null = null;
      this.imageVisible = false;
      this.frameReady.set(false);
      this.safeImgDataUrl.set(undefined);
      this.state.loadedImage = undefined;
      const canvas = document.createElement('canvas');
      onCleanup(() => {
        controller.abort();
        image.onload = image.onerror = null;
        image.src = '';
        if (url) URL.revokeObjectURL(url);
        canvas.width = canvas.height = 0;
        this.state.loadedImage = undefined;
      });
      void (async () => {
        try {
          const decoded = await decodeLocalImage(file, controller.signal);
          try {
            canvas.width = rotation % 2 ? decoded.height : decoded.width;
            canvas.height = rotation % 2 ? decoded.width : decoded.height;
            const context = canvas.getContext('2d');
            if (!context) throw new Error('Canvas unavailable');
            context.translate(canvas.width / 2, canvas.height / 2);
            context.rotate(rotation * Math.PI / 2);
            context.drawImage(decoded.image, -decoded.width / 2, -decoded.height / 2);
          } finally { decoded.release(); }
          const blob = await canvasBlob(canvas, 'image/png');
          canvas.width = canvas.height = 0;
          controller.signal.throwIfAborted();
          url = URL.createObjectURL(blob);
          image.onload = () => {
            if (controller.signal.aborted) return;
            const raster = { objectUrl: url!, image, size: { width: image.naturalWidth, height: image.naturalHeight } };
            this.state.loadedImage = { original: raster, transformed: raster, exifTransform: { rotate: 0, flip: false } };
            this.safeImgDataUrl.set(url!);
          };
          image.onerror = () => { if (!controller.signal.aborted) this.loadImageFailed.emit(); };
          image.src = url;
        } catch {
          if (!controller.signal.aborted) this.loadImageFailed.emit();
        } finally { canvas.width = canvas.height = 0; }
      })();
    });
  }

  override imageLoadedInView(): void {
    const element = this.sourceImage?.nativeElement;
    if (!element || !this.state.loadedImage) return;
    const { width, height } = element.getBoundingClientRect();
    if (width <= 1 || height <= 1) return;
    this.state.setMaxSize(width, height);
    this.resetCropperPosition();
    this.imageVisible = true;
    this.frameReady.set(true);
    this.cropperReady.emit({ width, height });
  }

  protected beginPointer(event: PointerEvent, resize: boolean): void {
    if (this.disabled || this.drag) return;
    event.preventDefault();
    event.stopPropagation();
    const frame = resize ? (event.currentTarget as HTMLElement).parentElement! : event.currentTarget as HTMLElement;
    frame.focus();
    frame.setPointerCapture(event.pointerId);
    this.drag = { x: event.clientX, y: event.clientY, resize, pointer: event.pointerId };
  }

  protected movePointer(event: PointerEvent): void {
    const drag = this.drag;
    if (!drag || drag.pointer !== event.pointerId) return;
    const crop = this.state.cropper();
    const bounds = this.state.maxSize();
    const dx = event.clientX - drag.x;
    const dy = event.clientY - drag.y;
    if (drag.resize) {
      const maxWidth = Math.min(bounds.width - crop.x1, (bounds.height - crop.y1) * 2 / 3);
      const width = Math.min(maxWidth, Math.max(Math.min(44, maxWidth), crop.x2 - crop.x1 + (Math.abs(dx) > Math.abs(dy) ? dx : dy * 2 / 3)));
      this.state.cropper.set({ ...crop, x2: crop.x1 + width, y2: crop.y1 + width * 3 / 2 });
    } else {
      const width = crop.x2 - crop.x1;
      const height = crop.y2 - crop.y1;
      const x1 = Math.min(bounds.width - width, Math.max(0, crop.x1 + dx));
      const y1 = Math.min(bounds.height - height, Math.max(0, crop.y1 + dy));
      this.state.cropper.set({ x1, y1, x2: x1 + width, y2: y1 + height });
    }
    this.drag = { ...drag, x: event.clientX, y: event.clientY };
    this.cropperChange.emit(this.state.cropper());
  }

  protected endPointer(): void { this.drag = null; }

  protected resizeKey(event: KeyboardEvent): void {
    if (!event.key.startsWith('Arrow')) return;
    event.preventDefault();
    event.stopPropagation();
    this.keyboardAccess(new KeyboardEvent('keydown', { key: event.key, shiftKey: true }));
  }

  override ngOnDestroy(): void {
    this.drag = null;
    super.ngOnDestroy();
  }
}
