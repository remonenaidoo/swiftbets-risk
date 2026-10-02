SELECT alert_id AS AlertId, kind AS Kind, fixture_id AS FixtureId, selection_id AS SelectionId, punter_ids AS PunterIds, coupon_ids AS CouponIds,
       total_stake AS TotalStakeMinor, summary AS Summary, raised_at AS RaisedAt
FROM risk.alerts ORDER BY raised_at DESC LIMIT @Limit;
