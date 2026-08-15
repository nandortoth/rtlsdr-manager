---
name: comment-reviewer
description: Reviews C# code comments for XML documentation completeness, clarity, consistency, and RTL-SDR/native-interop domain standards. Use when reviewing code quality, checking documentation, or ensuring comments explain intent rather than mechanics.
allowed-tools: Read, Grep, Glob, Bash
---

# Code Comment Review Skill

Reviews code comments in the RtlSdrManager project to ensure high-quality, consistent, and
accurate documentation.

**This library ships its XML documentation inside the NuGet package**
(`<GenerateDocumentationFile>true</GenerateDocumentationFile>`), so every public
`<summary>` is surfaced in consumers' IntelliSense. Missing or wrong documentation on a
public member is a shipped defect, not an internal tidiness issue.

## What This Skill Reviews

### 1. XML Documentation Completeness

**Public APIs** (classes, methods, properties, enum members) must have:
- `<summary>` — one-line description of what it does
- `<param>` — for each parameter, what it represents **and its unit**
- `<returns>` — what the method returns (if non-void)
- `<exception>` — every exception type that can escape, and when
- `<remarks>` — additional context: thread affinity, allocation behavior, native quirks

**Example:**
```csharp
/// <summary>
/// Set the tuner gain for the device.
/// Manual tuner gain mode must be enabled for this to work.
/// </summary>
/// <param name="value">Gain in dB. Must be one of <see cref="SupportedTunerGains"/>.</param>
/// <exception cref="InvalidOperationException">Thrown when AGC tuner gain mode is enabled.</exception>
/// <exception cref="ArgumentOutOfRangeException">Thrown when the gain is not supported by the tuner.</exception>
/// <remarks>
/// The supported-gain list is hardware-determined and cached after the first query.
/// </remarks>
```

Note what the example does *not* say: it describes the gain in dB and points at
`SupportedTunerGains`, without mentioning how the native layer encodes it. See
"Do not expose librtlsdr in customer-facing documentation" below.

**Enum members count as public API here** — they appear in IntelliSense and several were
only documented in 0.7.0. Every member of every `Modes/` and `Hardware/` enum needs a
`<summary>`.

### 2. Comment Quality and Clarity

**Good comments explain WHY, not WHAT.**

❌ Bad (restates the code):
```csharp
// Get the value from the device.
uint returnValue = LibRtlSdr.rtlsdr_get_center_freq(_deviceHandle!);
```

✅ Good (explains the non-obvious):
```csharp
// librtlsdr returns the cached dev->freq, which is 0 both when the device was never
// tuned and when the last tune failed — so 0 cannot be distinguished from an error here.
uint returnValue = LibRtlSdr.rtlsdr_get_center_freq(_deviceHandle!);
```

**On the existing step-comment style:** much of the older code narrates every statement
(`// Return the value.`, `// If we did not get 0, there is an error.`). Treat this as
**Info** severity at most — flag it, never mass-rewrite it, and don't make churn the point
of a review. New and modified code should not add more of it. The 0.7.0-era code in
`RtlSdrManagedDevice.Async.cs` shows the target style: dense where the reasoning is subtle,
silent where the code speaks for itself.

**Check for:**
- Comments that add value beyond what the code already says
- Clear explanations of complex or non-obvious logic
- Justification for design decisions, especially deliberate-looking oddities
- Warnings about edge cases, invariants, and assumptions
- No redundant or outdated comments (a comment that contradicts the code is Critical)

### 3. RTL-SDR and Native-Interop Standards

See [rtlsdr-doc-standards.md](rtlsdr-doc-standards.md) for full detail and examples. In
summary, always document:

- **Units.** Hz vs MHz, dB vs tenths of a dB, samples vs bytes. Most of this API's
  historical confusion is unit confusion.
- **Native error-code semantics.** What a nonzero return actually means for *that*
  function — several are counter-intuitive (`-2` from `rtlsdr_set_freq_correction` means
  "value unchanged"; `-5` from `rtlsdr_cancel_async` is normal on a requested stop).
- **Thread affinity.** Anything reachable from the native USB callback thread must say so.
- **Lifetime and ownership.** `SafeHandle`, `GCHandle`, `ArrayPool` rentals — who frees
  what, and when it is deliberately *not* freed.
- **Tuner applicability.** If a member only works on some tuners (or only with the
  KerberosSDR fork), say which and why.
