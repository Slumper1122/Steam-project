-- ============================================================
-- Steam Data Puller — Supabase schema
-- Run this once in the Supabase SQL Editor:
--   https://supabase.com/dashboard → your project → SQL Editor
-- ============================================================

-- Games (metadata, upserted on every pull)
CREATE TABLE IF NOT EXISTS games (
    app_id          BIGINT PRIMARY KEY,
    name            TEXT NOT NULL,
    developer       TEXT,
    publisher       TEXT,
    release_date    TEXT,
    is_early_access BOOLEAN DEFAULT FALSE,
    genres          TEXT,
    tags            TEXT,
    first_seen_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Snapshots (time-series rows — one per hourly pull if data changed)
CREATE TABLE IF NOT EXISTS snapshots (
    id                          BIGSERIAL PRIMARY KEY,
    app_id                      BIGINT NOT NULL REFERENCES games(app_id),
    captured_at                 TIMESTAMPTZ NOT NULL,

    -- Player metrics (100% accurate — official Steam API)
    current_players             INT,
    peak_ccu_24h                INT,

    -- Owner estimate (SteamSpy range — NOT exact, Steam doesn't publish this publicly)
    owners_low                  BIGINT,
    owners_high                 BIGINT,

    -- Review metrics (accurate — Steam Store API)
    review_score_desc           TEXT,
    total_positive              INT,
    total_negative              INT,
    total_reviews               INT,
    positive_pct                FLOAT,

    -- Playtime (SteamSpy — often 0 on free tier)
    avg_playtime_forever_min    INT,
    median_playtime_forever_min INT,

    -- Price
    price_usd                   NUMERIC(8,2),
    discount_pct                INT,

    -- Content metrics
    update_count                INT,
    achievement_count           INT,
    dlc_count                   INT
);

-- Index: fast lookup by game + time (for delta queries and graphs)
CREATE INDEX IF NOT EXISTS idx_snapshots_app_time
    ON snapshots (app_id, captured_at DESC);

-- ────────────────────────────────────────────────────────────
-- Row Level Security (optional — enables public read-only access)
-- ────────────────────────────────────────────────────────────
-- ALTER TABLE games     ENABLE ROW LEVEL SECURITY;
-- ALTER TABLE snapshots ENABLE ROW LEVEL SECURITY;

-- CREATE POLICY "Public read games"
--     ON games FOR SELECT USING (true);

-- CREATE POLICY "Public read snapshots"
--     ON snapshots FOR SELECT USING (true);
