# Feature 011 — authorized local linking stabilization

2026-11-18/19. Owner-authorized implementation and local tests only. No cloud
permission/configuration changes, publication, real Steam calls or deployment.
Product, Domain and the two-project Architecture remain authoritative. No schema,
new identity provider, persistent browser auth session or infrastructure is added.

## Empty/non-visible libraries

HTTP-success `{"response":{}}`, `{"response":{"game_count":0}}` and explicit
empty games arrays represent no visible games. Missing/null/non-object response,
unknown/error-only payloads, positive counts without games, invalid field types,
non-success HTTP (including 401/403/429), network failures and timeouts remain
provider failures, not empty successes. The UI explains empty/private game details.

## Browser correlation, not authentication

Only the existing JWT-authorized `POST /steam/link-requests` issues a bounded
HttpOnly, host-only, Path=/ cookie containing the hash of that request's random
state, never the raw state or a Steam/application credential. HTTPS uses the
`__Host-GameLibrary.SteamLink` cookie with Secure and SameSite=None. Production
linking requires HTTPS API and frontend origins. Local loopback HTTP tests use
`GameLibrary.SteamLink`, SameSite=Lax, on one host name across ports.

Both start and callback require the matching cookie before provider/DB work.
Transferring a URL into a different browser therefore grants no linking authority.
The cookie alone grants no application login or library access: the existing
hashed SQL state, expiry, verified Steam assertion and ownership path remain
authoritative. Missing/mismatched proof fails closed with normal failure feedback.

One cookie slot means a newly issued proof supersedes older URLs in that browser.
Callbacks never write/delete that slot: an old delayed response cannot erase a
new flow. The cookie expires at the state deadline; consumed/expired SQL state
rejects replay even while the old cookie is still present. A failed assertion
does not consume state. An already in-flight valid callback may win the SQL race;
it must never overwrite an existing link. This is flow correlation, not automatic
Supabase logout/JWT revocation or a new application session mechanism.

Only the authenticated link-request HttpClient call uses `withCredentials` and
an exact-origin credentialed CORS policy. Other API calls keep bearer-only CORS.
Cross-site QA hosts can be affected by third-party-cookie blocking: this must be
tested in supported hosted browsers before activation. No cookie bypass or new
same-site hosting infrastructure is silently introduced. State URLs/responses
must not be cached or used as referrers; credentials/state must not be logged.

## Concurrent callbacks

Keep provider verification outside SQL locks. Lock the pending state and then its
Library, recheck expiry using fresh UTC after waiting, and recheck link uniqueness.
Link creation/unlink use the same Library lock. Distinct Libraries racing for one
SteamID64 are still protected by the unique constraint; translate only the known
Steam link uniqueness conflicts to normal failure with rollback, not arbitrary DB
errors or blind retries. Exactly one successful callback consumes its own state.

## Explicit selected imports and notices

Retain success/failure callback notices in component memory while removing only
the recognized query flag with replaceUrl. Notices are presentation, never proof
of link ownership; GET status remains authoritative.

Select All becomes Select up to 100 when necessary. It retains explicit choices,
fills in displayed order up to 100 eligible candidates and excludes imported IDs.
Manual toggles cannot exceed 100; deselection remains available. Explain the limit
and require an explicit import for each reviewed batch. Never auto-import, chunk
requests in the background, merge manual same-name games or alter AppID identity.

## Required verification

Fake-HTTP client tests for response shapes, errors, request encoding and OpenID
verification; real PostgreSQL tests for state replay/expiry, browser proof, same-
and distinct-state races, uniqueness, user isolation, manual entries and minimal
runtime permissions; Angular notices/limit/credential tests; desktop/mobile
deterministic browser journeys including transferred URLs. No real Steam or QA DB
is used. Hosted cookie compatibility and owner-approved real Steam smoke remain
separate activation gates. See docs/deployment/steam-qa-activation.md.
