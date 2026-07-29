# Usage Dashboard

The dashboard window (popup settings → "Usage dashboard", or the tray menu) shows the estimated lifetime USD cost retained on this computer, estimated costs for today, the last 7 days, and the last 30 days, a daily cost chart, and a per-model breakdown. The lifetime card splits the estimate into Claude and Codex.

## Data sources

Costs cannot be derived from the utilization percentages the providers expose, so the dashboard reads the session logs both CLIs already write locally:

- Claude Code: `%USERPROFILE%\.claude\projects\**\*.jsonl` — assistant records carry `message.usage` token counts and the model name. Repeated emissions of the same message are deduplicated by message id and request id.
- Codex: `%USERPROFILE%\.codex\sessions\**\*.jsonl` — usage is derived from the deltas of consecutive cumulative `total_token_usage` values (summing `last_token_usage` over-counts), with the model tracked from the preceding `turn_context` record.

Only numeric usage values, timestamps, model names, and identifiers are extracted. Message content is never read into the application state and never persisted.

WSL-side logs are not scanned in this version.

## Incremental cache

Parsed results are cached at `%LOCALAPPDATA%\UsageBeacon\insights-cache.json`, keyed by file path, size, write time, and parser revision, so only new or changed logs are reparsed. Entries of deleted log files are retained on purpose: Claude Code prunes transcripts after roughly 30 days (`cleanupPeriodDays`), so the cache is the primary source for older days. Deleting the cache file forces a full rescan and loses history whose logs were already pruned.

### Retention

Detailed cached entries older than 180 days are moved into a path-independent archive. Each archived usage event retains its exact timestamp, service, model, five token buckets, and identity hash, but no message content. Repeated model names are stored once in a model table, and event fields use compact positional rows. This pricing-neutral format lets the lifetime Claude/Codex estimates be recalculated after a price-table update and lets future-dated records remain excluded. Identity hashes prevent a modified file or parser migration from counting the same usage twice. The per-file record is also kept after compaction, because removing it would make the next scan reparse a file that may be hundreds of megabytes.

The detailed window supports the 30-day cost chart and per-model table. Both detailed and archived events are priced for the "Recorded on this PC" lifetime card. The cache is rewritten only when a file or archive changed, and serialization streams directly to a temporary file before the atomic replacement.

The lifetime card is the history UsageBeacon can still observe, not an account-level total. Logs deleted before UsageBeacon scanned them, history lost before this format was introduced, cache deletion or corruption, and unreadable files can leave gaps. The earliest retained record shown in the card is therefore a lower bound, not a guarantee of continuous coverage. Schema-v1 token-only archives are reparsed once when their source logs still exist. Any legacy totals whose source logs are gone remain explicitly unpriced, and the card shows a `+` with an explanatory notice rather than inventing a Claude/Codex split.

## Cost estimation

Costs are **API-price equivalents**: tokens multiplied by published per-million-token API prices. Subscription plans (Claude Pro/Max, ChatGPT Plus/Pro) do not bill per token, so the numbers represent what the same usage would have cost on the API, not an invoice.

Vendor semantics differ and are normalized during parsing:

- Anthropic: `input_tokens`, cache writes (5m/1h), and cache reads are disjoint buckets, each billed at its own rate.
- OpenAI: `input_tokens` includes `cached_input_tokens` (billed as `(input - cached) + cached x cached rate`) and `output_tokens` already includes reasoning tokens.

The built-in price table is an embedded resource (`Resources/model-pricing.json`) with an "as of" date shown in the dashboard. Some values, notably for the newest models, are sourced from third-party price trackers rather than official price pages and may lag price changes. Rates can be a single timeless object or an effective-dated schedule. `claude-sonnet-5` therefore uses its introductory $2/$10 rate through 2026-08-31 and automatically uses the standard $3/$15 rate from 2026-09-01 UTC without repricing older usage. Models without a table entry are excluded from cost totals and listed in a notice. Adding a price later automatically reprices retained detailed and archived events.

`claude-opus-5` uses Anthropic's published standard price of $5 input and $25 output per million tokens. Anthropic documents this as unchanged from Opus 4.8, so the table also uses the published Opus 4.8 prompt-cache rates: $6.25 for 5-minute writes, $10 for 1-hour writes, and $0.50 for cache hits. See [What's new in Claude Opus 5](https://platform.claude.com/docs/en/about-claude/models/whats-new-opus-5) and [Claude pricing](https://platform.claude.com/docs/en/about-claude/pricing).

`gpt-5.2-codex` uses OpenAI's published API rates of $1.75 input, $0.175 cached input, and $14 output per million tokens. See [GPT-5.2-Codex model](https://developers.openai.com/api/docs/models/gpt-5.2-codex).

Known estimation gaps (both cause **under**-estimation and cannot be derived from the logs):

- OpenAI bills prompt-cache **writes** separately (1.25x input), but Codex rollouts do not record cache-write token counts.
- OpenAI long-context tiers (higher rates above the model's short-context threshold) are not applied because the logs do not mark which requests crossed the threshold.
- Anthropic records are unaffected: cache writes are logged explicitly, and the supported models price the 1M-token context window at standard rates.

### Overriding prices

Create `%LOCALAPPDATA%\UsageBeacon\model-pricing.json` to correct or extend prices without a new build. Entries replace the complete built-in schedule for that model; unknown names are added. A legacy object remains a timeless rate. Use an array with `effectiveFrom` dates for historical rates. Dates without an offset start at 00:00 UTC. All values are USD per million tokens:

```json
{
    "asOf": "2026-08-01",
    "models": {
        "claude-sonnet-5": [
            { "effectiveFrom": "0001-01-01", "input": 2, "cachedInput": 0.2, "cacheWrite5m": 2.5, "cacheWrite1h": 4, "output": 10 },
            { "effectiveFrom": "2026-09-01", "input": 3, "cachedInput": 0.3, "cacheWrite5m": 3.75, "cacheWrite1h": 6, "output": 15 }
        ]
    }
}
```

Schedule entries may be in any order, but duplicate effective dates invalidate the override. Model names match exactly first, then by the longest table key that is a prefix of the model name at a `-` boundary (`gpt-5` matches `gpt-5-codex` but never `gpt-5.5`).
