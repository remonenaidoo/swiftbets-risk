SELECT fixture_id AS FixtureId, version AS Version, worst_case AS WorstCaseMinor, cap AS CapMinor, cap_overridden AS CapOverridden,
       suspended AS Suspended, outcomes::text AS Outcomes, updated_at AS UpdatedAt
FROM risk.fixture_views ORDER BY worst_case DESC, fixture_id LIMIT @Limit;
