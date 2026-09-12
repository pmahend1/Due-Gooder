# DueGooder — write-up

Covers T19 (setup, connector reuse, pipeline, run instructions) and T20 (failure handling, scaling and
cost, accuracy, human steps). All numbers below are filled in from the measured run (S7) and the
automated accuracy check (S8) — see `docs/PLAN-B.md`'s Session log (S1–S8) for the full history behind
each decision.

## Per-school setup

Adding a school costs a `config/schools.yaml` entry, not code. Two shapes exist:

- **Homepage-only** (`homepage`, `timezone`): discovery finds the platform and its entry point on its own
  (T12) — probing known URL patterns, scanning the homepage and up to two hops of its own site for
  registrar/schedule links, then falling back to guessed hostnames (`ssb.`, `registration.`, `banner.`, …).
  This is the default and the common case: 37 of the 41 Banner 9 schools in the list, plus every
  non-Banner school in the manual review queue, are set up this way.
- **`platform` + `base_url` pinned** (skips discovery): used only where discovery can't find the entry
  point on its own — a blocked or unhelpful homepage, a multi-campus server needing a `mep_code`, or a
  non-standard port. 10 of 41 Banner 9 schools needed this after the S6 discovery run; each has a
  one-line comment recording why.

Every human step taken to build and grow the list — web searches, confirming a host with a live
`getTerms` probe, reading a multi-campus `mep_code` out of a browser URL — is logged in
[`config/human-steps.md`](../config/human-steps.md) and rolled into each run report's Human Steps
section, so setup cost is measured, not asserted. As of S6 the list holds 49 schools: 41 Banner 9 (26
originally known to collect before homepage-only discovery was added) and 8 non-Banner schools kept in
the review queue on purpose, to prove discovery correctly declines platforms it has no connector for
rather than guessing wrong.

## Connector reuse

One Banner 9 connector (`src/DueGooder.Connectors/Banner9/`) serves every Banner 9 school in the list —
41 of them by S6, spanning at least six different hosting setups (standard path, capital-case path,
multi-campus `mepCode`, non-default port, district-wide term lists, self-hosted vs. Ellucian cloud).
Every difference between those schools lives in `config/schools.yaml` (base URL, `mep_code`, optional
`page_size`) or is handled generically inside the connector (opaque term codes read from `description`
rather than parsed, "(View Only)" terms collected like any other, variable credit stored as a range).
**No school-specific branch exists in the connector code** — that was a design rule from the start
(`AGENTS.md`: "if you're about to write `if (school.Id is "...")`, make it a config field instead"), and
the fixture test suite proves it: `Every_captured_section_maps_to_a_distinct_key_without_failures` runs
the same connector against five schools' worth of recorded fixtures (eku, sunyempire, kccd, oakland, odu)
and passes with one code path.

We went further than the two fixtures the brief asks for specifically to demonstrate reuse holds up:
S1–S3 captured fixtures from 5 schools, and by S4's live pre-flight run the same connector had collected
real data from 26 schools without a single school-specific code change — only config rows and,
twice, genuine bug fixes to the shared mapper (the `sequenceNumber`-collision fix in S3, and the
credit/cross-list/TBA-location fixes in S5) that improved every school at once, not just the one that
surfaced the bug.

**Second platform (T15, stretch, not built):** Fabian researched Ellucian Colleague Self-Service against
two schools (`bellarmine`, `kishwaukee`) and documented the full request shape, term-identification and
mapping quirks in [`docs/colleague-fixtures.md`](colleague-fixtures.md) — including a real trap (Colleague's
`StartTime`/`EndTime` fields are UTC-stamped with the *request* date, not the class date) that would have
silently produced wrong meeting times if a connector had been built quickly. No connector code was
written; T15 was cut from the required plan at the S6 feature freeze, and the research deliberately
stopped at "what it would take," including a cost finding (Colleague needs roughly 50–100× the requests
per term that Banner 9 does, because it has no bulk section endpoint) that belongs in the scaling
discussion below.

## Pipeline

