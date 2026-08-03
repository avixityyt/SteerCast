import { clamp01 } from "./dom";

export type SteeringInteractionState = "free" | "cornering" | "loaded" | "countersteer" | "correcting";

export type SteeringPhysics = {
  steering: number;
  demand: number;
  speed: number;
  slipAngle: number;
  yawRate: number;
  timestamp: number;
};

export type SteeringInteraction = {
  state: SteeringInteractionState;
  label: string;
  score: number;
};

const labels: Record<SteeringInteractionState, string> = {
  free: "Free",
  cornering: "Cornering",
  loaded: "Loaded",
  countersteer: "Countersteer",
  correcting: "Correcting"
};

export class SteeringInteractionTracker {
  private lastSteering: number | null = null;
  private lastTimestamp = 0;
  private activity = 0;
  private yawCorrelation = 0;
  private calibrationWeight = 0;

  update(sample: SteeringPhysics): SteeringInteraction {
    const demand = clamp01(sample.demand);
    const sampleTime = Number.isFinite(sample.timestamp) && sample.timestamp > 0
      ? sample.timestamp
      : performance.now();
    const elapsed = this.lastTimestamp > 0
      ? Math.min(0.25, Math.max(0.008, (sampleTime - this.lastTimestamp) / 1000))
      : 1 / 60;
    const steeringVelocity = this.lastSteering === null
      ? 0
      : (sample.steering - this.lastSteering) / elapsed;
    const rawActivity = clamp01(Math.abs(steeringVelocity) / 1.8);
    this.activity = Math.max(rawActivity, this.activity * Math.exp(-elapsed / 0.24));

    // Learn whether this wheel/game pair reports steering and yaw with matching signs.
    // Only use low-slip grip driving, never a slide, for calibration.
    if (sample.speed >= 5
      && Math.abs(sample.steering) >= 0.04
      && Math.abs(sample.yawRate) >= 0.04
      && Math.abs(sample.slipAngle) <= 4) {
      const weight = Math.min(0.08, elapsed) * Math.min(1, Math.abs(sample.yawRate) / 0.3);
      this.yawCorrelation += Math.sign(sample.steering * sample.yawRate) * weight;
      this.calibrationWeight = Math.min(2, this.calibrationWeight + weight);
    }

    const yawPolarity = this.yawCorrelation < 0 ? -1 : 1;
    const alignedYaw = sample.yawRate * yawPolarity;
    const calibrated = this.calibrationWeight >= 0.18;
    const sliding = sample.speed >= 5 && Math.abs(sample.slipAngle) >= 2.2 && Math.abs(alignedYaw) >= 0.06;
    const countersteering = calibrated
      && sliding
      && Math.abs(sample.steering) >= 0.045
      && Math.sign(sample.steering) !== Math.sign(alignedYaw);
    const activelyCorrecting = calibrated
      && sliding
      && this.activity >= 0.14
      && Math.sign(steeringVelocity) === -Math.sign(alignedYaw);

    let state: SteeringInteractionState;
    if (activelyCorrecting) state = "correcting";
    else if (countersteering) state = "countersteer";
    else if (demand >= 0.3) state = "loaded";
    else if (demand >= 0.07) state = "cornering";
    else state = "free";

    const slipDemand = clamp01(Math.abs(sample.slipAngle) / 14);
    const correctionBoost = activelyCorrecting ? this.activity * 0.12 : 0;
    const score = clamp01(demand * 0.86 + slipDemand * 0.10 + correctionBoost);

    this.lastSteering = sample.steering;
    this.lastTimestamp = sampleTime;

    return { state, label: labels[state], score };
  }

  reset() {
    this.lastSteering = null;
    this.lastTimestamp = 0;
    this.activity = 0;
    this.yawCorrelation = 0;
    this.calibrationWeight = 0;
  }
}
