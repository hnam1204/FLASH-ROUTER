# V4.5.1 Regression Test Matrix

| Verification | Result | Evidence scope |
|---|---|---|
| Restore | PASS | Current solution |
| Debug build | PASS | 0 warnings, 0 errors |
| Release build | PASS | 106 warnings, 0 errors |
| Debug architecture tests | PASS | 11/11 |
| Debug integration tests | PASS | 204/204 |
| Release architecture tests | PASS | 11/11 |
| Release integration tests | PASS | 204/204 |
| Synthetic headed Playwright | PASS | Included in integration suite |
| Post-TOTP bounded observer | PASS | Source inspection plus synthetic tests |
| SQLite integrity | PASS | Integrity and foreign-key checks |
| Live provider authentication | NOT RUN | Explicit authorization not provided |
| Live provider usage | NOT RUN | Explicit authorization and supported credential not provided |
| Three real 60-second scheduler cycles | NOT RUN | No runtime cycle evidence captured |
| Visual screenshot export | NOT RUN | UI observed; export unavailable through current CUA API |

Commands used for the regression matrix were the solution-level `dotnet restore`, `dotnet build` for Debug and Release, and `dotnet test` for Debug and Release. No failing test was skipped.
