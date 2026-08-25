import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { SUPABASE_CLIENT } from './core/auth/supabase-client';
import { createMockSupabase } from './core/auth/testing/supabase-client.mock';

describe('App', () => {
  beforeEach(async () => {
    const mock = createMockSupabase();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), { provide: SUPABASE_CLIENT, useValue: mock.client }],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });
});
