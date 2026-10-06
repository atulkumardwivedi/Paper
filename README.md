# NIFTY Paper Portfolios — separate Angular / .NET application

## What is included

12 independent paper portfolios: Locked / MF Shield × 7 / 15 lots × upfront margin hedge / delayed 300-point protection hedge / both. This is a separate application on API5092 and Angular4202. It contains market-data clients only, no broker-order service or route.

A live dashboard shows each scenario's settings, realized P&L, estimated open net P&L, active short and hedge premiums, quote timestamps, pending actions and daily trade history. Select a portfolio to view candlesticks, VWAP and its entry/exit markers. Export that day's trades as CSV. Only available recorded dates appear, with today's date available while awaiting startup.

**Validation status:** source package, not a compiled release. The generation environment had no .NET SDK or Angular dependencies and could not reach dependency registries. C# / Angular build, runtime database integration and live ICICI requests have NOT been executed here. Run validate.ps1 on your machine before starting a paper session. No real credentials or market orders were used during creation.

## Setup on Windows

1. Install .NET10 SDK and Node20LTS (matching this application's existing Angular18 toolchain). Use PowerShell with npm available.
2. Edit api/appsettings.json. Set ConnectionStrings:TradingDb to your SAME ShareMarket/PostgreSQL database. Credentials belong in this local file, not in frontend code.
3. For missing-data downloads and near-real-time quote polling, set Paper:AllowApiDownloads=true and configure Breeze:ApiKey / ApiSecret. Session is read from existing sessionmaster.icicisession when UseSessionMaster=true, otherwise set Breeze:SessionToken. When API is disabled the system runs exclusively from database candles. It does not invent fresh quotes.
4. Confirm existing trading.premium_candles_5m includes is_complete and delta and unique(session_date,contract_key,candle_time). Existing trading.option_expiries must contain the upcoming expiry, or today's existing option candles must supply it. If no expiry exists, the worker waits visibly; it does not invent an expiry calendar or call another data source.
5. Run ./validate.ps1. It builds the API, runs the included rule/checkpoint/accounting checks, installs the UI dependencies and builds Angular. These tests do not substitute for the database restart checks in VALIDATION.md.
6. Run ./start-dev.ps1. Open http://localhost:4202. Keep the API process running for daily paper sessions. Angular can close without stopping the worker.

At startup the API executes api/Sql/schema.sql. It creates only its paper records/cache/control tables and missing shared API-counter tables. Existing market candles are not copied. Existing saved morning selections and live-trade records from your other application are not used or modified. The PostgreSQL account requires CREATE schema/table privileges for initial setup; subsequently normal read/write access suffices.

Ports are different from your old app. All supplied bindings are localhost. Do not publish this unauthenticated development application directly to the Internet.

## Verified historical reference settings

The profile sources are recorded in reference/profiles.json. The recovered 15-lot comparison recorded MFShield5067588.75 net on older539sessions, ZERO extra slippage. That was a historical result, not a promise for this corrected implementation. The Locked reference from that comparison recorded3828397.50 net at zero extra slippage; it is the recovered comparison default, not a demonstrated global maximum across every historical experiment.

| 15-lot setting | Locked | MF Shield |
|---|---:|---:|
| Target | 49000 | 100000 |
| Unhedged stop | 70000 | 25000 |
| 300-protection trigger, if enabled | 8500 | 5000 |
| Hedged combined stop | 10000 | 50000 |
| Cooldown | 2 completed candles | 2 completed candles |
| Extra exit | None | Saved MF squeeze-opposite rule |

7-lot rupee thresholds scale these values by7/15. This is an explicit proportional paper-testing variant, not the different7lot research preset used in some prior reports. All12profiles appear automatically. Upfront margin hedge selects the same-side farther-OTM100grid strike closest to10% of the signal candle short premium, within configurable1000points. Protection hedge is300points fartherOTM and bought when short-leg loss reaches the trigger. Both mode can have3legs; when both hedges would be the same contract it uses the existing hedge rather than buying it twice. If the protection quote is unavailable the initial applicable stop remains active and the state shows the issue.

Paper:LotSize defaults65. Check the contract lot size you actually intend to simulate; this setting is not an exchange contract-master lookup. Paper:SlippagePerFill defaults0 to match the historical50lakh reference. Change it to1 for a more adverse simulated-fill scenario before starting a new day's profiles. Charges are200sell+25buy per10lots per complete roundtrip leg:337.50 at15lots or157.50 at7lots. Open P&L deducts anticipated full roundtrip charges. No actual broker fees or margin reservation is modeled.

Each portfolio freezes settings in its day's state; restarting or changing appsettings does not change an existing trade's thresholds or quantity. New dates use the new configuration. Profile IDs contain the strategy, lots, hedge variant and rule version.

## Corrected strike and signal rules

- Anchor to the cash09:15–09:20IST candle CLOSE, never its open or a later substitute.
- Apply your supplied two branches and round to100 with ties-to-even.
- Sort CE ascending, PE descending. Same column is the opposite pair for signal calculations. 8Jan2026:26100CE/26200PE,26200CE/26100PE,26300CE/26000PE,26400CE/25900PE.
- Fixed8signal contracts throughout the day. Hedge contracts do not join the signal set.
- At least2paired bodies each meeting±1%, with the winning directional count greater than the other count. Sell PE on bullish, CE on bearish. Fresh direction required; no experimental OI entry gate.
- Choose stored candle delta nearest absolute0.5 among the selling side's4strikes. If null, estimate from the same candle cash close, premium and expiry. No Opstra delta overwrite or Opstra morning-spot lookup.
- Saved entry window: completed candle starts09:30–14:05IST. Risk decisions use completed closes, execute next candle OPEN. EOD closes at15:15OPEN. Candle data can arrive late; recorded theoretical fill time remains that candle's time.
- MF is cumulative signed typical-price×volume/1e7, rounded2decimals; sign follows typical-price change. Compare CE/PE four-strike line ranges to the median of their six preceding ranges: one side compresses≤0.7×baseline while opposite expands≥1.8×baseline. Adverse warning exits MFShield on next open. This is the archived squeeze rule, not actual capital-flow measurement or futures money flow.

## Persistence, restarts and missing data

paper.portfolios has a primary key(profile_id,session_date). One JSON state holds that portfolio's frozen settings, contracts, last processed close, previous direction, cooldown, pending action and trade journal. Updating the row commits all of those together. No separate duplicate candle dataset or duplicate trade ledger is maintained. Deterministic trade IDs and optimistic revision checks reject duplicate/concurrent state updates. A PostgreSQL advisory lease elects one coordinator across instances. Death of the API connection releases the lease.

On restart, unresolved older portfolios load first and resume their pending action/open trade. They must resolve before that same profile starts a new date. Historical candles reconstruct missed paper decisions; any fill more than5minutes late flags the trade Recovered. This is paper replay/recovery, not a claim the order could actually have executed while the application was offline. Incomplete signal candles skip entries. Missing active-leg prices pause that portfolio until prices become available; other portfolios continue. Missing8-pair data does not disable a price-based stop when the open legs' prices are available.

No positions are cleared at startup. Expiry days are excluded by default per prior research preference; set Paper:ExcludeExpiryDays=false before future dates to include them. Saturdays/Sundays are not auto-created. There is no holiday-calendar integration: on an exchange holiday no qualifying candles means no entry.

Pause persists across restarts and pauses BOTH entries and exits. Use it only when you intend to stop processing. Resume catches up from stored candles.

## Database-first quotes / calls

Existing complete candles are read first. Only a missing/incomplete requested candle can trigger a historical API request. Results upsert into the SAME trading.premium_candles_5m natural unique key; complete records are not overwritten. Requests and retries are shared across profiles. A historical missing date is retried at most hourly, current-day gaps at the configured60seconds.

Quotes cache ONE current value per contract in paper.quotes, shared by all scenarios. Existing candle closes seed the quote cache with their actual candle-end timestamp. If stale, and API enabled during market hours, request an ICICI quote. Default quote refresh30seconds, UIrefresh2seconds: **near-real-time polling, not a tick websocket**. Quote timestamps are observation times, not guaranteed exchange timestamps. If another authorized feeder writes fresh prices into paper.quotes, those are reused without extra API calls.

Shared trading.api_daily_budget and trading.api_minute_calls coordinate call usage with the existing app. Default reserve100calls,90/minute. A daily block stops further HTTP calls until the next ISTdate; rate-limit blocks back off60seconds, other API failures5minutes. Database processing continues. Many active contracts may exhaust the daily budget with30secondpolling; use60seconds or a database quote feeder as needed. Cached stale prices are labelled in the UI and never used as fresh signal candles.

## What this package does NOT establish

- It does not validate the old50lakh expectation on new data.
- It does not claim parity with the older application's Opstra-overwritten delta selection; that dependency is deliberately absent.
- It does not place real orders, calculate exchange margin, or monitor fills from a broker.
- It is not a Windows service installer. Keep the API running, or host it using your usual ASP.NET process/service setup after validating the development build.

API reference used when reviewing the inherited read-only adapter: https://api.icicidirect.com/breezeapi/documents/index.html . Historical requests use a full-day envelope and the parser filters returned candles to the requested ISTsession. Live provider behavior still requires the local integration check.