- **Upstream references.** Cite the librtlsdr function being wrapped when behavior is
  inherited from it.

### 4. Project Writing Conventions

The full rules live in `CLAUDE.md` under "Code and Documentation Conventions". Check every
comment against all three:

**American English.** *behavior*, *synchronization*, *initialize*, *center*, *canceled*,
*analyze* — not *behaviour*, *synchronisation*, *initialise*, *centre*, *cancelled*.
Severity: **Info**.

**No dash between connected clauses.** Use a colon to introduce an explanation, a semicolon
to link two related independent statements. Dashes stay available for genuine parenthetical
asides and ranges. Severity: **Info**.

❌ `// Rounding avoids truncation — a plain cast would turn 49.6 into 495`
✅ `// Rounding avoids truncation: a plain cast would turn 49.6 into 495`

**Do not expose `librtlsdr` in customer-facing documentation.** The library exists to hide
the native layer, so XML docs on `public`/`protected` members must describe behavior in
terms of *the device* and *this API*, never in terms of the native library's encoding,
caching, or return codes. Severity: **Warning** — it leaks an implementation detail into
shipped IntelliSense.

❌ `librtlsdr expresses gain in tenths of a dB; this property converts.`
✅ `Gain is expressed in dB; only the steps listed by SupportedTunerGains are accepted.`

Two deliberate exceptions, both user-actionable: **installation prerequisites** (the
consumer must install the native library) and **the KerberosSDR fork requirement** on
`FrequencyDitheringMode` / `SetGPIO`.

`internal` and `private` members are **exempt and should keep the native detail** — that is
where the reasoning belongs, and it is not shipped. When flagging a public leak, check
whether the explanation should move to an internal member rather than be deleted.

### 5. Consistency Across Codebase

**Ensure:**
- Similar members use similar documentation patterns (all the `ExecuteWithSuppression`
  property setters read alike; all the native imports document their return contract)
- Terminology is consistent — see the table in `CLAUDE.md`. Don't mix "sample" and
  "I/Q data" for the same thing, or "device pointer" and "device handle"
- The GPLv3 header block is present and the copyright year is current
- XML doc style matches across `Interop/`, `Modes/` and the device classes

## Default Scope

When no specific files are mentioned, the review scope is limited to **changed or newly
added files** relative to the `master` branch (this repo's default branch is `master`, not
`main`). Use `git diff --name-only master` and `git status` to determine which files to
review.

When the user specifies files or directories explicitly, review those instead.

Skip generated and vendored content: `**/obj/`, `**/bin/`, `artifacts/`.

## How to Use This Skill

**Examples:**
- "Review comments" (reviews changed/new files vs `master`)
- "Review comments in RtlSdrManagedDevice.Async.cs"
- "Check XML documentation in the Modes enums"
- "Are the interop comments accurate against ../rtl-sdr?"

I will:
1. Determine review scope (changed files by default, or user-specified files)
2. Read the files in scope
3. Analyze comments against the standards above
4. Identify issues with specific line references and severity
5. Suggest improvements with examples

## Verifying Interop Claims

When a comment asserts something about native behavior — an error code, a range, a
threading guarantee — **check it against the upstream source at `../rtl-sdr`** (tag
`v2.0.3`) rather than trusting the comment. Incorrect interop documentation is Critical:
it is the kind of error that gets copied into consumer code.

## Review Output Format

Categorize each finding by severity:

### Severity Levels

- **Critical** — Missing XML documentation on public API (it ships to consumers);
  factually incorrect comments; documentation that contradicts the code; wrong units;
  wrong native error-code semantics
- **Warning** — Undocumented thread affinity on callback-reachable code; undocumented
  exceptions that can escape; missing tuner applicability; undocumented buffer ownership;
  comments describing WHAT instead of WHY in new code
- **Info** — Legacy step-comment narration; minor wording; consistency suggestions;
  optional enhancements

### Report Structure

**Issues Found** (grouped by severity):
- Severity level
- File path and line number
- Current comment (or lack thereof)
- Why it's problematic
- Suggested improvement

**Summary:**
- Counts per severity level
- Overall comment quality assessment

## Tool Restrictions

This skill only **reads** — it won't modify code. `Bash` is permitted solely for read-only
git inspection (`git diff`, `git status`, `git log`) to determine scope; never run a
state-changing git command. After review, I can help implement suggested changes if you
approve them.
