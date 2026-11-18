import { vi } from 'vitest';

export function mockCoverVisibility() {
  const observers: CoverObserver[] = [];
  class CoverObserver {
    readonly disconnect = vi.fn();
    target: Element | null = null;
    constructor(private readonly callback: IntersectionObserverCallback, readonly options: IntersectionObserverInit) {
      observers.push(this);
    }
    observe(target: Element): void {
      this.target = target;
    }
    show(visible = true): void {
      this.callback([{ target: this.target, isIntersecting: visible } as IntersectionObserverEntry], this as unknown as IntersectionObserver);
    }
  }
  vi.stubGlobal('IntersectionObserver', CoverObserver);
  return observers;
}
