import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { captureRecoveryCode, RECOVERY_CODE } from './app/core/auth/password-recovery-client';

const recoveryCode = captureRecoveryCode();
bootstrapApplication(App, { ...appConfig, providers: [...appConfig.providers, { provide: RECOVERY_CODE, useValue: recoveryCode }] })
  .catch((err) => console.error(err));
