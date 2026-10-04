-- A coupon on several fixtures is journalled once per fixture, so stakes are counted once per coupon.
SELECT (SELECT COALESCE(SUM(amount), 0)::bigint FROM risk.fraud_money WHERE user_id = @UserId AND kind = 'deposit' AND at >= @Since) AS Deposited,
       (SELECT COALESCE(SUM(stake), 0)::bigint FROM (
            SELECT DISTINCT ON (coupon_id) (payload -> 'placed' ->> 'stakeMinor')::bigint AS stake FROM risk.journal
            WHERE kind = 'placed' AND payload -> 'placed' ->> 'punterId' = @UserId::text AND recorded_at >= @Since) s) AS Staked;
