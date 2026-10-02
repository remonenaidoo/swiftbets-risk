INSERT INTO risk.fixture_caps (fixture_id, cap, reason, set_by, set_at) VALUES (@FixtureId, @CapMinor, @Reason, @OperatorId, now())
ON CONFLICT (fixture_id) DO UPDATE SET cap = excluded.cap, reason = excluded.reason, set_by = excluded.set_by, set_at = excluded.set_at;
