# DueGooder

**Give it a university's name. Get back its actual class schedule.**

Built for the **DueGooder Bounty** at **HACK KENTUCKY 2026** (KYX × JPMorganChase, in partnership with Genuine Works, Sep 11–12, Louisville).

---

## The problem

DueGooder is a student planner. The best onboarding it could have: a student picks their school and their sections, and the schedule fills itself in. The catch is that the data behind that — every course, section, instructor, meeting time and room, for every term — is spread across thousands of registrar websites, and no two of them look alike.

Scrapers written one school at a time don't scale. At a few hours of engineering per school, 4,000 US institutions is a multi-year project. On top of that, every one of those scrapers breaks quietly the next time a registrar redesigns its page.

## The insight

Most schools don't build their own registration systems. They rent one. Behind the custom branding, a few platforms serve most of the market:

| Platform | What gives it away | Public data surface |
| --- | --- | --- |
| **Ellucian Banner 9** | `/StudentRegistrationSsb/ssb/...` | JSON endpoints for terms and class search |
| **Ellucian Banner 8** | `bwckschd.p_disp_dyn_sched` | Server-rendered HTML schedule |
| **Oracle PeopleSoft** | `/psc/`, `/psp/`, `SSR_CLSRCH` | Guest class search pages |
| **Workday Student** | `*.myworkday.com` | Guest course-section search |
| **Ellucian Colleague** | `/Student/Courses` Self-Service | JSON search endpoints |
| **Coursedog / CourseLeaf / Acalog** | Hostnames and page markers | Catalog, often a public class schedule too |

So the job isn't really scraping 4,000 websites. It's writing a handful of **connectors**, one per platform, and a **discovery engine** that works out which connector a given school needs. Once the Banner 9 connector works for one school, it works for hundreds, and each new Banner school takes a config row, not new code.

## What we're building

A pipeline that runs on its own once started. It takes a list of schools and fills a central database with normalized, current course section data.

```text
 school name / URL
        │
        ▼
┌───────────────┐   find the catalog and schedule pages
│  1. Discover  │   (homepage crawl, registrar links, sitemaps, search)
└───────┬───────┘
        ▼
┌───────────────┐   fingerprint the platform from URLs, HTML markers,
│ 2. Identify   │   headers and known API routes → Banner / PeopleSoft / …
└───────┬───────┘
        ▼
┌───────────────┐   the shared per-platform connector lists terms, then
│  3. Collect   │   pulls every section and its meeting times
└───────┬───────┘   (unknown platform → LLM-assisted fallback, or flag for review)
        ▼
┌───────────────┐   one schema for every school; source URL + timestamp
│ 4. Normalize  │   on every record; "missing" kept separate from "failed"
└───────┬───────┘
        ▼
┌───────────────┐   idempotent upserts, new-term detection, change and
│ 5. Refresh    │   staleness tracking, broken-connector alerts
└───────────────┘
```

### 1. Discover

Starting from a name or a homepage, find the registrar, the course catalog and the term schedule. Known URL patterns get probed first because they're cheap. After that we crawl links a few levels deep, and a search query is the last resort.

### 2. Identify

Each connector ships its own **fingerprint**: URL patterns, HTML markers, response headers, and a probe request against a known endpoint. The platform is the connector whose fingerprint scores highest. Every decision gets logged with the evidence behind it, so a wrong guess is easy to debug.

### 3. Collect

Connectors are shared code, and a school-specific difference is a config value, not a fork. A connector does three things: list terms, list sections for a term, and hand back raw records. If no fingerprint matches, a **generic fallback** tries structured extraction (tables, JSON-in-page, LLM-assisted parsing of schedule pages). If that fails as well, the school goes into a **manual review queue** along with everything we learned about it.

### 4. Normalize

Every school maps into one schema:

`school → term → department → course → section → meeting (days, start/end time, location) + instructor + enrollment status`

Every record carries its **source URL** and **retrieval timestamp**. When a field doesn't exist at a school, it's stored as empty. When we failed to extract a field, that's recorded as a failure with a reason. A school that doesn't publish room numbers and a school whose room-number parsing broke should never look the same.

### 5. Refresh

Records have stable natural keys (school + term + course + section), so re-running updates rows instead of duplicating them. Each run also:

- finds newly published terms,
- diffs results against the last run and flags big drops (for example, "section count fell 90%" means a broken integration, not a cancelled semester),
- notices endpoints that have moved or started returning errors,
- marks data stale when it hasn't been confirmed recently.

## How we'll prove it

The bounty asks for measured results, so every run produces a **run report** automatically:

- schools attempted, identified, collected and failed, broken down by platform
- terms, courses, sections and meetings collected
- field completeness per school (how much of the schema was filled)
- wall-clock runtime and requests made
- **cost**: compute, bandwidth and LLM tokens, with the calculation written out and estimates labeled as estimates
- every human step, disclosed (for example, "manually confirmed 2 schedule URLs")
- a list of failures with reasons

For accuracy, we'll hand-check a random sample of sections against the live registrar pages and publish the match rate.

## Judging criteria → our answer

| They judge | Our answer |
| --- | --- |
| Section and meeting accuracy | Real sections and meeting times from registration systems, not catalog blurbs, checked against a hand-verified sample |
| Connector reuse and per-school setup | One connector per platform; adding a school on a known platform takes zero or one config row |
| Cost and runtime at scale | Lightweight HTTP requests first, a headless browser only when needed, LLM calls only on fallback; cost measured per school |
| Reliable updates and failure handling | Idempotent upserts, run diffs, staleness tracking, failures kept separate from missing data, and a review queue |

## Tech stack (proposed)

- **Python 3.12**, managed with `uv`
- **httpx** (async) for HTTP, **selectolax / BeautifulSoup** for HTML, **Playwright** only for pages that render with JavaScript
- **Pydantic** for the normalized schema
- **SQLite** for local runs, **Postgres** for the shared database
- **Claude** for the unknown-platform fallback extractor
- A per-host rate limiter, respect for `robots.txt`, and an identifying User-Agent

## Repository layout (planned)

```text
src/duegooder/
  domain/          # normalized entities and rules — no I/O
  application/     # use cases: discover, identify, collect, refresh; ports
  connectors/      # one package per platform (banner9/, peoplesoft/, workday/, …) + fallback/
  adapters/        # database, HTTP, browser and LLM implementations of the ports
  cli/             # entry points
config/schools.yaml  # school list + per-school overrides
data/samples/        # sample normalized output
reports/             # generated run reports
docs/                # bounty brief, design notes
```

## Running it

> Placeholder. It gets filled in once the first connector lands.

```bash
uv sync
uv run duegooder run --schools config/schools.yaml   # discover → collect → normalize → store
uv run duegooder report --latest                     # print the run report
```

## Submission checklist

- [ ] Multiple universities collected through at least one connector shared across schools
- [ ] Code + run instructions
- [ ] Sample normalized data in `data/samples/`
- [ ] Write-up covering setup, failure handling and scaling
- [ ] Report from an actual run: schools, data volume, runtime, cost (estimates labeled), manual steps, failures

## Team

- Prateek Mahendrakar
- Fabian Garcia
