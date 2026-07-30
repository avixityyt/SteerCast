export type AxisCalibration = {
  minimum: number;
  maximum: number;
  center: number;
  deadZone: number;
  inverted: boolean;
  centered: boolean;
};

export type InputMapping = {
  steeringAxis: number;
  throttleAxis: number;
  brakeAxis: number;
  clutchAxis: number;
  handbrakeAxis: number;
  leftPaddleButton: number;
  rightPaddleButton: number;
  gearButtons: number[];
  steering: AxisCalibration;
  throttle: AxisCalibration;
  brake: AxisCalibration;
  clutch: AxisCalibration;
  handbrake: AxisCalibration;
};

export type OverlayElement = {
  x: number;
  y: number;
  scale: number;
  visible: boolean;
};

export type OverlayProfile = {
  schemaVersion: number;
  id: string;
  name: string;
  deviceId: string | null;
  width: number;
  height: number;
  framesPerSecond: number;
  wheelRotationDegrees: number;
  handbrakeEnabled: boolean;
  handbrakeDeviceId: string | null;
  mapping: InputMapping;
  layout: {
    wheel: OverlayElement;
    pedals: OverlayElement;
    gear: OverlayElement;
    handbrake: OverlayElement;
    buttons: OverlayElement;
  };
  theme: {
    name: string;
    accent: string;
    brake: string;
    surface: string;
    foreground: string;
    opacity: number;
  };
};

export type DeviceDescriptor = {
  id: string;
  name: string;
  vendorId: number;
  productId: number;
  axisCount: number;
  buttonCount: number;
  switchCount: number;
  isRacingWheel: boolean;
  connected: boolean;
};

export type InputFrame = {
  sequence: number;
  timestamp: number;
  deviceId: string;
  connected: boolean;
  steering: number;
  throttle: number;
  brake: number;
  clutch: number;
  handbrake: number;
  gear: number;
  buttons: number;
};

export type RawDeviceReading = {
  deviceId: string;
  timestamp: number;
  axes: number[];
  buttons: boolean[];
  switches: number[];
};

export type HealthResponse = {
  status: string;
  version: string;
  clients: number;
  devices: number;
};
