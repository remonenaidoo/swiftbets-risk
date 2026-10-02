-- Rolls back 0001_risk. All liability history, caps and alerts are lost; liability rebuilds only for coupons placed afterwards.
DROP SCHEMA IF EXISTS risk CASCADE;
