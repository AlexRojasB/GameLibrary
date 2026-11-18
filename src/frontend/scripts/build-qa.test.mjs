import assert from 'node:assert/strict';
import test from 'node:test';
import { qaEnvironment } from './build-qa.mjs';

const valid = {
  QA_API_BASE_URL: 'https://game-library-qa-api.onrender.com/',
  QA_SUPABASE_URL: 'https://abcdefghijklmnopqrst.supabase.co/',
  QA_SUPABASE_PUBLISHABLE_KEY: 'sb_publishable_synthetic-test-only',
};

test('separate HTTPS origins and public key, with test auth always disabled', () => {
  assert.deepEqual(qaEnvironment(valid), {
    apiBaseUrl: 'https://game-library-qa-api.onrender.com',
    supabaseUrl: 'https://abcdefghijklmnopqrst.supabase.co',
    supabaseKey: valid.QA_SUPABASE_PUBLISHABLE_KEY,
    e2e: { enabled: false },
  });
});

for (const name of Object.keys(valid)) {
  test(`missing ${name} fails before building`, () => {
    assert.throws(() => qaEnvironment({ ...valid, [name]: undefined }), new RegExp(name));
  });
}

for (const value of [
  'http://localhost:5218', 'https://user:password@example.com',
  'https://api.example.com/path', 'https://api.example.com?token=private',
  'https://api.example.com#token', '/api',
]) {
  test(`unsafe API origin shape ${value.split('?')[0].split('#')[0]} is rejected`, () => {
    assert.throws(() => qaEnvironment({ ...valid, QA_API_BASE_URL: value }), /QA_API_BASE_URL/);
  });
}

test('existing development project cannot be reused for QA', () => {
  assert.throws(() => qaEnvironment({ ...valid, QA_SUPABASE_URL: 'https://iramzxpjbnldhebykzhx.supabase.co' }), /separate/);
});

for (const value of ['sb_secret_synthetic', 'eyJhbGciOiJIUzI1NiJ9.synthetic', '']) {
  test('non-publishable credentials cannot enter the browser bundle', () => {
    assert.throws(() => qaEnvironment({ ...valid, QA_SUPABASE_PUBLISHABLE_KEY: value }), /public publishable/);
  });
}

test('unrelated environment variables and test-mode flags are not serialized', () => {
  const result = qaEnvironment({ ...valid, ConnectionStrings__Default: 'secret', E2E__Auth__Enabled: 'true' });
  assert.equal(JSON.stringify(result).includes('secret'), false);
  assert.deepEqual(result.e2e, { enabled: false });
});
