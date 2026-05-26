# Contributing to Mirrorgen

Mirrorgen is **pre-alpha**. The design is being finalized in `docs/CONCEPT.md`. Code is not yet open for general PRs — feedback and issues are welcome, but please open an issue to discuss before writing significant code.

## Bilingual docs

Mirrorgen ships its primary docs in English and Korean side by side:

| English             | Korean                  |
|---------------------|-------------------------|
| `README.md`         | `README_ko.md`          |
| `docs/CONCEPT.md`   | `docs/CONCEPT_ko.md`    |

**Rule**: any change to one side must update the other in the same commit. Reviewers must reject PRs that touch only one language. Treat the two files as one logical document with two surfaces; if the content has diverged, the PR is incomplete.

If you only speak one of the two languages, a "best-effort" translation in the other file is acceptable — leave a `<!-- TODO: review translation -->` comment near the new section so a fluent reviewer can polish it. A rough but synced translation is better than a clean but missing one.

## Code conventions

To be filled in as the codebase grows. For now:

- Target the latest stable .NET SDK (no preview-only features without discussion)
- One project per concern (Core / Attributes / Analyzers / MSBuild / Cli) — do not collapse them
- Public API surface changes go through `docs/CONCEPT.md` first

## Licensing

By contributing, you agree that your contributions are licensed under the project's MIT license.
