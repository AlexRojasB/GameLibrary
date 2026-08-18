import { InjectionToken } from '@angular/core';
import { createClient, SupabaseClient } from '@supabase/supabase-js';

import { environment } from '../../../environments/environment';

export function createSupabaseClient(): SupabaseClient {
  if (!environment.supabaseUrl || !environment.supabaseKey) {
    throw new Error('Supabase client requires environment.supabaseUrl and environment.supabaseKey.');
  }
  return createClient(environment.supabaseUrl, environment.supabaseKey);
}

export const SUPABASE_CLIENT = new InjectionToken<SupabaseClient>('Supabase client');