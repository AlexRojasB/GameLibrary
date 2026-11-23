# Buffered-response assertion regressions

Tests the exact internal helper compiled into `tools/cover-api/Verify.csproj`.
This executable reuses its existing xunit dependency; it does not start the API,
access a database/provider, decode images, perform network IO or force GC.

After preparing the existing pinned native build asset for the host RID:

```sh
dotnet build tools/cover-api-body-tests/BodyTests.csproj -c Release
dotnet run --project tools/cover-api-body-tests/BodyTests.csproj -c Release --no-build
```

Linux diagnostic CI builds first, then runs the executable through the existing
network/privilege isolation guard. These focused regressions supplement, never
replace, the complete 18-case API/native workload. Privacy, eight-read/two-upload
concurrency, provider counters and original request ordering remain asserted by
that unchanged workload. The allocation check measures one already-buffered body,
not Linux resident peak memory or the observer effect.
