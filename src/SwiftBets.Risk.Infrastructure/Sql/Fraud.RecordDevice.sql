-- Answers whether this is the customer's first sighting; both parts read the same snapshot, so the insert is not seen.
WITH added AS (
    INSERT INTO risk.fraud_devices (user_id, kind, device_hash, ip_prefix, asn, country, latitude, longitude, user_agent, seen_at)
    VALUES (@UserId, @Kind, @DeviceHash, @IpPrefix, @Asn, @Country, @Latitude, @Longitude, @UserAgent, @At))
SELECT NOT EXISTS (SELECT 1 FROM risk.fraud_devices WHERE user_id = @UserId);
