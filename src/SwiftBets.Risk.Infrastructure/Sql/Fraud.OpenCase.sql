WITH opened AS (
    INSERT INTO risk.fraud_cases (case_id, user_id, rule, severity, linked_user_ids, summary, status, raised_at)
    VALUES (@CaseId, @UserId, @Rule, @Severity, @LinkedUserIds, @Summary, 'open', @RaisedAt) ON CONFLICT DO NOTHING RETURNING case_id)
INSERT INTO risk.fraud_case_audit (case_id, actor, action, reason, at)
SELECT case_id, 'risk', 'raised', @Summary, @RaisedAt FROM opened;
