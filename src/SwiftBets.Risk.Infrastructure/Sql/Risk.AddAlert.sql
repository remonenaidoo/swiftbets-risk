INSERT INTO risk.alerts (alert_id, kind, fixture_id, selection_id, punter_ids, coupon_ids, total_stake, summary, raised_at)
VALUES (@AlertId, @Kind, @FixtureId, @SelectionId, @PunterIds, @CouponIds, @TotalStakeMinor, @Summary, @RaisedAt) ON CONFLICT DO NOTHING;
