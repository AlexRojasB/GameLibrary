import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormGroupDirective } from '@angular/forms';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';
import { Login } from './login/login';
import { Register } from './register/register';

const screens = [
  { component: Login, name: 'login', autocomplete: 'current-password', method: 'signIn' },
  { component: Register, name: 'register', autocomplete: 'new-password', method: 'signUp' },
] as const;

function input(fixture: ComponentFixture<Login | Register>): HTMLInputElement {
  return fixture.nativeElement.querySelector('input[formControlName="password"]');
}

function toggle(fixture: ComponentFixture<Login | Register>): HTMLButtonElement {
  return fixture.nativeElement.querySelector('button[aria-controls]');
}

describe.each(screens)('$name password visibility', screen => {
  let fixture: ComponentFixture<Login | Register>;
  let auth: { signIn: ReturnType<typeof vi.fn>; signUp: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    auth = { signIn: vi.fn().mockResolvedValue(undefined), signUp: vi.fn().mockResolvedValue({ needsEmailConfirmation: true }) };
    TestBed.configureTestingModule({
      imports: [Login, Register],
      providers: [provideRouter([]), { provide: AuthService, useValue: auth }],
    });
    fixture = TestBed.createComponent<Login | Register>(screen.component);
    fixture.detectChanges();
  });

  it('starts hidden and preserves the labelled native input and password-manager attributes', () => {
    const password = input(fixture);
    expect(password.type).toBe('password');
    expect(password.id).toBe(`${screen.name}-password`);
    expect(password.labels?.[0].textContent).toBe('Password');
    expect(password.autocomplete).toBe(screen.autocomplete);
    expect(password.required).toBe(true);
    expect(toggle(fixture).type).toBe('button');
    expect(toggle(fixture).textContent?.trim()).toBe('Show password');
    expect(toggle(fixture).getAttribute('aria-controls')).toBe(password.id);
    expect(toggle(fixture).disabled).toBe(false);
  });

  it.each(['', 'short', 'synthetic-password'])('preserves the same input, value and reactive validation state on both toggles (%j)', value => {
    const password = input(fixture);
    password.value = value;
    password.dispatchEvent(new Event('input'));
    password.dispatchEvent(new Event('blur'));
    const form = fixture.debugElement.query(By.directive(FormGroupDirective)).injector.get(FormGroupDirective).control;
    const control = form.get('password')!;
    expect(control.valid).toBe(value.length > 0 && (screen.name === 'login' || value.length >= 6));
    const before = { value: control.value, errors: control.errors, status: control.status, dirty: control.dirty, touched: control.touched };
    const values = vi.fn();
    const statuses = vi.fn();
    const valueChanges = control.valueChanges.subscribe(values);
    const statusChanges = control.statusChanges.subscribe(statuses);

    for (const [type, action] of [['text', 'Hide password'], ['password', 'Show password']]) {
      toggle(fixture).click();
      fixture.detectChanges();
      expect(input(fixture)).toBe(password);
      expect(password.type).toBe(type);
      expect(password.value).toBe(value);
      expect(password.autocomplete).toBe(screen.autocomplete);
      expect(password.required).toBe(true);
      expect(toggle(fixture).textContent?.trim()).toBe(action);
      expect({ value: control.value, errors: control.errors, status: control.status, dirty: control.dirty, touched: control.touched }).toEqual(before);
    }
    expect(values).not.toHaveBeenCalled();
    expect(statuses).not.toHaveBeenCalled();
    valueChanges.unsubscribe();
    statusChanges.unsubscribe();
  });

  it('does not submit a valid form on toggle, but explicit submit still sends the unchanged password', async () => {
    const email: HTMLInputElement = fixture.nativeElement.querySelector('input[type="email"]');
    email.value = 'synthetic@example.test';
    email.dispatchEvent(new Event('input'));
    input(fixture).value = 'synthetic-password';
    input(fixture).dispatchEvent(new Event('input'));
    const submit = vi.spyOn(fixture.componentInstance, 'onSubmit');
    toggle(fixture).click();
    fixture.detectChanges();
    expect(submit).not.toHaveBeenCalled();
    expect(auth.signIn).not.toHaveBeenCalled();
    expect(auth.signUp).not.toHaveBeenCalled();
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('button[type="submit"]');
    button.click();
    await fixture.whenStable();
    expect(submit).toHaveBeenCalledTimes(1);
    expect(auth[screen.method]).toHaveBeenCalledExactlyOnceWith('synthetic@example.test', 'synthetic-password');
  });

  it('starts hidden in a fresh instance rather than persisting visibility', () => {
    toggle(fixture).click();
    fixture.detectChanges();
    expect(input(fixture).type).toBe('text');
    fixture.destroy();
    fixture = TestBed.createComponent<Login | Register>(screen.component);
    fixture.detectChanges();
    expect(input(fixture).type).toBe('password');
    expect(toggle(fixture).textContent?.trim()).toBe('Show password');
  });
});

it('keeps login and registration password visibility independent', () => {
  TestBed.configureTestingModule({
    imports: [Login, Register],
    providers: [provideRouter([]), { provide: AuthService, useValue: {} }],
  });
  const login = TestBed.createComponent(Login);
  const register = TestBed.createComponent(Register);
  login.detectChanges();
  register.detectChanges();
  toggle(login).click();
  login.detectChanges();
  register.detectChanges();
  expect(input(login).type).toBe('text');
  expect(input(register).type).toBe('password');
  toggle(register).click();
  register.detectChanges();
  toggle(login).click();
  login.detectChanges();
  expect(input(login).type).toBe('password');
  expect(input(register).type).toBe('text');
});
