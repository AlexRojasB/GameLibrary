export interface ImageSize {
  width: number;
  height: number;
}

export interface PreparedImage extends ImageSize {
  file: File;
}

const unsupported = 'Choose a JPEG, PNG or static WebP image. Export HEIC/HEIF, GIF, SVG or other formats as JPEG or PNG first.';

export function checkSourceSize(size: ImageSize): void {
  if (!Number.isInteger(size.width) || !Number.isInteger(size.height) || size.width < 1 || size.height < 1 ||
      Math.max(size.width, size.height) > 8192 || size.width * size.height > 24_000_000) {
    throw new Error('Choose an image up to 24 megapixels with no edge larger than 8192 pixels.');
  }
}

// Inspect container dimensions/animation before asking the browser to allocate a raster.
// This is a local resource guard, not a replacement for authoritative server validation.
export function inspectImage(bytes: ArrayBuffer): ImageSize {
  const b = new Uint8Array(bytes);
  const v = new DataView(bytes);
  const text = (offset: number, count: number) => String.fromCharCode(...b.subarray(offset, offset + count));
  let size: ImageSize | undefined;
  if (b[0] === 0xff && b[1] === 0xd8) {
    let p = 2;
    while (p + 4 <= b.length) {
      if (b[p++] !== 0xff) break;
      while (b[p] === 0xff) p++;
      if (p + 3 > b.length) break;
      const marker = b[p++];
      if (marker === 0xda || marker === 0xd9) break;
      const length = v.getUint16(p);
      if (length < 2 || p + length > b.length) break;
      if ([0xc0, 0xc1, 0xc2].includes(marker) && length >= 8) {
        size = { width: v.getUint16(p + 5), height: v.getUint16(p + 3) };
        break;
      }
      p += length;
    }
  } else if (text(0, 8) === '\x89PNG\r\n\x1a\n') {
    let p = 8;
    while (p + 12 <= b.length) {
      const length = v.getUint32(p);
      if (p + 12 + length > b.length) break;
      const type = text(p + 4, 4);
      if (type === 'acTL' || type === 'fcTL' || type === 'fdAT') throw new Error('Animated PNG is not supported. Export a single still image first.');
      if (type === 'IHDR' && length === 13) size = { width: v.getUint32(p + 8), height: v.getUint32(p + 12) };
      p += length + 12;
    }
  } else if (text(0, 4) === 'RIFF' && text(8, 4) === 'WEBP') {
    let p = 12;
    while (p + 8 <= b.length) {
      const length = v.getUint32(p + 4, true);
      if (p + 8 + length > b.length) break;
      const type = text(p, 4);
      const d = p + 8;
      if (type === 'ANIM' || type === 'ANMF' || (type === 'VP8X' && (b[d] & 2))) throw new Error('Animated WebP is not supported. Export a single still image first.');
      if (type === 'VP8X' && length >= 10) size = { width: 1 + b[d + 4] + (b[d + 5] << 8) + (b[d + 6] << 16), height: 1 + b[d + 7] + (b[d + 8] << 8) + (b[d + 9] << 16) };
      if (type === 'VP8 ' && length >= 10 && text(d + 3, 3) === '\x9d\x01\x2a') size ??= { width: v.getUint16(d + 6, true) & 0x3fff, height: v.getUint16(d + 8, true) & 0x3fff };
      if (type === 'VP8L' && length >= 5 && b[d] === 0x2f) {
        const bits = v.getUint32(d + 1, true);
        size ??= { width: 1 + (bits & 0x3fff), height: 1 + ((bits >>> 14) & 0x3fff) };
      }
      p += 8 + length + (length % 2);
    }
  }
  if (!size) throw new Error(unsupported);
  checkSourceSize(size);
  return size;
}

export async function decodeLocalImage(blob: Blob, signal: AbortSignal): Promise<{ image: CanvasImageSource; width: number; height: number; release: () => void }> {
  signal.throwIfAborted();
  if (typeof createImageBitmap === 'function') {
    let bitmap: ImageBitmap;
    try {
      bitmap = await createImageBitmap(blob, { imageOrientation: 'from-image' });
    } catch {
      throw new Error('Unable to decode this image. Try a smaller image or export it as JPEG or PNG.');
    }
    if (signal.aborted) {
      bitmap.close();
      signal.throwIfAborted();
    }
    return { image: bitmap, width: bitmap.width, height: bitmap.height, release: () => bitmap.close() };
  }
  const url = URL.createObjectURL(blob);
  const image = new Image();
  const release = () => { image.src = ''; URL.revokeObjectURL(url); };
  try {
    await new Promise<void>((resolve, reject) => {
      const abort = () => { image.onload = image.onerror = null; release(); reject(new DOMException('Canceled', 'AbortError')); };
      const finish = (error?: Error) => {
        signal.removeEventListener('abort', abort);
        image.onload = image.onerror = null;
        if (error) reject(error); else resolve();
      };
      signal.addEventListener('abort', abort, { once: true });
      image.onload = () => finish();
      image.onerror = () => finish(new Error('Unable to decode this image. Try exporting it as JPEG or PNG.'));
      image.src = url;
    });
    signal.throwIfAborted();
    return { image, width: image.naturalWidth, height: image.naturalHeight, release };
  } catch (error) {
    release();
    throw error;
  }
}

