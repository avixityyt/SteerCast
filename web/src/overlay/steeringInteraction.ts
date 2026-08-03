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
  countersteering: boolean;
  counterDirection: -1 | 0 | 1;
  counterIntensity: number;
};

export class SteeringInteractionTracker {
  private lastSteering: number | null = null;
  private lastTimestamp = 0;
  private activity = 0;
  private yawCorrelation = 0;
  private calibrationWeight = 0;
  private counterEvidence = 0;
  private counterTension = 0;
  private counterDirection: -1 | 0 | 1 = 0;

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
    const counterCandidate = calibrated
      && sliding
      && Math.abs(sample.steering) >= 0.045
      && Math.sign(sample.steering) !== Math.sign(alignedYaw);
    const activelyCorrecting = calibrated
      && sliding
      && this.activity >= 0.14
      && Math.sign(steeringVelocity) === -Math.sign(alignedYaw);

    const slipDemand = clamp01(Math.abs(sample.slipAngle) / 14);
    const yawDemand = clamp01(Math.abs(alignedYaw) / 0.8);
    const correctionBoost = activelyCorrecting ? this.activity * 0.12 : 0;
    const score = clamp01(demand * 0.86 + slipDemand * 0.10 + correctionBoost);

    // Confirm countersteer over time instead of reacting to one noisy sign
    // crossing. Release slightly faster, but retain enough tail for a stable
    // visual when yaw briefly approaches zero during recovery.
    const evidenceTarget = counterCandidate ? 1 : 0;
    const evidenceTimeConstant = counterCandidate ? 0.14 : 0.10;
    this.counterEvidence += (evidenceTarget - this.counterEvidence)
      * (1 - Math.exp(-elapsed / evidenceTimeConstant));
    if (counterCandidate) {
      this.counterDirection = Math.sign(sample.steering) as -1 | 1;
    }

    // Real held countersteer still contains small corrections. Convert wheel
    // velocity into a continuous stability term rather than a brittle stopped
    // or moving decision, then smooth the resulting tension.
    const steeringStability = 1 - clamp01(Math.abs(steeringVelocity) / 0.9);
    const physicsIntensity = clamp01(demand * 0.58 + slipDemand * 0.30 + yawDemand * 0.12);
    const tensionTarget = counterCandidate
      ? physicsIntensity * (0.35 + steeringStability * 0.65) * this.counterEvidence
      : 0;
    const tensionTimeConstant = tensionTarget > this.counterTension ? 0.16 : 0.12;
    this.counterTension += (tensionTarget - this.counterTension)
      * (1 - Math.exp(-elapsed / tensionTimeConstant));

    const countersteering = this.counterEvidence >= (counterCandidate ? 0.55 : 0.25)
      && this.counterTension >= 0.18;
    const counterIntensity = countersteering ? clamp01(this.counterTension) : 0;
    const counterDirection = countersteering ? this.counterDirection : 0;

    this.lastSteering = sample.steering;
    this.lastTimestamp = sampleTime;

    return { score, countersteering, counterDirection, counterIntensity };
  }

  reset() {
    this.lastSteering = null;
    this.lastTimestamp = 0;
    this.activity = 0;
    this.yawCorrelation = 0;
    this.calibrationWeight = 0;
    this.counterEvidence = 0;
    this.counterTension = 0;
    this.counterDirection = 0;
  }
}
