<!-- markdownlint-disable-file MD041 -- copied into the run report under its own "Human steps" heading -->
Every step a person took, so per-school setup cost is visible. Claude Code (AI) did the searching and screening at a person's request; that still counts as a human step because a person started and reviewed it.

| When (UTC) | Step | Schools | Kind |
| --- | --- | --- | --- |
| 2026-09-11 | Found Banner 9 hosts with web searches (`"StudentRegistrationSsb" site:edu` and similar) and confirmed each one by loading its public `classSearch/getTerms` JSON | 18 | setup: discovery |
| 2026-09-11 | Wrote one `config/schools.yaml` entry per school: id, name, homepage, IANA timezone, platform, `base_url` (about 6 lines each, no code) | 41 | setup: config |
| 2026-09-11 | Read the multi-campus `mep_code` from the browser URL after picking a campus (`1UIUC`, `OSU`) | uiuc, okstate | setup: config |
| 2026-09-12 | Grew the list with more web searches and hosts sent by a teammate; screened each host with the CLI (`--term none`, then `--max-terms 1`) and kept only hosts that collected a live term | 23 added, 12 rejected | setup: discovery |
| 2026-09-12 | Put a non-default port into `base_url` | ccsf | setup: config |
| 2026-09-12 03:32 | Chose the overnight limits (`--max-terms 24 --max-hosts 12`) and started the run by hand with `caffeinate -i` | all | run |
| 2026-09-12 | Checked Montgomery's Spring 2028 in the public class search in a browser: 3,292 classes, the same count the run stored for term 202830; the page's row id `202830.30184` matches the stored term code and the CRN of ACCT 221 section 400 | montgomery | review: spot-check |
| 2026-09-12 | Read the run's failures (grep + jq over the log and JSON) and changed the Banner 9 mapper once for every school; no school-specific code or config was added | all | review |
| 2026-09-12 | Removed `platform` and `base_url` from every school so discovery finds them from the homepage, then put `base_url` back for the 10 collectable Banner 9 schools the S6 discovery run couldn't find, each with its reason in `config/schools.yaml` | 10 of 41 Banner 9 schools | setup: config |
| 2026-09-12 15:40 | Deleted the local DB and started the measured run by hand (`--max-terms 24 --max-hosts 12`, same limits as the overnight run) | all | run |
| 2026-09-12 17:09 | Started the refresh run by hand against the measured run's database, same limits, to prove zero duplicates | all | run |

Not done by a person: nothing was retried, skipped or edited by hand during the run. Schools whose robots.txt refuses us, or whose class search needs a sign-in, stay failed with that reason; nobody worked around them.
