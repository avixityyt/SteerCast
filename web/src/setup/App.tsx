import type { ComponentChildren } from "preact";
import { useEffect, useMemo, useRef, useState } from "preact/hooks";
import type { DeviceDescriptor, ForceFeedbackStatus, HealthResponse, OverlayElement, OverlayProfile, RawDeviceReading } from "../shared/types";
import {
  axisRows,
  elementNames,
  elementSizes,
  minimalLayout,
  themes,
  type DragState,
  type ElementName,
  type PanelName
} from "./config";

const minCanvasWidth = 320;
const minCanvasHeight = 240;
const maxCanvasWidth = 3840;
const maxCanvasHeight = 2160;
const minElementScale = 0.4;
const maxElementScale = 2;

export default function App() {
  const [profiles, setProfiles] = useState<OverlayProfile[]>([]);
  const [devices, setDevices] = useState<DeviceDescriptor[]>([]);
  const [selectedId, setSelectedId] = useState("default");
  const [profile, setProfile] = useState<OverlayProfile | null>(null);
  const [panel, setPanel] = useState<PanelName>("device");
  const [status, setStatus] = useState("Loading");
  const [appVersion, setAppVersion] = useState("dev");
  const [ffbStatus, setFfbStatus] = useState<ForceFeedbackStatus | null>(null);
  const [dragging, setDragging] = useState<DragState | null>(null);
  const [calibrating, setCalibrating] = useState<string | null>(null);
  const [showLaunchSplash, setShowLaunchSplash] = useState(() => new URLSearchParams(location.search).has("launch"));
  const previewRef = useRef<HTMLIFrameElement>(null);

  const overlayUrl = useMemo(
    () => `${location.origin}/overlay/${profile?.id ?? "default"}`,
    [profile?.id]
  );

  useEffect(() => {
    void initialize();
  }, []);

  useEffect(() => {
    document.title = `SteerCast v${appVersion} · Setup`;
  }, [appVersion]);

  useEffect(() => {
    if (!showLaunchSplash) return;
    const timeout = window.setTimeout(() => setShowLaunchSplash(false), 1200);
    return () => window.clearTimeout(timeout);
  }, [showLaunchSplash]);

  useEffect(() => {
    const next = profiles.find((candidate) => candidate.id === selectedId) ?? profiles[0];
    if (next) {
      setProfile(normalizeProfileForEditor(structuredClone(next)));
      setSelectedId(next.id);
      setStatus("");
    }
  }, [profiles, selectedId]);

  useEffect(() => {
    if (profile) {
      postPreview(profile);
    }
  }, [profile]);

  async function initialize() {
    try {
      await Promise.all([loadProfiles(), loadDevices(), loadHealth(), loadFfbStatus()]);
    } catch {
      setStatus("SteerCast could not load its local settings. Restart the app.");
    }
  }

  async function loadHealth() {
    const response = await fetch("/api/health", { cache: "no-store" });
    if (!response.ok) throw new Error("Could not load app health");
    const health: HealthResponse = await response.json();
    setAppVersion(health.version.replace(/\.0$/, ""));
  }

  async function loadFfbStatus() {
    const response = await fetch("/api/integrations/logitech", { cache: "no-store" });
    if (!response.ok) return;
    setFfbStatus(await response.json());
  }

  function postPreview(value: OverlayProfile) {
    previewRef.current?.contentWindow?.postMessage(
      { type: "steercast-preview-profile", profile: value },
      location.origin
    );
  }

  async function loadProfiles() {
    const response = await fetch("/api/profiles");
    if (!response.ok) throw new Error("Could not load profiles");
    setProfiles(await response.json());
  }

  async function loadDevices() {
    const response = await fetch("/api/devices");
    if (!response.ok) throw new Error("Could not load devices");
    setDevices(await response.json());
  }

  async function refreshDevices() {
    setStatus("Looking for controllers");
    try {
      const response = await fetch("/api/devices/refresh", { method: "POST" });
      if (!response.ok) throw new Error();
      setDevices(await response.json());
      setStatus("Controller list updated");
    } catch {
      setStatus("Controller refresh failed. Reconnect the wheel and try again.");
    }
  }

  async function save() {
    if (!profile) return;
    setStatus("Saving changes");
    try {
      const response = await fetch(`/api/profiles/${profile.id}`, {
        method: "PUT",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(profile)
      });
      if (!response.ok) {
        const problem = await response.json();
        setStatus(problem.error ?? "Changes could not be saved.");
        return;
      }
      const saved = await response.json();
      setProfiles((current) => current.map((item) => (item.id === saved.id ? saved : item)));
      setProfile(normalizeProfileForEditor(saved));
      setStatus("Changes saved");
    } catch {
      setStatus("Changes could not be saved. Check that SteerCast is still running.");
    }
  }

  async function duplicate() {
    if (!profile) return;
    const id = `${profile.id.replace(/-\d+$/, "")}-${Date.now().toString().slice(-6)}`;
    const copy = { ...structuredClone(profile), id, name: `${profile.name} copy` };
    try {
      const response = await fetch(`/api/profiles/${id}`, {
        method: "PUT",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(copy)
      });
      if (!response.ok) throw new Error();
      await loadProfiles();
      setSelectedId(id);
      setStatus("Profile duplicated");
    } catch {
      setStatus("The profile could not be duplicated.");
    }
  }

  async function copyUrl() {
    try {
      await navigator.clipboard.writeText(overlayUrl);
      setStatus("OBS URL copied");
    } catch {
      setStatus("Copy failed. Select the URL and copy it manually.");
    }
  }

  function patchProfile(patch: Partial<OverlayProfile>) {
    setProfile((current) => (current ? { ...current, ...patch } : current));
  }

  function patchCanvasSize(patch: Partial<Pick<OverlayProfile, "width" | "height">>) {
    setProfile((current) => {
      if (!current) return current;

      const width = normalizeCanvasWidth(patch.width, current.width);
      const height = normalizeCanvasHeight(patch.height, current.height);
      const scaleX = width / current.width;
      const scaleY = height / current.height;
      const layout = Object.fromEntries(
        elementNames.map((name) => {
          const element = current.layout[name];
          return [
            name,
            clampElement(name, {
              ...element,
              x: Math.round(element.x * scaleX),
              y: Math.round(element.y * scaleY)
            }, width, height)
          ];
        })
      ) as OverlayProfile["layout"];

      return { ...current, width, height, layout };
    });
  }

  function patchElement(name: ElementName, patch: Partial<OverlayElement>) {
    setProfile((current) =>
      current
        ? {
          ...current,
          layout: {
            ...current.layout,
            [name]: clampElement(name, { ...current.layout[name], ...patch }, current.width, current.height)
          }
        }
        : current
    );
  }

  async function calibrate(
    axisKey: "steeringAxis" | "throttleAxis" | "brakeAxis" | "clutchAxis" | "handbrakeAxis",
    calibrationKey: "steering" | "throttle" | "brake" | "clutch" | "handbrake"
  ) {
    const currentProfile = profile;
    const calibrationDeviceId = calibrationKey === "handbrake"
      ? currentProfile?.handbrakeDeviceId || currentProfile?.deviceId
      : currentProfile?.deviceId;

    if (!currentProfile || !calibrationDeviceId) {
      setStatus("Choose a controller before calibrating.");
      return;
    }

    const axis = currentProfile.mapping[axisKey];
    if (axis < 0) {
      setStatus(`Choose the ${calibrationKey} axis before calibrating.`);
      return;
    }

    setCalibrating(calibrationKey);
    setStatus(`Move ${calibrationKey} through its full range for four seconds`);
    const samples: number[] = [];
    let center: number | undefined;
    const deadline = performance.now() + 4000;

    try {
      while (performance.now() < deadline) {
        const response = await fetch(
          `/api/devices/${encodeURIComponent(calibrationDeviceId)}/reading`,
          { cache: "no-store" }
        );
        if (!response.ok) break;
        const reading: RawDeviceReading = await response.json();
        const value = reading.axes[axis];
        if (Number.isFinite(value)) {
          center ??= value;
          samples.push(value);
        }
        await new Promise((resolve) => setTimeout(resolve, 40));
      }

      const response = await fetch(`/api/calibration/${encodeURIComponent(calibrationDeviceId)}`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          samples,
          center,
          centered: calibrationKey === "steering",
          inverted: currentProfile.mapping[calibrationKey].inverted,
          deadZone: currentProfile.mapping[calibrationKey].deadZone
        })
      });

      if (!response.ok) {
        const problem = await response.json().catch(() => ({ error: "Calibration failed." }));
        setStatus(problem.error ?? "Calibration failed.");
        return;
      }

      const calibration = await response.json();
      patchProfile({ mapping: { ...currentProfile.mapping, [calibrationKey]: calibration } });
      setStatus(`${calibrationKey} calibrated. Save changes to keep it.`);
    } catch {
      setStatus("Calibration stopped because the controller could not be read.");
    } finally {
      setCalibrating(null);
    }
  }

  function startDrag(event: PointerEvent, name: ElementName) {
    const target = event.currentTarget as HTMLElement;
    const targetBounds = target.getBoundingClientRect();
    target.setPointerCapture(event.pointerId);
    setDragging({
      name,
      offsetX: event.clientX - targetBounds.left,
      offsetY: event.clientY - targetBounds.top
    });
  }

  function moveElement(event: PointerEvent) {
    if (!profile || !dragging) return;
    const bounds = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const element = profile.layout[dragging.name];
    const size = elementSizes[dragging.name];
    const x = ((event.clientX - bounds.left - dragging.offsetX) / bounds.width) * profile.width;
    const y = ((event.clientY - bounds.top - dragging.offsetY) / bounds.height) * profile.height;
    const maxX = Math.max(0, profile.width - size.width * element.scale);
    const maxY = Math.max(0, profile.height - size.height * element.scale);
    patchElement(dragging.name, {
      x: Math.round(Math.max(0, Math.min(maxX, x))),
      y: Math.round(Math.max(0, Math.min(maxY, y)))
    });
  }

  if (!profile) {
    return <main class="loading">{status}</main>;
  }

  const selectedDevice = devices.find((device) => device.id === profile.deviceId);
  const automaticDevice = devices.find((device) => device.isRacingWheel) ?? devices[0];
  const controllerLabel = selectedDevice?.name
    ?? (automaticDevice ? `Automatic: ${automaticDevice.name}` : "No controller found");
  const isKnownTheme = Object.keys(themes).includes(profile.theme.name);
  const previewAspect = normalizeCanvasWidth(profile.width, 800) / normalizeCanvasHeight(profile.height, 600);

  return (
    <main class="app-shell">
      <a class="skip-link" href="#setup-content">Skip to content</a>
      {showLaunchSplash && (
        <div class="launch-splash" role="status" aria-label="SteerCast is starting">
          <div class="launch-logo" aria-hidden="true">
            <img src="/brand/app-logo.gif" alt="" />
          </div>
          <p>SteerCast <span class="brand-version">v{appVersion}</span></p>
          <small>Starting local overlay server</small>
        </div>
      )}
      <header class="topbar">
        <div class="brand">
          <img class="brand-mark" src="/brand/app-logo.png" alt="" aria-hidden="true" />
          <div>
            <h1>SteerCast <span class="brand-version">v{appVersion}</span></h1>
            <p>Wheel input for OBS</p>
          </div>
        </div>
        <div class="topbar-actions">
          <span class={`device-state ${devices.length ? "connected" : ""}`}>
            <i></i>{controllerLabel}
          </span>
          <button class="button-secondary" onClick={() => void duplicate()}>Duplicate profile</button>
          <button class="button-primary" onClick={() => void save()}>Save changes</button>
        </div>
      </header>

      <section class="source-bar" aria-label="OBS browser source">
        <span class="source-label">Browser source</span>
        <code>{overlayUrl}</code>
        <span class="source-meta">{profile.width} x {profile.height} / {profile.framesPerSecond} FPS</span>
        <button class="copy-button" onClick={() => void copyUrl()}>Copy URL</button>
        <a class="open-link" href={overlayUrl} target="_blank">Open</a>
      </section>

      <div id="setup-content" class="studio">
        <nav class="rail" aria-label="SteerCast settings">
          <label class="profile-select">
            Profile
            <select value={selectedId} onChange={(event) => setSelectedId(event.currentTarget.value)}>
              {profiles.map((item) => <option value={item.id}>{item.name}</option>)}
            </select>
          </label>
          <div class="rail-tabs">
            <RailButton active={panel === "device"} label="Controller" onClick={() => setPanel("device")} />
            <RailButton active={panel === "layout"} label="Layout" onClick={() => setPanel("layout")} />
            <RailButton active={panel === "appearance"} label="Appearance" onClick={() => setPanel("appearance")} />
            <RailButton active={panel === "obs"} label="OBS setup" onClick={() => setPanel("obs")} />
          </div>
          <div class="rail-footer">
            <div class="rail-status">
              <span class="status-dot"></span>
              <span>{status || "Ready"}</span>
            </div>
            <p>Wheel artwork and device imagery are owned by their respective owners and used for visual input representation.</p>
          </div>
        </nav>

        <section class="stage-column">
          <div class="stage-heading">
            <div>
              <span class="section-kicker">Live preview</span>
              <h2>{profile.name}</h2>
            </div>
            <span class="stage-help">Drag visible modules to reposition them</span>
          </div>
          <div
            class="canvas"
            style={`--preview-aspect: ${previewAspect}; aspect-ratio: ${profile.width} / ${profile.height};`}
            onPointerMove={moveElement}
            onPointerUp={() => setDragging(null)}
            onPointerCancel={() => setDragging(null)}
          >
            <iframe
              ref={previewRef}
              class="overlay-preview"
              src={`${overlayUrl}?preview=1`}
              title="Live SteerCast overlay preview"
              onLoad={() => postPreview(profile)}
            />
            {elementNames.map((name) => {
              const element = profile.layout[name];
              const size = elementSizes[name];
              if (!element.visible) return null;
              return (
                <button
                  class={`layout-handle ${name}`}
                  aria-label={`Move ${name}`}
                  style={{
                    left: `${(element.x / profile.width) * 100}%`,
                    top: `${(element.y / profile.height) * 100}%`,
                    width: `${(size.width / profile.width) * 100}%`,
                    height: `${(size.height / profile.height) * 100}%`,
                    transform: `scale(${element.scale})`
                  }}
                  onPointerDown={(event) => startDrag(event, name)}
                >
                  <span>{name}</span>
                </button>
              );
            })}
          </div>
        </section>

        <aside class="inspector">
          {panel === "device" && (
            <>
              <InspectorHeader title="Controller" description="Select the wheel and set its steering range." />
              <Field label="Input device">
                <div class="field-action">
                  <select
                    value={profile.deviceId ?? ""}
                    onChange={(event) => patchProfile({ deviceId: event.currentTarget.value || null })}
                  >
                    <option value="">Automatic</option>
                    {devices.map((device) => <option value={device.id}>{device.name}</option>)}
                  </select>
                  <button class="icon-button" title="Refresh controllers" onClick={() => void refreshDevices()}>Refresh</button>
                </div>
              </Field>
              <section class="handbrake-card" aria-label="Handbrake input">
                <div class="module-title">
                  <label class="switch">
                    <input
                      type="checkbox"
                      checked={profile.handbrakeEnabled}
                      onChange={(event) => patchProfile({ handbrakeEnabled: event.currentTarget.checked })}
                    />
                    <span></span>
                  </label>
                  <strong>Handbrake</strong>
                </div>
                <p>Use the wheel handbrake signal, or map a separate USB handbrake/controller.</p>
                <Field label="Handbrake device">
                  <select
                    disabled={!profile.handbrakeEnabled}
                    value={profile.handbrakeDeviceId ?? ""}
                    onChange={(event) => patchProfile({ handbrakeDeviceId: event.currentTarget.value || null })}
                  >
                    <option value="">Same as wheel</option>
                    {devices.map((device) => <option value={device.id}>{device.name}</option>)}
                  </select>
                </Field>
              </section>
              <Field label="Steering range" hint="Use the same value configured in G Hub.">
                <div class="value-with-unit">
                  <input
                    type="number"
                    min="180"
                    max="1440"
                    step="10"
                    value={profile.wheelRotationDegrees}
                    onInput={(event) => patchProfile({ wheelRotationDegrees: event.currentTarget.valueAsNumber })}
                  />
                  <span>degrees</span>
                </div>
              </Field>
              <section class={`ffb-card ${ffbStatus?.available ? "available" : "unavailable"}`} aria-labelledby="ffb-title">
                <div class="module-title">
                  <span class="ffb-indicator" aria-hidden="true"></span>
                  <strong id="ffb-title">Force feedback telemetry</strong>
                  <span class="ffb-badge">Optional</span>
                </div>
                <p>
                  {ffbStatus?.available
                    ? "Reported wheel force and torque can be sent to the OBS overlay."
                    : ffbStatus?.gHubInstalled
                      ? "G HUB is detected, but it does not include FFB telemetry by itself."
                      : "SteerCast still reads your wheel normally without FFB telemetry."}
                </p>
                <div class="ffb-status-line">
                  <span>{ffbStatus?.available ? "Telemetry available" : "Telemetry unavailable"}</span>
                  <small>{ffbStatus?.status ?? "Optional Logitech adapter not installed."}</small>
                </div>
                <div class="ffb-status-line ffb-ghub-state">
                  <span>G HUB</span>
                  <small>{ffbStatus?.gHubInstalled ? (ffbStatus.gHubRunning ? "Installed and running" : "Installed") : "Not detected"}</small>
                </div>
              </section>
              <details class="advanced">
                <summary>Manual input mapping</summary>
                <p>Only use this when automatic Logitech detection does not map an input correctly.</p>
                <div class="axis-list">
                  {axisRows.map(([axisKey, calibrationKey]) => (
                    <div class="axis-row">
                      <label>
                        {calibrationKey}
                        <input
                          type="number"
                          min="-1"
                          max="32"
                          value={profile.mapping[axisKey]}
                          onInput={(event) => patchProfile({
                            mapping: { ...profile.mapping, [axisKey]: event.currentTarget.valueAsNumber }
                          })}
                        />
                      </label>
                      <button
                        disabled={calibrating !== null}
                        onClick={() => void calibrate(axisKey, calibrationKey)}
                      >
                        {calibrating === calibrationKey ? "Reading" : "Calibrate"}
                      </button>
                    </div>
                  ))}
                </div>
                <div class="two-fields">
                  <Field label="Left paddle">
                    <input type="number" min="-1" max="63" value={profile.mapping.leftPaddleButton} onInput={(event) => patchProfile({ mapping: { ...profile.mapping, leftPaddleButton: event.currentTarget.valueAsNumber } })} />
                  </Field>
                  <Field label="Right paddle">
                    <input type="number" min="-1" max="63" value={profile.mapping.rightPaddleButton} onInput={(event) => patchProfile({ mapping: { ...profile.mapping, rightPaddleButton: event.currentTarget.valueAsNumber } })} />
                  </Field>
                </div>
                <Field label="Gear buttons" hint="Reverse first, followed by gears 1 through 6.">
                  <input
                    value={profile.mapping.gearButtons.join(", ")}
                    placeholder="12, 13, 14, 15, 16, 17, 18"
                    onInput={(event) => patchProfile({
                      mapping: {
                        ...profile.mapping,
                        gearButtons: event.currentTarget.value
                          .split(",")
                          .map((value) => Number.parseInt(value.trim(), 10))
                          .filter(Number.isFinite)
                      }
                    })}
                  />
                </Field>
              </details>
            </>
          )}

          {panel === "layout" && (
            <>
              <InspectorHeader title="Layout" description="Show only what your stream needs." />
              <button
                class="full-button"
                onClick={() => patchProfile({ layout: structuredClone(minimalLayout) })}
              >
                Reset module positions
              </button>
              <div class="module-list">
                {elementNames.map((name) => {
                  const element = profile.layout[name];
                  return (
                    <section class="module-row">
                      <div class="module-title">
                        <label class="switch">
                          <input type="checkbox" checked={element.visible} onChange={(event) => patchElement(name, { visible: event.currentTarget.checked })} />
                          <span></span>
                        </label>
                        <strong>{name}</strong>
                      </div>
                      <div class="module-values">
                        <label>X<input type="number" value={element.x} onInput={(event) => patchElement(name, { x: event.currentTarget.valueAsNumber })} /></label>
                        <label>Y<input type="number" value={element.y} onInput={(event) => patchElement(name, { y: event.currentTarget.valueAsNumber })} /></label>
                        <label>Scale<input type="number" min="0.4" max="2" step="0.05" value={element.scale} onInput={(event) => patchElement(name, { scale: event.currentTarget.valueAsNumber })} /></label>
                      </div>
                    </section>
                  );
                })}
              </div>
            </>
          )}

          {panel === "appearance" && (
            <>
              <InspectorHeader title="Appearance" description="Keep the overlay legible against the game." />
              <Field label="Style">
                <select
                  value={profile.theme.name}
                  onChange={(event) => patchProfile({
                    theme: { ...themes[event.currentTarget.value as keyof typeof themes] }
                  })}
                >
                  {!isKnownTheme && <option value={profile.theme.name}>{profile.theme.name} (saved)</option>}
                  {Object.keys(themes).map((name) => <option value={name}>{name}</option>)}
                </select>
              </Field>
              <div class="color-grid">
                {(["accent", "brake", "surface", "foreground"] as const).map((key) => (
                  <label>
                    {key}
                    <span class="color-control">
                      <input type="color" value={profile.theme[key]} onInput={(event) => patchProfile({ theme: { ...profile.theme, [key]: event.currentTarget.value } })} />
                      <code>{profile.theme[key]}</code>
                    </span>
                  </label>
                ))}
              </div>
              <Field label="Panel opacity" hint={`${Math.round(profile.theme.opacity * 100)}%`}>
                <input type="range" min="0.2" max="1" step="0.05" value={profile.theme.opacity} onInput={(event) => patchProfile({ theme: { ...profile.theme, opacity: event.currentTarget.valueAsNumber } })} />
              </Field>
              <div class="two-fields">
                <Field label="Width"><input type="number" min={minCanvasWidth} max={maxCanvasWidth} value={profile.width} onInput={(event) => patchCanvasSize({ width: event.currentTarget.valueAsNumber })} /></Field>
                <Field label="Height"><input type="number" min={minCanvasHeight} max={maxCanvasHeight} value={profile.height} onInput={(event) => patchCanvasSize({ height: event.currentTarget.valueAsNumber })} /></Field>
              </div>
              <Field label="Frame rate">
                <select value={profile.framesPerSecond} onChange={(event) => patchProfile({ framesPerSecond: Number(event.currentTarget.value) })}>
                  <option value="60">60 FPS</option>
                  <option value="120">120 FPS</option>
                </select>
              </Field>
            </>
          )}

          {panel === "obs" && (
            <>
              <InspectorHeader title="OBS setup" description="Add one browser source. No plugin is required." />
              <ol class="setup-steps">
                <li><span>1</span><p>In OBS, add a <strong>Browser</strong> source.</p></li>
                <li><span>2</span><p>Paste the Browser source URL shown above.</p></li>
                <li><span>3</span><p>Set the size to <strong>{profile.width} x {profile.height}</strong> and FPS to <strong>{profile.framesPerSecond}</strong>.</p></li>
                <li><span>4</span><p>Leave custom CSS empty.</p></li>
              </ol>
              <button class="full-button primary" onClick={() => void copyUrl()}>Copy Browser source URL</button>
              <Field label="Profile name">
                <input value={profile.name} onInput={(event) => patchProfile({ name: event.currentTarget.value })} />
              </Field>
            </>
          )}
        </aside>
      </div>
    </main>
  );
}

