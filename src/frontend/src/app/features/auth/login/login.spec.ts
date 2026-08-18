import { Component } from '@angular/core';
import { provideRouter, Router } from '@angular/router';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Login } from './login';
import { SUPABASE_CLIENT } from '../../../core/auth/supabase-client';
import { createMockSupabase, createTestSession } from '../../../core/auth/testing/supabase-client.mock';

@Component({ template: '' })
class DummyComponent {}

describe('Login', () => {
  let fixture: ComponentFixture<Login>;

  function configure(signInResponse: unknown): Router {
    const mock = createMockSupabase();
    mock.auth.signInWithPassword.mockResolvedValue(signInResponse);
    TestBed.configureTestingModule({
      imports: [Login, DummyComponent],
      providers: [
        provideRouter([
          { path: 'login', component: Login },
          { path: '', component: DummyComponent },
        ]),
        { provide: SUPABASE_CLIENT, useValue: mock.client },
      ],
    });
    fixture = TestBed.createComponent(Login);
    fixture.detectChanges();
    return TestBed.inject(Router);
  }

  async function submit(email: string, password: string): Promise<void> {
    const emailInput = fixture.nativeElement.querySelector('#login-email') as HTMLInputElement;
    emailInput.value = email;
    emailInput.dispatchEvent(new Event('input'));
    const passwordInput = fixture.nativeElement.querySelector('#login-password') as HTMLInputElement;
    passwordInput.value = password;
    passwordInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    const button = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
    button.click();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('signs in and navigates home on success', async () => {
    const router = configure({
      data: { session: createTestSession(), user: createTestSession().user },
      error: null,
    });
    await router.navigateByUrl('/login');

    await submit('user@example.com', 'password');

    expect(router.url).toBe('/');
  });

  it('shows a normalized error and stays on the form on failure', async () => {
    const router = configure({
      data: { session: null },
      error: { name: 'AuthApiError', message: 'Invalid login credentials', status: 400 },
    });
    await router.navigateByUrl('/login');

    await submit('user@example.com', 'wrong');

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Invalid email or password.');
    expect(router.url).toBe('/login');
  });
});