```text
school name / homepage
        │
        ▼
┌───────────────┐   known-path probes, then a same-site crawl (≤10 pages, ≤2 hops)
│  1. Discover  │   ranked by schedule/registrar link text
└───────┬───────┘
        ▼
┌───────────────┐   every connector's fingerprint scored against each candidate
│ 2. Identify   │   page; ≥0.9 confidence identifies a school, else → review queue
└───────┬───────┘   with every evidence line recorded
        ▼
┌───────────────┐   per-host queue, up to N hosts run at once, one school at a
│  3. Collect   │   time within a host; term → collect → map → dedupe → upsert
└───────┬───────┘   (Banner9GapRecovery halves the search window on a partial
        ▼            failure instead of losing the whole term)
┌───────────────┐   one schema; source_url + retrieved_at (UTC) on every record;
│ 4. Normalize  │   raw text kept beside every parsed field; missing ≠ failed
└───────┬───────┘
        ▼
┌───────────────┐   upsert by natural key (school+term+course+CRN); unchanged
│  5. Refresh   │   content writes nothing, only last_confirmed_at moves; new
└───────────────┘   terms picked up; a >50% section-count drop is flagged, not
                     silently accepted; nothing is ever deleted
```

Concurrency: schools are queued by host (`BaseUrl.Authority`) and up to `--max-hosts` hosts run in
parallel; within one host, schools and terms run sequentially, so one slow or misbehaving host never
throttles the rest of the run and one school's failure never stops another's. Politeness is built into
every request: per-host rate limiting (`--min-interval`, default 2s, raised automatically by a
`robots.txt` `Crawl-delay`), `robots.txt` enforced per RFC 9309 (longest-match group, 4xx/redirect-loop
= allow, 5xx/dropped connection = disallow-all), retries on transient failures, and an identifying
User-Agent (`DueGooderBot/0.1 …`) on every request. Nothing requires a sign-in or bypasses an access
control; a platform that needs one (Workday, in this list) goes to the review queue with that reason
instead.

### Running it

```bash
dotnet build DueGooder.slnx
dotnet test DueGooder.slnx

# Full pipeline: discover → identify → collect → normalize → store
dotnet run --project src/DueGooder.Cli -- run --schools config/schools.yaml

# Re-run against the same DB to see the refresh guarantees (zero duplicates, only real changes written)
dotnet run --project src/DueGooder.Cli -- run --schools config/schools.yaml

# Finish (or regenerate) a run's Markdown report from its JSON
dotnet run --project src/DueGooder.Cli -- report --run reports/<run-id>.json

# Export the normalized data (T16/T17): full CSV+JSON to data/export/, plus a
# representative sample and a 50-row spot-check sheet (with source URLs) to data/samples/
dotnet run --project src/DueGooder.Cli -- export --db data/duegooder.db
```

`run --help`-equivalent usage (schools/school/term filters, `--cache`, `--max-terms`, `--max-hosts`,
`--min-interval`) is in `src/DueGooder.Cli/Program.cs`; `export`'s options (which schools go in the
sample, spot-check size, random seed) are in `src/DueGooder.Cli/ExportCommand.cs`. Every run writes
`reports/<run-id>.json` (rewritten after each school, so a crash keeps finished results) and a matching
`.md` report.

## Failure handling

Three different things get recorded, deliberately kept apart because they mean different things:

1. **A field the school doesn't publish** → stored as `null`. Not a failure.
2. **A field we tried to extract and couldn't** → an `ExtractionFailure(Field, Reason, RawValue)` entry on
   the section, keeping the raw source text for audit. Across the S5 overnight run's 1.75M sections, this
   was **0** after the mapper fixes — every failure that did occur (e.g. a meeting entry with no
   `meetingTime`) surfaces this way rather than being silently dropped.
3. **A school (or term) we couldn't collect at all** → recorded with a specific, human-readable reason
   and the URL that failed (robots.txt disallow, a sign-in redirect, an unreadable robots.txt treated as
   disallow-all, a platform with no connector) and put in the run report's failure list or manual review
   queue. Nothing fails silently or gets retried into looking like success.

Beyond per-request failures, the pipeline watches for **partial and gradual failure**, not just outright
errors:

