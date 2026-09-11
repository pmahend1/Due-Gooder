# AGENTS.md

Guidance for AI coding agents (and humans) working in this repository.

## Project context

DueGooder is a hackathon entry for the **DueGooder Bounty** (HACK KENTUCKY 2026). The brief is [docs/DUEGOODER-BOUNTY.pdf](docs/DUEGOODER-BOUNTY.pdf) and the pitch is [ReadMe.md](ReadMe.md).

**Goal:** turn a university's name or website into public course and section data (sections and meeting times, not just catalog descriptions) in a central database, and design it so it works for thousands of schools.

**How we're judged:** section and meeting accuracy · connector reuse and per-school setup · cost and runtime at scale · reliable updates and failure handling. Scale and cost claims have to be backed by measured results.

Keep those four criteria in mind for every change. A change that lowers per-school setup or makes a failure easier to see is worth more than one that makes a single school slightly nicer.

## Architecture

The pipeline runs **Discover → Identify → Collect → Normalize → Refresh**. The code follows Clean Architecture, and dependencies point inward only:

```text
src/
  DueGooder.Domain/          # entities (School, Term, Course, Section, Meeting, Instructor…) and rules. No I/O, no framework types.
  DueGooder.Application/     # use cases + ports (IConnector, ISectionRepository, IHttpFetcher, ILlmExtractor, IClock)
  DueGooder.Connectors/      # one folder per platform: Banner9/, Banner8/, PeopleSoft/, Workday/, Colleague/, … + Fallback/
  DueGooder.Infrastructure/  # port implementations: HttpClient, Playwright, EF Core (SQLite/Postgres), Claude
  DueGooder.Cli/             # composition root and commands
tests/DueGooder.Tests/
```

- Project references enforce the dependency rule: `Domain` references nothing, `Application` references only `Domain`, and so on outward. Never add a reference that points outward.
- Connectors depend on ports, not on concrete clients, so they can run against recorded fixtures.
- Build the concrete classes at the composition root in `DueGooder.Cli`.

## The connector contract

A connector is **shared across every school on its platform**. It never contains school-specific branches.

Each connector provides:

1. **Fingerprint:** returns a confidence score and the evidence behind it (matched URL patterns, HTML markers, headers, probe responses) for a candidate site.
2. **List terms:** discovers every available term, including ones published since the last run.
3. **Collect sections:** for a term, yields raw section records with meetings.
4. **Map:** turns the raw records into normalized domain entities.

Rules:

- School-specific differences (base URL, term code format, page size, auth-free guest path) go in `config/schools.yaml`, never in code. If you're about to write `if (school.Id is "...")`, make it a config field instead.
- Prefer a platform's JSON or API endpoints over HTML, and HTML over a headless browser. Use Playwright only when the data really does require JavaScript, since it's the most expensive path.
- Use the LLM fallback only when no connector's fingerprint matches with enough confidence. Log its token usage for cost reporting.
- Anything that can't be collected goes to the **manual review queue** with the evidence gathered. Never drop it silently.

## Data rules

- Every normalized record carries `source_url` and `retrieved_at` (UTC).
- **Missing is not failed.** A field the school doesn't publish is stored as `null`. A field we failed to extract gets an `ExtractionFailure` entry with a reason. Never turn a placeholder like `"TBA"` into `null` without keeping the original raw text.
- Keep the raw source value next to the parsed value for times, days and locations, so accuracy can be audited.
- Use stable natural keys (school + term + course + section) so refreshes upsert instead of duplicating.
- Times are local to the school. Store the school's IANA timezone and keep meeting times as local wall-clock times plus days of the week.

## Refresh and failure handling

- Runs must be idempotent. Re-running with no source changes produces zero net writes.
- Compare every run against the previous one. A large drop in section count, or a term vanishing, flags the integration as broken rather than deleting data.
- Record endpoint health (status codes, schema drift) per school so moved endpoints show up.
- Mark data stale when it hasn't been re-confirmed within the refresh window.

## Metrics are a deliverable

The final submission needs a report from a real run. Instrument as you build, not afterwards:

- per school: platform detected, confidence, terms/sections/meetings collected, field completeness, requests, bytes, runtime, errors
- per run: totals, wall-clock time, LLM tokens, and estimated cost, with the formula written out and estimates labeled
- every human intervention, logged explicitly

Reports go in `reports/`. Sample normalized output goes in `data/samples/`.

## Being a good citizen

- Access only public, unauthenticated data. Never log in, never bypass CAPTCHAs or access controls.
- Respect `robots.txt`, rate-limit per host (conservative by default), and send an identifying User-Agent.
- Cache responses during development so the same registrar isn't hit over and over.

## Tooling

- C# / .NET 10, one solution `DueGooder.sln`.
- `dotnet build` builds. `dotnet test` runs tests. `dotnet format` formats.
- Run the pipeline with `dotnet run --project src/DueGooder.Cli -- run --schools config/schools.yaml`.
- Key libraries: `IHttpClientFactory` + `Microsoft.Extensions.Http.Resilience`, AngleSharp, Microsoft.Playwright (JavaScript pages only), EF Core (SQLite locally, Npgsql for Postgres), the official Anthropic C# SDK, xUnit.
- Test connectors against **recorded fixtures** (saved HTML/JSON responses) in `tests/fixtures/<platform>/`, not against live sites. Keep a small opt-in live smoke test per connector (an xUnit trait that's excluded by default).
- When adding a connector, add fixtures from at least **two different schools** on that platform. That's how we show reuse.

## Code style

- Readable names over comments. Comment the *why*, not the *what*. Use `//` for 1–2 lines and a single `/* */` block for 3+. Use XML doc comments on public API.
- Use explicit constant patterns: `count is 0`, `isEnabled is false`, `user is null`, `name is ""`, `items.Count is 0`. Never use a bare `!` on a boolean; write `is false`. Use `==` only to compare two variables.
- One type per file (class, record, struct, enum, interface), named after the type. When you move a nested type out, rename it if its name is vague on its own.
- Members go in two regions, in this order: `#region State` (constants, fields, properties) and `#region Methods`, each closed with its name (`#endregion State`).
- Align wrapped parameter and argument lists under the first argument. A call that fits on one line stays on one line.
- SOLID: add a new platform by adding a connector, never by growing a `switch` on platform type.
- Business logic stays free of I/O and framework types.

## Hackathon priorities

The task breakdown, owners and dependencies are in [docs/PLAN.md](docs/PLAN.md). Check off tasks there as they land. In order:

1. One connector (Banner 9 is the likely first) working end to end for **several schools**, with normalized output stored.
2. Discovery and identification running automatically from a school list.
3. The run report with measured runtime and cost.
4. Refresh: idempotent upserts, new-term detection, broken-integration flags.
5. More platforms (PeopleSoft, Workday, Colleague), then the fallback extractor.

Breadth across schools on a working connector beats half-finished connectors for many platforms.

## Git

- Small, focused commits with imperative messages.
- Don't commit secrets, API keys, `.env` files, large raw crawl dumps or `.DS_Store`.
