# Bounded Whole-API Verification

This is test tooling, not a third production application or a deployment image.
It hosts the existing API through its HTTP test factory on **real Kestrel sockets**,
with real PostgreSQL, the unchanged native processor and the **production Storage
HTTP adapter**. A loopback HTTP fixture simulates only Storage protocol responses;
JWT validation uses existing deterministic test keys, not real Supabase discovery.
The final timer scenario reuses the existing manual-clock integration regression.

Run from the repository with Docker, jq and the existing local
`game-library-postgres-test` container available:

```sh
bash tools/cover-api/run.sh
```

The runner targets only that named local Docker PostgreSQL container. It injects
its credential privately into the child environment, creates/drops generated
`cover_http_*` databases, and never resets the source test/E2E/development database.
Do not enable shell tracing or print Docker Env/connection strings. Its test-only
password connection disables optional GSS negotiation; no Kerberos system library
is installed or claimed supported by the approved image.

The Dockerfile uses the exact approved Noble SDK/runtime digests, linux/amd64,
RID linux-x64, framework-dependent .NET 10 and non-root app user. The runtime has
no SDK or inspection/codec packages added. Native assets are compared with a normal
API publish. Loopback endpoints use ephemeral ports; no Docker ports are published.
Read-only filesystem, 32 MiB /tmp, all capabilities dropped, no-new-privileges,
256 PID limit, **512 MiB memory/no additional swap and two CPUs** are enforced.

The process requires explicit `--local-bounded-verification` and asserts cgroup
limits. Work is finite: three overlapping large-image batches, fixed fault/cancel/
restart cases, twenty small successful rate-test uploads/cancels, four minute-window
waits, and bounded probes. An eight-minute operation cancellation budget bounds
the run; allow up to ten minutes including Docker build/teardown in an outer runner.
It is not a soak test, a throughput benchmark or an exhaustive native-fuzz proof.

Eight Storage reads send headers/one byte then stall, exercising allocated API
read buffers during uploads. The exact-2-MiB read source is an explicitly seeded
transport fixture: canonical JPEG plus valid COM segments and matching test metadata.
It is not claimed to be untouched processor output. Upload fixtures use real
near-4MP JPEGs/PNG with inert trailing padding to the exact 5 MiB file limit;
the normal processor strips that padding before the adapter writes fresh output.

The driver and Storage fixture share the API cgroup/process. Report both process
RSS and **cgroup memory.peak/events**, not RSS as if it were the enforced charge.
The external PostgreSQL server is outside the API budget, as it would be in the
approved architecture. Host shutdown uses `IHost.StopAsync` with a pending read,
not factory disposal canceling its clients. This does not verify an actual hosting
vendor's SIGTERM/kill grace, ingress/CDN/TLS configuration or service-manager policy.

Full evidence, limitations and remaining operator requirements:
[G06 report](../../specs/013-camera-cover-upload/g06-operational-verification.md).
This tool does not prove real Supabase permissions and must never receive live
credentials, deploy, enable production uploads or provision remote resources.
