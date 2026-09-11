# DueGooder — Hackathon Plan

**Goal:** a university's name or website goes in; normalized, current sections and meeting times come out, in one database, through connectors shared across schools.

**Judged on:** section and meeting accuracy · connector reuse and per-school setup · cost and runtime at scale · reliable updates and failure handling.

**Deadline: Sat Sep 12, 5:00 PM ET.** Building starts Fri Sep 11, ~6:45 PM ET.

| Owner | Focus |
| --- | --- |
| **Prateek** | Solution architecture, domain, Banner 9 connector, storage, pipeline, platform detection, refresh, run report |
| **Fabian** | School list, fixtures, second-platform research, cost model, sample data, accuracy check, write-up (failure handling / scaling / cost), demo |

Fabian's tasks need only a browser, a spreadsheet or text editor, and git. No .NET toolchain.

**Hand-off rule:** each task that feeds into someone else's work has a deadline. If it isn't in by then, it gets reassigned so the critical path keeps moving.

| Block | Clock (ET) | Milestone |
| --- | --- | --- |
| 0 — Setup | Fri 6:45–8:15 PM | Skeleton builds, contract written, school list started |
| 1 — First slice | Fri 8:15–11:45 PM | **Checkpoint 1:** one Banner 9 school end to end |
| 2 — Automate | Fri 11:45 PM – Sat 1:30 AM | Pipeline runs the whole list; unattended overnight run started |
| 💤 Sleep | Sat 1:30–7:30 AM | Overnight run collects data and exposes failures |
| 3 — Harden | Sat 7:30–11:30 AM | Failures fixed, platform detection, refresh, report. **Feature freeze at 11:30 AM** |
| 4 — Measure + submit | Sat 11:30 AM – 5 PM | Measured run by 12:30 PM, write-up by 3 PM, rehearse by 4 PM, **submit by 4:30 PM** |

**Task format:** `ID` Title · owner — depends on · **done when** …

---

## Block 0 — Setup (Fri 6:45–8:15 PM)

- [x] **T0** Switch the docs to C# and add `.gitignore` · Prateek
- [x] **T1** Solution skeleton · Prateek — T0 · **done when** `DueGooder.slnx` has Domain, Application, Connectors, Infrastructure, Cli and Tests projects, references point inward only, and `dotnet build` passes
- [x] **T2** Contract · Prateek — T1 · **done when** the domain entities (`School`, `Term`, `Course`, `Section`, `Meeting`, `Instructor`, `ExtractionFailure`) have natural keys, missing fields are represented differently from `ExtractionFailure`, raw values sit next to parsed ones, and `IConnector` (Fingerprint → ListTerms → CollectSections → Map) is defined
- [x] **T3** School list · Fabian — none · **due 9:30 PM** · **done when** `config/schools.yaml` has 20+ schools with name, homepage and suspected platform, including at least **15 on Banner 9** plus a few PeopleSoft/Colleague/Workday schools.
  - _How:_ search `"StudentRegistrationSsb" site:edu`. A school is on Banner 9 if `https://<host>/StudentRegistrationSsb/ssb/term/termSelection?mode=search` loads a term picker. Kentucky schools make a nice demo story.

## Block 1 — First vertical slice (Fri 8:15–11:45 PM)

- [ ] **T4** Banner 9 fixtures · Fabian — T3 · **due 9:30 PM** · **done when** term and class-search JSON responses from **2 schools** are saved in `tests/fixtures/banner9/` (browser DevTools → Network → copy the response; trim to 1–2 pages of results)
- [ ] **T5** Banner 9 connector · Prateek — T2, T4 · **done when** it lists terms, pages through class search, maps sections, meetings, instructors and enrollment, and its fixture tests pass for both schools
- [ ] **T6** Storage + upsert · Prateek — T2 · **done when** there's an EF Core SQLite schema, `ISectionRepository` upserts by natural key, and a test proves that re-saving identical data writes nothing
- [ ] **T7** Single-school CLI · Prateek — T5, T6 · **done when** `duegooder run --school <id>` collects one school into SQLite with source URLs and timestamps

**Checkpoint 1 (target 11:45 PM, hard limit 12:30 AM):** one Banner 9 school end to end. If you miss 12:30 AM, drop T15 now.

## Block 2 — Automate (Fri 11:45 PM – Sat 1:30 AM)

- [ ] **T8** Pipeline over the list · Prateek — T7, T3 · **done when** `duegooder run --schools config/schools.yaml` processes every school without human input, running schools concurrently while keeping requests to any one host sequential, and one failing school doesn't stop the run
- [ ] **T9** Politeness + metrics · Prateek — T7 · **done when** there's a per-host rate limiter, `robots.txt` is respected, an identifying User-Agent is sent, a dev response cache is in place, and requests, bytes, duration and counts are recorded per school
- [ ] **T10** Start the overnight run · Prateek — T8, T9 · **done when** a full run over the list is going unattended (`caffeinate -i` keeps the Mac awake) with its logs saved

