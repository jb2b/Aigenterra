# Aigenterra working agreements

## Architecture

- Preserve `Domain <- Application <- outer adapters and hosts`.
- Keep Domain framework- and runtime-independent; Application owns use-case-driven ports.
- Do not invent domain concepts, repositories or abstractions without a concrete use case.
- Keep Api as composition root, AppHost as orchestrator and ServiceDefaults technical only.
- Keep provider-specific telemetry inside its adapter and preserve Collector fail-open behavior.

## Verification

- Restore, build, format-check and test `Aigenterra.slnx` after code changes.
- Update architecture tests when project dependencies intentionally change.

## Git

- Use Conventional Commits such as `feat: concise description`.
- Present the commit message before committing.
- Do not add remotes or push unless explicitly requested.
