# US-118 — Test report (iteration 1)
**Verdict:** ✅ Approved with notes · **Tester:** Cuélebre · **Date:** 2026-11-01

## Summary
| Check | Result | Evidence |
|---|---|---|
| run_checks (typecheck, test, build; no lint configured) | ✅ | all exit 0 |
| AC 1 Own layout | ✅ | app.css tokens, MainLayout.razor and .css, index.html |
| AC 2 Reusable components | ✅ | Components/*.razor (static review) |
| AC 3 Not template-like | ⛔ | visual review at desktop and 375px not possible here |

## Notes
- Static review only; screenshots were not captured.
- The tests read source files. They do not render components.
- FormField: the error `<p>` has an id, but nothing links it to the input via aria-describedby (minor).
- Home.razor catches only HttpRequestException; any other exception leaves the spinner showing (minor).

## Not tested
- AC 3 visual review at desktop and 375px width, including the Menu toggle collapsing under 640px. This needs a manual check in a browser.
