SELECT actor AS Actor, action AS Action, reason AS Reason, at AS At FROM risk.fraud_case_audit WHERE case_id = @CaseId ORDER BY at, id;
