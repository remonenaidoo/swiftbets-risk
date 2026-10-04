-- Every device and bank account the customer used, with all the accounts that share it.
SELECT 'device' AS Link, d.device_hash AS Key, array_agg(DISTINCT o.user_id) AS UserIds
FROM (SELECT DISTINCT device_hash FROM risk.fraud_devices WHERE user_id = @UserId) d
JOIN risk.fraud_devices o ON o.device_hash = d.device_hash GROUP BY d.device_hash
UNION ALL
SELECT 'bank account' AS Link, b.fingerprint AS Key, array_agg(o.user_id) AS UserIds
FROM risk.fraud_bank_accounts b JOIN risk.fraud_bank_accounts o ON o.fingerprint = b.fingerprint
WHERE b.user_id = @UserId GROUP BY b.fingerprint;
