import type { OverlayProfile } from "../shared/types";

export const elementNames = ["wheel", "pedals", "gear", "handbrake", "buttons"] as const;
export type ElementName = (typeof elementNames)[number];
export type PanelName = "device" | "layout" | "appearance" | "obs";
export type DragState = { name: ElementName; offsetX: number; offsetY: number };

export const elementSizes: Record<ElementName, { width: number; height: number }> = {
  wheel: { width: 420, height: 430 },
  pedals: { width: 360, height: 205 },
  gear: { width: 220, height: 250 },
  handbrake: { width: 150, height: 210 },
  buttons: { width: 340, height: 44 }
};

export const minimalLayout: OverlayProfile["layout"] = {
  wheel: { x: 390, y: 60, scale: 1, visible: true },
  pedals: { x: 55, y: 380, scale: 1, visible: true },
  gear: { x: 840, y: 92, scale: 1, visible: true },
  handbrake: { x: 545, y: 330, scale: 1, visible: true },
  buttons: { x: 70, y: 535, scale: 1, visible: false }
};

export const themes = {
  Palette: {
    name: "Palette",
    accent: "#CEDFD9",
    brake: "#9B6A6C",
    surface: "#1B1815",
    foreground: "#EBFCFB",
    opacity: 0.88
  },
  Stone: {
    name: "Stone",
    accent: "#B09398",
    brake: "#9B6A6C",
    surface: "#171411",
    foreground: "#EBFCFB",
    opacity: 0.92
  },
  Broadcast: {
    name: "Broadcast",
    accent: "#CEDFD9",
    brake: "#B09398",
    surface: "#0e1216",
    foreground: "#EBFCFB",
    opacity: 0.96
  },
  Minimal: {
    name: "Minimal",
    accent: "#EBFCFB",
    brake: "#EBFCFB",
    surface: "#11100f",
    foreground: "#EBFCFB",
    opacity: 0.78
  }
} as const;

export const axisRows = [
  ["steeringAxis", "steering"],
  ["throttleAxis", "throttle"],
  ["brakeAxis", "brake"],
  ["clutchAxis", "clutch"],
  ["handbrakeAxis", "handbrake"]
] as const;