## Block 3 — Harden (Sat 7:30–11:30 AM)

- [ ] **T11** Triage the overnight run · Prateek — T10 · **done when** mapping edge cases are fixed (TBA times, online/async sections, multiple meetings, cross-listed courses) and every remaining failure has a recorded reason
- [ ] **T12** Detect the platform and find the schedule page · Prateek — T5 · **done when** a school's homepage alone gets resolved to its Banner 9 base URL (probe known paths, check HTML markers, run the probe request), the evidence is logged, and non-Banner schools land in the review list with that evidence
- [ ] **T13** Run report · Prateek — T9, T22 · **done when** each run writes `reports/<run-id>.md` + `.json` with: schools attempted/identified/collected/failed; term, section and meeting counts; field completeness; runtime; requests; **cost with the formula written out and estimates labeled**; human steps; and failures with reasons
- [ ] **T14** Refresh · Prateek — T6, T8 · **done when** re-running writes zero duplicates, new terms get picked up, a large drop in section count flags the integration as broken (without deleting data), and each record has a `last_confirmed_at`
- [ ] **T15** _(stretch)_ Second platform · Fabian researches, Prateek builds — T12 · **done when** the most common non-Banner platform in the list has its URL patterns, endpoints and fixtures from **2 schools** documented, and a connector for it works. Build it only if T11–T14 are done by 10:30 AM.
- [ ] **T22** Cost model · Fabian — none · **due 10:00 AM** · **done when** a spreadsheet (or `docs/cost-model.md`) has current prices for a small cloud VM, bandwidth and LLM tokens, with sources linked, and a formula for cost per school and per 1,000 schools. It takes measured runtime, requests and bytes as inputs, labels every assumption as an estimate, and gets the real numbers plugged in after T16.

## Block 4 — Measured run and submission (Sat 11:30 AM – 5 PM)

No new features after 11:30 AM. Only fixes that affect the measured run.

- [ ] **T16** Measured run · Prateek — T11–T14 · **done when** a run from a clean DB completes, followed by a refresh run that shows zero duplicates, both reports are committed, and the full normalized data is exported to CSV/JSON
- [ ] **T17** Sample data + spot-check sheet · Fabian — T16 · **done when** representative samples from the export (a few schools, including edge cases like TBA and online sections) are in `data/samples/` with a short README, and a 50-row random spot-check sheet with source URLs is ready
- [ ] **T18** Accuracy spot-check · Fabian — T17 · **done when** each sampled section has been compared with its live registrar page (course, section, days, times, room, instructor), marked match or mismatch with a note, and the match rate is calculated
- [ ] **T19** Write-up: setup and architecture · Prateek — T16 · **done when** it covers per-school setup, connector reuse, the pipeline and run instructions, and the README "Running it" section is final
- [ ] **T20** Write-up: failure handling, scaling, cost · Fabian — T16, T18 · **done when** it covers how failures are detected and reported, the scaling and cost math from the measured run (estimates labeled), the accuracy result, and the disclosed human steps
- [ ] **T21** Demo · Fabian presents, Prateek runs it live — T19, T20 · **done when** the script is rehearsed and it opens with the Kentucky story, shows one connector across many schools, a refresh with zero duplicates, and a flagged failure; submission checklist ticked off

---

## Cut line

In order, cut: **T15** (second platform) → the shallow crawl in **T12** (keep only the known-path probes) → the stale/broken-integration flags in **T14** (keep the zero-duplicate refresh). Never cut **T13, T16, T17, T19, T20, T22**: breadth on one working connector plus measured, honest results is what's judged.

## Bounty requirement → task

| Requirement | Tasks |
| --- | --- |
| Find catalogs and schedules automatically | T12 |
| Identify the platform (Banner, PeopleSoft, Workday, custom) | T12, T15 |
| Reuse connectors with minimal setup | T5, T8, T11 |
| Fallback or flag for manual review | T12 (review list with evidence) |
| Normalized fields + source URL + timestamp; missing ≠ failed | T2, T5 |
| Refresh without duplicates, new terms, broken/stale detection | T6, T14 |
| Real sections and meeting times | T5, T11 |
| Demo multiple schools on a shared connector | T16, T21 |
| Code, run instructions, sample data, write-up | T17, T19, T20 |
| Actual run report: schools, data, runtime, cost, manual steps, failures | T13, T16, T22 |
| Accuracy | T18 |
