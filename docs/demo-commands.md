# Demo commands (table round)

Copy-paste reference for the live table demo. Everything below was run once against the real
repo state before this file was written, so the expected outputs are real, not projected.

## 1. Live pipeline: one connector across many schools

Uses `config/demo-schools.yaml` (5 schools, not the full 49) so discovery + collection finish in
seconds instead of the full run's 1h28m. `uiuc` is deliberately included — it's already commented
in `config/schools.yaml` as "the failure-handling example" (its class search needs a sign-in).

```bash
dotnet run --project src/DueGooder.Cli -- run --schools config/demo-schools.yaml --db data/demo.db --max-terms 1
```

Run the exact same command again, against the same `data/demo.db`, to show the refresh guarantee:

```bash
dotnet run --project src/DueGooder.Cli -- run --schools config/demo-schools.yaml --db data/demo.db --max-terms 1
```

The second run's printed summary should show near-zero rows written for content, since sections
upsert on a natural key.

## 2. Tests, live

```bash
dotnet test DueGooder.slnx
```

Expected: **96 passed, 0 failed, ~1 second.** Fixture-based (recorded real responses from 5
schools) — no network dependency, safe to run cold at the table.

## 3. SQL — one query per judging criterion

Run against the real submission database, `data/duegooder.db` (1.75M sections from the measured +
refresh runs). All verified against the current file.

**Reliable updates & failure handling**

```bash
sqlite3 data/duegooder.db "SELECT SchoolId,TermCode,Subject,CourseNumber,SectionId,COUNT(*) c FROM Sections GROUP BY 1,2,3,4,5 HAVING c>1;"
```
→ 0 rows. Also backed by a real `UNIQUE INDEX` on those five columns — a duplicate isn't just
absent, it's structurally impossible.

```bash
sqlite3 data/duegooder.db "SELECT SUM(CASE WHEN RetrievedAt=LastConfirmedAt THEN 1 ELSE 0 END) new_or_unconfirmed, SUM(CASE WHEN RetrievedAt<LastConfirmedAt THEN 1 ELSE 0 END) confirmed_unchanged FROM Sections;"
```
→ `0 | 1,749,890` — every stored section shows a confirmation timestamp later than its original
retrieval: the refresh reconfirmed all of them without rewriting any source data.

```bash
sqlite3 data/duegooder.db "SELECT COUNT(*) FROM ExtractionFailureRow;"
```
→ 0, across 1.75M sections.

```bash
sqlite3 data/duegooder.db "SELECT SchoolId,TermCode,SectionCount,GapCount,GapDetail FROM Terms WHERE GapCount>0;"
```
→ `msudenver`, 2 terms, each with the exact missing record and reason — a partial failure gets
flagged and the rest of the term kept, not thrown away or hidden.

**Section and meeting accuracy**

```bash
sqlite3 data/duegooder.db "SELECT COUNT(*) total_meetings, SUM(CASE WHEN StartTime IS NULL THEN 1 ELSE 0 END) no_start_time FROM MeetingRow;"
```
→ `2,044,834 | 890,301` — 43.5% of meetings have no start time (async/TBA sections that don't
publish one), paired with the 0 extraction failures above: missing is not the same thing as
broken.

```bash
sqlite3 data/duegooder.db "SELECT COUNT(*) FROM Sections WHERE SchoolId='montgomery' AND TermCode='202830';"
```
→ 3,292 — matches the count confirmed live in the browser (Montgomery's "Spring 2028 (View Only)"
term turned out to be Spring 2027 copied forward; see `docs/WRITEUP.md`'s Accuracy section).

**Connector reuse & breadth**

```bash
sqlite3 data/duegooder.db "SELECT COUNT(DISTINCT SchoolId) schools, COUNT(*) sections FROM Sections;"
```
→ `26 | 1,749,890` — one `Banner9Connector`, zero school-specific code, 26 schools' worth of real
section data in one schema.

Cost and runtime-at-scale are not DB-queryable (they're wall-clock/dollar figures) — point at
`reports/run-20260912T170914Z.md` for those instead of a query.
