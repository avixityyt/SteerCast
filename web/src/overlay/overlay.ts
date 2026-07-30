import type { InputFrame, OverlayElement, OverlayProfile } from "../shared/types";
import { clamp01, isImageAvailable, required } from "./dom";
import "./styles/overlay.css";

const profileId = document.body.dataset.profile ?? "default";
const isPreview = new URLSearchParams(location.search).has("preview");
const overlay = required<HTMLElement>("overlay");
const wheel = required<HTMLElement>("wheel");
const steeringIndicator = required<HTMLElement>("steering-indicator");
const pedalImages = {
  clutch: required<HTMLImageElement>("pedal-image-clutch"),
  brake: required<HTMLImageElement>("pedal-image-brake"),
  throttle: required<HTMLImageElement>("pedal-image-throttle")
};
const shifterImage = required<HTMLImageElement>("shifter-image");
const steeringValue = required<HTMLOutputElement>("steering-value");
const gear = required<HTMLOutputElement>("gear");
const shiftStick = required<HTMLElement>("shift-stick");
const handbrakeLever = required<HTMLElement>("handbrake-lever");
const handbrakeFill = required<HTMLElement>("handbrake-fill");
const handbrakeValue = required<HTMLOutputElement>("handbrake-value");
const connectionState = required<HTMLElement>("connection-state");
const assetImages = [
  ...document.querySelectorAll<HTMLImageElement>("#wheel-image-stack img, #pedal-image-stack img, #shifter-image-stack img")
];
const pedalChannels = {
  clutch: required<HTMLElement>("pedals-panel").querySelector<HTMLElement>(".clutch")!,
  brake: required<HTMLElement>("pedals-panel").querySelector<HTMLElement>(".brake")!,
  throttle: required<HTMLElement>("pedals-panel").querySelector<HTMLElement>(".throttle")!
};
const buttons = [...document.querySelectorAll<HTMLElement>("[data-bit]")];

let profile: OverlayProfile;
let pendingFrame: InputFrame | null = null;
let renderedSequence = -1;
let lastMessageAt = 0;
let reconnectDelay = 250;

void initialize();
void initializeAssetPack();

window.addEventListener("message", (event) => {
  if (
    event.origin === location.origin
    && event.data?.type === "steercast-preview-profile"
    && event.data.profile
  ) {
    profile = event.data.profile as OverlayProfile;
    applyProfile(profile);
  }
});

async function initialize() {
  const response = await fetch(`/api/profiles/${encodeURIComponent(profileId)}`, { cache: "no-store" });
  if (!response.ok) {
    setConnection(`Profile "${profileId}" was not found`, true);
    return;
  }

  profile = await response.json();
  applyProfile(profile);
  connect();
  requestAnimationFrame(renderLoop);
  setInterval(checkStale, 500);
}

function connect() {
  const protocol = location.protocol === "https:" ? "wss:" : "ws:";
  const socket = new WebSocket(`${protocol}//${location.host}/ws/input/${encodeURIComponent(profileId)}`);

  socket.addEventListener("open", () => {
    reconnectDelay = 250;
    setConnection("Connected", false);
  });
  socket.addEventListener("message", (event) => {
    pendingFrame = JSON.parse(event.data);
    lastMessageAt = performance.now();
  });
  socket.addEventListener("close", () => {
    setConnection("Reconnecting...", true);
    window.setTimeout(connect, reconnectDelay);
    reconnectDelay = Math.min(reconnectDelay * 2, 5000);
  });
  socket.addEventListener("error", () => socket.close());
}

function renderLoop() {
  // Coalesce websocket updates into the browser paint loop to avoid rendering more often than OBS can display.
  const frame = pendingFrame;
  if (frame && frame.sequence !== renderedSequence) {
    renderedSequence = frame.sequence;
    applyFrame(frame);
  }
  requestAnimationFrame(renderLoop);
}

function applyFrame(frame: InputFrame) {
  if (!frame.connected) {
    setConnection("Wheel disconnected", true);
    return;
  }

  setConnection("", false);
  const degrees = frame.steering * (profile.wheelRotationDegrees / 2);
  wheel.style.transform = `rotate(${degrees}deg)`;
  steeringIndicator.style.transform = `translate3d(${frame.steering * 84}px, 0, 0)`;
  steeringValue.value = `${Math.round(degrees)} degrees`;
  setPedal(pedalChannels.clutch, pedalImages.clutch, frame.clutch);
  setPedal(pedalChannels.brake, pedalImages.brake, frame.brake);
  setPedal(pedalChannels.throttle, pedalImages.throttle, frame.throttle);
  setHandbrake(frame.handbrake);
  gear.value = frame.gear === -1 ? "R" : frame.gear === 0 ? "N" : String(frame.gear);
  setShifter(frame.gear);

  const mask = BigInt(Math.max(0, Math.trunc(frame.buttons)));
  for (const button of buttons) {
    const bit = BigInt(Number(button.dataset.bit));
    button.classList.toggle("pressed", (mask & (1n << bit)) !== 0n);
  }
}

