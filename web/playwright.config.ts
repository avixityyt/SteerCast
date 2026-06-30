import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./tests",
  timeout: 15_000,
  use: {
    baseURL: "http://127.0.0.1:38271",
    headless: true,
    launchOptions: {
      executablePath: "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe"
    }
  },
  webServer: {
    command: '"..\\src\\SteerCast.App\\bin\\Release\\net10.0-windows10.0.19041.0\\win-x64\\SteerCast.exe" --background',
    url: "http://127.0.0.1:38271/api/health",
    reuseExistingServer: true,
    timeout: 15_000
  }
});
