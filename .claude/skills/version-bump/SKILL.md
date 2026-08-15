---
name: version-bump
description: Bump the RtlSdrManager version number across the repository. Use when the user gives a new version (e.g. "bump to 0.7.2", "/version-bump 0.7.2") and wants the csproj version fields, package release notes, README install example, and CHANGELOG updated consistently.
allowed-tools: Read, Edit, Grep, Glob, Bash
---

# Version Bump Skill

Sets a new project version number in one pass, keeping the single source of truth and every
illustrative version string consistent. Follows the versioning workflow in `CLAUDE.md`,
[Semantic Versioning](https://semver.org/), and
[Keep a Changelog](https://keepachangelog.com/).

## Input

The new version, given as an argument (`/version-bump 0.7.2`) or in the message.

1. It must be a SemVer `MAJOR.MINOR.PATCH` string (optionally a pre-release suffix like
   `0.8.0-rc1`). If it is missing or malformed, **stop and ask** for a valid version — do
   not guess.
2. Read the current version from `src/RtlSdrManager/RtlSdrManager.csproj` for reference and
   sanity: warn (but continue if the user insists) if the new version is not strictly
   greater than the current one.
3. **Ask for the release date** if it is not given, defaulting to today (`YYYY-MM-DD`).
   Unlike some projects, this one dates every changelog section — there is no `Unreleased`
   heading convention here (verify against `CHANGELOG.md` before assuming otherwise).

## What a *bump* is (and is not)

A bump advances the version number and prepares the changelog section, release notes, and
install example. It does **not** build, pack, tag, or publish — `./build.sh` and
`./publish.sh` are separate steps run by the user. Never commit: the user reviews the diff
and commits manually (project git rule in `CLAUDE.md`).

## Steps

### 1. Source of truth (always)

`src/RtlSdrManager/RtlSdrManager.csproj` — **four** edits, not one:

- `<Version>` → `X.Y.Z`
- `<FileVersion>` → `X.Y.Z.0` (note the fourth component)
- `<AssemblyVersion>` → `X.Y.Z.0` (note the fourth component)
- `<PackageReleaseNotes>` → replace the body with the new version's notes:

  ```
  vX.Y.Z (YYYY-MM-DD):

  ADDED / CHANGED / FIXED:
  - <short summary lines mirroring the CHANGELOG section>

  See CHANGELOG.md for complete details and previous releases.
  ```

  Keep it **trimmed to the current version only** — that is the established convention
  (commit `2878f9f`). Use the section headings that actually apply; don't invent changes.
  If the changelog content for this version isn't written yet, say so and leave a clearly
  marked placeholder for the user rather than fabricating entries.

A pre-release suffix (`0.8.0-rc1`) goes in `<Version>` only; `<FileVersion>` and
`<AssemblyVersion>` take the numeric part (`0.8.0.0`), since assembly versions cannot carry
a suffix.

### 2. Changelog (always)

Edit `CHANGELOG.md` — **three** places:

1. **New section**, inserted directly below the intro block and above the previous newest
   section. Match the existing heading format exactly: `## [X.Y.Z] - YYYY-MM-DD`
   (ASCII hyphen with single spaces — *not* an em dash). Use `### Added` / `### Changed` /
   `### Fixed` / `### Removed` subsections as applicable. Mark incompatible changes
   `**BREAKING**` inline, as the 0.7.0 section does. Do not invent entries.
2. **Version History Summary table** — add a row at the top of the table body:
   `| **X.Y.Z** | YYYY-MM-DD | <one-line key change> |`
3. **Footnote link** at the bottom of the file, above the existing ones:
   `[X.Y.Z]: https://github.com/nandortoth/rtlsdr-manager/releases/tag/vX.Y.Z`

Do **not** touch already-released sections, their table rows, or their footnote links.

### 3. README install example (always)

`README.md` — the `<PackageReference Include="RtlSdrManager" Version="…" />` line in the
Installation section. Update it regardless of its current value.

### 4. Historical version references — do NOT bump

Some prose records *when* a behaviour was introduced. These are facts about the past and
must stay put. Currently:

- `README.md` — *"Since v0.7.1 the default mode stores each `IQData` as two bytes
  internally…"*

Rule of thumb: if the sentence would still be true after the release, leave it. If it is an
install/copy-paste example of the current version, update it. When a grep turns up a version
in a spot not listed in these steps, use judgment and report what you decided.

### 5. Do NOT touch

- `publish.sh` — derives the version from the `.nupkg` filename at run time; the `0.7.1` in
  its comment is an illustrative parsing example, not a declaration.
- `build.sh` — does not hardcode a version.
- `**/obj/`, `**/bin/`, `artifacts/` — build output, including any stale `.nupkg`.
- Dependency and badge versions — `.NET-10.0` badge, `librtlsdr` 2.x / 2.0.3 references,
  `Microsoft.SourceLink.GitHub` 8.0.0, xUnit versions.
- The GPLv3 header blocks (they carry a copyright year, not a version).

### 6. Verify and report

Grep for the **previous** version to confirm nothing intended was missed and that the
remaining hits are deliberate:

```bash
grep -rn "<old-version>" README.md CHANGELOG.md src/ docs/ --include="*.md" --include="*.csproj"
```

Expected surviving hits: the previous release's changelog section, table row and footnote
link, and any historical prose reference from step 4.

Then present a concise summary: old → new version, the files changed, and any version
occurrences intentionally left. **Do not run any git state-changing command** and do not
commit.

## Example

`/version-bump 0.7.2` (release date 2026-08-15) →

- `src/RtlSdrManager/RtlSdrManager.csproj`: `<Version>` `0.7.1` → `0.7.2`;
  `<FileVersion>` / `<AssemblyVersion>` `0.7.1.0` → `0.7.2.0`; `<PackageReleaseNotes>`
  rewritten for `v0.7.2 (2026-08-15)`
- `CHANGELOG.md`: new `## [0.7.2] - 2026-08-15` section, new summary-table row, new
  footnote link
- `README.md`: `<PackageReference … Version="0.7.2" />`
- Left alone: the "Since v0.7.1 …" sentence in `README.md`, all 0.7.1 changelog content
- Report changed files; leave the commit to the user.