function RailButton({ active, label, onClick }: { active: boolean; label: string; onClick: () => void }) {
  return <button class={active ? "active" : ""} onClick={onClick}>{label}</button>;
}

function InspectorHeader({ title, description }: { title: string; description: string }) {
  return <header class="inspector-header"><h2>{title}</h2><p>{description}</p></header>;
}

function Field({ label, hint, children }: { label: string; hint?: string; children: ComponentChildren }) {
  return <label class="field"><span>{label}{hint && <small>{hint}</small>}</span>{children}</label>;
}

function clamp(value: number, minimum: number, maximum: number) {
  return Math.min(maximum, Math.max(minimum, value));
}

function finiteOr(value: number | undefined, fallback: number) {
  return Number.isFinite(value) ? (value as number) : fallback;
}

function normalizeCanvasWidth(value: number | undefined, fallback: number) {
  return Math.round(clamp(finiteOr(value, fallback), minCanvasWidth, maxCanvasWidth));
}

function normalizeCanvasHeight(value: number | undefined, fallback: number) {
  return Math.round(clamp(finiteOr(value, fallback), minCanvasHeight, maxCanvasHeight));
}

function clampElement(name: ElementName, element: OverlayElement, width: number, height: number): OverlayElement {
  const size = elementSizes[name];
  const scale = clamp(finiteOr(element.scale, 1), minElementScale, maxElementScale);
  const maxX = Math.max(0, width - size.width * scale);
  const maxY = Math.max(0, height - size.height * scale);

  return {
    ...element,
    scale,
    x: Math.round(clamp(finiteOr(element.x, 0), 0, maxX)),
    y: Math.round(clamp(finiteOr(element.y, 0), 0, maxY))
  };
}

function normalizeProfileForEditor(profile: OverlayProfile): OverlayProfile {
  const width = normalizeCanvasWidth(profile.width, 800);
  const height = normalizeCanvasHeight(profile.height, 600);
  const layout = Object.fromEntries(
    elementNames.map((name) => [
      name,
      clampElement(name, profile.layout?.[name] ?? minimalLayout[name], width, height)
    ])
  ) as OverlayProfile["layout"];

  return { ...profile, width, height, layout };
}
