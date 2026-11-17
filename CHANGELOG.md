# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.3.0] - 2026-11-17

### Added

- Dual targeting: the tool now builds for `net10.0` and `net11.0`.
- Release workflow (`.github/workflows/release.yml`): manual dispatch, version taken from this changelog, per-RID Native AOT standalone binaries attached to a GitHub Release, and the tool package pushed to nuget.org through trusted publishing (OIDC, no API-key secret).

### Changed

- SDK pinned to `11.0.100` and default target to `net11.0`.
- Dependencies: CliWrap 3.10.5, FSharp.SystemCommandLine 2.3.0 (stable), FsToolkit.ErrorHandling 5.2.0, Spectre.Console 0.57.2, JDeck 1.1.0.
- The download user agent (`NvmFs/...`) is now built lazily from the executing assembly version instead of a hardcoded literal.
- Removed the explicit `Microsoft.SourceLink.GitHub` reference: the .NET SDK includes SourceLink implicitly, and the pinned 1.0.0 reference was suppressing it.
- Release binaries are Native AOT (ahead-of-time compiled, no trimming-only JIT builds). The index decoder runs directly on `System.Text.Json` with no reflection, so it survives AOT.

### Fixed

- Index entries without a `v8` property (current nodejs.org prereleases omit it) no longer fail the whole index decode; `v8` is now optional.
- A missing or unreadable `index.json` no longer crashes `list` with an unhandled exception; it reports the problem and continues with an empty index.

## [1.2.0] - 2025-06-23

### Changed

- Target framework `net8.0` → `net9.0`, SDK `8.0.100` → `9.0.100`.
- CLI layer rewritten on FSharp.SystemCommandLine `2.0.0-beta5.3` (`setAction`, builder syntax) via PR #5.
- JSON decoding moved from Thoth.Json.Net to JDeck; the separate `FsToolkit.ErrorHandling.TaskResult` package is gone (folded into FsToolkit.ErrorHandling 5.0.0).
- Dependencies: CliWrap 3.9.0, Spectre.Console 0.50.0, Fantomas 7.0.2.
- The `version` / `--lts` / `--current` options are now validated as mutually exclusive, and option handling was simplified across `install`, `use`, `uninstall`, and `list`.
- Internal readability refactor across `Cmd`, `Common`, `Env`, `IO`, and `Program` with no command-surface changes.
