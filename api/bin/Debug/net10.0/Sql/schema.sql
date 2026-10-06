-- Execute once against the SAME ShareMarket database. Existing market data is not copied.
CREATE SCHEMA IF NOT EXISTS paper;
CREATE TABLE IF NOT EXISTS paper.portfolios(
 profile_id text NOT NULL, session_date date NOT NULL, state jsonb NOT NULL,
 updated_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY(profile_id,session_date));
CREATE TABLE IF NOT EXISTS paper.quotes(
 contract_key text PRIMARY KEY, price numeric NOT NULL CHECK(price>0), observed_at timestamptz NOT NULL,
 source text NOT NULL);
CREATE TABLE IF NOT EXISTS paper.requests(
 request_key text PRIMARY KEY, retry_after timestamptz NOT NULL, message text NOT NULL DEFAULT '');
CREATE TABLE IF NOT EXISTS paper.api_minute(
 minute timestamptz PRIMARY KEY, used_count integer NOT NULL);
CREATE TABLE IF NOT EXISTS paper.control(id integer PRIMARY KEY CHECK(id=1), paused boolean NOT NULL DEFAULT false);
INSERT INTO paper.control(id) VALUES(1) ON CONFLICT DO NOTHING;
-- Shared counter also used by the existing application.
CREATE TABLE IF NOT EXISTS trading.api_daily_budget(
 usage_date date PRIMARY KEY, used_count integer NOT NULL DEFAULT 0, updated_at timestamptz NOT NULL DEFAULT now());
-- Requires the existing trading.premium_candles_5m and its UNIQUE
-- (session_date,contract_key,candle_time). No market-data table migration/deletion here.
CREATE TABLE IF NOT EXISTS trading.api_minute_calls(reserved_at timestamptz NOT NULL,call_count integer NOT NULL CHECK(call_count>0));
