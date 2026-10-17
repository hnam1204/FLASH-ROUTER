# FLASH ROUTER - BE-07F Windows Release Certification

Date: 2026-10-16

## Certified current-source artifact

| Property | Result |
| --- | --- |
| Artifact | `.local/releases/BE-07/win-x64-verified-3` |
| Target | `net8.0`, Release, self-contained `win-x64` |
| Files | 387 |
| Bytes | 111,555,654 |
| Entry assembly SHA-256 | `E4808D65BB05B5046540527D2CFD876F109D12B99AD8282AA7AE03FB810CAB77` |
| Checksum entries | 387 |
| Checksums verified | 387; mismatches 0 |
| Signed | No |
| Manifest | `.local/releases/BE-07/manifest.json` |
| Checksums | `.local/releases/BE-07/checksums.sha256` |

The existing BE-06 artifact was not overwritten. Earlier exploratory BE-07 output directories are preserved; `win-x64-verified-3` is the only BE-07 artifact referenced as certified.

## Package safety

The package scan found no SQLite database, vault ciphertext, DPAPI key ring, local runtime folder, real credential, test fixture, cookie, token, password, or sensitive log. Runtime data was created only in an isolated `.local/test-data` root during cold start.

## Current-artifact cold start

The fresh artifact was launched with an isolated workspace root, `Integrated` storage, Production environment, and `http://127.0.0.1:5288`.

- Migration startup: PASS.
- `/`: HTTP 200.
- `/connections`: HTTP 200.
- `/backups`: HTTP 200.
- `/app.css`: HTTP 200.
- Listener observed: `127.0.0.1:5288`.
- Isolated SQLite database and Data Protection directory were created under the test root.
- The test process was stopped after verification; the BE-06 artifact already has separate graceful-shutdown evidence.

## Regression scope and limitations

The source changed only in provider observation persistence/readback and its offline regression tests; no UI code changed. Therefore the BE-06 browser evidence remains relevant, but it used `375x812`, `768x900`, `1024x900`, `1280x900`, and `1440x900`. The exact BE-07 requested sizes `768x1024`, `1024x768`, and `1280x800` were not newly captured. No synthetic 0/12/100/500/1000 account resource benchmark was added in this phase.

The package remains unsigned, .NET 8 remains the target, and the two transitive High-advisory cryptography packages remain open. This is an offline local release claim, not a security-clean, live-provider, or production-distribution claim.

## Verdict

`BE-07F WINDOWS RELEASE - GO`

The current source has a reproducible local Windows artifact with verified checksums, isolated cold-start evidence, and no bundled secrets. Limitations remain explicitly recorded above.
