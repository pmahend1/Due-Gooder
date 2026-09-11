# DueGooder — Hackathon Plan

**Goal:** a university's name or website goes in; normalized, current sections and meeting times come out, in one database, through connectors shared across schools.

**Judged on:** section and meeting accuracy · connector reuse and per-school setup · cost and runtime at scale · reliable updates and failure handling.

| Lane | Owner | Focus |
| --- | --- | --- |
| **A** — Core | Prateek | Domain, storage, pipeline, refresh, metrics and reporting |
| **B** — Collection | Fabian | School list, connectors, fingerprinting, discovery |
| **S** — Shared | Both | Kickoff, measured run, submission |

Owners can swap lanes. What matters is that each lane can keep moving without waiting on the other.

**Deadline: Sat Sep 12, 5:00 PM ET.** H0 = Fri Sep 11, ~9:00 PM ET, which gives 20 hours.

| Phase | Hours | Clock (ET) | Milestone |
| --- | --- | --- | --- |
| 0 — Kickoff | H0–H1 | Fri 9–10 PM | Skeleton builds, contract agreed |
| 1 — First slice | H1–H5 | Fri 10 PM – Sat 2 AM | **Sync 1:** one school end to end |
| 2 — Automate + scale | H5–H10 | Sat 2–7 AM | **Sync 2:** automated multi-school run + report |
| 3 — Reliability + breadth | H10–H14 | Sat 7–11 AM | **Feature freeze at 11 AM** |
| 4 — Measured run + submit | H14–H20 | Sat 11 AM – 5 PM | Run done by 12 PM, write-up by 3 PM, rehearse by 4 PM, submit by 4:30 PM |

Take rest in turns (each person gets about 2 hours during Phase 2) so one lane is always moving. Runs that need no babysitting, like the scaling runs in B6, are a good time for the other person to sleep.

**Task format:** `ID` Title — depends on · **done when** …

---

## Phase 0 — Kickoff (H0–H1 · Fri 9–10 PM, together)

- [x] **S1** Switch the docs to C# and add `.gitignore` — none · **done when** README/AGENTS.md describe .NET and `.gitignore` covers `bin/`, `obj/`, `.DS_Store`, `*.db`, `.env`, the crawl cache
- [ ] **S2** Solution skeleton — S1 · **done when** `DueGooder.sln` has Domain, Application, Connectors, Infrastructure, Cli and Tests projects, references point inward only, and `dotnet build` passes
- [ ] **S3** Agree the contract — S2 · **done when** the domain entities (`School`, `Term`, `Course`, `Section`, `Meeting`, `Instructor`, `ExtractionFailure`) and the `IConnector` interface (Fingerprint → ListTerms → CollectSections → Map) are committed. This is the only hard dependency between the lanes.

## Phase 1 — First vertical slice (H1–H5 · Fri 10 PM – Sat 2 AM)

### Phase 1 · Lane A — Prateek

- [ ] **A1** Domain entities — S3 · **done when** natural keys (school + term + course + section) exist, a missing field is represented differently from an `ExtractionFailure`, and raw source values sit next to parsed ones for times, days and locations
- [ ] **A2** Storage + upsert — A1 · **done when** there's an EF Core SQLite schema, `ISectionRepository` upserts by natural key, and a test proves that re-saving identical data writes nothing
- [ ] **A3** Run metrics — S2 · **done when** the HTTP adapter records requests, bytes and duration per school, and a run context aggregates counts per school and per run

### Phase 1 · Lane B — Fabian

- [ ] **B1** Demo school list — none · **done when** `config/schools.yaml` lists schools with their homepage and suspected platform, including 10+ on Banner 9 (Kentucky schools are a nice touch for the demo)
- [ ] **B2** Banner 9 fixtures — B1 · **done when** term and section search JSON from at least **2 schools** is saved in `tests/fixtures/banner9/`
- [ ] **B3** Banner 9 connector — S3, B2 · **done when** it lists terms, pages through class search, maps sections, meetings, instructors and enrollment, and its fixture tests pass for both schools

### Sync 1

- [ ] One Banner 9 school collected end to end into SQLite, with its source URLs and timestamps.

## Phase 2 — Automate and scale (H5–H10 · Sat 2–7 AM)

### Phase 2 · Lane A — Prateek

