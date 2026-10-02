INSERT INTO risk.journal (fixture_id, version, coupon_id, kind, payload, recorded_at)
VALUES (@FixtureId, @Version, @CouponId, @Kind, CAST(@Payload AS jsonb), now());
