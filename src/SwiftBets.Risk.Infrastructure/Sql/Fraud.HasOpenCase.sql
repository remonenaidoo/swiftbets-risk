SELECT EXISTS (SELECT 1 FROM risk.fraud_cases WHERE user_id = @UserId AND status = 'open' AND severity >= @Severity);
