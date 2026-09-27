# Open Bug Fixes Implementation Plan

**Goal:** Resolve open behavior bugs #20, #21, #22, and #19 while preserving retained usage and accurate dashboard estimates.

**Scope:** Existing cache loading, dashboard empty-state decision, pricing override validation, and popup settings synchronization. Feature requests remain outside this plan.

## 1. #20: Recover from wrong-typed cache JSON

- Add regression cases for array, null, string, and wrong-typed `schemaVersion` roots in `UsageLogCacheTests`.
- Verify each case fails against the current loader, then validate element kinds before typed JSON operations in `UsageLogCache.Load`.
- Verify available synthetic logs can be scanned after fallback; retain v1 migration and v2 loading coverage.

## 2. #21: Display retained history without source directories

- Add a view-model test with retained recent and archived data, then remove both source directories.
- Add a testable window loading decision covering both retained data and a fresh installation.
- Load cached data before choosing the no-data state; keep the existing local-coverage disclosure.

## 3. #22: Reject incomplete price overrides

- Add regression cases for empty and partial timeless and dated rates, plus explicit zero prices.
- Require every price field in `ModelPricingCatalog.TryParsePricing` before deserialization.
- Confirm invalid overrides leave built-in prices and historical estimates intact.

## 4. #19: Restore accepted picker selections after failed saves

- Add STA UI coverage for all four pickers, save failure, and a later successful retry.
- Resynchronize selections from the view model with selection handlers suppressed during synchronization.
- Confirm the error stays visible after failure and clears after successful persistence.

## Validation and handoff

- Run targeted tests after each fix, then full Debug and Release tests and builds.
- Request independent review of the resulting changes and address verified findings.
- Do not close the four bug Issues until their fixes are released or otherwise available to users.
