# Daily usage comparison and GPT-6 pricing plan

## Goal

Make high-usage days obvious by showing daily estimated cost and token volume together, and price GPT-6 Astra and Sol from published API rates. Replace ambiguous `+` suffixes with explicit coverage text.

## Design

Use two vertically aligned, synchronized 7/30-day charts with numerical scales and shared date selection. The upper plot shows USD-derived cost split by Claude and Codex; the lower shows input and output tokens. Show the highest-cost and highest-token days with their values. Selected-day details show cost, input, and output together. Keep the model table below as secondary information. Show a localized warning beside the charts when some tokens have no known price; still include those tokens in token volume.

Keep the lifetime, today, 7-day, and 30-day summary cards above both charts. Derive rounded chart ticks from the maximum in the displayed range and scale bars against the rounded ceiling. Provide a compact daily numeric grid below the plots so every day's estimated cost and token count is readable without hover. Selecting a numeric day uses the same shared detail panel as selecting its bars.

## Pricing

Add the official Standard, short-context API rates per million tokens as of 2026-09-25: `gpt-6-astra` input 10, cached input 1, output 50; `gpt-6-sol` input 2, cached input 0.2, output 10. Codex logs do not expose cache writes or the 272K context pricing tier, so these remain API-equivalent approximations rather than billed amounts. Source: https://developers.openai.com/api/docs/pricing and the model pages. Preserve unknown-model exclusion when a rate cannot be verified.

## Steps

1. Add failing tests for the embedded GPT-6 rates and their effect on cost aggregation; update the pricing table and documentation; rerun.
2. Add focused tests for daily chart preparation: cost/token scales, zero days, peak selection, and incomplete pricing. Implement a presentation helper used by the WPF view.
3. Replace the single chart with synchronized cost and token plots, visible scale labels, peak summaries, and a shared selected-day detail. Move the model breakdown below this analysis area.
4. Remove all ambiguous `+` cost suffixes and provide localized coverage messages close to the affected numbers.
5. Run Debug and Release test suites, check the diff, and visually inspect the app where possible. Preserve unrelated working-tree changes; do not stage, commit, push, or deploy.
