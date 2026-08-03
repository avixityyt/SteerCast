import { expect, test } from "@playwright/test";

test("setup loads the saved profile and OBS URL", async ({ page }) => {
  const errors: Error[] = [];
  page.on("pageerror", (error) => errors.push(error));
  await page.goto("/setup");
  await expect(page.getByRole("heading", { name: "SteerCast" })).toBeVisible();
  await expect(page.locator(".brand-mark")).toHaveAttribute("src", "/brand/app-logo.png");
  await expect(page.getByText("Browser source", { exact: true })).toBeVisible();
  await expect(page.locator(".source-bar code")).toContainText("/overlay/default");
  await expect(page.getByText("Live preview")).toBeVisible();
  await expect(page.getByTitle("Live SteerCast overlay preview")).toBeVisible();
  await expect(page.getByRole("button", { name: "Save changes" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Layout" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Games" })).toBeVisible();
  await expect(page.getByLabel("Handbrake input")).toBeVisible();
  await page.getByRole("button", { name: "Games" }).click();
  await expect(page).toHaveURL(/panel=games/);
  await expect(page.getByRole("heading", { name: "Game integrations" })).toBeVisible();
  await expect(page.locator('select[name="game-integration"]')).toHaveValue("dirt-rally-2");
  await expect(page.getByRole("heading", { name: "Quick setup" })).toBeVisible();
  await expect(page.getByText("Start a stage", { exact: true })).toBeVisible();
  await expect(page.getByText(/less than two minutes/i)).toBeVisible();
  await expect(page.getByLabel("Enable game integration")).toBeAttached();
  await expect(page.getByText("Not FFB", { exact: true })).toBeVisible();
  await expect(page.getByRole("progressbar", { name: "Derived vehicle load" })).toBeVisible();
  await page.getByRole("button", { name: "Layout" }).click();
  await expect(page.getByRole("button", { name: "Reset module positions" })).toBeVisible();
  await page.getByRole("button", { name: "Appearance" }).click();
  await expect(page.getByText("Panel opacity")).toBeVisible();
  await expect(page.getByText("respective owners")).toBeVisible();
  expect(errors).toEqual([]);
});

