---
title: "AI usage"
description: "What the AI-paired sessions of this project cost, by phase, work package, model, and effort, and the observed AIU for each size class."
type: status
audience: [maintainers, contributors, learners]
last-verified: 2026-11-04
owner: "@RostomOhannessian"
---

# AI usage

This page is generated from [ai-usage.yaml](ai-usage.yaml), which is the source of truth, by `governance usage render`. Do not edit it by hand. A session adds its own records with the `/ai-usage-recorder` skill, then runs the command and commits both files. CI fails when this page differs from the command's output. Last updated 2026-11-04.

The unit is the AI unit (AIU) that the Copilot session store reports for each model call. Derived AIU is the part that was computed from token counts and recorded prices instead, because only the cloud copy of the store held the calls, and it is a floor. [How to read the ledger, the attribution rules, and the estimate](../explanation/ai-usage-statistics.md) explain the columns. To estimate the work that remains, run `dotnet run --project tools/Governance.Auditor -- usage estimate`.

## Totals

| Measure | Value |
| --- | --- |
| Sessions | 14 |
| Slices (records) | 26 |
| Calls | 5,398 |
| AIU | 39,664 |
| Derived AIU | 12,021 (30.3%) |
| Model time | 17.7 hours |
| Period | 2026-08-22 to 2026-11-04 |
| Peak context | 760,572 tokens in one call (session 1667626f) |
| Compactions | 10 |

## By phase

Delivery is the work of a work package. Planning is the work on the plan itself, and coordination is a session that starts, monitors, or hands over other sessions.

| Phase | Name | Delivery AIU | Planning AIU | Coordination AIU | Fix AIU | Total AIU | Calls | Derived share |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | Foundation and early publication | 5,133 | 3,989 | 0 | 0 | 9,122 | 845 | 0.0% |
| 1 | API core, data layer, and engineering foundation | 26,948 | 406 | 1,619 | 1,569 | 30,543 | 4,553 | 39.4% |

- Phase 0 is recorded at phase level: Phase 0 was built in one session before work packages were tracked one by one, so its usage is recorded at phase level. WP0.1 to WP0.7 have no records of their own. Its pull requests changed 112 files, with 14,527 additions and 97 deletions.

## By work package

The completed work packages and what it cost to deliver each. Changed lines are the additions and deletions of its pull request. Exact means that one session held the work package alone, and windowed means that its window was derived from a session that held more. Derived is the share of its AIU that was computed from token counts and not read from the store.

| Work package | Size | Title | AIU | Calls | Model minutes | Peak context | Compactions | User turns | Changed lines | AIU per 1,000 lines | Confidence | Derived |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| WP1.0 | S | Risk spikes | 1,337 | 179 | 32 | 421,089 | 0 | 3 | 726 | 1,841 | windowed | 0.0% |
| WP1.1 | M | Solution and build foundation | 1,355 | 163 | 35 | 755,467 | 1 | 3 | 2,806 | 483 | windowed | 0.0% |
| WP1.2 | M | Governance core | 1,889 | 282 | 53 | 758,942 | 1 | 1 | 10,849 | 174 | exact | 0.0% |
| WP1.3 | M | Documentation toolchain | 1,934 | 295 | 51 | 760,572 | 1 | 1 | 7,222 | 268 | exact | 0.0% |
| WP1.4 | L | Developer environment | 3,271 | 553 | 90 | 721,597 | 1 | 1 | 8,441 | 388 | windowed | 0.0% |
| WP1.5 | M | Contract v1 design | 1,934 | 284 | 53 | 670,069 | 0 | 2 | 4,926 | 393 | windowed | 0.0% |
| WP1.6 | M | Domain model | 1,889 | 336 | 55 | 687,304 | 1 | 4 | 5,041 | 375 | windowed | 0.0% |
| WP1.7 | L | Application layer | 2,599 | 335 | 46 | 658,362 | 0 | 2 | 6,599 | 394 | exact | 100.0% |
| WP1.8 | L | Persistence and outbox | 2,870 | 474 | 75 | 701,959 | 1 | 1 | 12,356 | 232 | exact | 100.0% |
| WP1.9 | M | Caching | 1,509 | 254 | 39 | 708,030 | 1 | 1 | 5,768 | 262 | exact | 100.0% |
| WP1.10 | L | HTTP API | 3,652 | 565 | 86 | 709,100 | 2 | 2 | 11,970 | 305 | exact | 100.0% |
| WP1.14 | S | Session boundaries | 1,474 | 196 | 35 | 525,136 | 0 | 1 | 3,392 | 434 | exact | 0.0% |
| WP1.15 | M | AI usage statistics | 1,236 | 177 | 35 | 520,070 | 0 | 3 | 4,023 | 307 | exact | 0.0% |

## Observed AIU by size class

The size classes are the focused effort of plan section 5.7, and the weights are their midpoints in days: S 0.5, M 3, and L 6.5. `governance usage estimate` turns these numbers into a range for each work package that remains.

