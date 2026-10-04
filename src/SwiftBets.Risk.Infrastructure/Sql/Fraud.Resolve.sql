WITH resolved AS (
    UPDATE risk.fraud_cases SET status = 'resolved', resolved_by = @Actor, resolution = @Resolution, resolved_reason = @Reason, resolved_at = @At
    WHERE case_id = @CaseId AND status = 'open' RETURNING case_id)
INSERT INTO risk.fraud_case_audit (case_id, actor, action, reason, at)
SELECT case_id, @Actor, 'resolved:' || @Resolution, @Reason, @At FROM resolved;
