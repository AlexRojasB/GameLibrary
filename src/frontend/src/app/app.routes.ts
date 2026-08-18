import { Routes } from '@angular/router';

import { HealthCheck } from './core/health/health-check/health-check';
import { authGuard, guestGuard } from './core/auth/auth.guard';
import { Login } from './features/auth/login/login';
import { Register } from './features/auth/register/register';
import { Home } from './features/auth/home/home';
import { PlatformsList } from './features/platforms/platforms-list/platforms-list';
import { VideoGamesList } from './features/games/video-games-list/video-games-list';
import { BoardGamesList } from './features/games/board-games/board-games-list/board-games-list';

export const routes: Routes = [
  { path: '', component: Home, canActivate: [authGuard] },
  { path: 'login', component: Login, canActivate: [guestGuard] },
  { path: 'register', component: Register, canActivate: [guestGuard] },
  { path: 'health', component: HealthCheck },
  { path: 'platforms', component: PlatformsList, canActivate: [authGuard] },
  { path: 'video-games', component: VideoGamesList, canActivate: [authGuard] },
  { path: 'board-games', component: BoardGamesList, canActivate: [authGuard] },
];