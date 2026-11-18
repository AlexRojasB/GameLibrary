import { afterNextRender, Component, computed, DestroyRef, effect, ElementRef, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { ImageTransform } from 'ngx-image-cropper';

import { EXPORT_COVER_IMAGE, PreparedImage } from './image-preparation';
import { CoverCropper } from './cover-cropper';

@Component({
  selector: 'app-cover-editor',
  imports: [CoverCropper],
  templateUrl: './cover-editor.html',
  styleUrl: './cover-editor.scss',
})
export class CoverEditor {
  readonly image = input.required<PreparedImage>();
  readonly initialFit = input(false);
  readonly chosen = output<Blob>();
  readonly canceled = output<void>();

  protected readonly mode = signal<'crop' | 'fit'>('crop');
  protected readonly rotation = signal(0);
  protected readonly transform = signal<ImageTransform>({ scale: 1, translateUnit: '%' });
  protected readonly ready = signal(false);
  protected readonly exporting = signal(false);
  protected readonly error = signal('');
  protected readonly fitUrl = signal('');
  protected readonly fitStyle = computed(() => `rotate(${this.rotation() * 90}deg)`);
  private readonly cropper = viewChild(CoverCropper);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly exportImage = inject(EXPORT_COVER_IMAGE);
  private generation = 0;
  private operation = new AbortController();
  private destroyed = false;

  constructor() {
    effect(() => {
      const image = this.image();
      const fit = this.initialFit();
      untracked(() => {
        this.invalidate();
        this.mode.set(fit ? 'fit' : 'crop');
        this.rotation.set(0);
        this.transform.set({ scale: 1, translateUnit: '%' });
        this.ready.set(false);
        this.error.set('');
        this.releaseUrls();
        try {
          this.fitUrl.set(URL.createObjectURL(image.file));
        } catch {
          this.error.set('Unable to show the image preview. Cancel and choose a smaller image, or use an image URL.');
        }
      });
    });
    afterNextRender(() => this.heading()?.nativeElement.focus());
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.invalidate();
      this.releaseUrls();
    });
  }

  protected cropReady(): void {
    this.ready.set(true);
  }

  protected loadFailed(): void {
    this.ready.set(false);
    this.error.set('Unable to open this image. Try a smaller JPEG or PNG, or use an image URL.');
  }

  protected changeMode(mode: 'crop' | 'fit'): void {
    this.invalidate();
    if (this.mode() !== mode) this.ready.set(false);
    this.mode.set(mode);
    this.transform.set({ scale: 1, translateUnit: '%' });
    this.error.set('');
  }

  protected zoom(value: number): void {
    this.updateTransform({ ...this.transform(), scale: Math.min(4, Math.max(1, value)) });
  }

  protected move(horizontal: number, vertical: number): void {
    const transform = this.transform();
    this.updateTransform({ ...transform, translateUnit: '%', translateH: Math.max(-100, Math.min(100, (transform.translateH ?? 0) + horizontal)), translateV: Math.max(-100, Math.min(100, (transform.translateV ?? 0) + vertical)) });
  }

  protected updateTransform(transform: ImageTransform): void {
    this.invalidate();
    this.transform.set(transform);
  }

  protected cropChanged(): void {
    this.invalidate();
  }

  protected rotate(): void {
    this.invalidate();
    this.rotation.update((value) => (value + 1) % 4);
    this.ready.set(false);
    this.transform.set({ scale: 1, translateUnit: '%' });
    this.error.set('');
  }

  protected reset(): void {
    this.changeMode(this.initialFit() ? 'fit' : 'crop');
    if (this.rotation() !== 0) this.ready.set(false);
    this.rotation.set(0);
    this.cropper()?.resetCropperPosition();
  }

  protected cancel(): void {
    this.invalidate();
    this.canceled.emit();
  }

  protected async useImage(): Promise<void> {
    if (this.exporting() || (this.mode() === 'crop' && !this.ready()) || (this.mode() === 'fit' && !this.fitUrl())) return;
    this.invalidate();
    const generation = this.generation;
    const source = this.image();
    const signal = this.operation.signal;
    this.exporting.set(true);
    this.error.set('');
    let cropUrl: string | undefined;
    try {
      let blob: Blob;
      if (this.mode() === 'fit') {
        blob = await this.exportImage(this.image().file, 'fit', this.rotation(), signal);
      } else {
        const crop = await this.cropper()?.crop('blob');
        cropUrl = crop?.objectUrl ?? undefined;
        if (!crop?.blob) throw new Error('Unable to crop this image. Try resetting the crop.');
        if (this.destroyed || generation !== this.generation || source !== this.image()) return;
        // Cropper zoom scales its canvas. Bound final pixels by source sampling,
        // not the enlarged canvas, to avoid upscaling image content.
        const scale = this.transform().scale ?? 1;
        const sourceWidth = (crop.imagePosition.x2 - crop.imagePosition.x1) / scale;
        const maxWidth = Math.min(crop.width, sourceWidth);
        blob = await this.exportImage(crop.blob, 'crop', 0, signal, { width: maxWidth, height: maxWidth * 3 / 2 });
      }
      if (!this.destroyed && generation === this.generation && source === this.image()) this.chosen.emit(blob);
    } catch (error) {
      if (!this.destroyed && generation === this.generation && !signal.aborted) this.error.set(error instanceof Error ? error.message : 'Unable to export this image. Try a smaller JPEG or PNG.');
    } finally {
      if (cropUrl) URL.revokeObjectURL(cropUrl);
      if (!this.destroyed) this.exporting.set(false);
    }
  }

  private invalidate(): void {
    this.generation++;
    this.operation.abort();
    this.operation = new AbortController();
  }

  private releaseUrls(): void {
    if (this.fitUrl()) URL.revokeObjectURL(this.fitUrl());
    this.fitUrl.set('');
  }
}
