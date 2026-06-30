export function required<T extends Element>(id: string): T {
  const element = document.getElementById(id);
  if (!element) throw new Error(`Missing #${id}`);
  return element as unknown as T;
}

export function clamp01(value: number) {
  return Number.isFinite(value) ? Math.max(0, Math.min(1, value)) : 0;
}

export function isImageAvailable(image: HTMLImageElement) {
  if (image.complete) {
    return Promise.resolve(image.naturalWidth > 0 && image.naturalHeight > 0);
  }

  return new Promise<boolean>((resolve) => {
    image.addEventListener("load", () => resolve(image.naturalWidth > 0 && image.naturalHeight > 0), { once: true });
    image.addEventListener("error", () => resolve(false), { once: true });
  });
}
