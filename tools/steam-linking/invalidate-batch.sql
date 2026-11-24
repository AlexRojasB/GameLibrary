-- OPERATOR-ONLY procedure, never application startup/migration or an HTTP endpoint.
-- Requires separately verified all-replica admission stop, drain and primary
-- reconciliation. The session acknowledgment below is NOT proof of that barrier.
-- Run on the explicitly approved primary with psql -X -v ON_ERROR_STOP=1.
-- One Library and at most 100 rows per invocation. No SKIP LOCKED or silent skips.
SET statement_timeout = '10s';
BEGIN ISOLATION LEVEL READ COMMITTED;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '10s';
SET LOCAL transaction_timeout = '15s';
DO $invalidation$
DECLARE
    target_library uuid;
    affected integer;
BEGIN
    IF current_setting('gamelibrary.steam_link_quiesced', true) IS DISTINCT FROM 'on' THEN
        RAISE EXCEPTION 'Steam invalidation requires explicit quiescence acknowledgment';
    END IF;
    SELECT l.id INTO target_library FROM public.libraries l
    WHERE EXISTS (SELECT 1 FROM public.steam_link_requests r WHERE r.library_id = l.id
        AND r.protocol_version = 2 AND r.consumed_at IS NULL AND r.cancelled_at IS NULL)
    ORDER BY l.id LIMIT 1 FOR UPDATE OF l;

    -- A new statement after the Library lock sees the resolved writer, not the
    -- discovery snapshot. Preserve consumption/receipts even after a lock wait.
    WITH locked AS (
        SELECT id FROM public.steam_link_requests WHERE library_id = target_library
            AND protocol_version = 2 AND consumed_at IS NULL AND cancelled_at IS NULL
        ORDER BY id LIMIT 100 FOR UPDATE
    )
    UPDATE public.steam_link_requests SET cancelled_at = clock_timestamp()
    WHERE id IN (SELECT id FROM locked) AND library_id = target_library
        AND protocol_version = 2 AND consumed_at IS NULL AND cancelled_at IS NULL;
    GET DIAGNOSTICS affected = ROW_COUNT;
    RAISE NOTICE 'Steam v2 invalidation batch: % rows cancelled', affected;
END;
$invalidation$;
COMMIT;
-- With admission stopped AND earlier granting work resolved, repeat bounded
-- invocations until this count is zero. Missing schema/permissions/errors fail closed.
SELECT count(*) AS outstanding_v2 FROM public.steam_link_requests
WHERE protocol_version = 2 AND consumed_at IS NULL AND cancelled_at IS NULL;
