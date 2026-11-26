import { Routes } from '@angular/router';

import { HealthCheck } from './core/health/health-check/health-check';
import { authGuard, guestGuard } from './core/auth/auth.guard';
import { Login } from './features/auth/login/login';
import { Register } from './features/auth/register/register';
import { ForgotPassword } from './features/auth/forgot-password/forgot-password';
import { ResetPassword } from './features/auth/reset-password/reset-password';
import { Home } from './features/auth/home/home';
import { PlatformsList } from './features/platforms/platforms-list/platforms-list';
import { LibraryPage } from './features/library/library-page/library-page';
import { ManageHub } from './features/manage/manage-hub';
import { SteamPage } from './features/manage/steam/steam-page';
import { PlayLogPage } from './features/play-log/play-log-page/play-log-page';

export const routes: Routes = [
  { path: '', component: Home, canActivate: [authGuard] },
  { path: 'login', component: Login, canActivate: [guestGuard] },
  { path: 'register', component: Register, canActivate: [guestGuard] },
  { path: 'forgot-password', component: ForgotPassword },
  { path: 'reset-password', component: ResetPassword },
  { path: 'health', component: HealthCheck },
  { path: 'library', component: LibraryPage, canActivate: [authGuard] },
  { path: 'random-picker', loadComponent: () => import('./features/random-picker/random-picker-page/random-picker-page').then((module) => module.RandomPickerPage), canActivate: [authGuard] },
  { path: 'play-log', component: PlayLogPage, canActivate: [authGuard] },
  { path: 'manage', component: ManageHub, canActivate: [authGuard] },
  { path: 'manage/steam', component: SteamPage, canActivate: [authGuard] },
  { path: 'platforms', component: PlatformsList, canActivate: [authGuard] },
  { path: 'video-games', loadComponent: () => import('./features/games/video-games-list/video-games-list').then((module) => module.VideoGamesList), canActivate: [authGuard] },
  { path: 'board-games', loadComponent: () => import('./features/games/board-games/board-games-list/board-games-list').then((module) => module.BoardGamesList), canActivate: [authGuard] },
];
