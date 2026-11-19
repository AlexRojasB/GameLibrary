import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  use: {
    baseURL: 'http://127.0.0.1:4200',
    trace: 'on-first-retry',
  },
  webServer: [
    {
      command:
        'ASPNETCORE_ENVIRONMENT=Test E2E__Auth__Enabled=true E2E__CoverImageSearch__Enabled=true E2E__CoverUploads__Enabled=true CoverUploads__Enabled=true E2E__SteamIntegration__Enabled=true AllowedOrigins__0=http://127.0.0.1:4200 SteamIntegration__Enabled=true SteamIntegration__WebApiKey=e2e-fake-key SteamIntegration__PublicApiBaseUrl=http://127.0.0.1:5218 SteamIntegration__FrontendBaseUrl=http://127.0.0.1:4200 dotnet run --project ../backend/GameLibrary.Api/GameLibrary.Api.csproj --urls http://127.0.0.1:5218',
      url: 'http://127.0.0.1:5218/health',
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
    {
      command: 'npm run start:e2e',
      url: 'http://127.0.0.1:4200',
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
  ],
  projects: [
    {
      name: 'chromium-desktop',
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'mobile-chrome',
      use: { ...devices['Pixel 7'] },
    },
  ],
});
