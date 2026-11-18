import { canvasBlob, checkSourceSize, coverDimensions, decodeLocalImage, exportCover, inspectImage, prepareImage } from './image-preparation';

function png(width = 600, height = 900, animated = false): ArrayBuffer {
  const bytes = new Uint8Array(animated ? 45 : 33);
  bytes.set([137, 80, 78, 71, 13, 10, 26, 10]);
  const view = new DataView(bytes.buffer);
  view.setUint32(8, 13);
  bytes.set([73, 72, 68, 82], 12);
  view.setUint32(16, width);
  view.setUint32(20, height);
  if (animated) bytes.set([97, 99, 84, 76], 37);
  return bytes.buffer;
}

function source(bytes = png()): File {
  const file = new File([bytes], 'camera.heic', { type: 'application/octet-stream' });
  Object.defineProperty(file, 'arrayBuffer', { value: vi.fn().mockResolvedValue(bytes) });
  return file;
}

describe('cover source inspection', () => {
  afterEach(() => vi.restoreAllMocks());

  it('uses actual PNG bytes rather than filename or MIME', () => {
    expect(inspectImage(png())).toEqual({ width: 600, height: 900 });
  });

  it('rejects animation chunks, unsupported formats and truncated headers before decode', () => {
    expect(() => inspectImage(png(600, 900, true))).toThrow(/Animated PNG/);
    for (const text of ['GIF89a', '<svg/>', 'ftypheic', '%PDF', 'ftypavif']) {
      expect(() => inspectImage(new TextEncoder().encode(text).buffer)).toThrow(/JPEG, PNG or static WebP/);
    }
    expect(() => inspectImage(new Uint8Array([255, 216, 255, 255, 255]).buffer)).toThrow();
  });

  it('inspects JPEG SOF dimensions', () => {
    const bytes = new Uint8Array([255, 216, 255, 192, 0, 8, 8, 3, 132, 2, 88, 1]);
    expect(inspectImage(bytes.buffer)).toEqual({ width: 600, height: 900 });
  });

  it('inspects static WebP and rejects its animation flag', () => {
    const bytes = new Uint8Array(30);
    bytes.set(new TextEncoder().encode('RIFF'), 0);
    bytes.set(new TextEncoder().encode('WEBPVP8X'), 8);
    const view = new DataView(bytes.buffer);
    view.setUint32(16, 10, true);
    bytes.set([87, 2, 0, 131, 3, 0], 24);
    expect(inspectImage(bytes.buffer)).toEqual({ width: 600, height: 900 });
    bytes[20] = 2;
    expect(() => inspectImage(bytes.buffer)).toThrow(/Animated WebP/);
  });

  it('bounds both edges and total pixels before allocating a raster', () => {
    expect(() => inspectImage(png(8193, 100))).toThrow(/8192/);
    expect(() => inspectImage(png(6000, 4001))).toThrow(/24 megapixels/);
    expect(() => checkSourceSize({ width: 0, height: 100 })).toThrow();
    expect(() => checkSourceSize({ width: 6000, height: 4000 })).not.toThrow();
  });

  it('checks byte size before reading or decoding the file', async () => {
    const file = source();
    Object.defineProperty(file, 'size', { value: 20 * 1024 * 1024 + 1 });
    await expect(prepareImage(file, new AbortController().signal)).rejects.toThrow(/20 MiB/);
    expect(file.arrayBuffer).not.toHaveBeenCalled();
    await expect(prepareImage(new File([], 'empty.jpg'), new AbortController().signal)).rejects.toThrow(/non-empty/);
  });
});

describe('cover output dimensions', () => {
  it('caps at 960 x 1440 with canonical integer 2k:3k dimensions', () => {
    expect(coverDimensions(2400, 3600)).toEqual({ width: 960, height: 1440 });
    expect(coverDimensions(161, 242)).toEqual({ width: 160, height: 240 });
    expect(coverDimensions(961, 1441)).toEqual({ width: 960, height: 1440 });
  });

  it('accepts one pixel of rounding and rejects materially different ratios', () => {
    expect(coverDimensions(600, 901)).toEqual({ width: 600, height: 900 });
    expect(() => coverDimensions(600, 902)).toThrow(/2:3/);
    expect(() => coverDimensions(900, 600)).toThrow(/2:3/);
    expect(() => coverDimensions(158, 237)).toThrow(/too small/);
    expect(() => coverDimensions(NaN, 240)).toThrow();
  });

  it('fits square or landscape artwork without cropping or upscaling content', () => {
    expect(coverDimensions(600, 600, true)).toEqual({ width: 600, height: 900 });
    expect(coverDimensions(1200, 600, true)).toEqual({ width: 960, height: 1440 });
    expect(coverDimensions(160, 160, true)).toEqual({ width: 160, height: 240 });
  });
});

