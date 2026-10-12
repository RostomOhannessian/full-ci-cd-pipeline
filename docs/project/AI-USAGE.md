---
title: "AI usage"
description: "What the AI-paired sessions of this project cost, by phase, work package, model, and effort, and the observed AIU for each size class."
type: status
audience: [maintainers, contributors, learners]
last-verified: 2026-10-12
owner: "@RostomOhannessian"
---

# AI usage

This page is generated from [ai-usage.yaml](ai-usage.yaml), which is the source of truth, by `governance usage render`. Do not edit it by hand. A session adds its own records with the `/ai-usage-recorder` skill, then runs the command and commits both files. CI fails when this page differs from the command's output. Last updated 2026-10-12.

The unit is the AI unit (AIU) that the Copilot session store reports for each model call. [How to read the ledger, the attribution rules, and the estimate](../explanation/ai-usage-statistics.md) explain the columns. To estimate the work that remains, run `dotnet run --project tools/Governance.Auditor -- usage estimate`.

## Totals

| Measure | Value |
| --- | --- |
| Sessions | 7 |
| Slices (records) | 16 |
| Calls | 3,389 |
| AIU | 26,793 |
| Model time | 12.7 hours |
| Period | 2026-08-22 to 2026-10-12 |
| Peak context | 760,572 tokens in one call (session 1667626f) |
| Compactions | 6 |

## By phase

Delivery is the work of a work package. Planning is the work on the plan itself, and coordination is a session that starts, monitors, or hands over other sessions.

| Phase | Name | Delivery AIU | Planning AIU | Coordination AIU | Fix AIU | Total AIU | Calls |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | Foundation and early publication | 5,133 | 3,989 | 0 | 0 | 9,122 | 845 |
| 1 | API core, data layer, and engineering foundation | 16,114 | 406 | 1,151 | 0 | 17,671 | 2,544 |

- Phase 0 is recorded at phase level: Phase 0 was built in one session before work packages were tracked one by one, so its usage is recorded at phase level. WP0.1 to WP0.7 have no records of their own. Its pull requests changed 112 files, with 14,527 additions and 97 deletions.

## By work package

The completed work packages and what it cost to deliver each. Changed lines are the additions and deletions of its pull request. Exact means that one session held the work package alone, and windowed means that its window was derived from a session that held more.

| Work package | Size | Title | AIU | Calls | Model minutes | Peak context | Compactions | User turns | Changed lines | AIU per 1,000 lines | Confidence |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| WP1.0 | S | Risk spikes | 1,337 | 179 | 32 | 421,089 | 0 | 3 | 726 | 1,841 | windowed |
| WP1.1 | M | Solution and build foundation | 1,355 | 163 | 35 | 755,467 | 1 | 3 | 2,806 | 483 | windowed |
| WP1.2 | M | Governance core | 1,889 | 282 | 53 | 758,942 | 1 | 1 | 10,849 | 174 | exact |
| WP1.3 | M | Documentation toolchain | 1,934 | 295 | 51 | 760,572 | 1 | 1 | 7,222 | 268 | exact |
| WP1.4 | L | Developer environment | 3,271 | 553 | 90 | 721,597 | 1 | 1 | 8,441 | 388 | windowed |
| WP1.5 | M | Contract v1 design | 1,934 | 284 | 53 | 670,069 | 0 | 2 | 4,926 | 393 | windowed |
| WP1.6 | M | Domain model | 1,889 | 336 | 55 | 687,304 | 1 | 4 | 5,041 | 375 | windowed |
| WP1.14 | S | Session boundaries | 1,474 | 196 | 35 | 525,136 | 0 | 1 | 3,392 | 434 | exact |
| WP1.15 | M | AI usage statistics | 1,032 | 161 | 33 | 511,568 | 0 | 1 | n/a | n/a | exact |

## Observed AIU by size class

The size classes are the focused effort of plan section 5.7, and the weights are their midpoints in days: S 0.5, M 3, and L 6.5. `governance usage estimate` turns these numbers into a range for each work package that remains.

| Size | Completed | Minimum AIU | Median AIU | Maximum AIU | Median AIU per day of weight |
| --- | --- | --- | --- | --- | --- |
| S | 2 | 1,337 | 1,405 | 1,474 | 2,811 |
| M | 6 | 1,032 | 1,889 | 1,934 | 630 |
| L | 1 | 3,271 | 3,271 | 3,271 | 503 |

## By model and effort

Tokens are the sums over the calls. Fresh input is the input that was not served from the cache, and reasoning tokens are part of the output tokens. A sub-agent is a helper that the main agent started, often with a different model. Effort is unspecified for the calls that compact a conversation.

| Model | Effort | Agent | Calls | AIU | Share | Fresh input | Cache read | Cache write | Output | Reasoning |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `claude-sonnet-5.5` | xhigh | main | 2,085 | 14,921 | 55.7% | 4,240 | 784,702,592 | 13,200,396 | 3,379,359 | 1,310,324 |
| `claude-sonnet-5.5` | max | main | 267 | 3,286 | 12.3% | 540 | 109,560,772 | 1,694,395 | 670,943 | 359,068 |
| `claude-sonnet-5` | max | sub-agent | 228 | 1,979 | 7.4% | 456 | 33,911,498 | 2,424,933 | 694,892 | 420,606 |
| `claude-opus-5.5` | max | main | 47 | 1,920 | 7.2% | 100 | 13,431,481 | 2,055,935 | 311,617 | 178,379 |
| `claude-sonnet-5.5` | xhigh | sub-agent | 425 | 1,776 | 6.6% | 868 | 75,540,018 | 1,865,329 | 553,819 | 321,169 |
| `gpt-5.4` | medium | sub-agent | 261 | 1,463 | 5.5% | 1,336,349 | 25,492,480 | 0 | 327,463 | 127,820 |
| `claude-sonnet-5.5` | medium | main | 30 | 999 | 3.7% | 84 | 10,239,327 | 3,544,771 | 10,569 | 214 |
| `claude-opus-5.5` | high | main | 1 | 249 | 0.9% | 4 | 0 | 483,866 | 3,568 | 69 |
| `claude-sonnet-5.5` | unspecified | main | 6 | 111 | 0.4% | 24 | 4,236,053 | 36,437 | 53,337 | 0 |
| `gpt-5.6-sol` | max | main | 39 | 90 | 0.3% | 117 | 1,895,008 | 178,240 | 45,586 | 20,939 |

## By session

A session is an opaque ID, shown by its first eight characters. A session that spans days, or that holds several work packages, appears once here and in several slices above.

| Session | Slices | Calls | AIU | Peak context | Compactions | First call | Last call |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `e3240907` | 4 | 874 | 10,010 | 650,786 | 1 | 2026-08-22 | 2026-10-12 |
| `c88a7f9b` | 3 | 348 | 2,731 | 755,467 | 1 | 2026-10-07 | 2026-10-09 |
| `5c881a32` | 1 | 282 | 1,889 | 758,942 | 1 | 2026-10-09 | 2026-10-09 |
| `1667626f` | 1 | 295 | 1,934 | 760,572 | 1 | 2026-10-09 | 2026-10-10 |
| `dcf73ed7` | 5 | 1,233 | 7,724 | 721,597 | 2 | 2026-10-10 | 2026-10-12 |
| `2072a7ae` | 1 | 196 | 1,474 | 525,136 | 0 | 2026-10-12 | 2026-10-12 |
| `e16afa95` | 1 | 161 | 1,032 | 511,568 | 0 | 2026-10-12 | 2026-10-12 |
