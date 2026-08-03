import type { ComponentChildren } from "preact";
import { useEffect, useMemo, useRef, useState } from "preact/hooks";
import type { DeviceDescriptor, GameIntegrationSettings, GameIntegrationSnapshot, HealthResponse, OverlayElement, OverlayProfile, RawDeviceReading, TelemetryCaptureStatus } from "../shared/types";
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
const panelNames: PanelName[] = ["device", "games", "layout", "appearance", "obs"];

function panelFromUrl(): PanelName {
  const value = new URLSearchParams(location.search).get("panel") as PanelName | null;
  return value && panelNames.includes(value) ? value : "device";
}

export default function App() {
  const [profiles, setProfiles] = useState<OverlayProfile[]>([]);
  const [devices, setDevices] = useState<DeviceDescriptor[]>([]);
  const [selectedId, setSelectedId] = useState("default");
  const [profile, setProfile] = useState<OverlayProfile | null>(null);
  const [panel, setPanel] = useState<PanelName>(panelFromUrl);
  const [status, setStatus] = useState("Loading");
  const [appVersion, setAppVersion] = useState("dev");
  const [gameIntegration, setGameIntegration] = useState<GameIntegrationSnapshot | null>(null);
  const [telemetryCapture, setTelemetryCapture] = useState<TelemetryCaptureStatus | null>(null);
  const [integrationSaving, setIntegrationSaving] = useState(false);
  const [captureSaving, setCaptureSaving] = useState(false);
  const [setupSaving, setSetupSaving] = useState(false);
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
    if (panel !== "games") return;
    void Promise.all([loadGameIntegration(), loadTelemetryCapture()]).catch(() => undefined);
    const timer = window.setInterval(() => {
      void Promise.all([loadGameIntegration(), loadTelemetryCapture()]).catch(() => undefined);
    }, 1000);
    return () => window.clearInterval(timer);
  }, [panel]);

  useEffect(() => {
    document.title = `SteerCast v${appVersion} · Setup`;
  }, [appVersion]);

  useEffect(() => {
    const restorePanel = () => setPanel(panelFromUrl());
    window.addEventListener("popstate", restorePanel);
    return () => window.removeEventListener("popstate", restorePanel);
  }, []);

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
      await Promise.all([loadProfiles(), loadDevices(), loadHealth(), loadGameIntegration(), loadTelemetryCapture()]);
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

  async function loadGameIntegration() {
    const response = await fetch("/api/game-integrations", { cache: "no-store" });
    if (!response.ok) throw new Error("Could not load game integrations");
    setGameIntegration(await response.json());
  }

  async function loadTelemetryCapture() {
    const response = await fetch("/api/telemetry-capture", { cache: "no-store" });
    if (!response.ok) throw new Error("Could not load telemetry capture status");
    setTelemetryCapture(await response.json());
  }

  async function toggleTelemetryCapture() {
    if (!telemetryCapture || captureSaving) return;
    const wasRecording = telemetryCapture.recording;
    setCaptureSaving(true);
    setStatus(wasRecording ? "Saving driving sample" : "Starting driving sample");
    try {
      const response = await fetch(
        wasRecording ? "/api/telemetry-capture/stop" : "/api/telemetry-capture/start",
        {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: wasRecording ? undefined : JSON.stringify({ profileId: selectedId, durationSeconds: 30 })
        }
      );
      if (!response.ok) throw new Error();
      const next: TelemetryCaptureStatus = await response.json();
      setTelemetryCapture(next);
      setStatus(next.recording ? "Driving sample recording" : next.message);
    } catch {
      setStatus(wasRecording ? "Driving sample could not be saved." : "Driving sample could not be started.");
      await loadTelemetryCapture().catch(() => undefined);
    } finally {
      setCaptureSaving(false);
    }
  }

  async function updateGameIntegration(patch: Partial<GameIntegrationSettings>) {
    if (!gameIntegration || integrationSaving) return;
    const settings = { ...gameIntegration.settings, ...patch };
    setIntegrationSaving(true);
    setStatus("Applying game integration");
    try {
      const response = await fetch("/api/game-integrations", {
        method: "PUT",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(settings)
      });
      if (!response.ok) throw new Error();
      setGameIntegration(await response.json());
      setStatus(settings.enabled ? "Game integration enabled" : "Game integration disabled");
    } catch {
      setStatus("Game integration could not be changed.");
      await loadGameIntegration().catch(() => undefined);
    } finally {
      setIntegrationSaving(false);
    }
  }

  async function configureDirtRally2() {
    if (!gameIntegration || setupSaving) return;
    setSetupSaving(true);
    setStatus("Configuring DiRT Rally 2.0");
    try {
      const setupResponse = await fetch("/api/game-integrations/dirt-rally-2/configure", { method: "POST" });
      if (!setupResponse.ok) {
        const error = await setupResponse.json().catch(() => ({ message: "Automatic setup failed." }));
        throw new Error(error.message);
      }

      let snapshot: GameIntegrationSnapshot = await setupResponse.json();
      if (!snapshot.settings.enabled) {
        const enableResponse = await fetch("/api/game-integrations", {
          method: "PUT",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ ...snapshot.settings, enabled: true })
        });
        if (!enableResponse.ok) throw new Error("The listener could not be enabled.");
        snapshot = await enableResponse.json();
      }

      setGameIntegration(snapshot);
      setStatus("DiRT Rally 2.0 is ready for a connection");
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Automatic setup failed.");
      await loadGameIntegration().catch(() => undefined);
    } finally {
      setSetupSaving(false);
    }
  }

  function postPreview(value: OverlayProfile) {
    previewRef.current?.contentWindow?.postMessage(
      { type: "steercast-preview-profile", profile: value },
      location.origin
    );
  }

  function selectPanel(nextPanel: PanelName) {
    setPanel(nextPanel);
    const url = new URL(location.href);
    if (nextPanel === "device") url.searchParams.delete("panel");
    else url.searchParams.set("panel", nextPanel);
    history.pushState(null, "", url);
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
  const selectedGame = gameIntegration?.games.find((game) => game.id === gameIntegration.settings.gameId);

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
            <RailButton active={panel === "device"} label="Controller" onClick={() => selectPanel("device")} />
            <RailButton active={panel === "games"} label="Games" onClick={() => selectPanel("games")} />
            <RailButton active={panel === "layout"} label="Layout" onClick={() => selectPanel("layout")} />
            <RailButton active={panel === "appearance"} label="Appearance" onClick={() => selectPanel("appearance")} />
            <RailButton active={panel === "obs"} label="OBS setup" onClick={() => selectPanel("obs")} />
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

          {panel === "games" && (
            <>
              <InspectorHeader
                title="Game integrations"
                description="Add optional game-exported data without touching the wheel or game process."
              />
              {gameIntegration ? (
                <div class="integration-panel">
                  <Field label="Game">
                    <select
                      name="game-integration"
                      aria-label="Game"
                      value={gameIntegration.settings.gameId}
                      disabled={integrationSaving || gameIntegration.settings.enabled}
                      onChange={(event) => void updateGameIntegration({ gameId: event.currentTarget.value })}
                    >
                      {gameIntegration.games.map((game) => <option value={game.id}>{game.name}</option>)}
                    </select>
                  </Field>

                  <section class="game-setup-assistant" aria-labelledby="game-setup-heading">
                    <div class="game-setup-heading">
                      <div>
                        <h3 id="game-setup-heading">Quick setup</h3>
                        <p>Usually takes less than two minutes.</p>
                      </div>
                      <span class={`setup-state ${gameIntegration.telemetry.available ? "complete" : ""}`}>
                        {gameIntegration.telemetry.available ? "Live" : gameIntegration.setup.configured ? "Configured" : "Setup needed"}
                      </span>
                    </div>

                    <ol class="game-setup-steps">
                      <li class={gameIntegration.setup.configFound ? "complete" : "current"}>
                        <span aria-hidden="true">{gameIntegration.setup.configFound ? "✓" : "1"}</span>
                        <div>
                          <strong>Find the game</strong>
                          <p>{gameIntegration.setup.message}</p>
                          {gameIntegration.setup.configPaths[0] && <code title={gameIntegration.setup.configPaths[0]}>{gameIntegration.setup.configPaths[0]}</code>}
                        </div>
                      </li>
                      <li class={gameIntegration.setup.configured ? "complete" : gameIntegration.setup.configFound ? "current" : ""}>
                        <span aria-hidden="true">{gameIntegration.setup.configured ? "✓" : "2"}</span>
                        <div>
                          <strong>Enable local UDP output</strong>
                          <p>{gameIntegration.setup.configured ? "The game will send telemetry only to this computer." : "Close DiRT Rally 2.0 first. SteerCast will create a backup before changing the UDP setting."}</p>
                          {gameIntegration.setup.backupPaths.length > 0 && <p class="backup-note">Backup created beside the original configuration.</p>}
                        </div>
                      </li>
                      <li class={gameIntegration.telemetry.available ? "complete" : gameIntegration.setup.configured && gameIntegration.settings.enabled ? "current" : ""}>
                        <span aria-hidden="true">{gameIntegration.telemetry.available ? "✓" : "3"}</span>
                        <div>
                          <strong>Start a stage</strong>
                          <p>{gameIntegration.telemetry.available ? "Connection confirmed. Derived steering demand is ready for the overlay." : gameIntegration.settings.enabled ? "Open the game and begin a stage. SteerCast will confirm the first packet automatically." : "Enable the listener, then open the game and begin a stage."}</p>
                        </div>
                      </li>
                    </ol>

                    {!gameIntegration.setup.configFound ? (
                      <button class="full-button" disabled={setupSaving} onClick={() => void loadGameIntegration().catch(() => setStatus("The game configuration could not be checked."))}>
                        Check again
                      </button>
                    ) : !gameIntegration.telemetry.available && (!gameIntegration.setup.configured || !gameIntegration.settings.enabled) ? (
                      <button
                        class="full-button primary setup-action"
                        disabled={setupSaving || integrationSaving || !gameIntegration.setup.canConfigure}
                        aria-busy={setupSaving}
                        onClick={() => void configureDirtRally2()}
                      >
                        {setupSaving && <span class="button-spinner" aria-hidden="true"></span>}
                        <span>{gameIntegration.setup.configured ? "Enable and test" : "I’ve closed the game — Configure"}</span>
                      </button>
                    ) : null}
                  </section>

                  <div class="integration-control-row">
                    <div>
                      <strong>Enable integration</strong>
                      <p>Starts a local listener only for the selected game.</p>
                    </div>
                    <label class="switch" aria-label="Enable game integration">
                      <input
                        type="checkbox"
                        checked={gameIntegration.settings.enabled}
                        disabled={integrationSaving}
                        onChange={(event) => void updateGameIntegration({ enabled: event.currentTarget.checked })}
                      />
                      <span></span>
                    </label>
                  </div>

                  <div class={`integration-status ${gameIntegration.telemetry.available ? "receiving" : "waiting"}`} aria-live="polite">
                    <span class="integration-status-dot" aria-hidden="true"></span>
                    <div>
                      <strong>{gameIntegration.telemetry.available ? "Receiving telemetry" : gameIntegration.settings.enabled ? "Waiting for game" : "Integration off"}</strong>
                      <p>{gameIntegration.telemetry.status}</p>
                    </div>
                  </div>

                  <section class="integration-section" aria-labelledby="signal-heading">
                    <div class="integration-section-heading">
                      <h3 id="signal-heading">Signal</h3>
                      <span class="quality-badge">Not FFB</span>
                    </div>
                    <strong class="signal-name">{selectedGame?.signalLabel ?? "Derived steering demand"}</strong>
                    <p>{selectedGame?.summary}</p>
                    <div class="integration-meter" role="progressbar" aria-label="Derived steering demand" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(gameIntegration.telemetry.strength * 100)}>
                      <span style={{ transform: `scaleX(${gameIntegration.telemetry.strength})` }}></span>
                    </div>
                    <output class="integration-value">{Math.round(gameIntegration.telemetry.strength * 100)}%</output>
                  </section>

                  <section class="integration-section" aria-labelledby="overlay-output-heading">
                    <div class="integration-control-row compact">
                      <div>
                        <h3 id="overlay-output-heading">Overlay output</h3>
                        <p>Show a clearly labelled derived-load meter near the steering readout.</p>
                      </div>
                      <label class="switch" aria-label="Show game telemetry on overlay">
                        <input
                          type="checkbox"
                          checked={gameIntegration.settings.showOnOverlay}
                          disabled={integrationSaving || !gameIntegration.settings.enabled}
                          onChange={(event) => void updateGameIntegration({ showOnOverlay: event.currentTarget.checked })}
                        />
                        <span></span>
                      </label>
                    </div>
                  </section>

                  <section class="integration-section capture-section" aria-labelledby="capture-heading">
                    <div class="integration-section-heading">
                      <h3 id="capture-heading">Driving sample</h3>
                      <span class="sample-rate-badge">20 Hz max</span>
                    </div>
                    <p>Record 30 seconds of steering, pedals, slip, and yaw from the existing stream. No extra wheel or game polling.</p>
                    <button
                      class={`full-button ${telemetryCapture?.recording ? "recording" : ""}`}
                      disabled={captureSaving || (!telemetryCapture?.recording && !gameIntegration.telemetry.available)}
                      aria-busy={captureSaving}
                      onClick={() => void toggleTelemetryCapture()}
                    >
                      {captureSaving && <span class="button-spinner" aria-hidden="true"></span>}
                      <span>{telemetryCapture?.recording ? `Stop and save (${telemetryCapture.remainingSeconds}s)` : "Record 30-second sample"}</span>
                    </button>
                    <p class="capture-status" role="status" aria-live="polite">
                      {telemetryCapture?.message ?? "Loading capture controls…"}
                      {telemetryCapture?.recording ? ` ${telemetryCapture.sampleCount} samples.` : ""}
                    </p>
                    {telemetryCapture?.latestFile && !telemetryCapture.recording && (
                      <code class="capture-path" title={telemetryCapture.latestFile}>{telemetryCapture.latestFile}</code>
                    )}
                  </section>

                  <details class="integration-setup">
                    <summary>Manual setup and recovery</summary>
                    <ol>
                      <li>Close the game.</li>
                      <li>Edit <code>Documents\My Games\DiRT Rally 2.0\hardwaresettings\hardware_settings_config.xml</code>.</li>
                      <li>Set <code>{`<udp enabled="true" extradata="3" ip="127.0.0.1" port="${selectedGame?.port ?? 20777}" delay="1" />`}</code>.</li>
                      <li>Save the file, restart the game, then begin a stage.</li>
                      <li>If needed, restore the file ending in <code>.steercast-backup</code>.</li>
                    </ol>
                  </details>

                  <p class="local-note">Local only. No process access, injection, virtual controller, or wheel commands.</p>
                </div>
              ) : (
                <p class="panel-loading" role="status">Loading game integrations…</p>
              )}
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