function setPedal(element: HTMLElement, assetImage: HTMLElement, value: number) {
  const normalized = clamp01(value);
  const eased = 1 - Math.pow(1 - normalized, 1.7);
  const travel = assetImage.id.includes("throttle") ? 16 : 13;
  const tilt = assetImage.id.includes("throttle") ? 13 : 10;
  const fill = element.querySelector<HTMLElement>(".pedal-fill");
  const output = element.querySelector<HTMLOutputElement>("output");
  element.style.setProperty("--pedal-arm-angle", `${normalized * -4}deg`);
  element.style.setProperty("--pedal-face-angle", `${normalized * 5}deg`);
  element.style.setProperty("--pedal-face-travel", `${normalized * 34}px`);
  assetImage.style.transform = `translate3d(0, ${eased * travel}px, 0) rotateX(${eased * tilt}deg) scaleY(${1 - eased * 0.04})`;
  if (fill) fill.style.setProperty("--pedal-level", String(normalized));
  if (output) output.value = `${Math.round(normalized * 100)}%`;
}

function setHandbrake(value: number) {
  const normalized = clamp01(value);
  handbrakeLever.style.setProperty("--handbrake-angle", `${-14 - normalized * 36}deg`);
  handbrakeFill.style.transform = `scaleY(${normalized})`;
  handbrakeValue.value = `${Math.round(normalized * 100)}%`;
}

function setShifter(value: number) {
  const positions: Record<number, [number, number, number]> = {
    [-1]: [62, 50, 5],
    0: [0, 0, 0],
    1: [-45, -52, -5],
    2: [-45, 52, 5],
    3: [0, -52, 0],
    4: [0, 52, 0],
    5: [45, -52, 5],
    6: [45, 52, -5]
  };
  const [x, y, lean] = positions[value] ?? positions[0];
  shiftStick.style.setProperty("--shift-x", `${x}px`);
  shiftStick.style.setProperty("--shift-y", `${y}px`);
  shiftStick.style.setProperty("--shift-lean", `${lean}deg`);
  shifterImage.style.transform = `translate(-50%, -50%) translate(${x * 0.9}px, ${y * 0.72}px) rotate(${lean}deg)`;
}

async function initializeAssetPack() {
  // If any bundled image fails, keep the SVG/CSS fallback usable instead of showing broken images.
  const loaded = await Promise.all(assetImages.map(isImageAvailable));
  document.body.classList.toggle("asset-pack", loaded.every(Boolean));
}

function applyProfile(value: OverlayProfile) {
  document.documentElement.style.setProperty("--accent", value.theme.accent);
  document.documentElement.style.setProperty("--brake", value.theme.brake);
  document.documentElement.style.setProperty("--surface", value.theme.surface);
  document.documentElement.style.setProperty("--foreground", value.theme.foreground);
  document.documentElement.style.setProperty("--panel-opacity", String(value.theme.opacity));
  document.documentElement.style.setProperty("--canvas-width", `${value.width}px`);
  document.documentElement.style.setProperty("--canvas-height", `${value.height}px`);

  applyElement("wheel-panel", value.layout.wheel);
  applyElement("pedals-panel", value.layout.pedals);
  applyElement("gear-panel", value.layout.gear);
  applyElement("handbrake-panel", value.layout.handbrake, value.handbrakeEnabled);
  applyElement("buttons-panel", value.layout.buttons);
  fitPreview();
}

function fitPreview() {
  if (!isPreview || !profile) return;
  const scale = Math.min(innerWidth / profile.width, innerHeight / profile.height);
  overlay.style.transform = `scale(${scale})`;
}

function applyElement(id: string, element: OverlayElement, forceVisible = true) {
  const node = required<HTMLElement>(id);
  node.style.left = `${element.x}px`;
  node.style.top = `${element.y}px`;
  node.style.setProperty("--element-scale", String(element.scale));
  node.hidden = !forceVisible || !element.visible;
}

function checkStale() {
  if (lastMessageAt && performance.now() - lastMessageAt > 1500) {
    setConnection("Input signal stale", true);
  }
}

function setConnection(message: string, visible: boolean) {
  connectionState.textContent = message;
  connectionState.classList.toggle("visible", visible);
  overlay.classList.toggle("disconnected", visible);
}

window.addEventListener("resize", fitPreview);
