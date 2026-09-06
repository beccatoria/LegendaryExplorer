# Merge Decisions

This file records intentional merge-resolution choices so future upstream syncs can follow the same decisions.

## FFDec / JPEXS installation detection

- Date: 2026-09-06
- Scope: `LegendaryExplorer/UserControls/ExportLoaderControls/JPEXSWFExportLoader.xaml.cs`
- Upstream reference commit: `54e4e3c8a` (`Update FFDec installation detection`)
- Decision: Keep the branch implementation for detection logic on `becca-LEX`.
- Rationale: The branch logic checks more valid install paths (multiple uninstall registry hives/locations plus Program Files fallbacks), while upstream commit `54e4e3c8a` only checks `HKCU\Software\JPEXS\FFDec` and may miss valid installs.
- Follow-up: Preserve the upstream-style optimization to skip repeated scans when a valid executable path is already cached.
