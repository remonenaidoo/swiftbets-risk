INSERT INTO risk.fraud_bank_accounts (user_id, fingerprint, saved_at) VALUES (@UserId, @Fingerprint, @At)
ON CONFLICT (user_id) DO UPDATE SET fingerprint = EXCLUDED.fingerprint, saved_at = EXCLUDED.saved_at;
SELECT user_id FROM risk.fraud_bank_accounts WHERE fingerprint = @Fingerprint AND user_id <> @UserId;
