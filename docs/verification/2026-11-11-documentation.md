# Documentation verification · 11 November 2026

The authoritative checkout is **`C:\Development\XAIHT\repo\Tlachihuani`**.
Angela corrected the location after the earlier documentation pass used the old
checkout. All edits and checks described here apply to this corrected path.
Current documentation is **[0.3.3 pre-release](../releases/v0.3.3.md)**, preserving
her explicit channel designation.

## Scope and provenance

At the beginning of this pass, `Directory.Build.props` and seven internal project
lock files already contained local version edits. They were preserved byte for
byte. The declared product version is 0.3.3. This documentation pass does not
build a package, create or move tags, change either Tlamatini, modify an installed
service, commit or push.

The README, version badge, maintainer instructions, handoff, release notes,
installation/building guidance, operating guide, test documentation and file
inventory now describe the same pre-release. Active Tlachihuani checkout paths
use the corrected location. Historical release versions, original icon provenance,
dependency versions and dated verification evidence retain their original values.

Byte comparisons confirmed that tracked application code, workspace assets and
test harnesses match their counterparts in the original checkout. The
[10 November recovery results](2026-11-10-torrenter-recovery.md) remain dated
evidence, not tests rerun here. Their local artifact path is explicitly historical.

## Documentation checks

Validation is performed from a verified visible foreground console. It checks
the Git root, product/internal dependency version consistency, preserved baseline
files, current documentation versions and paths, local document links, UTF-8,
SVG labels and Git whitespace. The file inventory includes tracked files and
new documentation, excluding ignored builds and verification artifacts.

A headed Chrome preview checks the README banner at 1280 × 900 and 390 × 844
CSS-pixel viewports, including loaded assets, badge text bounds and horizontal
overflow. This is documentation visual verification, not a product UI or
installed-service acceptance test. No tracked PDF, PPTX or DOCX requires regeneration.

## Observed results

**Passed:** 47 Markdown files, 161 local document/asset links, 17 internal project
version ranges and a 168-file working-tree inventory. The pass changed 21
documentation/banner files; all other baseline files, including the eight
pre-existing version metadata/lock changes, retained identical SHA-256 hashes.
Current documentation has no active reference to the former Tlachihuani checkout.
UTF-8, the version badge's accessible text and scoped Git whitespace checks passed.

Both headed Chrome viewport checks passed with loaded images, visible foreground
verification, no horizontal overflow and no clipped version-label text. The saved
desktop and mobile preview images were also visually inspected. The dedicated
preview browser closed afterward; the development console remains open.

Local check reports, console transcript and screenshots are stored under
`artifacts/verification/docs-033-correct-root-20261111/` in the authoritative
checkout. These ignored artifacts are not shipped. See `checks.json` and
`visual.json` for the observed outcomes, with `session.log` for execution evidence.