describe('local raster preparation and export', () => {
  let context: { drawImage: ReturnType<typeof vi.fn>; fillRect: ReturnType<typeof vi.fn>; translate: ReturnType<typeof vi.fn>; rotate: ReturnType<typeof vi.fn>; fillStyle: string };
  let close: ReturnType<typeof vi.fn>;
  let canvases: HTMLCanvasElement[];

  beforeEach(() => {
    context = { drawImage: vi.fn(), fillRect: vi.fn(), translate: vi.fn(), rotate: vi.fn(), fillStyle: '' };
    close = vi.fn();
    canvases = [];
    vi.stubGlobal('createImageBitmap', vi.fn().mockResolvedValue({ width: 600, height: 900, close }));
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(function (this: HTMLCanvasElement) {
      canvases.push(this);
      return context as unknown as CanvasRenderingContext2D;
    });
    vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation((_callback, _type) => _callback(new Blob(['fresh raster'], { type: _type })));
  });

  afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });

  it('honors orientation, reduces the working edge to 2048 and releases original pixels before encoding', async () => {
    vi.mocked(createImageBitmap).mockResolvedValue({ width: 4000, height: 6000, close } as unknown as ImageBitmap);
    vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation((callback, type) => {
      expect(close).toHaveBeenCalledOnce();
      callback(new Blob(['new pixels'], { type }));
    });
    const file = source(png(4000, 6000));
    const result = await prepareImage(file, new AbortController().signal);
    expect(createImageBitmap).toHaveBeenCalledWith(file, { imageOrientation: 'from-image' });
    expect(result.width).toBe(1365);
    expect(result.height).toBe(2048);
    expect(result.file.type).toBe('image/png');
    expect(result.file.name).toBe('working-cover.png');
    expect(result.file).not.toBe(file);
    expect(canvases[0].width).toBe(0);
  });

  it('does not upscale small working sources', async () => {
    const result = await prepareImage(source(), new AbortController().signal);
    expect([result.width, result.height]).toEqual([600, 900]);
  });

  it('rejects over-limit dimensions before browser decode', async () => {
    await expect(prepareImage(source(png(9000, 100)), new AbortController().signal)).rejects.toThrow(/8192/);
    expect(createImageBitmap).not.toHaveBeenCalled();
  });

  it('closes a late bitmap when canceled during decode', async () => {
    const controller = new AbortController();
    let finish!: (bitmap: ImageBitmap) => void;
    vi.mocked(createImageBitmap).mockReturnValue(new Promise((resolve) => finish = resolve));
    const pending = decodeLocalImage(new Blob(), controller.signal);
    controller.abort();
    finish({ width: 600, height: 900, close } as unknown as ImageBitmap);
    await expect(pending).rejects.toThrow();
    expect(close).toHaveBeenCalledOnce();
  });

  it('releases bitmaps and canvas on draw and export failures', async () => {
    context.drawImage.mockImplementationOnce(() => { throw new Error('allocation'); });
    await expect(prepareImage(source(), new AbortController().signal)).rejects.toThrow();
    expect(close).toHaveBeenCalledOnce();
    expect(canvases[0].width).toBe(0);
    vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation((callback) => callback(null));
    await expect(exportCover(new Blob(), 'crop', 0, new AbortController().signal)).rejects.toThrow(/export/);
    expect(close).toHaveBeenCalledTimes(2);
    expect(canvases[1].width).toBe(0);
  });

  it('exports whole square art on white as a new JPEG Blob at quality 90', async () => {
    const raster = { width: 600, height: 600, close } as unknown as ImageBitmap;
    vi.mocked(createImageBitmap).mockResolvedValue(raster);
    const original = new Blob(['metadata and original bytes']);
    const result = await exportCover(original, 'fit', 0, new AbortController().signal);
    expect(result.type).toBe('image/jpeg');
    expect(result).not.toBe(original);
    expect(context.fillStyle).toBe('#ffffff');
    expect(context.fillRect).toHaveBeenCalledWith(0, 0, 600, 900);
    expect(context.drawImage).toHaveBeenCalledWith(raster, -300, -300, 600, 600);
    expect(HTMLCanvasElement.prototype.toBlob).toHaveBeenCalledWith(expect.any(Function), 'image/jpeg', 0.9);
    expect(close).toHaveBeenCalledOnce();
  });

  it('applies 90-degree rotation before choosing Fit dimensions', async () => {
    vi.mocked(createImageBitmap).mockResolvedValue({ width: 900, height: 600, close } as unknown as ImageBitmap);
    await exportCover(new Blob(), 'fit', 1, new AbortController().signal);
    expect(context.fillRect).toHaveBeenCalledWith(0, 0, 600, 900);
    expect(context.rotate).toHaveBeenCalledWith(Math.PI / 2);
  });

  it('rejects crop ratio errors even when a caller supplies a sampling cap', async () => {
    vi.mocked(createImageBitmap).mockResolvedValue({ width: 600, height: 600, close } as unknown as ImageBitmap);
    await expect(exportCover(new Blob(), 'crop', 0, new AbortController().signal, { width: 160, height: 240 })).rejects.toThrow(/2:3/);
  });

  it('caps zoomed crop output to its source sampling without upscaling', async () => {
    await exportCover(new Blob(), 'crop', 0, new AbortController().signal, { width: 300, height: 450 });
    expect(context.fillRect).toHaveBeenCalledWith(0, 0, 300, 450);
  });

  it('ignores an export that finishes after cancellation and releases its canvas', async () => {
    const controller = new AbortController();
    vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation((callback, type) => {
      controller.abort();
      callback(new Blob(['late'], { type }));
    });
    await expect(exportCover(new Blob(), 'crop', 0, controller.signal)).rejects.toThrow();
    expect(close).toHaveBeenCalledOnce();
    expect(canvases[0].width).toBe(0);
  });

  it('reports browser format fallback instead of accepting a non-JPEG export', async () => {
    vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation((callback) => callback(new Blob(['png'], { type: 'image/png' })));
    await expect(canvasBlob(document.createElement('canvas'))).rejects.toThrow(/export/);
  });
});
