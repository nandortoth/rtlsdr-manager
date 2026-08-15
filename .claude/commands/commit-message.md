# Draft Commit Message

Analyze the staged changes and their context (what the code does, why it was changed) to draft a commit message following this project's conventions. The message must reflect the intent and scope of the changes, not just list modified files. The user will commit manually.

## Instructions

1. **Gather context** by running these commands in parallel:
   - `git diff --cached` to see staged changes
   - `git diff` to see unstaged changes
   - `git status` (never use `-uall`)

2. **If nothing is staged**, inform the user and stop.

3. **Check for unstaged changes.** If there are unstaged changes (especially in files that are also partially staged), **warn the user** before presenting the commit message. Use unstaged changes as additional context to write a better, more informed commit message — but the message must only describe what is staged. Never stage files — the user decides what to commit.

4. **Draft a commit message** following these rules:

   ### Format
   - **One line only.** No body, no footer, no `Co-Authored-By`.
   - **Imperative mood**, starting with a verb.
   - **No trailing period.**
   - **No conventional-commits prefix** (`feat:`, `fix:`, etc.).
   - Keep it under 80 characters when possible; up to ~100 is acceptable for complex changes (the longest subject in this repo's history is 100).

   > Match the style of the recent history (0.5.0 onward). Older commits contain typos and
   > vague subjects (`Small fix in README.md`, `Bugfix in DeviceInfo handling`,
   > `Implemening KerberosSDR features`) — do **not** imitate those.

   ### Verb choice
   Pick the verb that best describes the change. Verbs marked ★ are the ones this repo
   actually uses most:

| Verb | Use when… |
|------|-----------|
| `Fix` ★ | Correcting a bug or wrong behavior |
| `Add` ★ | Introducing wholly new functionality, files, or tests |
| `Update` ★ | Modifying existing behavior, configuration, or docs |
| `Optimize` ★ | Reducing CPU, allocations, or memory on a hot path |
| `Harden` ★ | Making lifecycle/teardown/error paths robust against edge cases |
| `Support` ★ | Enabling a new platform, tuner, distribution, or target framework |
| `Bump` ★ | Advancing the version number |
| `Document` | Adding or correcting documentation and XML doc comments |
| `Implement` | Building a significant new capability |
| `Refactor` | Restructuring without changing behavior |
| `Improve` | Enhancing quality or readability |
| `Clean up` | Tidying several small, related loose ends |
| `Remove` | Deleting code, files, or features |
| `Rename` | Changing names of files, classes, or members |
| `Replace` | Swapping one implementation or algorithm for another |
| `Store` | Changing how data is represented or persisted |
| `Trim` | Cutting content down (release notes, docs, dead code) |
| `Target` | Changing the target framework |
| `Roll back` | Reverting state on a failure path |
| `Create` | Producing new test suites, scripts, or packaging |
| `Rewrite` | Substantially reworking existing content |

   ### Content guidelines
   - Use the project's domain terminology naturally (I/Q, tuner gain, AGC, PPM, direct
     sampling, bias tee, GPIO, KerberosSDR, raw buffer mode, async buffer, P/Invoke).
   - **Name the API surface affected**, since this is a library and consumers read the log
     to understand upgrades: `Fix spurious error from StopReadSamplesAsync on requested stop`.
   - When multiple areas are affected, list them in parentheses:
     `Clean up tuner-gain validation, async buffer guard, and scope-count accessor`.
   - For bug fixes, state *what* was fixed, not *how*:
     `Fix crash and leak paths in async read and console suppression`.
   - For performance work, say what it saves:
     `Store IQData components as bytes to cut sample memory`.
   - **Breaking changes belong in `CHANGELOG.md`**, marked `**BREAKING**`. Do not flag them
     in the subject: no commit in this repo's history does, and the subject is better spent
     naming what changed.
   - Native-interop changes should mention the native side when relevant:
     `Add native library fallback paths for additional Linux distributions`.

5. **Present the message** for the user to copy. Format it like:

   ```
   Proposed commit message:
   <message>
   ```

## Important
- **Never run any git command that modifies state** (`git commit`, `git add`, `git push`, `git stash`, `git reset`, etc.). Only read commands (`git diff`, `git status`, `git log`) are allowed.
- Only draft the message — the user commits manually.
