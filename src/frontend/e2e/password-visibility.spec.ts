import { expect, test } from '@playwright/test';

import { email, password as syntheticPassword, mockAuthenticatedNavigation } from './fixtures/authenticated-navigation';

test('shows/hides existing passwords with keyboard controls and submits only on explicit sign in', async ({ page, context }) => {
  const fixture = await mockAuthenticatedNavigation(context);
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
    await expect(show).toHaveAccessibleName('Show password');
    await expect(show).toHaveText('');
    await expect(show.locator('svg')).toHaveAttribute('aria-hidden', 'true');
    await expect(show.locator('svg')).toHaveAttribute('focusable', 'false');
    await expect(show.locator('svg circle')).toHaveCount(1);
    await expect(show.locator('svg line')).toHaveCount(0);
    await page.getByLabel('Email', { exact: true }).fill(email);
    await password.fill(syntheticPassword);
    await password.press('Tab');
    await expect(show).toBeFocused();
    expect(await show.evaluate(button => button.matches(':focus-visible'))).toBe(true);
    expect(await show.evaluate(button => parseFloat(getComputedStyle(button).outlineWidth))).toBeGreaterThan(0);
    await show.press('Enter');
    const hide = page.getByRole('button', { name: 'Hide password', exact: true });
    await expect(hide).toBeFocused();
    await expect(hide).toHaveAccessibleName('Hide password');
    await expect(hide).toHaveText('');
    await expect(hide.locator('svg line')).toHaveCount(1);
    await expect(password).toHaveAttribute('type', 'text');
    await expect(password).toHaveValue(syntheticPassword);
    await expect(password).toHaveAttribute('autocomplete', autocomplete);

    const box = await hide.boundingBox();
    expect(box!.height).toBeGreaterThanOrEqual(44);
    expect(box!.width).toBeGreaterThanOrEqual(44);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await hide.press('Space');
    await expect(password).toHaveAttribute('type', 'password');
    await expect(password).toHaveValue(syntheticPassword);
    await expect(show).toBeFocused();
    await expect(show.locator('svg line')).toHaveCount(0);
    await show.locator('svg').click();
    await expect(password).toHaveAttribute('type', 'text');
    expect(authRequests).toBe(0);

    // Leave a visible field; the next screen must still start hidden.
    if (id === 'login-password') await page.getByRole('link', { name: 'Create one', exact: true }).click();
    else await page.getByRole('link', { name: 'Sign in', exact: true }).click();
  }

  const password = page.getByLabel('Password', { exact: true });
  await expect(password).toHaveAttribute('type', 'password');
  await page.getByLabel('Email', { exact: true }).fill(email);
  await password.fill(syntheticPassword);
  await page.getByRole('button', { name: 'Show password', exact: true }).click();
  await page.getByRole('button', { name: 'Hide password', exact: true }).click();
  expect(authRequests).toBe(0);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('heading', { name: /your game night starts here/i })).toBeVisible();
  expect(authRequests).toBe(1);
  expect(fixture.signIns).toBe(1);
});
