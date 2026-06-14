# Model Ranking for Daily Driver

Tracked from code review quality, signal-to-noise ratio, cost, and output structure.

## Methodology

Models are evaluated by running them as code-review prep agents on the same HVO.WebSite repo (full-codebase or per-architecture-group split). Each review produces candidate findings that are independently validated. Key metrics: accepted candidate rate, false positive rate, output structure quality, depth of analysis, and cost.

---

## Current Ranking

| Rank | Model | Source | Cost | Findings | Accept Rate | Signal/Noise | Strengths | Weaknesses | Tested |
|---|---|---|---|---|---|---|---|---|---|
| 1 | **GPT 5.5** | Zen | Paid | 31 findings, 24 files | Very high | Excellent | Most targeted, fewest false positives, best final recommendations | Costs money | Yes |
| 2 | **DeepSeek V4 Pro** | Go | $1.74/M (Go sub) | 12 findings, 0 false positives | 100% | Excellent | Precise line numbers, contextual evidence, call flow, 0 false positives | Narrower per-session context (Web/API only) | Yes |
| 3 | **DeepSeek V4 Flash** | Zen Free / Go | Free / $0.14/M | 10 high-confidence | ~70% | High | Best structured output, clearest file/line refs, good dedup | Some false positives (Docker, dev-mode) | Yes |
| 4 | **Qwen3.7 Plus** | Go | $0.40/M (Go sub) | 9 findings, 0 false positives | 100% | High | Good gateway/worker lifecycle analysis, correct findings | More verbose than V4 Pro | Yes |
| 5 | **MiniMax M3** | Go | $0.30/M (Go sub) | 10+ findings, 0 false positives | 100% | High | Good BLE/BT-specific analysis, caught ARM memory concerns | Less broadly useful; focused on hardware | Yes |
| 6 | **Qwen3 Coder Next (local)** | Ollama | Free (local) | 62 findings, 9 files | Moderate | Medium | Broadest coverage, 256K context, caught things others missed | Noisiest, highest false-positive rate, slower | Yes |
| 7 | **Nemotron 3 Ultra Free** | Zen Free | Free | 10 candidates, 3 high-confidence | ~60% | Medium | Deep protocol analysis, good on async/concurrency patterns | More verbose, some low-confidence noise | Yes |
| 8 | **MiMo V2.5 Free** | Zen Free | Free | 5 candidates, 1 high-confidence | ~80% | High | Excellent at pattern comparison (JkBms vs Victron), concise | Limited breadth, fewer total findings | Yes |
| — | **Kimi K2.7 Code** | Go | $0.95/M (Go sub) | — | — | — | Not yet tested | Not yet tested | No |
| — | **MiMo V2.5 Pro** | Go | $1.74/M (Go sub) | — | — | — | Not yet tested | Not yet tested | No |
| — | **Qwen3.7 Max** | Go | $2.50/M (Go sub) | — | — | — | Not yet tested | Not yet tested | No |
| — | **GLM-5.1** | Go | $1.40/M (Go sub) | — | — | — | Not yet tested | Not yet tested | No |

---

## Tested Models — Detail

### 1. GPT 5.5 (Zen)
- **Model ID:** `opencode/gpt-5.5`
- **Test date:** Prior session (GPT55 review)
- **Output:** 24 files, 31 findings
- **Accept rate:** Very high
- **False positives:** Very few
- **Structure:** Professional-grade, targeted, clear reasoning
- **Cost:** Pay-as-you-go Zen pricing
- **Verdict:** Best quality but most expensive. Use for critical reviews/final validation.

