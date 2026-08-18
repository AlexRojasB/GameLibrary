import { Component } from '@angular/core';
import { provideRouter, Router } from '@angular/router';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Register } from './register';
import { SUPABASE_CLIENT } from '../../../core/auth/supabase-client';
import { createMockSupabase, createTestSession } from '../../../core/auth/testing/supabase-client.mock';

@Component({ template: '' })
class DummyComponent {}

describe('Register', () => {
  let fixture: ComponentFixture<Register>;

  function configure(signUpResponse: unknown): Router {
    const mock = createMockSupabase();
    mock.auth.signUp.mockResolvedValue(signUpResponse);
    TestBed.configureTestingModule({
      imports: [Register, DummyComponent],
      providers: [
        provideRouter([
          { path: 'register', component: Register },
          { path: '', component: DummyComponent },
        ]),
        { provide: SUPABASE_CLIENT, useValue: mock.client },
      ],
    });
    fixture = TestBed.createComponent(Register);
    fixture.detectChanges();
    return TestBed.inject(Router);
  }

  async function submit(email: string, password: string): Promise<void> {
    const emailInput = fixture.nativeElement.querySelector('#register-email') as HTMLInputElement;
    emailInput.value = email;
    emailInput.dispatchEvent(new Event('input'));
    const passwordInput = fixture.nativeElement.querySelector('#register-password') as HTMLInputElement;
    passwordInput.value = password;
    passwordInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    const button = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
    button.click();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('shows a confirmation notice when email confirmation is required', async () => {
    const router = configure({
      data: { user: { id: 'user-2', email: 'new@example.com' }, session: null },
      error: null,
    });
    await router.navigateByUrl('/register');

    await submit('new@example.com', 'password');

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Check your email');
    expect(router.url).toBe('/register');
  });

  it('authenticates and navigates home when the session is immediately usable', async () => {
    const session = createTestSession();
    const router = configure({
      data: { user: session.user, session },
      error: null,
    });
    await router.navigateByUrl('/register');

    await submit('new@example.com', 'password');

    expect(router.url).toBe('/');
  });

  it('shows a normalized error on failed registration', async () => {
    const router = configure({
      data: { user: null, session: null },
      error: { name: 'AuthApiError', message: 'User already registered', status: 400 },
    });
    await router.navigateByUrl('/register');

    await submit('taken@example.com', 'password');

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('An account with this email already exists.');
    expect(router.url).toBe('/register');
  });
});