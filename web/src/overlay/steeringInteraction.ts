import { clamp01 } from "./dom";

export type SteeringInteractionState = "quiet" | "turning" | "high-load" | "countersteer" | "correction";

export type SteeringInteraction = {
  state: SteeringInteractionState;
  label: string;
  score: number;
};

const labels: Record<SteeringInteractionState, string> = {
  quiet: "Quiet",
  turning: "Turning",
  "high-load": "High load",
  countersteer: "Countersteer",
  correction: "Correction"
};

export class SteeringInteractionTracker {
  private lastSteering: number | null = null;
  private lastTimestamp = 0;
  private lastVelocity = 0;
  private activity = 0;
  private correctionUntil = 0;

  update(steering: number, load: number, loadDirection: number, timestamp: number): SteeringInteraction {
    const normalizedLoad = clamp01(load);
    const sampleTime = Number.isFinite(timestamp) && timestamp > 0 ? timestamp : performance.now();
    const elapsed = this.lastTimestamp > 0
      ? Math.min(0.25, Math.max(0.008, (sampleTime - this.lastTimestamp) / 1000))
      : 1 / 60;
    const velocity = this.lastSteering === null ? 0 : (steering - this.lastSteering) / elapsed;
    const rawActivity = clamp01(Math.abs(velocity) / 1.6);
    const decay = Math.exp(-elapsed / 0.32);
    this.activity = Math.max(rawActivity, this.activity * decay);

    const reversed = Math.abs(velocity) > 0.35
      && Math.abs(this.lastVelocity) > 0.35
      && Math.sign(velocity) !== Math.sign(this.lastVelocity);
    if (reversed && normalizedLoad >= 0.08) this.correctionUntil = sampleTime + 280;

    const countersteering = normalizedLoad >= 0.08
      && Math.abs(steering) >= 0.05
      && loadDirection !== 0
      && Math.sign(steering) !== Math.sign(loadDirection);

    let state: SteeringInteractionState;
    if (sampleTime < this.correctionUntil) state = "correction";
    else if (countersteering) state = "countersteer";
    else if (normalizedLoad >= 0.28 && this.activity >= 0.2) state = "high-load";
    else if (this.activity >= 0.08) state = "turning";
    else state = "quiet";

    this.lastSteering = steering;
    this.lastTimestamp = sampleTime;
    this.lastVelocity = velocity;

    return {
      state,
      label: labels[state],
      score: clamp01(this.activity * (0.72 + normalizedLoad * 0.28))
    };
  }

  reset() {
    this.lastSteering = null;
    this.lastTimestamp = 0;
    this.lastVelocity = 0;
    this.activity = 0;
    this.correctionUntil = 0;
  }
}
