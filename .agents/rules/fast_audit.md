# Fast Audit & High-Speed Execution Guidelines

## 1. Fast Code Search & Audit Strategy
- **Do NOT read large files line-by-line:** For files over 300 lines (especially large controllers like `BusBookingsController.cs`), never read the entire file or use wide range views.
- **Pinpoint with `grep_search` first:** Always search for specific route attributes (e.g. `[HttpPost("cancel")]`), action names, or method invocations (e.g. `SendBusCancellationAsync`).
- **Targeted Slices Only:** After locating the line number via `grep_search`, use `view_file` strictly on the targeted function or block (30–60 lines max).
- **Deliver Direct Findings:** State the exact file, line number, call hierarchy, and root cause immediately without filler or re-summarizing untouched code.

## 2. Parallel Task Splitting & Subagents
- When auditing or modifying multiple independent flows (e.g. public controller, admin controller, email service, pdf generator), split the tasks and run them concurrently using parallel subagents.
- Synthesize subagent findings concisely and directly.

## 3. Fast Build & Process Hygiene
- If a build fails due to a locked `.exe` (e.g. `MSB3026 / MSB3027`), immediately terminate the orphaned background process (`Stop-Process -Name ... -Force`) instead of waiting through 10 retry timeouts.
- Avoid polling loops; allow asynchronous tasks and reactive notifications to handle progress.
