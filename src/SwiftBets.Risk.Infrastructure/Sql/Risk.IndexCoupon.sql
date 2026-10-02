INSERT INTO risk.coupon_fixtures (coupon_id, fixture_id) SELECT @CouponId, unnest(@FixtureIds) ON CONFLICT DO NOTHING;
