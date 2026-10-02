SELECT payload::text FROM risk.journal WHERE fixture_id = @FixtureId AND version > @After ORDER BY version;
