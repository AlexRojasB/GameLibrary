import { defineConfig } from '@playwright/test';

import navigation from './playwright.auth-navigation.config';

// Reuse the existing frontend-only server and desktop/mobile projects. All
// selected Auth/API calls are mocked; no backend or database is required.
export default defineConfig(navigation, {
  testMatch: ['password-visibility.spec.ts', 'password-reset.spec.ts'],
});
