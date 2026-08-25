import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { Component } from '@angular/core';
import { App } from './app';
import { SUPABASE_CLIENT } from './core/auth/supabase-client';
import { createMockSupabase } from './core/auth/testing/supabase-client.mock';

@Component({ selector: 'app-route-stub', template: '' })
class RouteStub {}

describe('App', () => {
  beforeEach(async () => {
    const mock = createMockSupabase();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([{ path: 'play-log', component: RouteStub }]), { provide: SUPABASE_CLIENT, useValue: mock.client }],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('adds Play Log to desktop nav without adding it to mobile nav', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const desktopLinks = [...element.querySelectorAll('.app-shell__desktop-nav a')].map((link) => link.textContent?.trim());
    const mobileLinks = [...element.querySelectorAll('.app-shell__mobile-nav a')].map((link) => link.textContent?.trim());

    expect(desktopLinks).toEqual(['Home', 'Library', 'Pick', 'Play Log', 'Manage']);
    expect(mobileLinks).toEqual(['Home', 'Library', 'Pick', 'Manage']);
  });

  it('does not mark Manage active on Play Log', async () => {
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/play-log');
    await fixture.whenStable();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.app-shell__desktop-nav a[href="/play-log"]')?.classList.contains('is-active')).toBe(true);
    expect(element.querySelector('.app-shell__desktop-nav a[href="/manage"]')?.classList.contains('is-active')).toBe(false);
    expect(element.querySelector('.app-shell__mobile-nav a[href="/manage"]')?.classList.contains('is-active')).toBe(false);
  });
});
