SELECT case_id AS CaseId, user_id AS UserId, rule AS Rule, severity AS Severity, linked_user_ids AS LinkedUserIds, summary AS Summary, status AS Status,
       raised_at AS RaisedAt, resolved_by AS ResolvedBy, resolution AS Resolution, resolved_reason AS ResolvedReason, resolved_at AS ResolvedAt
FROM risk.fraud_cases WHERE case_id = @CaseId;
