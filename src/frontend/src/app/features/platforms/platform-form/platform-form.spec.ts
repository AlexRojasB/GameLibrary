import { ComponentFixture, TestBed } from '@angular/core/testing';

import { PlatformForm } from './platform-form';

describe('PlatformForm', () => {
  let fixture: ComponentFixture<PlatformForm>;
  let component: PlatformForm;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [PlatformForm] });
    fixture = TestBed.createComponent(PlatformForm);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('rejects an empty name on submit and does not emit', () => {
    const spy = vi.spyOn(component.submitted, 'emit');
    component.name.setValue('');
    component.submit();

    expect(spy).not.toHaveBeenCalled();
    expect(component.name.hasError('required')).toBe(true);
  });

  it('rejects a whitespace-only name on submit and does not emit', () => {
    const spy = vi.spyOn(component.submitted, 'emit');
    component.name.setValue('   ');
    component.submit();

    expect(spy).not.toHaveBeenCalled();
    expect(component.name.hasError('required')).toBe(true);
  });

  it('rejects a trimmed name longer than 100 characters on submit', () => {
    const spy = vi.spyOn(component.submitted, 'emit');
    component.name.setValue('x'.repeat(101));
    component.submit();

    expect(spy).not.toHaveBeenCalled();
    expect(component.name.hasError('maxlength')).toBe(true);
  });

  it('trims the name and emits it on submit', () => {
    const spy = vi.spyOn(component.submitted, 'emit');
    component.name.setValue('  Steam  ');
    component.submit();

    expect(spy).toHaveBeenCalledWith('Steam');
  });

  it('preloads the initial name for editing', () => {
    fixture.componentRef.setInput('initialName', 'Steam');
    fixture.detectChanges();

    expect(component.name.value).toBe('Steam');
  });

  it('emits cancelled when the cancel button is clicked', () => {
    const spy = vi.spyOn(component.cancelled, 'emit');
    const cancelButton = (fixture.nativeElement as HTMLElement).querySelector(
      'button[type="button"]',
    ) as HTMLButtonElement;
    cancelButton.click();

    expect(spy).toHaveBeenCalled();
  });

  it('displays the server error message', () => {
    fixture.componentRef.setInput('serverError', 'A platform with this name already exists.');
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('A platform with this name already exists.');
  });
});