### 2. DeepSeek V4 Pro (Go)
- **Model ID:** `opencode-go/deepseek-v4-pro`
- **Test date:** 2026-06-13
- **Scope tested:** Web/API architecture group (HVO.WebSite.v9, controllers, services, middleware, auth, EF Core, tests)
- **Output:** 12 candidates, 0 false positives
- **Accept rate:** 100% (all 12 findings accepted after GPT validation)
- **Strengths:** Best Go model tested. Precise line-number references, contextual evidence snippets, call-flow mapping, clear disprove conditions. Excellent at EF Core async pattern detection, DateTimeKind analysis, index usage, telemetry gaps.
- **Weaknesses:** 0 false positives indicates slightly conservative — may miss edge cases. Narrower per-session context limited to Web/API group.
- **Cost:** $1.74/1M input, $3.48/1M output (Go subscription, 3,450 requests/5hr)
- **Verdict:** Best overall quality among Go models. Recommended as daily driver for critical reviews where quality matters more than cost.

### 3. DeepSeek V4 Flash (Zen Free / Go)
- **Model ID:** `opencode/deepseek-v4-flash-free` (Zen) / `opencode/deepseek-v4-flash` (Go)
- **Test date:** 2026-06-13
- **Scope tested:** Web/API architecture group (HVO.WebSite.v9, auth, telemetry, tests)
- **Output:** 10 candidates, 4 high-confidence
- **Accept rate:** ~70% (7 of 10 accepted, 3 rejected after GPT validation)
- **False positives:** Docker --disable-parallel, dev-mode exception exposure, API key brute-force timing
- **Structure:** Excellent — best organized output of all free models. Clear file/line references, call flow, risk, disprove conditions.
- **Cost:** Free (Zen) / $0.14/1M input, $0.28/1M output (Go)
- **Verdict:** Best free option. Good daily driver for routine work.

### 4. Qwen3.7 Plus (Go)
- **Model ID:** `opencode-go/qwen3.7-plus`
- **Test date:** 2026-06-13
- **Scope tested:** Gateway architecture group (SolarAssistant MQTT/TCP, TplinkKasa poll loops, file I/O, telemetry)
- **Output:** 9 candidates, 0 false positives
- **Accept rate:** 100% (all 9 findings accepted)
- **Strengths:** Excellent at worker lifecycle, MQTT/TCP timeout patterns, sync-over-async detection, test-coverage gaps. Good multi-level analysis (TCP → worker → health check → OTel).
- **Weaknesses:** Slightly more verbose output than DeepSeek V4 Pro. Missed the non-atomic registry write (CAND-05 was found by V4 Pro instead).
- **Cost:** $0.40/1M input, $1.60/1M output (Go subscription, 4,300 requests/5hr)
- **Verdict:** Good value at $0.40/M. Best for gateway/network-level analysis.

### 5. MiniMax M3 (Go)
- **Model ID:** `opencode-go/minimax-m3`
- **Test date:** 2026-06-13
- **Scope tested:** Hardware/BLE group (JkBms Bluetooth, Davis TCP, VictronSmartShunt BLE)
- **Output:** 10+ findings, 0 false positives
- **Accept rate:** 100% (all findings accepted)
- **Strengths:** Found ARM memory model concerns (non-volatile bools, lock-free shared state), BLE GATT timeout analysis, duplicate session code, test-coverage gaps. Good at protocol-level analysis.
- **Weaknesses:** Less broadly useful — hardware-specific findings don't translate to general web/API work. Output very verbose (truncated at 75K+ chars).
- **Cost:** $0.30/1M input, $1.20/1M output (Go subscription, 3,200 requests/5hr)
- **Verdict:** Best for hardware/BLE-specific reviews. Not a general-purpose daily driver.

### 6. DeepSeek V4 Flash (Zen Free / Go)
- **Model ID:** `opencode/deepseek-v4-flash-free` (Zen) / `opencode-go/deepseek-v4-flash` (Go)
- **Test date:** 2026-06-13
- **Scope tested:** Web/API architecture group (HVO.WebSite.v9, auth, telemetry, tests)
- **Output:** 10 candidates, 4 high-confidence
- **Accept rate:** ~70% (7 of 10 accepted, 3 rejected after GPT validation)
- **False positives:** Docker --disable-parallel, dev-mode exception exposure, API key brute-force timing
- **Structure:** Excellent — best organized output of all free models. Clear file/line references, call flow, risk, disprove conditions.
- **Cost:** Free (Zen) / $0.14/1M input, $0.28/1M output (Go)
- **Verdict:** Best current free option. Recommended as daily driver for most work.

