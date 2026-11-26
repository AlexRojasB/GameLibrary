import { captureRecoveryCode, recoveryRedirect } from './password-recovery-client';

describe('password recovery callback boundary', () => {
  afterEach(() => window.history.replaceState(null, '', '/'));

  it('captures one code once and strips URL values before bootstrap', () => {
    window.history.replaceState(null, '', '/reset-password?code=synthetic-code');
    const take = captureRecoveryCode();
    expect(window.location.pathname + window.location.search + window.location.hash).toBe('/reset-password');
    expect(take()).toBe('synthetic-code');
    expect(take()).toBeNull();
  });

  it.each([
    '', '?code=', '?code=one&code=two', '?code=one&next=https://evil.test',
    '?type=recovery', '#access_token=synthetic&type=recovery',
    '?error=denied&error_description=private-value', '#error=expired&error_description=private-value',
    '?code=one#type=recovery', '?token_hash=one&type=recovery',
  ])('fails closed and scrubs unsupported callbacks: %s', suffix => {
    window.history.replaceState(null, '', '/reset-password' + suffix);
    expect(captureRecoveryCode()()).toBeNull();
    expect(window.location.search + window.location.hash).toBe('');
  });

  it('does not consume or change non-recovery auth URLs', () => {
    window.history.replaceState(null, '', '/login#access_token=synthetic&type=signup');
    expect(captureRecoveryCode()()).toBeNull();
    expect(window.location.hash).toContain('type=signup');
  });

  it.each([
    '/reset-password/', '/%72eset-password', '/reset-password;extra=value', '/%72eset-password/',
  ])('rejects noncanonical recovery path %s before the normal SDK can consume URL credentials', path => {
    window.history.replaceState(null, '', path + '?code=synthetic-code#access_token=synthetic&type=recovery');
    expect(captureRecoveryCode()()).toBeNull();
    expect(window.location.pathname + window.location.search + window.location.hash).toBe('/reset-password');
  });

  it('uses only the current origin and the fixed callback, never query destinations', () => {
    window.history.replaceState(null, '', '/forgot-password?redirectTo=https://evil.test');
    expect(recoveryRedirect()).toBe(window.location.origin + '/reset-password');
  });
});
