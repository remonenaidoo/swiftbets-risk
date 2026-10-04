-- Rolls back 0002_fraud. Device sightings, bank fingerprints, fraud cases and their audit are lost.
DROP INDEX IF EXISTS risk.journal_punter;
DROP TABLE IF EXISTS risk.fraud_case_audit;
DROP TABLE IF EXISTS risk.fraud_cases;
DROP TABLE IF EXISTS risk.fraud_money;
DROP TABLE IF EXISTS risk.fraud_bank_accounts;
DROP TABLE IF EXISTS risk.fraud_devices;
