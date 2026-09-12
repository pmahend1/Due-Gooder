# Cost model

**Owner:** Fabian · **Task:** T22 · **Feeds:** the run report's Cost section (T13) and the scaling write-up (T20)

What it costs to collect one school, and what 1,000 schools would cost. Cost and runtime at scale is one of the four things we're judged on, and the brief says the claims have to be backed by measured results — so this document separates the two kinds of number strictly:

- **measured** — comes out of a real run's `reports/<run-id>.json`. Until [T16](PLAN.md) lands, every measured slot here is a placeholder written as `___`.
- **estimate** — a price, a machine choice, or an extrapolation. Every one is labeled. Nothing in the extrapolation to 1,000 schools is measured: it is arithmetic on top of measured per-school numbers, and the assumptions it rests on are listed in [What this model leaves out](#what-this-model-leaves-out).

Prices are public list prices, in USD unless noted, **all checked 2026-09-12**. They are estimates in the sense that matters here: we have not been billed for any of it. Re-check them before the submission, since three of the four vendor pages render prices in the browser and can move without notice (Hetzner raised cloud prices in June 2026).

## Prices

### Compute: one small cloud VM

One always-on small VM runs the whole pipeline; the work is I/O-bound and politeness-bound, not CPU-bound (see [What this model leaves out](#what-this-model-leaves-out)).

| Option | Spec | $/hour | $/month | Included outbound | Source (checked 2026-09-12) |
| --- | --- | ---: | ---: | --- | --- |
| **AWS `t4g.small`** (baseline) | 2 vCPU ARM, 2 GB | 0.0168 | 12.26 | 100 GB/mo free, account-wide | [Vantage's EC2 table for `t4g.small`, us-east-1](https://instances.vantage.sh/aws/ec2/t4g.small) · list price confirmed against [AWS EC2 on-demand pricing](https://aws.amazon.com/ec2/pricing/on-demand/) |
| DigitalOcean Basic Droplet | 1 vCPU, 1 GB | 0.00893 | 6.00 | 1,000 GiB/mo | [DigitalOcean Droplet pricing](https://www.digitalocean.com/pricing/droplets) |
| Hetzner Cloud `CX23` | 2 vCPU, 4 GB | €0.0088 | €5.49 | 20 TB/mo (EU) | [CostGoat's Hetzner tracker, updated 2026-09-05](https://costgoat.com/pricing/hetzner) · billing rules from [Hetzner Cloud billing FAQ](https://docs.hetzner.com/cloud/billing/faq/) |

Notes on these prices, all **estimates**:

- AWS is the baseline because it is the number already used in `reports/run-20260912T033215Z.md`, and because its per-GB egress price is the least forgiving of the three — if the model works out cheap on AWS it is cheap anywhere.
- AWS's own on-demand table is JavaScript-rendered, so the `t4g.small` hourly rate is quoted from Vantage's mirror of the AWS price list. Hetzner's pages render prices client-side too; the `CX23` figures come from a third-party tracker, so treat them as indicative and confirm in the Hetzner console before quoting them. The DigitalOcean figures are from DigitalOcean's own pricing page.
- Hetzner and DigitalOcean bill hourly up to a monthly cap, so a run that takes 25 hours costs 25 hours, not a month ([Hetzner billing FAQ](https://docs.hetzner.com/cloud/billing/faq/)). AWS on-demand bills per second with a 60-second minimum. For a pipeline that runs on a schedule and can be shut down between runs, hourly is the right unit; for a machine left running, use the monthly price.
- Hetzner prices are in EUR and this model does not convert them. If we quote Hetzner in the write-up, convert at the rate on the day and say so.

### Bandwidth

We are almost entirely a *download* workload: we fetch registrar JSON and pages and write rows to a local database. Outbound is request headers and query strings only, a rounding error next to inbound.

| Direction | AWS | DigitalOcean | Hetzner |
| --- | --- | --- | --- |
| Inbound (what we actually consume) | **$0/GB** | **$0/GB** | **$0/GB** |
| Outbound, beyond what's included | $0.09/GB, first 10 TB/mo | $0.01/GiB | €1.00/TB (≈ €0.001/GB) |
| Included outbound | 100 GB/mo free, account-wide | 1,000 GiB/mo per Droplet pool | 20 TB/mo (EU servers) |

Sources, checked 2026-09-12: AWS's 100 GB/month free egress allowance is stated on [AWS EC2 on-demand pricing](https://aws.amazon.com/ec2/pricing/on-demand/); the $0.09/GB first-10-TB tier is from [EgressCost's AWS data-transfer summary](https://egresscost.com/aws/data-transfer-pricing/) (AWS's own tier table is JavaScript-rendered). DigitalOcean's $0.01/GiB overage and "inbound transfer to Droplets is free" are from [DigitalOcean's bandwidth billing docs](https://docs.digitalocean.com/products/billing/bandwidth/). Hetzner's "we only bill for outgoing traffic; incoming and internal traffic is free" is from the [Hetzner Cloud billing FAQ](https://docs.hetzner.com/cloud/billing/faq/); the €1.00/TB overage figure is widely reported but is not on a Hetzner page we could read, so treat it as an **estimate to confirm**.

Because inbound is free on all three providers, the bytes we measure per school (`requests.bodyBytes`) cost **$0** as long as we run on one of them. We still measure and report them: they are the number that would matter on a provider that bills ingress, and they are the honest size of the load we put on registrars.

### Storage

Not a per-run cost — it recurs monthly whether or not we run. Kept as a separate line for that reason.

| Option | $/GB-month | Source (checked 2026-09-12) |
| --- | ---: | --- |
| Amazon EBS `gp3` (baseline) | 0.08 | [Amazon EBS pricing](https://aws.amazon.com/ebs/pricing/) — $0.08/GB-month in us-east-1, including 3,000 IOPS and 125 MiB/s |

Estimate. Postgres-as-a-service instead of a file on a disk would cost considerably more; that choice isn't made yet.

### LLM tokens (the fallback extractor)

The fallback extractor is the last resort: it runs only when no connector's fingerprint matches a school's site with enough confidence ([AGENTS.md, the connector contract](../AGENTS.md)). Per-school its cost dwarfs the infrastructure cost, so the share of schools that reach it is the single most important input in this model.

| Model | Input $/MTok | Output $/MTok | Batch input | Batch output | Cache read $/MTok |
| --- | ---: | ---: | ---: | ---: | ---: |
| **Claude Haiku 4.5** (baseline for the fallback) | 1.00 | 5.00 | 0.50 | 2.50 | 0.10 |
| Claude Sonnet 5 (for pages Haiku can't hold) | 2.00 | 10.00 | 1.00 | 5.00 | 0.20 |
| Claude Opus 5 (not planned; for reference) | 5.00 | 25.00 | 2.50 | 12.50 | 0.50 |

Source for all of the above: [Anthropic's pricing page](https://platform.claude.com/docs/en/about-claude/pricing), checked 2026-09-12. Three details from that page that change the arithmetic:

- **The Batch API is a 50% discount on both input and output.** A refresh crawl is not latency-sensitive, so the batch columns are the realistic ones for scheduled runs. The model below uses the standard columns as the conservative case and shows the batch case beside it.
- **Prompt caching**: a cache read costs 0.1× base input ($0.10/MTok on Haiku 4.5), a 5-minute cache write 1.25× base input. Our fallback prompt is the same extraction instructions plus schema on every call, with only the page body changing — exactly the shape caching is for. Not modeled below, because we haven't measured how big the fixed part of the prompt is; it can only make the LLM line cheaper.
- **Token counts don't transfer between these models.** Claude 4.7 and later (so Sonnet 5 and Opus 5) use a newer tokenizer that produces roughly 30% more tokens for the same text; Haiku 4.5 uses the older one. So a page measured at 100k tokens on Haiku 4.5 is about 130k tokens on Sonnet 5, and the price difference between the two rows above understates the real difference by that much. Measure tokens per model, don't convert.

## Inputs we measure

Every row is measured per school and per run, and every one of these is already recorded in `reports/<run-id>.json` except the two LLM rows, which stay at zero until the fallback extractor exists.

| Symbol | Input | Unit | Where it comes from | Measured value (T16) |
| --- | --- | --- | --- | ---: |
| `S_att` | Schools attempted | count | run summary | `___` |
| `S_col` | Schools collected | count | run summary | `___` |
| `h` | Runtime per collected school | hours | `schools[].duration`, averaged | `___` |
| `H_wall` | Wall-clock runtime of the run | hours | `run.duration` | `___` |
| `k` | Hosts crawled at once | count | `settings.maxConcurrentHosts` | `___` |
| `r` | Requests per school | count | `schools[].requests.requests` | `___` |
| `b_in` | Bytes downloaded per school | GB (10⁹ B) | `schools[].requests.bodyBytes` | `___` |
| `b_out` | Bytes uploaded per school | GB | not instrumented yet — **estimate** it as `r` × ~1 kB of headers | `___` |
| `f` | Share of schools needing the LLM fallback | fraction | schools whose platform was resolved by the fallback ÷ `S_att` | `___` |
| `t_in` | LLM input tokens per fallback school | tokens | fallback extractor usage log | `___` |
| `t_out` | LLM output tokens per fallback school | tokens | fallback extractor usage log | `___` |
| `d` | Database size per collected school | GB | database file size ÷ `S_col` | `___` |
| `n` | Runs per month (refresh cadence) | count | a **policy choice**, not a measurement | `___` |

And the prices, all estimates, from the tables above:

| Symbol | Price | Baseline value |
| --- | --- | ---: |
| `P_vm` | VM $/hour | 0.0168 (`t4g.small`) |
| `P_in` | Inbound $/GB | 0.00 |
| `P_out` | Outbound $/GB | 0.09 (AWS list, above the free 100 GB) |
| `P_store` | Storage $/GB-month | 0.08 (EBS `gp3`) |
| `P_tin` | LLM input $/MTok | 1.00 (Haiku 4.5) |
| `P_tout` | LLM output $/MTok | 5.00 (Haiku 4.5) |

## The formulas

The one thing to get right: **the VM is rented by wall-clock hour for the whole fleet, not per school.** Schools on different hosts run at the same time, so `k` schools' worth of school-time costs one hour of VM time per hour. Dividing school-hours by `k` is what converts measured per-school runtime into billable VM hours, and it's why per-school compute cost falls as `k` rises.

```text
cost per school   = compute + transfer + llm
  compute         = (h ÷ k) × P_vm
  transfer        = b_in × P_in + b_out × P_out            [P_in = 0 on all three providers]
  llm             = f × (t_in × P_tin + t_out × P_tout) ÷ 1,000,000

cost per 1,000 schools (per run)   = 1,000 × cost per school
wall-clock hours per 1,000 schools = 1,000 × h ÷ k                              [estimate]

storage per month (1,000 schools)  = 1,000 × d × P_store
monthly cost (1,000 schools)       = n × cost per 1,000 + storage per month     [estimate]
```

Two cross-checks worth running against the same measured numbers, because they catch a wrong `k` or a wrong runtime:

```text
run cost (as billed)  = H_wall × P_vm
                      + S_col × transfer
                      + (total LLM input tokens × P_tin + total output tokens × P_tout) ÷ 1,000,000

politeness floor      = 1,000 × r × min-request-interval-seconds ÷ k ÷ 3,600     [hours for 1,000]
```

### Where this lives in code

The run report computes the same thing in `src/DueGooder.Cli/RunCostEstimate.cs`, with the same baseline prices (`t4g.small` at $0.0168/h, inbound at $0/GB, `gp3` at $0.08/GB-month, Haiku 4.5 at $1/$5 per MTok). That file is the authority for what a run reports; this document is the authority for where the prices came from and what the numbers do and don't claim. If one changes, change the other — T13 and T22 disagreeing in the submission would be worse than either being slightly stale.

Two inputs this document models that the code does not, both **deliberate** and worth a look when the fallback lands:

- **`b_out`** — the code's transfer line is inbound only, which is $0 and therefore correct today. Outbound is priced here because it's the line that would bite on a provider that bills it, and because it isn't instrumented yet.
- **`f`, the fallback share** — the code multiplies absolute measured token totals, which is right for reporting a run that happened. `f` is what makes the 1,000-school extrapolation meaningful before the fallback exists, and it's the input this model is most sensitive to.

`run cost (as billed)` uses measured wall clock, so it is the number to trust for a run that actually happened; `cost per school × S_col` should land close to it, and a gap means `k` wasn't the real level of parallelism (idle hosts at the end of a run, mostly). The `politeness floor` is the fastest 1,000 schools could go at the configured per-host delay even with infinitely fast responses — if the extrapolated wall clock is near the floor, runtime is delay-bound and a bigger VM buys nothing; if it's well above, time is going into transfer and parsing and a faster machine or more concurrency would help.

## Worked example

**Example only — every input below is made up.** Plausible, and in the neighborhood of `reports/run-20260912T033215Z.md`, but not measured. It exists to show the arithmetic and the shape of the answer, and it gets replaced wholesale after T16.

Example inputs: `h` = 0.30 h/school · `k` = 12 · `r` = 190 · `b_in` = 0.240 GB · `b_out` = 0.002 GB · `f` = 0.20 · `t_in` = 120,000 · `t_out` = 8,000 · `d` = 0.043 GB · `n` = 4 (weekly refresh), with Haiku 4.5 at $1/$5 per MTok on a `t4g.small`.

| Line | Arithmetic | Per school | Per 1,000 schools, per run |
| --- | --- | ---: | ---: |
| Compute | (0.30 ÷ 12) × $0.0168 | $0.00042 | $0.42 |
| Transfer in | 0.240 GB × $0 | $0.00000 | $0.00 |
| Transfer out | 0.002 GB × $0.09 | $0.00018 | $0.18 |
| LLM fallback | 0.20 × (120,000 × $1 + 8,000 × $5) ÷ 10⁶ | $0.03200 | $32.00 |
| **Total per run** | | **$0.0326** | **$32.60** |
| Storage, per month | 0.043 GB × $0.08 | $0.0034 | $3.44 |
| **Monthly, weekly refresh** | 4 runs + storage | $0.134 | **$133.84** |

Wall clock for 1,000 schools: 1,000 × 0.30 ÷ 12 = **25 hours** per run (estimate). The politeness floor at the 2-second per-host interval is 190 × 2 ÷ 12 ÷ 3,600 × 1,000 ≈ **8.8 hours**, so in this example runtime is not purely delay-bound — about two-thirds of it is transfer and parsing, and raising `k` or the VM size would cut it. Both numbers scale with `k`: at `k` = 48 the same run finishes in about 6 hours for the same total VM cost, provided one small VM can still keep 48 hosts busy (**unverified**).

What the example shows, and what we'd expect to survive contact with real numbers:

- **The LLM fallback is 98% of the cost.** Infrastructure for 1,000 schools is about **$0.60 per run**; the fallback at a 20% share is **$32**. Moving one school off the fallback and onto a real connector saves $0.16 per run — about 270× the entire infrastructure cost of collecting that school ($0.0006). That is the cost argument for the connector-first architecture, and it is why `f` is the number to drive down, ahead of any hosting decision.
- Two levers on the LLM line, from the pricing page: the **Batch API** halves it ($32.00 → $16.00 per 1,000), and **prompt caching** cuts the fixed instruction part of `t_in` to 10% of base input price. Both apply cleanly to a scheduled refresh.
- At `f` = 0.05, which is what a second and third connector should buy, the LLM line falls to $8.00 per 1,000 per run and the monthly total to about **$38**.
- Bandwidth never matters at this scale on these providers: 240 GB downloaded per 1,000 schools is free everywhere, and 2 GB of outbound sits inside AWS's free 100 GB.

## After the measured run (T16)

1. Read `h`, `H_wall`, `k`, `r`, `b_in`, `S_att`, `S_col` and the database size out of `reports/<run-id>.json` and fill in the `___` column above.
2. Set `f`, `t_in`, `t_out` from the fallback extractor's usage log. If the fallback still doesn't exist at submission time, say so plainly and report the LLM line as a **priced scenario** at a stated `f`, not as a measured cost — the way `reports/run-20260912T033215Z.md` reports 0 tokens today.
3. Redo the worked example with those numbers, drop the "example only" warning from it, and keep the estimate labels on every price and on both 1,000-school rows.
4. Re-check all six source links and update the check date. Note any price that moved.
5. Reconcile against the run report's Cost section and the prices in `src/DueGooder.Cli/RunCostEstimate.cs` so T13 and this document can't disagree.

## What this model leaves out

Named here so the write-up doesn't have to claim more than we measured:

- **That a small VM keeps up.** Runtime was measured on a developer Mac, not on a `t4g.small`. The work is I/O- and delay-bound, which is the reason to think a 2 vCPU box is enough, but it has not been measured on one. **Estimate.**
- **That schools are like the ones we measured.** The measured per-school numbers come from Banner 9 schools whose `robots.txt` permits crawling. A school on a platform that needs a headless browser costs far more per school than any line here, because Playwright is CPU- and memory-bound in a way that JSON fetching isn't. If the 1,000 are not mostly Banner-like, this model is optimistic.
- **Linear extrapolation.** 1,000 schools is the per-school cost times 1,000. Real fleets hit per-host rate limits, retry storms, Postgres instead of SQLite, and a monitoring bill. **Estimate.**
- **Human time.** Per-school setup is about six lines of YAML plus finding the base URL, all logged in [config/human-steps.md](../config/human-steps.md) and the run report. Not priced here, deliberately: at 1,000 schools it is plausibly the largest cost in the whole system, and pricing it would mean inventing an hourly rate. The right claim is the measured setup time per school, which T20 takes from the human-steps log.
- **Egress to consumers.** This model prices collecting data, not serving it. Anyone reading the database at scale changes the bandwidth line completely, and on AWS at $0.09/GB that line would dominate.
- **Development and one-off costs.** Hackathon build time, the overnight runs, and the tokens this project spent on Claude Code are not in here.
