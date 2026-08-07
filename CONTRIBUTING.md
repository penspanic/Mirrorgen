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

## Releases

Releases are cut from `main`, and only from `main`.

- Version comes from the git tag via [MinVer](https://github.com/adamralph/minver);
  the prefix is `v`, so `git tag v0.5.0` publishes `0.5.0`.
- A tag whose name contains `-` (e.g. `v0.5.0-alpha.1`) is published as a
  prerelease.
- Pushing a `v*.*.*` tag triggers `.github/workflows/release.yml`, which packs
  the whole solution and pushes to nuget.org.

**A push to nuget.org cannot be undone** — nuget.org will not unlist a version
on request. The workflow therefore refuses to publish a tag that is not an
ancestor of `origin/main`. `0.3.0-alpha.39` was published from an off-branch
commit before that guard existed and is stuck there permanently; the guard is
what stops a repeat.

## Licensing

By contributing, you agree that your contributions are licensed under the project's MIT license.
