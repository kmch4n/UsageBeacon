# Usage Dashboard

The dashboard window (popup settings → "Usage dashboard", or the tray menu) shows token usage and estimated USD costs for today, the last 7 days, and the last 30 days, a daily cost chart, and a per-model breakdown.

## Data sources

Costs cannot be derived from the utilization percentages the providers expose, so the dashboard reads the session logs both CLIs already write locally:

- Claude Code: `%USERPROFILE%\.claude\projects\**\*.jsonl` — assistant records carry `message.usage` token counts and the model name. Repeated emissions of the same message are deduplicated by message id and request id.
- Codex: `%USERPROFILE%\.codex\sessions\**\*.jsonl` — usage is derived from the deltas of consecutive cumulative `total_token_usage` values (summing `last_token_usage` over-counts), with the model tracked from the preceding `turn_context` record.

Only numeric usage values, timestamps, model names, and identifiers are extracted. Message content is never read into the application state and never persisted.

WSL-side logs are not scanned in this version.

## Incremental cache

Parsed results are cached at `%LOCALAPPDATA%\UsageBeacon\insights-cache.json`, keyed by file path, size, and write time, so only new or changed logs are reparsed. Entries of deleted log files are retained on purpose: Claude Code prunes transcripts after roughly 30 days (`cleanupPeriodDays`), so the cache is the primary source for older days. Deleting the cache file forces a full rescan and loses days whose logs were already pruned.

### Retention

Cached entries older than 180 days are dropped at the end of each scan so the cache reaches a bounded steady state. The per-file record is kept even when all of its entries are dropped, because removing it would make the next scan reparse a file that may be hundreds of megabytes and then discard the whole result.

The retention window is far outside the 30 days the dashboard displays, so pruning never changes a visible number. If a log file that was already pruned from the cache is later modified, it is reparsed in full and its still-recent entries return normally; identity deduplication makes that safe.

## Cost estimation

Costs are **API-price equivalents**: tokens multiplied by published per-million-token API prices. Subscription plans (Claude Pro/Max, ChatGPT Plus/Pro) do not bill per token, so the numbers represent what the same usage would have cost on the API, not an invoice.

Vendor semantics differ and are normalized during parsing:

- Anthropic: `input_tokens`, cache writes (5m/1h), and cache reads are disjoint buckets, each billed at its own rate.
- OpenAI: `input_tokens` includes `cached_input_tokens` (billed as `(input - cached) + cached x cached rate`) and `output_tokens` already includes reasoning tokens.

The built-in price table is an embedded resource (`Resources/model-pricing.json`) with an "as of" date shown in the dashboard. Some values, notably for the newest models, are sourced from third-party price trackers rather than official price pages and may lag price changes. `claude-sonnet-5` uses the official introductory price ($2/$10) that applies until 2026-08-31 and must be raised to the standard $3/$15 afterwards. Models without a table entry are excluded from cost totals and listed in a notice; their token counts are still shown.

Known estimation gaps (both cause **under**-estimation and cannot be derived from the logs):

- OpenAI bills prompt-cache **writes** separately (1.25x input), but Codex rollouts do not record cache-write token counts.
- OpenAI long-context tiers (higher rates above the model's short-context threshold) are not applied because the logs do not mark which requests crossed the threshold.
- Anthropic records are unaffected: cache writes are logged explicitly, and the supported models price the 1M-token context window at standard rates.

### Overriding prices

Create `%LOCALAPPDATA%\UsageBeacon\model-pricing.json` to correct or extend prices without a new build. Entries replace built-in models by name; unknown names are added. All values are USD per million tokens:

```json
{
    "asOf": "2026-08-01",
    "models": {
        "claude-sonnet-5": { "input": 2, "cachedInput": 0.2, "cacheWrite5m": 2.5, "cacheWrite1h": 4, "output": 10 }
    }
}
```

Model names match exactly first, then by the longest table key that is a prefix of the model name at a `-` boundary (`gpt-5` matches `gpt-5-codex` but never `gpt-5.5`).
