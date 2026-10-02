CREATE SCHEMA IF NOT EXISTS risk;

-- Every change to a fixture's book, in order: the event-sourced record each fixture actor replays after its snapshot.
CREATE TABLE risk.journal
(
    fixture_id  text        NOT NULL,
    version     bigint      NOT NULL CHECK (version > 0),
    coupon_id   uuid        NOT NULL,
    kind        text        NOT NULL CHECK (kind IN ('placed', 'settled')),
    payload     jsonb       NOT NULL,
    recorded_at timestamptz NOT NULL,
    PRIMARY KEY (fixture_id, version),
    UNIQUE (fixture_id, coupon_id, kind)
);

-- The latest snapshot per fixture; replay starts from its version.
CREATE TABLE risk.snapshots
(
    fixture_id text        PRIMARY KEY,
    version    bigint      NOT NULL,
    state      jsonb       NOT NULL,
    taken_at   timestamptz NOT NULL
);

-- Which fixtures each coupon touches, because settlement events carry only the coupon.
CREATE TABLE risk.coupon_fixtures
(
    coupon_id  uuid NOT NULL,
    fixture_id text NOT NULL,
    PRIMARY KEY (coupon_id, fixture_id)
);

-- The console's read model: each fixture's latest liability.
CREATE TABLE risk.fixture_views
(
    fixture_id     text        PRIMARY KEY,
    version        bigint      NOT NULL,
    worst_case     bigint      NOT NULL,
    cap            bigint      NOT NULL,
    cap_overridden boolean     NOT NULL,
    suspended      boolean     NOT NULL,
    outcomes       jsonb       NOT NULL,
    updated_at     timestamptz NOT NULL
);

CREATE INDEX fixture_views_worst_case ON risk.fixture_views (worst_case DESC);

-- A trader's cap for one fixture; 0 suspends it. No row means the service default.
CREATE TABLE risk.fixture_caps
(
    fixture_id text        PRIMARY KEY,
    cap        bigint      NOT NULL CHECK (cap >= 0),
    reason     text        NOT NULL,
    set_by     text        NOT NULL,
    set_at     timestamptz NOT NULL
);

CREATE TABLE risk.alerts
(
    alert_id     uuid        PRIMARY KEY,
    kind         text        NOT NULL,
    fixture_id   text        NOT NULL,
    selection_id text        NULL,
    punter_ids   uuid[]      NOT NULL,
    coupon_ids   uuid[]      NOT NULL,
    total_stake  bigint      NOT NULL,
    summary      text        NOT NULL,
    raised_at    timestamptz NOT NULL
);

CREATE INDEX alerts_raised_at ON risk.alerts (raised_at DESC);
