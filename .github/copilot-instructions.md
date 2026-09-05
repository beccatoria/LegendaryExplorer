# Copilot Instructions

## Project Guidelines
- Keep implementation progress messages concise and avoid rambling; proceed directly with requested code changes.
- Use simple, non-technical language to ensure clarity, as the user is not a confident coder.
- Do not replace or regress the user's customized Level Editor workflow/features while implementing Interp preview milestones; preserve their existing Level Editor behavior and UI.
- Execute steps immediately after stating that a step will start; avoid announcing starts without performing the step.
- Perform all work on the user's fork/branch (becca-LEX) and not on scott-LEX; use scott-LEX only as an optional reference source without aiming to port all features.
- Ensure conversation owner binding resolves from StartConversation Kismet Owner variable links (tag-based or direct object link), not by literal 'owner' actor lookup.
- Use strict conversation owner and speaker conventions: Player is always labeled 'player' (not 'shepard'), owner tags like 'hench_mystic' are canonical and should not be alias-expanded; focus on true source of misses rather than alias fallbacks.
- For Interp Preview lifecycle work, prefer complete root-cause resolutions and explicit ownership/state models over minimal mitigations or localized workarounds. The user is the human tester; mark automated/technical implementation separately, and do not mark manual validation complete until the user reports the result.
- For Interp Preview reviews, keep scope practical and tied to the current milestone's exit criteria; address root-cause blockers but defer later-stage concerns to avoid implementation-review cycles that impede progress.
- Clearly state when planning is complete and explicitly instruct when to switch agents (e.g., Sol planning -> Codex implementation) to avoid ambiguity.
- When build errors involve AFC/DLC-specific references, confirm whether issues are true compile/runtime blockers before broad fixes, as some code paths may reference DLC content not installed locally and may never execute.