test("overlay loads profile styling and detailed controls", async ({ page }) => {
  await page.goto("/overlay/default");
  await expect(page.locator("#wheel")).toBeVisible();
  await expect(page.locator(".wheel-instrument")).toBeVisible();
  await expect(page.locator(".wheel-grip")).toBeVisible();
  await expect(page.locator(".steering-gauge")).toBeVisible();
  await expect(page.locator("#steering-value")).toHaveClass(/visually-hidden/);
  await expect(page.locator(".feedback-readout")).toHaveCount(0);
  await expect(page.locator("#pedals-panel")).toBeVisible();
  await expect(page.locator(".pedal-face")).toHaveCount(3);
  await expect(page.locator(".pedal-track")).toHaveCount(3);
  await expect(page.locator(".pedal-meta output")).toHaveCount(3);
  await expect(page.locator(".pedal-meta output").first()).toHaveClass(/visually-hidden/);
  await expect(page.locator("#handbrake-panel")).toBeAttached();
  await expect(page.locator("#handbrake-lever")).toBeAttached();
  await expect(page.locator(".gate-plate")).toBeVisible();
  await expect(page.locator(".shift-knob")).toBeVisible();
  await expect(page.locator("#shift-stick")).toBeVisible();
  await expect(page.locator("#gear")).toHaveText(/^(N|R|[1-6])$/);
  expect(await page.locator(".gear-body").evaluate((element) => getComputedStyle(element, "::before").content))
    .toBe("none");
  await expect(page.locator("html")).toHaveCSS("--accent", /#(?:cedfd9|b09398|ebfcfb|28a9ff)/i);
});

test("setup launch route shows startup splash", async ({ page }) => {
  await page.goto("/setup?launch=1");
  await expect(page.getByRole("status", { name: "SteerCast is starting" })).toBeVisible();
  await expect(page.getByText("Starting local overlay server")).toBeVisible();
});

test("hashed overlay assets are never served stale", async ({ request }) => {
  const htmlResponse = await request.get("/overlay/default");
  expect(htmlResponse.ok()).toBeTruthy();

  const html = await htmlResponse.text();
  const script = html.match(/src="([^"]*overlay-[^"]+\.js)"/)?.[1];
  const stylesheet = html.match(/href="([^"]*assets\/overlay-[^"]+\.css)"/)?.[1];

  expect(script).toBeTruthy();
  expect(stylesheet).toBeTruthy();

  for (const asset of [script!, stylesheet!]) {
    const response = await request.get(asset);
    expect(response.ok()).toBeTruthy();
    expect(response.headers()["cache-control"]).toContain("no-store");
  }
});

test("branding assets are served from the configured path", async ({ request }) => {
  const logoResponse = await request.get("/brand/app-logo.png");
  expect(logoResponse.ok()).toBeTruthy();
  expect(logoResponse.headers()["content-type"]).toContain("image/png");

  const animatedLogoResponse = await request.get("/brand/app-logo.gif");
  expect(animatedLogoResponse.ok()).toBeTruthy();
  expect(animatedLogoResponse.headers()["content-type"]).toContain("image/gif");
});

test("bundled G920 image assets activate overlay image mode", async ({ page, request }) => {
  const imageResponse = await request.get("/assets/g920/wheel.png");
  expect(imageResponse.ok()).toBeTruthy();
  expect(imageResponse.headers()["content-type"]).toContain("image/png");

  await page.goto("/overlay/default");
  await page.waitForFunction(() => document.body.classList.contains("asset-pack"));
  await expect(page.locator(".wheel-mask")).toHaveCount(14);

  const imageState = await page.evaluate(() => ({
    wheelWidth: (document.getElementById("wheel-image") as HTMLImageElement | null)?.naturalWidth ?? 0,
    pedalWidth: (document.getElementById("pedal-base-image") as HTMLImageElement | null)?.naturalWidth ?? 0,
    shifterWidth: (document.getElementById("shifter-base-image") as HTMLImageElement | null)?.naturalWidth ?? 0
  }));

  expect(imageState).toEqual({
    wheelWidth: 512,
    pedalWidth: 618,
    shifterWidth: 486
  });
});

test("overlay pedal visuals use physical input semantics", async ({ page }) => {
  await page.addInitScript(() => {
    class FakeSocket {
      static instance: FakeSocket | undefined;
      private listeners = new Map<string, Array<(event: MessageEvent | Event) => void>>();

      constructor() {
        FakeSocket.instance = this;
        window.setTimeout(() => this.emit("open", new Event("open")), 0);
      }

      addEventListener(type: string, listener: (event: MessageEvent | Event) => void) {
        const listeners = this.listeners.get(type) ?? [];
        listeners.push(listener);
        this.listeners.set(type, listeners);
      }

      close() {
        this.emit("close", new Event("close"));
      }

      emit(type: string, event: MessageEvent | Event) {
        for (const listener of this.listeners.get(type) ?? []) {
          listener(event);
        }
      }
    }

    Object.defineProperty(window, "WebSocket", { value: FakeSocket });
    Object.defineProperty(window, "__sendSteerCastFrame", {
      value(frame: unknown) {
        FakeSocket.instance?.emit("message", new MessageEvent("message", { data: JSON.stringify(frame) }));
      }
    });
  });

  await page.goto("/overlay/default");
  await page.waitForFunction(() => document.body.classList.contains("asset-pack"));

  const frame = {
    sequence: 1,
    timestamp: Date.now(),
    deviceId: "test",
    connected: true,
    steering: 1,
    throttle: 1,
    brake: 1,
    clutch: 1,
    handbrake: 0,
    gear: 0,
    buttons: 0,
    gameTelemetryStrength: 0.68,
    gameTelemetryDirection: -1,
    gameTelemetryKind: "derived-telemetry",
    gameTelemetrySource: "dirt-rally-2-udp"
  };

  await page.evaluate((value) => (window as unknown as { __sendSteerCastFrame(frame: unknown): void }).__sendSteerCastFrame(value), frame);
  await page.waitForFunction(() => (document.getElementById("pedal-image-throttle") as HTMLElement).style.transform.includes("translate3d"));

  expect(await page.locator("#pedal-image-throttle").evaluate((element) => (element as HTMLElement).style.transform))
    .toBe("translate3d(0px, 16px, 0px) rotateX(13deg) scaleY(0.96)");
  expect(await page.locator("#steering-indicator").evaluate((element) => (element as HTMLElement).style.transform))
    .toBe("translate3d(84px, 0px, 0px)");
  const throttleRail = await page.locator(".throttle .pedal-track").boundingBox();
  const brakeRail = await page.locator(".brake .pedal-track").boundingBox();
  const clutchRail = await page.locator(".clutch .pedal-track").boundingBox();
  expect(throttleRail).not.toBeNull();
  expect(brakeRail).not.toBeNull();
  expect(clutchRail).not.toBeNull();
  expect(brakeRail!.x).toBeGreaterThan(clutchRail!.x);
  expect(throttleRail!.x).toBeGreaterThan(brakeRail!.x);
  expect(throttleRail!.height).toBeGreaterThan(throttleRail!.width * 5);
  await expect(page.locator(".feedback-readout")).toHaveCount(0);
  await expect(page.locator("#game-telemetry")).toBeVisible();
  await expect(page.locator("#game-telemetry-value")).toHaveText("68%");
});

test("setup remains usable at a narrow desktop width", async ({ page }) => {
  await page.setViewportSize({ width: 900, height: 900 });
  await page.goto("/setup");
  await expect(page.getByRole("heading", { name: "SteerCast" })).toBeVisible();
  await expect(page.getByTitle("Live SteerCast overlay preview")).toBeVisible();
  await page.getByRole("button", { name: "OBS setup" }).click();
  await expect(page.getByRole("button", { name: "Copy Browser source URL" })).toBeVisible();
});

test("setup keeps layout handles aligned after changing OBS canvas size", async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1000 });
  await page.goto("/setup");
  await page.getByRole("button", { name: "Appearance" }).click();
  await page.getByLabel("Width").fill("800");
  await page.getByLabel("Height").fill("800");
  await expect(page.locator(".source-meta")).toContainText("800 x 800");

  const canvas = await page.locator(".canvas").boundingBox();
  expect(canvas).not.toBeNull();
  expect(Math.abs(canvas!.width - canvas!.height)).toBeLessThanOrEqual(2);

  const handle = await page.locator(".layout-handle.wheel").boundingBox();
  const rendered = await page.frameLocator(".overlay-preview").locator("#wheel-panel").boundingBox();
  expect(handle).not.toBeNull();
  expect(rendered).not.toBeNull();
  expect(Math.abs(handle!.x - rendered!.x)).toBeLessThanOrEqual(2);
  expect(Math.abs(handle!.y - rendered!.y)).toBeLessThanOrEqual(2);
  expect(Math.abs(handle!.width - rendered!.width)).toBeLessThanOrEqual(3);
  expect(Math.abs(handle!.height - rendered!.height)).toBeLessThanOrEqual(3);

  for (const box of await page.locator(".layout-handle").evaluateAll((elements) =>
    elements
      .filter((element) => getComputedStyle(element).display !== "none")
      .map((element) => {
        const rect = element.getBoundingClientRect();
        return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
      })
  )) {
    expect(box.x).toBeGreaterThanOrEqual(canvas!.x - 1);
    expect(box.y).toBeGreaterThanOrEqual(canvas!.y - 1);
    expect(box.x + box.width).toBeLessThanOrEqual(canvas!.x + canvas!.width + 1);
    expect(box.y + box.height).toBeLessThanOrEqual(canvas!.y + canvas!.height + 1);
  }
});