### 7. Qwen3 Coder Next (local Ollama)
- **Model ID:** `ollama/qwen3-coder-next-q8-256k:latest`
- **Test date:** Prior session (Qwen pre-review)
- **Scope tested:** Full repo
- **Output:** 9 files, 62 findings
- **Accept rate:** Moderate
- **False positives:** Higher than DeepSeek V4 Flash — many low-confidence findings rejected
- **Strengths:** 256K context allows full-repo ingestion, caught edge cases (outbox non-atomic updates, BLE adapter coordination)
- **Weaknesses:** Noisiest model tested, verbose output, slower on local hardware
- **Cost:** Free (local)
- **Verdict:** Good fallback for offline or large-context work. Not ideal as primary daily driver.

### 8. Nemotron 3 Ultra Free (Zen)
- **Model ID:** `opencode/nemotron-3-ultra-free`
- **Test date:** 2026-06-13
- **Scope tested:** Gateway architecture group (SolarAssistant, TplinkKasa)
- **Output:** 10 candidates, 3 high-confidence
- **Accept rate:** ~60%
- **Strengths:** Excellent protocol-level analysis (MQTT, TCP), deep async/concurrency inspection, good deduplication
- **Weaknesses:** More verbose, some low-confidence noise, output less structured than DeepSeek V4 Flash
- **Cost:** Free
- **Verdict:** Good for specialized protocol/hardware review. Not ideal for general-purpose daily use.

### 9. MiMo V2.5 Free (Zen)
- **Model ID:** `opencode/mimo-v2.5-free`
- **Test date:** 2026-06-13
- **Scope tested:** Hardware/BLE group (JkBms, Davis, VictronSmartShunt)
- **Output:** 5 candidates, 1 high-confidence
- **Accept rate:** ~80%
- **Strengths:** Excellent pattern comparison (caught JkBms-vs-Victron BLE coordinator gap), concise output, focused findings
- **Weaknesses:** Limited breadth, fewer candidates than other models, some very low-confidence findings
- **Cost:** Free
- **Verdict:** Good for targeted comparison/pattern analysis. Not comprehensive enough for daily driver.

---

## Untested Models — Remaining Priority Queue

These Go models are still available but untested:

| Priority | Model | Why Test | Expected Value |
|---|---|---|---|
| 1 | **Kimi K2.7 Code** (Go) | $0.95/M — code-focused model; compare vs DeepSeek V4 Pro | Potentially better for code-specific reviews |
| 2 | **MiMo V2.5 Pro** (Go) | $1.74/M — upgrade from V2.5 Free; 3250 req/5hr | May outperform MiniMax M3 on hardware reviews |
| 3 | **Qwen3.7 Max** (Go) | $2.50/M — highest quality tier; only 950 req/5hr | Potentially best overall, but expensive |
| 4 | **GLM-5.1** (Go) | $1.40/M — 880 req/5hr | Low priority; limited throughput |

---

## Daily Driver Recommendation (Current)

**Tier 1 (Best quality): DeepSeek V4 Pro** ($1.74/M input, Go) — 100% accept rate, 0 false positives, precise evidence packs. Best for important reviews where quality matters.

**Tier 2 (Best value): DeepSeek V4 Flash** (Free Zen or $0.14/M Go) — Best free option. Good daily driver for routine work with occasional false positives.

**Tier 3 (Budget hardware): Qwen3.7 Plus** ($0.40/M, Go) — Best for gateway/network-level analysis. Solid value.

**Reserve for final validation: GPT 5.5** (Zen) — Highest quality but highest cost. Use only for critical final validation after pre-review.

---

## Notes

- Last updated: 2026-06-13
- All tests performed on the same HVO.WebSite repository for comparability
- Accept rate = findings accepted after independent GPT validation
- Models are tested as read-only prep agents; final quality includes output structure, not just findings count
- DeepSeek V4 Pro vs GPT 5.5 comparison report: `code-review-gpt55/V4Pro-vs-GPT55-Comparison.md`
