INSERT INTO risk.snapshots (fixture_id, version, state, taken_at)
VALUES (@FixtureId, @Version, CAST(@State AS jsonb), now())
ON CONFLICT (fixture_id) DO UPDATE SET version = excluded.version, state = excluded.state, taken_at = excluded.taken_at
WHERE risk.snapshots.version < excluded.version;