- [ ] **A4** Pipeline + `run` command — A2, B3 · **done when** `duegooder run --schools config/schools.yaml` processes every school without human input, running schools concurrently while keeping requests to any one host sequential
- [ ] **A5** Politeness — A4 · **done when** there's a per-host rate limiter, `robots.txt` is respected, an identifying User-Agent is sent, and a dev response cache avoids re-hitting registrars
- [ ] **A6** Run report — A3, A4 · **done when** each run writes `reports/<run-id>.md` + `.json` with: schools attempted/identified/collected/failed by platform; term, section and meeting counts; field completeness; runtime; requests; **cost with the formula written out and estimates labeled**; human steps; and failures with reasons

### Phase 2 · Lane B — Fabian

- [ ] **B4** Fingerprinting — B3 · **done when** each connector scores a site using URL patterns, HTML markers, headers and a probe request, and the platform chosen is logged with its evidence
- [ ] **B5** Discovery — B4 · **done when** a school name or homepage resolves to a registrar/schedule URL (known paths probed first, then a shallow crawl), with no hand-entered URLs
- [ ] **B6** Scale Banner 9 — B3 · **done when** every Banner 9 school in the list collects, and the edge cases are handled: TBA times, online/async sections, multiple meetings, cross-listed courses

### Sync 2

- [ ] An automated run over the whole school list produces a report.

## Phase 3 — Reliability and breadth (H10–H14 · Sat 7–11 AM)

### Phase 3 · Lane A — Prateek

- [ ] **A7** Refresh — A4, A6 · **done when** re-running finds new terms, diffs against the previous run, flags an integration as broken on large drops (without deleting any data), and marks unconfirmed data stale
- [ ] **A8** Review queue + endpoint health — A7, B4 · **done when** unidentified or failed schools land in a review queue with the evidence, and moved or erroring endpoints are recorded per school

### Phase 3 · Lane B — Fabian

- [ ] **B7** Second platform connector — B4 · **done when** whichever of PeopleSoft / Colleague / Banner 8 is most common in the list works, with fixtures from **2 schools**
- [ ] **B8** _(stretch)_ LLM fallback extractor — B4, A3 · **done when** unknown platforms get LLM-assisted extraction from schedule pages, token usage feeds the cost metrics, and low-confidence results go to the review queue

## Phase 4 — Measured run and submission (H14–H20 · Sat 11 AM – 5 PM, together)

No new features after 11 AM. Only fixes that affect the measured run.

- [ ] **S4** Full measured run — all above · **done when** a run from a clean DB completes and its report is committed
- [ ] **S5** Accuracy spot-check — S4 · **done when** ~50 randomly sampled sections have been hand-checked against the live pages and the match rate is recorded
- [ ] **S6** Sample data — S4 · **done when** normalized samples are exported to `data/samples/`
- [ ] **S7** Write-up — S4, S5 · **done when** it covers per-school setup, failure handling, scaling and cost math, and the disclosed human steps, the README "Running it" section is final, and the submission checklist is ticked off
- [ ] **S8** Demo — S7 · **done when** the script is rehearsed and it shows one connector across several schools, a refresh with zero duplicates, and a flagged failure

---

## Cut line

If time runs short, drop **B8**, then **B7**. Protect **S4–S7**. Check at each sync: if Sync 1 isn't done by **Sat 3 AM** or Sync 2 by **Sat 8 AM**, cut the next item on this list right away instead of hoping to catch up. Breadth on one working connector plus measured, honest results is what's judged.

## Bounty requirement → task

| Requirement | Tasks |
| --- | --- |
| Find catalogs and schedules automatically | B5 |
| Identify the platform (Banner, PeopleSoft, Workday, custom) | B4 |
| Reuse connectors with minimal setup | B3, B6, B7 |
| Fallback or flag for manual review | B8, A8 |
| Normalized fields + source URL + timestamp; missing ≠ failed | A1, B3 |
| Refresh without duplicates, new terms, broken/stale detection | A2, A7, A8 |
| Real sections and meeting times | B3, B6 |
| Demo multiple schools on a shared connector | B6, S8 |
| Code, run instructions, sample data, write-up | S6, S7 |
| Actual run report: schools, data, runtime, cost, manual steps, failures | A6, S4 |
| Accuracy | S5 |
