# Native verifier helper regressions

`dotnet run --project tools/cover-api-body-tests -c Release` executes the actual
internal body-count helper via a test-only friend assembly, reusing existing xunit.
Twelve cases cover complete EOF, short/extra bodies, cancellation, disposal,
buffering semantics, errors and bounded allocation. No API/DB/Storage/native host
or forced GC runs. The allocation probe is not Linux peak-memory evidence.

GetAsync still buffers before completion. The helper avoids only its second full
copy, uses an Int64 count and propagates cancellation during counting. This can
observe cancellation where an immediate buffered byte-array copy did not.