export function canvasBlob(canvas: HTMLCanvasElement, type = 'image/jpeg'): Promise<Blob> {
  return new Promise((resolve, reject) => {
    canvas.toBlob((blob) => {
      if (blob && blob.size > 0 && blob.type === type) resolve(blob);
      else reject(new Error('Unable to export this image. Try a smaller JPEG or PNG.'));
    }, type, 0.9);
  });
}

export async function prepareImage(file: File, signal: AbortSignal): Promise<PreparedImage> {
  if (file.size === 0 || file.size > 20 * 1024 * 1024) throw new Error('Choose a non-empty image no larger than 20 MiB.');
  const bytes = await file.arrayBuffer();
  signal.throwIfAborted();
  inspectImage(bytes);
  const decoded = await decodeLocalImage(file, signal);
  const canvas = document.createElement('canvas');
  try {
    try {
      checkSourceSize(decoded);
      const scale = Math.min(1, 2048 / Math.max(decoded.width, decoded.height));
      canvas.width = Math.max(1, Math.floor(decoded.width * scale));
      canvas.height = Math.max(1, Math.floor(decoded.height * scale));
      const context = canvas.getContext('2d');
      if (!context) throw new Error('Image editing is unavailable in this browser. Use an image URL instead.');
      context.drawImage(decoded.image, 0, 0, canvas.width, canvas.height);
    } finally {
      decoded.release();
    }
    const blob = await canvasBlob(canvas, 'image/png');
    signal.throwIfAborted();
    return { file: new File([blob], 'working-cover.png', { type: 'image/png' }), width: canvas.width, height: canvas.height };
  } finally {
    canvas.width = canvas.height = 0;
  }
}

export function coverDimensions(width: number, height: number, fit = false): ImageSize {
  if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0) throw new Error('Invalid image dimensions.');
  if (!fit && Math.abs(height - Math.round(width * 3 / 2)) > 1) throw new Error('Choose a portrait crop with a 2:3 ratio.');
  const k = Math.min(480, Math.floor(fit ? Math.max(width / 2, height / 3) : Math.min(width / 2, height / 3)));
  if (k < 80) throw new Error('This selection is too small. Choose a larger image or crop of at least 160 x 240 pixels.');
  return { width: 2 * k, height: 3 * k };
}

export async function exportCover(blob: Blob, mode: 'crop' | 'fit', rotation: number, signal: AbortSignal, maxSize?: ImageSize): Promise<Blob> {
  const decoded = await decodeLocalImage(blob, signal);
  const canvas = document.createElement('canvas');
  try {
    const sideways = rotation % 2 !== 0;
    const width = sideways ? decoded.height : decoded.width;
    const height = sideways ? decoded.width : decoded.height;
    if (mode === 'crop') coverDimensions(width, height);
    const size = coverDimensions(maxSize?.width ?? width, maxSize?.height ?? height, mode === 'fit');
    canvas.width = size.width;
    canvas.height = size.height;
    const context = canvas.getContext('2d');
    if (!context) throw new Error('Image export is unavailable in this browser.');
    context.fillStyle = '#ffffff';
    context.fillRect(0, 0, size.width, size.height);
    const scale = Math.min(1, size.width / width, size.height / height);
    context.translate(size.width / 2, size.height / 2);
    context.rotate(rotation * Math.PI / 2);
    context.drawImage(decoded.image, -decoded.width * scale / 2, -decoded.height * scale / 2, decoded.width * scale, decoded.height * scale);
    const result = await canvasBlob(canvas);
    signal.throwIfAborted();
    if (result.size > 5 * 1024 * 1024) throw new Error('The edited image is too large. Choose a smaller crop.');
    return result;
  } finally {
    decoded.release();
    canvas.width = canvas.height = 0;
  }
}
import { InjectionToken } from '@angular/core';

export const PREPARE_COVER_IMAGE = new InjectionToken<typeof prepareImage>('PREPARE_COVER_IMAGE', { providedIn: 'root', factory: () => prepareImage });
export const EXPORT_COVER_IMAGE = new InjectionToken<typeof exportCover>('EXPORT_COVER_IMAGE', { providedIn: 'root', factory: () => exportCover });
