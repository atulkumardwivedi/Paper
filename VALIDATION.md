# Validation status and acceptance checks

## Performed in the generation environment
- Recovered the archived two15lot configuration objects and copied their thresholds/provenance.
- Inspected raw8January data and confirmed correctedCE/PEpair mapping against the user's screenshot.
- Reviewed persistence path: single portfolio row contains journal and checkpoint; optimisticrevision predicate, uniqueprofile/date key, deterministictradeID, cross-instanceadvisorylock.
- Reviewed API path: market-data-only adapter, shared daily/minute budget, durable retry markers and global backoff; no order client/route.
- Checked package JSON/configuration parsing and expected source-file presence.

## Not run here
.NET compilation, included C#checks, Angular compilation/rendering, database migrations, DBrestart/concurrency tests and ICICIrequests. SDK/dependencies are unavailable and network package downloads did not succeed. The package must not be described as having passed these checks.

## Run locally before a paper session
1. ./validate.ps1 must pass. This executes supplied tests for both strikebranches, exact8Janpairs,12uniqueprofiles, archivedthresholds, JSONcheckpointrestart, multi-legP&L/charges and directioncalculation.
2. Start API with AllowApiDownloads=false against a test COPY of the database. Confirm it creates onlypaperrecords/counters; existing premiumrowcount is unchanged.
3. On a current trading date with stored candles, confirm12profiles and compare pairlists/settings with the README. Signalbars should be complete; fills should be nextOPEN.
4. Record active tradeID, quantities, pendingaction and lastCloseIndex. Terminate API, restart; confirm sameID/settings/positions and no duplicate trade. Repeat during a queuedhedge/exit. Restart a second APIinstance on anotherport and confirm single coordinator/revision protection.
5. Remove one candle only from the TESTcopy. Database-onlymode must make no networkcall and label missingdata. Otherportfolios should continue; no stalequote entry.
6. Configure ICICI on a test date and enablefallback. Confirm one shared fetch percontract request rather than12; repeatedpolling reusesdatabase. Inspect sharedcounter. Exhaust a reduced TESTbudget and confirm HTTPcalls stop while database processing continues.
7. Confirm pausedflag survivesrestart. Resume and inspect reconstructedflag for downtime fills. Confirm15:15EODclosure afterrestart and older unresolvedtrade blocks sameprofile's nextdayentry.
8. Verify open P&L by hand: short(entry−mark)×quantity + boughthedges(mark−entry)×quantity − roundtripcharges. Compare premiums and timestamps to the source.
9. For exact research parity, run recorded sessions through this API and compare every fill to archivedresearch. That parity audit has NOT yet been completed.
