import { defineConfig, devices } from '@playwright/test';

// Frontend-only regressions: every Auth/API operation is mocked in the browser.
// No backend, database, provider, email, or Steam process is needed.
export default defineConfig({
  testDir: './e2e',
  testMatch: ['authenticated-back-navigation.spec.ts', 'password-reset.spec.ts', 'back-navigation.spec.ts'],
  fullyParallel: false,
  retries: 0,
  workers: 1,
  use: { baseURL: 'http://127.0.0.1:4200', trace: 'off' },
  webServer: {
    command: 'npm run start:e2e',
    url: 'http://127.0.0.1:4200',
    reuseExistingServer: false,
    timeout: 120_000,
  },
  projects: [
    { name: 'chromium-desktop', use: { ...devices['Desktop Chrome'] } },
    { name: 'mobile-chrome', use: { ...devices['Pixel 7'] } },
  ],
});
