INSERT INTO risk.fraud_devices (user_id, kind, device_hash, ip_prefix, asn, country, latitude, longitude, user_agent, seen_at)
VALUES (@UserId, @Kind, @DeviceHash, @IpPrefix, @Asn, @Country, @Latitude, @Longitude, @UserAgent, @At);
