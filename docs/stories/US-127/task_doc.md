# US-127: VIP rows and configurable seat prices
**Status:** Ready for UAT · **Category:** development · **Points:** 3 · **Sprint:** not recorded

## Summary
Admins can now mark a whole auditorium row as VIP or set it back to Standard. Seat prices (Standard 140 SEK, VIP 200 SEK) are read from configuration instead of being hard-coded, so they can be changed without a code change.

## What was built
- `PUT /api/auditoriums/{id}/rows/{row}/type` (`AuditoriumsController`): admin-only; sets the Type of every seat in one row. Idempotent.
- `RowTypeRequest` (`Shared/Dtos/AuditoriumDto.cs`): request body with a nullable `Type` marked `[Required]` and `[EnumDataType]`, so a missing Type is rejected with 400 and is not read as Standard.
- `PricingOptions` (`Api/Pricing/PricingOptions.cs`): strongly typed `Pricing` configuration section.
- `IPriceCalculator` / `PriceCalculator` (`Api/Pricing/PricingOptions.cs`): returns the price for a `SeatType`, reading the current values through `IOptionsMonitor`.
- `PricingController` (`Api/Controllers/PricingController.cs`): anonymous `GET /api/pricing`, returns `PricingDto`.
- `PricingDto` (`Shared/Dtos/AuditoriumDto.cs`): `{StandardSeat, VipSeat}`.
- `Api/Program.cs`: binds and validates `PricingOptions` at startup; registers `IPriceCalculator` as a singleton.
- `Api/appsettings.json`: `Pricing` section with `StandardSeat=140` and `VipSeat=200`.
- Tests: `Tests/PricingTests.cs` (new), `Tests/AuditoriumTests.cs` (row type tests).

## How it works
- **AC 1 (Mark row VIP):** An admin sends `{"Type":"VIP"}` to the row endpoint and receives 204. Every seat in that row becomes VIP; other rows are unchanged. `{"Type":"Standard"}` reverts the row. Repeating a call returns 204 again with no change.
- **AC 2 (Invalid input):** Unknown auditorium or row outside 1..Rows returns 404. A Type other than `Standard` or `VIP`, or a missing Type, returns 400. Staff and Customer roles get 403. No token gets 401.
- **AC 3 (Prices from config):** `IPriceCalculator.GetPrice(SeatType)` returns 140 for Standard and 200 for VIP. Changing the `Pricing` values changes the result without a code change.
- **AC 4 (No hard-coded prices):** Production code in the Api project contains no 140 or 200 literals. The defaults live only in `appsettings.json`.
- **AC 5 (Startup validation):** A missing `Pricing` value or a value of 0 or less stops the application at startup with the message "Configuration value 'Pricing:<key>' is missing or not greater than 0."
- **AC 6 (Past bookings unaffected):** Changing a row type or a price does not change `TotalPrice` of existing bookings. The price snapshot is enforced by the booking story, which must call `IPriceCalculator` at booking time.

## How to test
Run the checks: `npm run typecheck --silent`, `npm run test --silent`, `npm run build --silent`.

- **AC 1:** As Admin, `PUT /api/auditoriums/{id}/rows/2/type` with `{"Type":"VIP"}` returns 204. `GET /api/auditoriums/{id}` lists `VipRows:[2]`. Send `{"Type":"Standard"}` returns 204 and VipRows is empty. Repeat a call and expect 204.
- **AC 2:** Row 0 or Rows+1, or an unknown auditorium, returns 404. `{"Type":"Gold"}` or a missing Type returns 400. A Staff or Customer token returns 403. No token returns 401.
- **AC 3:** `GET /api/pricing` returns `{StandardSeat:140,VipSeat:200}`. `PricingTests` covers changed values with in-memory configuration.
- **AC 4:** `Ac4_Production_code_has_no_price_literals` scans Api sources. 140 and 200 appear only in `appsettings.json`.
- **AC 5:** Set `Pricing:VipSeat` to 0 (or remove it) and start the Api. Startup fails with the message above.
- **AC 6:** `Us127_Ac6_...` confirms booking TotalPrice and seat Price are unchanged after a row type change.

## Deployment notes
- **Configuration:** The `Pricing` section must exist in the target environment's configuration (`Pricing:StandardSeat` and `Pricing:VipSeat`, both greater than 0). Without it, the application does not start.
- **Migrations:** None listed in the story's changed files.
- **Pre-deploy / post-deploy steps:** None recorded.

## Known limitations
- No admin UI for editing prices (out of scope).
- Per-showing pricing is not supported (out of scope).
- The booking story must store the price snapshot through `IPriceCalculator`. AC 6 depends on it.
