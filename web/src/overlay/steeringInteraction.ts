import { clamp01 } from "./dom";

export type SteeringPhysics = {
  steering: number;
  demand: number;
  speed: number;
  slipAngle: number;
  yawRate: number;
  timestamp: number;
};

export type SteeringInteraction = {
  score: number;
  forceActive: boolean;
  forceDirection: -1 | 0 | 1;
  forceIntensity: number;
};

export class SteeringInteractionTracker {
  private lastSteering: number | null = null;
  private lastTimestamp = 0;
  private activity = 0;
  private aligningForce = 0;
  private forceDirection: -1 | 0 | 1 = 0;

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

    const slipDemand = clamp01(Math.abs(sample.slipAngle) / 14);
    const sliding = sample.speed >= 5
      && Math.abs(sample.slipAngle) >= 4
      && Math.abs(sample.yawRate) >= 0.1;

    // Slip angle approximates the road-wheel angle that would align the car
    // with its direction of travel. Do not clamp the target: if the target is
    // beyond steering lock, the derived force should still appear to push
    // against a wheel being held at full lock.
    const targetSteering = sample.slipAngle / 28;
    const steeringError = targetSteering - sample.steering;
    const steeringStability = 0.45 + (1 - clamp01(Math.abs(steeringVelocity) / 1.2)) * 0.55;
    const forceTarget = sliding
      ? demand * clamp01(Math.abs(steeringError) / 0.55) * steeringStability
      : 0;
    if (forceTarget >= 0.03) {
      this.forceDirection = Math.sign(steeringError) as -1 | 1;
    }

    const forceTimeConstant = forceTarget > this.aligningForce ? 0.14 : 0.12;
    this.aligningForce += (forceTarget - this.aligningForce)
      * (1 - Math.exp(-elapsed / forceTimeConstant));

    const forceActive = this.aligningForce >= 0.14;
    const forceIntensity = forceActive ? clamp01(this.aligningForce) : 0;
    const forceDirection = forceActive ? this.forceDirection : 0;
    const motionBoost = sliding ? this.activity * 0.08 : 0;
    const score = clamp01(demand * 0.88 + slipDemand * 0.08 + motionBoost);

    this.lastSteering = sample.steering;
    this.lastTimestamp = sampleTime;

    return { score, forceActive, forceDirection, forceIntensity };
  }

  reset() {
    this.lastSteering = null;
    this.lastTimestamp = 0;
    this.activity = 0;
    this.aligningForce = 0;
    this.forceDirection = 0;
  }
}
