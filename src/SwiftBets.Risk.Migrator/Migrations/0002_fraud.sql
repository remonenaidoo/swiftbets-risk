-- Fraud signals (BACKLOG 33). Devices are client-side hashes, IPs are network prefixes; nothing raw is kept.
CREATE TABLE risk.fraud_devices
(
    id          bigserial        PRIMARY KEY,
    user_id     uuid             NOT NULL,
    kind        text             NOT NULL CHECK (kind IN ('sign-in', 'register', 'deposit', 'withdrawal')),
    device_hash char(64)         NOT NULL,
    ip_prefix   text             NULL,
    asn         text             NULL,
    country     char(2)          NULL,
    latitude    double precision NULL,
    longitude   double precision NULL,
    user_agent  text             NULL,
    seen_at     timestamptz      NOT NULL
);

CREATE INDEX fraud_devices_hash ON risk.fraud_devices (device_hash, user_id);
CREATE INDEX fraud_devices_user ON risk.fraud_devices (user_id, seen_at DESC);

-- One saved payout account per customer, as a keyed hash from payments.
CREATE TABLE risk.fraud_bank_accounts
(
    user_id     uuid        PRIMARY KEY,
    fingerprint char(64)    NOT NULL,
    saved_at    timestamptz NOT NULL
);

CREATE INDEX fraud_bank_accounts_fingerprint ON risk.fraud_bank_accounts (fingerprint);

-- Deposits and withdrawals, once each, for the deposit-then-withdraw rule; stakes are read from the journal.
CREATE TABLE risk.fraud_money
(
    reference uuid        NOT NULL,
    kind      text        NOT NULL CHECK (kind IN ('deposit', 'withdrawal')),
    user_id   uuid        NOT NULL,
    amount    bigint      NOT NULL,
    at        timestamptz NOT NULL,
    PRIMARY KEY (reference, kind)
);

CREATE INDEX fraud_money_user ON risk.fraud_money (user_id, at);

-- Advisory cases for staff; at most one open case per customer and rule.
CREATE TABLE risk.fraud_cases
(
    case_id         uuid        PRIMARY KEY,
    user_id         uuid        NOT NULL,
    rule            smallint    NOT NULL,
    severity        smallint    NOT NULL CHECK (severity BETWEEN 1 AND 3),
    linked_user_ids uuid[]      NOT NULL,
    summary         text        NOT NULL,
    status          text        NOT NULL CHECK (status IN ('open', 'resolved')),
    raised_at       timestamptz NOT NULL,
    resolved_by     text        NULL,
    resolution      text        NULL CHECK (resolution IN ('confirmed', 'cleared')),
    resolved_reason text        NULL,
    resolved_at     timestamptz NULL
);

CREATE UNIQUE INDEX fraud_cases_one_open ON risk.fraud_cases (user_id, rule) WHERE status = 'open';
CREATE INDEX fraud_cases_status ON risk.fraud_cases (status, raised_at DESC);

-- Who did what to a case, and why.
CREATE TABLE risk.fraud_case_audit
(
    id      bigserial   PRIMARY KEY,
    case_id uuid        NOT NULL REFERENCES risk.fraud_cases (case_id),
    actor   text        NOT NULL,
    action  text        NOT NULL,
    reason  text        NULL,
    at      timestamptz NOT NULL
);

CREATE INDEX fraud_case_audit_case ON risk.fraud_case_audit (case_id, at);

-- A customer's stakes for the deposit-then-withdraw rule, straight from the liability journal.
CREATE INDEX journal_punter ON risk.journal (((payload -> 'placed' ->> 'punterId')), recorded_at) WHERE kind = 'placed';
