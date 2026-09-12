# Ellucian Colleague Self-Service — request shape and fixtures

**Owner:** Fabian · **Task:** T15 (stretch, research half only) · **Feeds:** a future Colleague connector · **Captured:** 2026-09-12

T15 was cut from the required plan at the 11:30 AM feature freeze, so **this is research, not a connector** — no connector code was written, and none should be until after the submission. What's here is enough for someone to write one without touching a live site: the request shape, the fixtures, and the quirks that would otherwise cost an afternoon.

The headline: **Colleague Self-Service publishes sections as JSON, with no sign-in**, on identical endpoint paths at both schools tested, so one connector serves both. The catch is request volume — see [Cost of a Colleague crawl](#cost-of-a-colleague-crawl), which is the part that matters for the [cost model](cost-model.md).

## What's confirmed

| School (`id` in `config/schools.yaml`) | Hosting | Terms published | Fixtures | Notes |
| --- | --- | --- | --- | --- |
| `bellarmine` — Bellarmine University | Self-hosted IIS, `selfservice.bellarmine.edu` | **1** (`2026FA` Fall 2026) | ✅ 4 files | 581 courses over 20 pages for the one term. In-person sections with real rooms and times. Kentucky school, so it fits the demo story |
| `kishwaukee` — Kishwaukee College | Ellucian cloud, `kish-ss.colleague.elluciancloud.com` | **3** (`SP26`, `SU26`, `FA26`) | ✅ 5 files | 216 courses over 8 pages for `FA26`. Has both online/async sections (`ACC-121`) and on-campus lab sections (`AMT-116`) |
| `swccd` — Southwestern College | Self-hosted, `collselfserv.swccd.edu` | not attempted | — | Held in reserve. Two schools was the T15 bar, and every extra capture is live traffic on a real registrar |

`robots.txt` was checked first on all three hosts: `selfservice.bellarmine.edu` and `collselfserv.swccd.edu` answer **404**, `kish-ss.colleague.elluciancloud.com` answers **400**. All three are 4xx, which RFC 9309 treats as "unavailable" — crawling is allowed. (Only 5xx or a dropped connection means disallow-all, which is what blocked 10 of the Banner hosts in the overnight run.) Nothing asked us to log in at any point; every response below came back `200` to an anonymous request.

## The request shape

Four steps. Everything after step 1 is JSON, and no headless browser is needed anywhere.

| # | Method | Path | What it's for |
| --- | --- | --- | --- |
| 1 | GET | `/Student/Courses/Search` | **Handshake.** Sets the `.ColleagueSelfServiceAntiforgery` cookie and carries a `__RequestVerificationToken` hidden input. Both are required by every later call |
| 2 | GET | `/Student/Courses/GetCatalogAdvancedSearch` | The filter model: **terms**, subjects, locations, academic levels, course types, topic codes, days of week, time ranges |
| 3 | POST | `/Student/Courses/PostSearchCriteria` | Search. Returns a **course** listing with `MatchingSectionIds` per course, plus `TotalItems` / `TotalPages` / `CurrentPageIndex` |
| 4 | POST | `/Student/Courses/Sections` | The actual **sections** for one course: term, meetings, faculty, seats, dates |

Both schools serve exactly these paths, and they are discoverable rather than guessed: the entry page embeds them as `Ellucian.Course.ActionUrls.*` assignments. Other endpoints in that map, not needed for collection: `SectionDetails`, `GetSubjects`, `AdvancedSearchAjax`, `SearchResult`.

### Step 1 — handshake (mandatory)

```bash
UA="DueGooder/0.1 (HACK KENTUCKY 2026 course-data bounty)"
BASE="https://selfservice.bellarmine.edu"

curl -sS -A "$UA" -c cookies.txt -o search-page.html "$BASE/Student/Courses/Search"
TOKEN=$(grep -o '__RequestVerificationToken" type="hidden" value="[^"]*"' search-page.html \
        | sed 's/.*value="//; s/"$//')
```

There is no way around this step: the JSON endpoints reject a request carrying only the cookie with `400 Could not validate this request's antiforgery token.` The token goes in a **`__RequestVerificationToken` request header** and the cookie must be sent alongside it — they are a matched pair, so one session per school per run. Banner 9 already needs a cookie session (`IHttpSession`), so the port that exists covers this.

### Step 2 — terms and filters

```bash
curl -sS -A "$UA" -b cookies.txt \
  -H "__RequestVerificationToken: $TOKEN" -H "X-Requested-With: XMLHttpRequest" \
  "$BASE/Student/Courses/GetCatalogAdvancedSearch"
```

→ [`get-catalog-advanced-search.json`](../tests/fixtures/colleague/bellarmine/get-catalog-advanced-search.json)

### Step 3 — search one term

```bash
curl -sS -A "$UA" -b cookies.txt \
  -H "Content-Type: application/json; charset=UTF-8" \
  -H "__RequestVerificationToken: $TOKEN" -H "X-Requested-With: XMLHttpRequest" \
  --data-binary @criteria.json \
  "$BASE/Student/Courses/PostSearchCriteria"
```

`criteria.json` is the full criteria model. Every field the app sends, with the values that work:

```json
{"keyword":"","terms":["2026FA"],"subjects":["ACCT"],"pageNumber":1,"sortOn":2,"sortDirection":0,
 "requirement":null,"subrequirement":null,"courseIds":[],"sectionIds":[],"requirementText":null,
 "subrequirementText":null,"group":null,"startTime":null,"endTime":null,"openSections":null,
 "academicLevels":[],"courseLevels":[],"synonyms":[],"courseTypes":[],"topicCodes":[],"days":[],
 "locations":[],"faculty":[],"onlineCategories":[],"keywordComponents":[],
 "startDate":null,"endDate":null,"startsAtTime":null,"endsByTime":null}
```

Two traps here, each of which costs a request to discover:

- **`sortOn` and `sortDirection` must be numbers**, not empty strings (`2` = Section Name, `0` = Ascending, per the app's own comments). Send `""` and the whole model fails to bind, and the server answers `400 Search criteria is required when searching for courses.` — which reads like "you sent no filters" and sends you looking in the wrong place.
- **Use `null`, not `""`,** for the unused scalar fields. The app sends `""` for some of them, but `null` binds cleanly for all.

Leave `subjects` empty to page through a whole term; that's the shape a connector wants. `pageNumber` is 1-based, and page size is fixed at 30 by the server — the criteria model has no page-size field, so it cannot be raised.

→ [`post-search-criteria-2026fa-page1.json`](../tests/fixtures/colleague/bellarmine/post-search-criteria-2026fa-page1.json) (whole term, page 1 of 20) and [`post-search-criteria-acct-2026fa.json`](../tests/fixtures/colleague/bellarmine/post-search-criteria-acct-2026fa.json) (one subject)

### Step 4 — sections for a course

```bash
curl -sS -A "$UA" -b cookies.txt \
  -H "Content-Type: application/json; charset=UTF-8" \
  -H "__RequestVerificationToken: $TOKEN" -H "X-Requested-With: XMLHttpRequest" \
  --data-binary '{"courseId":"1","sectionIds":["35541","35542","35543"]}' \
  "$BASE/Student/Courses/Sections"
```

`courseId` and `sectionIds` both come from the step-3 response (`Courses[].Id`, `Courses[].MatchingSectionIds`). The response nests as `SectionsRetrieved.TermsAndSections[].Sections[].Section`, with the term's own dates on `TermsAndSections[].Term`.

→ [`post-sections-acct201.json`](../tests/fixtures/colleague/bellarmine/post-sections-acct201.json), [`post-sections-amt116.json`](../tests/fixtures/colleague/kishwaukee/post-sections-amt116.json) (on-campus), [`post-sections-acc121.json`](../tests/fixtures/colleague/kishwaukee/post-sections-acc121.json) (online/async)

## How terms are identified

Terms come back from step 2 as bare tuples — `{"Item1":"2026FA","Item2":"Fall 2026"}` — where `Item1` is the code and `Item2` the name.

**The code format is the school's own and can't be guessed:** Bellarmine uses `2026FA`, Kishwaukee uses `FA26`. Unlike Banner 9's `mep_code`, though, nothing has to go into `config/schools.yaml` for it, because the codes are discovered from the endpoint. Per-school setup stays at **`base_url` and nothing else**.

**Only terms the school flags for course search are visible**, and that list can be very short: Bellarmine publishes exactly one term. So "list terms" for a Colleague school means "the terms this school currently lets the public search", not "every term that exists". A refresh should expect the list to shrink under it and must not read a term disappearing as data loss — the broken-integration rule in [T14](PLAN.md) already covers that shape.

Term start and end dates, plus registration windows, arrive with the sections in step 4 (`TermsAndSections[].Term`), not in step 2.

## Mapping quirks

These are the things that would silently produce wrong data.

**Meeting times are the dangerous field.** A meeting looks like this:

```json
{"InstructionalMethodCode":"LC","StartTime":"2026-09-12T13:00:00+00:00","EndTime":"2026-09-12T13:50:00+00:00",
 "RawStartTime":"0001-01-01T09:00:00-05:00","Days":[1,3,5],"Room":"CNMH*270",
 "StartDate":"2026-08-20T00:00:00-04:00","EndDate":"2026-12-02T00:00:00-05:00","Frequency":"W","IsOnline":false}
```

- **Do not use `StartTime` / `EndTime`.** They are the time-of-day converted to UTC and stamped with **the date the request was made** (`2026-09-12` above is the capture date, not a class date). Reading them as instants shifts every class by the school's UTC offset.
- **`RawStartTime` carries the real wall-clock time** (`09:00`), but its offset is junk: Kishwaukee returns `-05:51`, which is Chicago's pre-1900 local mean time, because the value is anchored at year `0001`. Take the time-of-day and drop the offset. This is exactly the case the data rules in [AGENTS.md](../AGENTS.md) are written for — keep the raw string next to the parsed value so the audit trail survives.
- `FormattedMeetingTimes` runs parallel to `Meetings` and holds display-ready strings (`DaysOfWeekDisplay: "M/W/F"`, `StartTimeDisplay: "9:00 AM"`, `EndTimeDisplay: "9:50 AM"`). It is the natural raw text to store beside the parsed time, and it is how the day-number mapping below was verified rather than assumed.

**`Days` is a list of .NET `DayOfWeek` integers**, Sunday = 0. `[1,3,5]` is Monday/Wednesday/Friday — confirmed against `DaysOfWeekDisplay: "M/W/F"` in the same payload.

**`Room` is `BUILDINGCODE*ROOM`**, e.g. `CNMH*270`, `E*134`. A bare `"*"` means no building and no room, which is what online sections carry — a placeholder, so it maps to `null` with the raw `"*"` kept, never to a room called `*`. The friendly building name lives in `FormattedMeetingTimes[].BuildingDisplay` and does **not** match the code (`E` → `Caukin Building`); it can also contain a comma (`Centro, McGowan Hall`), which matters for CSV export.

**Online/async sections** look like: `Days: []`, `RawStartTime: null`, `Room: "*"`, `IsOnline: true`, `InstructionalMethodDisplay: "Online - Anytime"`, `LocationDisplay: "Online"`. Missing days and times there mean the school doesn't publish them, so they are `null`, not extraction failures.

**Section identity — three different values, all needed:**

| Field | Example | What it is |
| --- | --- | --- |
| `Id` | `35541` | Internal Self-Service section id. What step 4 takes as `sectionIds` |
| `Synonym` | `0034948` (Bellarmine), `60289` (Kish) | The registrar-facing code, Colleague's closest analogue to a Banner CRN. Zero-padded at Bellarmine, so it is a **string**, not a number |
| `SectionNameDisplay` | `ACCT-201-01`, `AMT-116-3A01` | Subject-course-section as students see it. Section numbers are not numeric (`3A01`) |

**Other fields.** `MinimumCredits` / `MaximumCredits` / `VariableCreditIncrement` give the same fixed-or-range credit shape Banner has. `Capacity` / `Enrolled` / `Available` / `Waitlisted` are live counters, so they move between a crawl and a re-check. Instructors come as `Sections[].InstructorDetails[]` with a `FacultyId` and an **abbreviated** name only (`Eiden, B`, `Banasiak, T`) — no full names are published, so a full instructor name is a school-level `null` for Colleague, not a failure. A section's `StartDate`/`EndDate` and its meeting's dates can differ (section ends `2026-12-10`, meeting ends `2026-12-02`), so meeting dates have to be stored per meeting.

## Cost of a Colleague crawl

This is the finding with consequences. Banner 9 returns up to 500 sections per request; **Colleague returns 30 courses per request and then needs one more request per course** to get its sections. There is no bulk section endpoint: a keyword search was tested to see whether it returns a section listing inline, and it does not — `TypeOfSearchResultView` stays `CatalogListing`, `Sections` is `null`, and `CourseFullModels` holds display text only ([`post-search-criteria-keyword-fa26.json`](../tests/fixtures/colleague/kishwaukee/post-search-criteria-keyword-fa26.json)).

Measured page and course counts, with the request count they imply:

| School, term | Courses | Pages | Requests for one term | At the 2 s per-host delay |
| --- | ---: | ---: | ---: | ---: |
| `kishwaukee`, `FA26` | 216 | 8 | 2 + 8 + 216 = **226** | ~7.5 min |
| `bellarmine`, `2026FA` | 581 | 20 | 2 + 20 + 581 = **603** | ~20 min |

For comparison, a Banner 9 term with 3,292 sections took about 7 requests in the overnight run. **Colleague costs roughly 50–100× the requests per term.** That lands on `r` (requests per school) in [docs/cost-model.md](cost-model.md), and because our runtime is set by the per-host delay rather than by CPU, it lands on runtime too: a Colleague school costs minutes per term where a Banner school costs seconds.

Two things worth testing before a connector commits to the two-step crawl, neither tried here because both are guesses about unpublished behaviour: whether the "section listing view" referenced in the app's own code is reachable through a request the UI never makes, and whether `sectionIds` or `synonyms` in the criteria model accept a batch large enough to be useful. Anything that is not a request the public UI itself makes deserves a second look on good-citizen grounds before we rely on it.

## Capturing another school

1. `git checkout develop && git pull`, then `git checkout -b fixtures/colleague-<school-id>`.
2. **Check `robots.txt` first**: `curl -sS -A "$UA" -o /dev/null -w '%{http_code}\n' https://<host>/robots.txt`. A 5xx or a dropped connection means stop — that is disallow-all under RFC 9309. Read any rules a 200 returns.
3. Run steps 1–4 above in order, **one request at a time, at least a second apart**. Write responses straight to `tests/fixtures/colleague/<school-id>/` with `-o`; never paste them through an editor.
4. Keep it to about five files: the filter model, one whole-term search page, one subject-filtered search, and one or two section responses — including an online-only section if the school has any, since that is where mapping breaks.
5. **Don't commit the entry page.** It is ~280 KB and carries a live antiforgery cookie and token. These fixtures are JSON only, which is also what `tests/fixtures/banner9/` does.
6. Add a row to the table at the top, including the school's term-code format and anything odd. A school that turns out to need a sign-in is still a useful row.

## Rules

- **Never edit a fixture by hand.** It has to be exactly what the school sent, or the tests prove nothing. If one looks wrong, capture it again.
- Public, unauthenticated data only. If anything asks for a login, stop and write down where.
- One request at a time per host, at least a second apart, with the identifying User-Agent above.
- Don't re-run captures for fun; each one hits a real college's server.

## Not done, and open questions

- **No connector code.** Deliberate: T15's build half is cut and the freeze is on.
- `swccd` is not captured. It is the third school if either of these two turns out to be unrepresentative.
- **Fingerprinting is not designed.** The markers are the `<title>… Self-Service</title>`, the `Ellucian.Course.ActionUrls.*` assignments in the entry page, and the `.ColleagueSelfServiceAntiforgery` cookie. Unlike Banner 9 there is no cheap JSON probe, because the antiforgery handshake means identifying a Colleague school costs an HTML fetch.
- **Past terms were not probed.** Both schools publish only current and upcoming terms; whether an unlisted term code still answers is untested, and testing it means asking for data the school's own UI doesn't offer. That should be a deliberate decision, not an accident.
- `SectionDetails` (the per-section pane, which may carry books and fees) is not captured.
