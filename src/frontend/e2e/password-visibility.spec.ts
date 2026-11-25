import { expect, test } from '@playwright/test';

test('shows/hides existing passwords with keyboard controls and submits only on explicit sign in', async ({ page, request }) => {
  expect((await request.post('http://127.0.0.1:5218/e2e/reset')).ok()).toBeTruthy();
  let authRequests = 0;
  page.on('request', request => {
    if (request.method() === 'POST' && /\/(e2e\/auth\/session|auth\/v1\/signup)$/.test(new URL(request.url()).pathname)) {
      authRequests++;
    }
  });
  await page.goto('/login');

  for (const [id, autocomplete] of [['login-password', 'current-password'], ['register-password', 'new-password']]) {
    const password = page.getByLabel('Password', { exact: true });
    const show = page.getByRole('button', { name: 'Show password', exact: true });
    await expect(password).toHaveAttribute('id', id);
    await expect(password).toHaveAttribute('type', 'password');
    await expect(password).toHaveAttribute('autocomplete', autocomplete);
    await expect(show).toHaveAttribute('type', 'button');
    await expect(show).toHaveAttribute('aria-controls', id);
    await page.getByLabel('Email', { exact: true }).fill('player@example.com');
    await password.fill('password123'); // Synthetic local-only credentials.
    await password.press('Tab');
    await expect(show).toBeFocused();
    await show.press('Enter');
    const hide = page.getByRole('button', { name: 'Hide password', exact: true });
    await expect(hide).toBeFocused();
    await expect(password).toHaveAttribute('type', 'text');
    await expect(password).toHaveValue('password123');
    await expect(password).toHaveAttribute('autocomplete', autocomplete);

    const box = await hide.boundingBox();
    expect(box!.height).toBeGreaterThanOrEqual(44);
    expect(box!.width).toBeGreaterThanOrEqual(44);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await hide.press('Space');
    await expect(password).toHaveAttribute('type', 'password');
    await expect(password).toHaveValue('password123');
    await expect(show).toBeFocused();
    await show.click();
    await expect(password).toHaveAttribute('type', 'text');
    expect(authRequests).toBe(0);

    // Leave a visible field; the next screen must still start hidden.
    if (id === 'login-password') await page.getByRole('link', { name: 'Create one', exact: true }).click();
    else await page.getByRole('link', { name: 'Sign in', exact: true }).click();
  }

  const password = page.getByLabel('Password', { exact: true });
  await expect(password).toHaveAttribute('type', 'password');
  await page.getByLabel('Email', { exact: true }).fill('player@example.com');
  await password.fill('password123');
  await page.getByRole('button', { name: 'Show password', exact: true }).click();
  await page.getByRole('button', { name: 'Hide password', exact: true }).click();
  expect(authRequests).toBe(0);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('heading', { name: /your game night starts here/i })).toBeVisible();
  expect(authRequests).toBe(1);
});
