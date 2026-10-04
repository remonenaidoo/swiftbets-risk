SELECT latitude AS Latitude, longitude AS Longitude, seen_at AS SeenAt FROM risk.fraud_devices
WHERE user_id = @UserId AND latitude IS NOT NULL AND longitude IS NOT NULL ORDER BY seen_at DESC LIMIT 1;
