INSERT INTO risk.fixture_views (fixture_id, version, worst_case, cap, cap_overridden, suspended, outcomes, updated_at)
VALUES (@FixtureId, @Version, @WorstCaseMinor, @CapMinor, @CapOverridden, @Suspended, CAST(@Outcomes AS jsonb), @UpdatedAt)
ON CONFLICT (fixture_id) DO UPDATE SET version = excluded.version, worst_case = excluded.worst_case, cap = excluded.cap,
    cap_overridden = excluded.cap_overridden, suspended = excluded.suspended, outcomes = excluded.outcomes, updated_at = excluded.updated_at
WHERE risk.fixture_views.version <= excluded.version;
