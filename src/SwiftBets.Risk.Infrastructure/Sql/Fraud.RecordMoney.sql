INSERT INTO risk.fraud_money (reference, kind, user_id, amount, at) VALUES (@Reference, @Kind, @UserId, @Amount, @At) ON CONFLICT DO NOTHING;
