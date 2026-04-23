# Copilot Instructions

## Scope
- This repository is an AutoCAD plugin for DWG batch merge on .NET 8 for Windows.
- Prefer links to existing docs over duplicating long explanations.

## Canonical Docs
- Architecture and flow: [AutoBIMFusion/docs/TECHNICAL_DOCUMENTATION.md](../AutoBIMFusion/docs/TECHNICAL_DOCUMENTATION.md)
- Merge algorithm and viewport math: [AutoBIMFusion/docs/ALGORITHM.md](../AutoBIMFusion/docs/ALGORITHM.md)
- Design principles and coding style: [README.md](../README.md)

## Build And Environment
- Always build with AutoCAD-specific configurations from `Directory.Build.props`: `DebugA25/ReleaseA25`, `DebugA26/ReleaseA26`, `DebugA27/ReleaseA27`.
- Typical commands:
	- `dotnet restore`
	- `dotnet build AutoBIMFusion.slnx -c ReleaseA25` (or `ReleaseA26`, `ReleaseA27`)
- Do not assume generic `Debug`/`Release` are valid for plugin verification.
- Post-build bundle deployment is configured in project build targets; verify paths before changing packaging logic.

## Critical Implementation Rules
- Run merge operations in document lock scope.
- For layout/viewport context changes, use AutoCAD command context APIs and preserve system variable state.
- Keep validation methods free of logging; log in callers.
- Ensure temp file cleanup in `finally` paths.

## Coding Preferences
- Prioritize readability and consistency between similar code blocks.
- Prefer concise names for methods/parameters when readability is preserved.
- Use Extract Variable for complex arguments before long method calls.
- Use `ArgumentNullException.ThrowIfNull(...)` for null checks.
- Avoid local functions and unnecessary wrapper layers.
- Do not split one operation into multiple thin wrapper methods without clear benefit.
- Avoid introducing extra settings classes when a direct parameter is sufficient.

## Repository-Specific Clarification
- Request "обновить метаданные" means updating documentation metadata in `.md` files, not only project metadata.