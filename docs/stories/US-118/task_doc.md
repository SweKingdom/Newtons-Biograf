# US-118: Custom Blazor design system and layout
**Status:** Ready for UAT · **Category:** development · **Points:** 5 · **Sprint:** not recorded

## Summary
The customer app no longer uses the Blazor start template look. It now has a dark cinema palette with a warm gold accent, a top navigation header, a footer, and a mobile menu toggle below 640px. Shared components for buttons, form fields, alerts, spinner and cards are available for later feature stories.

## What was built
- `wwwroot/css/app.css`: design tokens on `:root` (colours, fonts, spacing `--space-1..6`, radius, shadow), base reset, typography, container and utility classes.
- `wwwroot/index.html`: links only `app.css`. No Bootstrap.
- `Layout/MainLayout.razor` + `.razor.css`: header with text logo "Newtons Biograf", top navigation, `<main>@Body</main>`, footer. Mobile toggle button with `aria-expanded` below 640px.
- `Components/AppButton.razor`: `Variant` (Primary, Secondary, Danger), `Type`, `Disabled`, `OnClick`, `ChildContent`.
- `Components/FormField.razor`: `Label`, `Id`, `ChildContent`, `Error`. Label bound with `for`; error shown with `role="alert"`.
- `Components/AlertBox.razor`: `Kind` (Info, Success, Error), `ChildContent`. `role="alert"` for Error.
- `Components/Spinner.razor`: CSS-only spinner, `role="status"`, `aria-label="Loading"`.
- `Components/Card.razor`: `Title`, `ChildContent`.
- `Pages/Home.razor`: minimal placeholder using Card and Spinner.
- `_Imports.razor`: Components namespace added.
- `NewtonsBiograf.Client.csproj`: explicit Microsoft.Extensions.Http package reference (the Client did not compile without it).
- `tests/NewtonsBiograf.Tests/DesignSystemTests.cs`: inspects CSS and Razor source for AC 1 to AC 3.
- `package.json`: `typecheck`, `test`, `build` scripts that wrap `dotnet`. No lint script.

## How it works
**AC 1: Own layout.** `:root` in `app.css` defines the colour, font, spacing, radius and shadow tokens. `MainLayout` renders a header, a top nav, `main` and a footer. There is no sidebar.

**AC 2: Reusable components.** The five components exist as one file each. They render their parameters as described above. Components are not yet rendered in a test; they are checked by source inspection and by manual review.

**AC 3: Not template-like.** The page uses a dark background with a gold accent. There is no purple sidebar and no "Hello, world". Below 640px the navigation collapses behind a Menu button whose `aria-expanded` attribute toggles. Visual review at desktop and 375px is still pending (manual).

## How to test
Checks: `npm run typecheck`, `npm run test`, `npm run build` (these wrap `dotnet build` and `dotnet test`). No lint is configured.

- **AC 1:** Open `app.css` and `MainLayout.razor`. **Verify:** `:root` holds the colour, font, spacing, radius and shadow tokens; the layout has header, top nav, main and footer, and no sidebar.
- **AC 2:** Render each component on a temporary page (do not commit it). **Verify:** AppButton shows 3 variants; FormField shows the label and an error with `role="alert"`; AlertBox shows 3 kinds; Spinner spins; Card shows its title.
- **AC 3:** Run the client and view it at desktop width and at 375px. **Verify:** dark background with gold accent, no purple sidebar, no "Hello, world", and the nav collapses to a Menu button (`aria-expanded` toggles) under 640px.

## Deployment notes
- No pre-deployment or post-deployment steps. No migrations. No configuration to set.
- Restore NuGet packages after pulling: the Client project has a new explicit Microsoft.Extensions.Http reference.

## Known limitations
- FormField error text is not linked to the input with `aria-describedby`.
- Home.razor catches only `HttpRequestException`. Other failures leave the spinner showing.
- DesignSystemTests check source text only; components are not rendered.
- AC 3 (visual review at desktop and 375px) has not been performed in a browser yet.
- Branding beyond a text/CSS logo is out of scope. Feature pages and their nav links are added by their own stories.
- The project has no lint check configured.
