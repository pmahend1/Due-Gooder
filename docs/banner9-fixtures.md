# Banner 9 fixtures — how to capture more schools

**Owner:** Fabian · **Feeds:** the Banner 9 connector tests (T5) · **Time:** about 10 minutes per school

## Why this matters

The Banner 9 connector is one piece of code shared by every Banner 9 school. Its tests run against saved responses ("fixtures") instead of live sites. Each extra school with fixtures is proof the connector really is reused with no school-specific code. That proof is one of the four things we're judged on.

## What's already done

| School (`id` in `config/schools.yaml`) | Term captured | Status |
| --- | --- | --- |
| `eku` — Eastern Kentucky | 202710 (Fall 2026) | ✅ full set |
| `sunyempire` — SUNY Empire State | 202680 (Fall 2026) | ✅ full set (mostly online sections, one with 4 meetings) |
| `uiuc` — Illinois Urbana-Champaign | 120268 (Fall 2026) | ⚠️ `getTerms.json` only. Class search redirects to a university login, and we only collect public data |
| `kccd` — Kern Community College District | 202670 (Fall 2026) | ✅ full set (4089 sections). One district-wide term, no `mep_code`. The two captured pages span 5 Bakersfield College campuses (Main, Online, Arvin-Lamont, Delano, Southwest); no Cerro Coso or Porterville sections appear in them, but results are sorted by subject so that isn't settled either way. Part-of-term codes are letters (`B`, `BNC`, `BOT`), course numbers carry a campus letter (`B70A`), and `creditHours` is null with the value only in `creditHourLow` (0.5-credit and 0-credit sections) |
| `oakland` — Oakland University | 202640 (Fall Semester 2026) | ✅ full set (3658 sections). Fall is `…40`, not `…70` — `getTerms` also lists Continuing Education 2026-2027 (`202633`) and OUWB med school (`202635`), so the number really can't be guessed. Sections split across Main Campus and Internet; some have two `LEC` meeting rows for one section |
| `odu` — Old Dominion | 202610 (Fall 2026) | ✅ full set (7279 sections, largest so far). `partOfTerm` mixes `16W`, `8A` and `8B` inside the one Fall 2026 term, so the eight-week terms don't need separate captures. `sequenceNumber` is `"0"` on every section in the captured pages, so it can't be part of the natural key — fixed in T5: `SectionKey` now keys on the CRN (`courseReferenceNumber`), and the published `sequenceNumber` is kept as `Section.DisplaySectionNumber` for display only. Some meetings are `TBA`/`ONLN` building/room with `null` begin/end times, which map to `null` (not a failure) since the field just isn't published for those meetings |

## Your task

`kccd`, `oakland` and `odu` are done (see the table above) and are now wired into `Banner9Fixtures`, `Banner9ConnectorTests` and `Banner9MappingTests` — all five schools with a full fixture set (eku, sunyempire, kccd, oakland, odu) pass the distinct-key test.

Capture fixtures for **2–3 more schools** from `config/schools.yaml`. Good picks, each with a quirk worth testing:

- `wvu`, `lehigh`: large, ordinary schools, good as a baseline
- `ctstate`, `tric`: multi-campus community colleges, to follow up on the `kccd` question below (do sections from every college come back in one term?)
- `ucmerced`, `uidaho`: different hosting setups, worth a look for base-URL surprises

## Steps

1. **Get the latest code:** `git checkout develop && git pull`, then `git checkout -b fixtures/<school-id>`.
2. **Find the school** in `config/schools.yaml`. Copy its `id`, its `base_url` and, if present, its `mep_code`.
3. **Find the term code.** Open this in a browser (put the school's `base_url` in front):
   `<base_url>ssb/classSearch/getTerms?searchTerm=&offset=1&max=20`
   Pick the main current term (Fall 2026, not "(View Only)"). Codes mean different things at different schools, so always read the `description` and never guess from the number.
4. **Run the capture script** from the repo root:

   ```bash
   tools/capture-banner9-fixtures.sh <school-id> <base_url> <term-code> [mep_code]
   # e.g.
   tools/capture-banner9-fixtures.sh kccd https://reg-prod.ec.kccd.edu/StudentRegistrationSsb/ 202670
   ```

   It makes 6 polite requests and writes 5 files into `tests/fixtures/banner9/<school-id>/`:
   `getTerms.json`, `term-search.json`, `searchResults-0.json`, `searchResults-10.json`, `resetDataForm.json`.
   It ends with `OK: <school> captured.` or with a `FAILED:` line that says what went wrong.
5. **If it fails**, add a row to the table above with the reason (e.g. "302 → login") and pick another school. A school we *can't* collect is still a useful finding, because the write-up has to report failures honestly.
6. **Add a row to the table above** for each school that worked, with the term and anything unusual you noticed (online-only, no instructors listed, strange section numbers…).
7. **Commit and open a PR into `develop`:**

   ```bash
   git add tests/fixtures/banner9/<school-id> docs/banner9-fixtures.md
   git commit -m "Add Banner 9 fixtures for <school-id>"
   git push -u origin fixtures/<school-id>
   gh pr create --base develop --fill
   ```

## Rules

- **Never edit the JSON files by hand.** They must be exactly what the school sent, or the tests prove nothing. If a file looks wrong, re-run the script.
- **Don't re-run the script over and over** against the same school; each run hits a real university server.
- Public data only: if anything asks you to log in, stop and record it (step 5).
- Needs `bash` and `curl`, which ship with macOS and Git for Windows (use "Git Bash").

## Using Claude Code for this

Paste this as your first message:

```text
Read docs/banner9-fixtures.md and follow it for the schools <id1>, <id2>, <id3> from config/schools.yaml.
Look up each school's current Fall 2026 term code with getTerms before capturing.
Don't print whole JSON files into the conversation; check them with jq (totalCount, number of records).
Update the status table in the doc, then commit on a branch fixtures/<ids> and open a PR into develop.
```

## Done when

At least 2 new schools have a full set of 5 files in `tests/fixtures/banner9/`, the table above is updated (including any schools that failed and why), and the PR is open against `develop`.