| Size | Completed | Minimum AIU | Median AIU | Maximum AIU | Median AIU per day of weight |
| --- | --- | --- | --- | --- | --- |
| S | 2 | 1,337 | 1,405 | 1,474 | 2,811 |
| M | 7 | 1,236 | 1,889 | 1,934 | 630 |
| L | 4 | 2,599 | 3,071 | 3,652 | 472 |

## By model and effort

Tokens are the sums over the calls. Fresh input is the input that was not served from the cache, and reasoning tokens are part of the output tokens. A sub-agent is a helper that the main agent started, often with a different model. Effort is unspecified for the calls that compact a conversation. A row with an unknown effort is derived: its AIU was computed from token counts and recorded prices, because the store that held the calls does not report the effort or the reasoning tokens.

| Model | Effort | Agent | Calls | AIU | Share | Fresh input | Cache read | Cache write | Output | Reasoning |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `claude-sonnet-5.5` | xhigh | main | 2,183 | 15,771 | 39.8% | 4,454 | 812,685,407 | 15,101,396 | 3,474,171 | 1,352,781 |
| `claude-sonnet-5.5` | unknown | main | 1,911 | 12,021 | 30.3% | 3,858 | 713,549,703 | 9,239,959 | 2,574,957 | unknown |
| `claude-sonnet-5.5` | max | main | 267 | 3,286 | 8.3% | 540 | 109,560,772 | 1,694,395 | 670,943 | 359,068 |
| `claude-sonnet-5` | max | sub-agent | 228 | 1,979 | 5.0% | 456 | 33,911,498 | 2,424,933 | 694,892 | 420,606 |
| `claude-opus-5.5` | max | main | 47 | 1,920 | 4.8% | 100 | 13,431,481 | 2,055,935 | 311,617 | 178,379 |
| `claude-sonnet-5.5` | xhigh | sub-agent | 425 | 1,776 | 4.5% | 868 | 75,540,018 | 1,865,329 | 553,819 | 321,169 |
| `gpt-5.4` | medium | sub-agent | 261 | 1,463 | 3.7% | 1,336,349 | 25,492,480 | 0 | 327,463 | 127,820 |
| `claude-sonnet-5.5` | medium | main | 30 | 999 | 2.5% | 84 | 10,239,327 | 3,544,771 | 10,569 | 214 |
| `claude-opus-5.5` | high | main | 1 | 249 | 0.6% | 4 | 0 | 483,866 | 3,568 | 69 |
| `claude-sonnet-5.5` | unspecified | main | 6 | 111 | 0.3% | 24 | 4,236,053 | 36,437 | 53,337 | 0 |
| `gpt-5.6-sol` | max | main | 39 | 90 | 0.2% | 117 | 1,895,008 | 178,240 | 45,586 | 20,939 |

## By session

A session is an opaque ID, shown by its first eight characters. A session that spans days, or that holds several work packages, appears once here and in several slices above.

| Session | Slices | Calls | AIU | Peak context | Compactions | First call | Last call |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `e3240907` | 4 | 874 | 10,010 | 650,786 | 1 | 2026-08-22 | 2026-10-12 |
| `c88a7f9b` | 3 | 348 | 2,731 | 755,467 | 1 | 2026-10-07 | 2026-10-09 |
| `5c881a32` | 1 | 282 | 1,889 | 758,942 | 1 | 2026-10-09 | 2026-10-09 |
| `1667626f` | 1 | 295 | 1,934 | 760,572 | 1 | 2026-10-09 | 2026-10-10 |
| `dcf73ed7` | 6 | 1,254 | 8,192 | 721,597 | 2 | 2026-10-10 | 2026-10-12 |
| `2072a7ae` | 1 | 196 | 1,474 | 525,136 | 0 | 2026-10-12 | 2026-10-12 |
| `e16afa95` | 2 | 177 | 1,236 | 520,070 | 0 | 2026-10-12 | 2026-10-12 |
| `74a18e87` | 1 | 61 | 178 | 200,489 | 0 | 2026-10-12 | 2026-10-12 |
| `0e6780b7` | 1 | 335 | 2,599 | 658,362 | 0 | 2026-11-01 | 2026-11-02 |
| `6f91000a` | 1 | 132 | 687 | 452,780 | 0 | 2026-11-02 | 2026-11-02 |
| `a41f0966` | 1 | 474 | 2,870 | 701,959 | 1 | 2026-11-02 | 2026-11-02 |
| `2fc37633` | 1 | 151 | 704 | 433,956 | 0 | 2026-11-03 | 2026-11-03 |
| `f784fcdc` | 1 | 254 | 1,509 | 708,030 | 1 | 2026-11-03 | 2026-11-03 |
| `78384335` | 2 | 565 | 3,652 | 709,100 | 2 | 2026-11-04 | 2026-11-04 |