- **Partial term recovery** (`Banner9GapRecovery`): when Banner answers `success:false` for part of a
  term's result window, the connector halves the window until only the truly unreturnable records are
  left, and stores the term anyway if at most 5% is missing — with the gap's size and reason recorded on
  the term (`GapCount`/`GapDetail`), and the school marked `Partial`. The alternative (discarding a term
  because one record in a few hundred won't return) would have thrown away good data over a single
  registrar quirk.
- **Broken-integration detection**: a refresh compares each term's section count against what was last
  stored. A drop of more than 50% (and at least 20 sections) is **not written** and flags "broken
  integration suspected" instead of silently replacing good data with a scraping failure that looks like
  an empty semester. A term the source stops listing, or a school that stored data before but collects
  nothing now, is flagged the same way. Nothing is ever deleted — a section the source stops listing
  keeps its row and simply stops being confirmed (goes stale), rather than vanishing.
- **Idempotent refresh**: sections upsert on a stable natural key (school + term + subject + course +
  the platform's own section id), so re-collecting never duplicates a row. A section whose content
  hasn't changed keeps its original `source_url`/`retrieved_at` and only moves `last_confirmed_at`
  (via one bulk statement per batch, not a tracked write per row) — proven both in a unit test
  (`EfSectionRepositoryTests`) and against a live re-fetch: the measured run
  (`reports/run-20260912T033215Z.md`, from a clean DB) collected 1,745,787 sections, then a second,
  independent refresh run against that same database (`reports/run-20260912T170914Z.md`) re-collected
  every school and stored **1,638,359 sections unchanged (only `last_confirmed_at` moved), 3,341
  changed (live field drift such as enrollment counts), 108,190 added, and confirmed by direct SQL
  query — `SELECT school, term, subject, course, section, COUNT(*) ... HAVING COUNT(*) > 1` over the
  full 1,749,890-row table — that zero rows share a natural key.** Nearly all of the "added" total
  (108,190) is explained by two schools whose entire history came back new on the refresh after a
  transient failure on the first run (`uncc`: 93,686 sections; `utrgv`: 14,501 sections; together
  108,187 of the 108,190), not by a duplication bug — every other school's terms show the expected
  "0 added" pattern for a true refresh.

## Scaling and cost

Full methodology, prices and sources are in [`docs/cost-model.md`](cost-model.md); this section carries
the headline numbers once measured.

Two S7 runs measure this: a **measured run** from a clean database
(`reports/run-20260912T033215Z.md`) and a second **refresh run** against that same database
(`reports/run-20260912T170914Z.md`), both against all 49 configured schools with `--max-terms 24
--max-hosts 12`. The refresh is the later/final state and is what the numbers below come from,
since it includes two schools (`uncc`, `utrgv`) that only failed transiently on the first run.

- **Schools:** 49 attempted, 27 identified (15 from the homepage alone, 12 via config), 22 sent to
  manual review with evidence. Of the identified schools, **25 collected every attempted term
  completely, 1 partial** (`msudenver`, a recorded gap of 1 unreturnable record in each of 2 terms —
  see the failure-handling section above), **1 failed** (`uiuc`, needs a sign-in past the public
  class search).
- **Data:** 1,986 terms listed, 539 attempted, 534 collected; **1,749,890 sections, 2,044,834
  meetings, 0 extraction failures.** Full normalized export in `data/export/` (CSV + JSON); curated
  samples and a 50-row spot-check sheet in `data/samples/`.
- **Requests and runtime:** 5,474 requests (36 retries, 242 network errors, all recovered), 6,028.5 MB
  of response bodies, 01:28:23 wall clock (7:55:38 of school-time run at once across 12 hosts).
- **Cost (all estimates except the measured inputs; formula and full breakdown in
  [`docs/cost-model.md`](cost-model.md)):** $0.0248 for this run, **$0.0010 per collected school**,
  $0.0966/month to store the database at this size. Extrapolated to 1,000 schools: **~24.4 wall-clock
  hours, ~$0.41 per run, ~$3.72/month storage** — all with 0 LLM tokens spent, since the fallback
  extractor doesn't exist yet and every collected section came from the deterministic Banner 9
  connector.
- **Refresh cost:** confirmed near-zero relative to a fresh run, as expected — of 1,749,890 stored
  sections, 1,638,359 (93.6%) needed no content write at all (only `last_confirmed_at` moved), 3,341
  had real field changes (enrollment drift, mostly), and the 108,190 "added" rows are almost entirely
  two schools recovering from a transient first-run failure, not new duplication (see the
  failure-handling section above for the exact figures and the zero-duplicate SQL proof).

What's already known and doesn't change once the numbers land:

- **The dominant cost lever is the LLM fallback share (`f`), not infrastructure.** In the cost model's
  worked example, infrastructure for 1,000 schools is under $1 per run while the LLM fallback (at a 20%
  fallback rate) is ~$32 — about 98% of total cost. Every school moved from "needs the LLM fallback" to
  "has a real connector" is worth roughly 270× the entire infrastructure cost of collecting that school.
  That's the argument for connector-first architecture, and it's why T15's research (documenting a second
  platform's request shape, even without building it) has real value ahead of a from-scratch fallback
  build.
- **Colleague, if built, would cost far more per school than Banner 9** — no bulk section endpoint means
  roughly one request per course on top of paging, ~50–100× Banner 9's request count per term (measured:
  Bellarmine's one term took 603 requests vs. ~7 for a comparably sized Banner term). At the fixed
  per-host politeness delay, that's minutes of wall clock per Colleague term against seconds per Banner
  term — a concrete number for "connector reuse and setup cost vary by platform," not just an assertion.
- **The pipeline is delay-bound, not compute-bound**, by design (politeness rate limiting dominates
  runtime well before CPU does), which is why a small VM is the right baseline instead of a bigger one —
  see the cost model's "politeness floor" cross-check for how to verify that against the measured numbers.

## Accuracy

**Automated-only — no human hand-check.** T18 was scoped as a manual spot-check by Fabian plus an
automated diff on the rest, but Fabian ran out of Claude usage credits before he could do his half, and
with the deadline hours away that half was **skipped, not deferred**: there is no manual verification
number in this write-up, and the result below should not be read as if there were.

Instead, all 50 rows of `data/samples/spot-check.csv` were re-collected **live** with `duegooder verify`
(`src/DueGooder.Cli/VerifyCommand.cs`): for each row's school and term it re-runs the same
`Banner9Connector` used for collection — identifying the platform live for schools without a configured
`base_url`, listing terms, and paging class search — matches the stored row to the live section by CRN,
and diffs title, days, start time, end time, building, room and primary instructor field-by-field.
Sections were matched by CRN rather than re-fetched by URL because a `searchResults` URL's `pageOffset`
is only valid for that specific request's sort order and session; the connector's own term + class-search
walk is the same path a live URL would resolve through.

**Result: 48/48 checkable rows matched (100.0%).** The remaining 2 rows (both `fhda`, terms 202642 and
202541) could not be checked: `fhda`'s Banner term-list endpoint returned HTTP 500 at verification time,
confirmed independently with a plain `curl` a few seconds apart returning 500 twice — a live outage on
that registrar's server, not a data or connector problem. Those 2 rows are marked `unknown` in
`spot-check.csv` with that reason and are excluded from the rate rather than counted as mismatches; `fhda`
is 27,597 of the measured run's 1,749,890 stored sections (1.6%), and this outage happened at
verification time, after `fhda`'s own data was already collected and stored.

This number is real but narrower than a hand-check would have been: it re-verifies the same connector
logic and JSON parsing that produced the stored data, using the same field extraction, so it would not
catch a systematic misreading of the source (e.g. a mis-mapped JSON field that's wrong the same way both
times) the way independent human judgment against the registrar's own rendered page could. It does catch
drift since collection (enrollment changes, room reassignments, cancelled sections, stale terms) and
exercises the connector against 17 schools' live sites in one run, which is itself a meaningful reliability
signal on top of the accuracy number.

One accuracy caveat already known from S5, worth stating regardless of the final rate: Montgomery
College's "Spring 2028 (View Only)" term turned out to be Spring 2027 copied forward by the registrar
(3,283 of 3,292 sections share the same course + section number as Spring 2027, and 3,055 share the same
CRN) — a school publishing a future term as a planning shell, not a collection bug. Confirmed against the
live class search in a browser (3,292 classes, matching the run's count).

## Human steps disclosed

Every manual step taken across the project, with what it was and how many schools it touched, is logged
in [`config/human-steps.md`](../config/human-steps.md) and reproduced verbatim in each run report's Human
Steps section. Summary: building and growing the school list (searches + live confirmation, ~41 Banner 9
+ 8 non-Banner schools), two multi-campus codes read from a browser, one non-default port, starting each
unattended run by hand with its chosen limits, one live spot-check of a surprising result (Montgomery),
and reading run failures to fix the shared mapper (a review step, not a per-school workaround — nothing
was retried, skipped, or hand-edited to make a failing school look like it succeeded).
