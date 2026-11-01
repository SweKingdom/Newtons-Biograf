# Testing

This document lists every automated test scenario of Newtons Biograf, what it proves and how to run it. Test names are copied from the code in `tests/NewtonsBiograf.Tests`. The tests are run locally; no CI or automated check pipeline is configured, so this document does not claim any test result.

## 1. Overview

- Test project: `tests/NewtonsBiograf.Tests` (xUnit, .NET 8). It references the Api, Client and Shared projects.
- Scenario tests (`Scenarios/`, namespace `NewtonsBiograf.Tests.Scenarios`) prove the core business rules. They use:
  - `TestDb` (`Infrastructure/TestDb.cs`): a real SQLite database created with the EF Core migrations, so unique indexes really apply. It is seeded with two auditoriums, one movie ("Dune") and two customers. `TestDb.InMemory()` keeps one open in-memory connection. `TestDb.File()` uses a temporary file, deleted on dispose, so parallel contexts truly race (used by the parallel booking test).
  - `ApiFactory` (`Infrastructure/ApiFactory.cs`): the real API (`WebApplicationFactory<Program>`) on the test database. It records `BookingCreated` and `BookingCancelled` events, can call a service directly (`With<TService,TResult>`) and gives HTTP clients signed in with a real JWT (`Client`).
  - `FakeClock : TimeProvider` (`Infrastructure/TestDb.cs`): time the test moves by hand. The API reads time through `TimeProvider`; there is no `IClock` in the code.
  - Showing starts are fixed at 2040-03-10 18:00 UTC. All four scenario classes share `[Collection("Scenarios")]`, so they do not run in parallel.
- Other API test classes (`MovieTests`, `ShowingTests`, `BookingCreateTests`, ...) start their own `WebApplicationFactory<Program>` on a migrated SQLite database, either in-memory or a temporary file.
- Client test classes (`*ClientTests`, `DesignSystemTests`) need no browser. They check the typed API clients with stub HTTP handlers, render pages and components to HTML on the server (`HtmlRenderer`), and read the Razor and CSS source for structure.
- Theory tests (`[Theory]`) are one row below; each of their `InlineData` cases is listed separately by `dotnet test --list-tests`.

## 2. How to run

Run every command from the repository root. `dotnet test` finds the solution `NewtonsBiograf.sln`.

| Goal | Command |
|---|---|
| All tests | `dotnet test` |
| All tests (project script, same tests) | `npm run test --silent` |
| One test class | `dotnet test --filter "FullyQualifiedName~BookingScenarios"` |
| One method | `dotnet test --filter "FullyQualifiedName~Customer_can_cancel_exactly_two_hours_before_start"` |
| All scenario tests (the four classes in `Scenarios/`) | `dotnet test --filter "FullyQualifiedName~NewtonsBiograf.Tests.Scenarios"` |
| Several classes | `dotnet test --filter "FullyQualifiedName~BookingScenarios\|FullyQualifiedName~CancellationScenarios"` |
| One class only, when its name is part of another (for example `AuthTests` and `ClientAuthTests`) | `dotnet test --filter "FullyQualifiedName~NewtonsBiograf.Tests.AuthTests."` (full namespace and a trailing dot) |
| Verbose output | `dotnet test --logger "console;verbosity=detailed"` |
| List all tests without running them | `dotnet test --list-tests` |

Filtering by scenario area (class names are in section 3):

| Scenario area | Filter |
|---|---|
| Booking seats, double booking | `--filter "FullyQualifiedName~BookingScenarios"` |
| Cancellation (2 hour rule, staff cancel) | `--filter "FullyQualifiedName~CancellationScenarios"` |
| Showing overlap | `--filter "FullyQualifiedName~ShowingOverlapScenarios"` |
| Access (401, 403, own bookings) | `--filter "FullyQualifiedName~AccessScenarios"` |
| Booking validation and started showing (API) | `--filter "FullyQualifiedName~BookingCreateTests"` |
| Showing in the past (API) | `--filter "FullyQualifiedName~ShowingTests"` |

Notes:

- `~` means "contains". `~AuthTests` also matches `ClientAuthTests`, and `~Cancellation` matches the scenario, API and client classes. Use the namespace and a trailing dot (see the table) when that matters.
- Xunit runs test classes in parallel, except the classes of one collection (the scenario classes).
- Some tests read source files and locate the repository by looking upward for `NewtonsBiograf.sln`; run the tests from inside the repository.

## 3. Scenario table

Columns: Scenario | Purpose | Steps | Expected result | Test (class.method). Namespace is `NewtonsBiograf.Tests` unless the class is under `Scenarios` (`NewtonsBiograf.Tests.Scenarios`).

### 3.1 Booking seats and double booking (`Scenarios.BookingScenarios`)

| Scenario | Purpose | Steps | Expected result | Test |
|---|---|---|---|---|
| Customer books three seats | Booking total, status, reference | Book 2 Standard + 1 VIP seat for a showing | Status Confirmed, total 480 (2 × 140 + 200), 8-character reference, 3 active `BookingSeat` rows, saved total = sum of seat prices, one `BookingCreated` event | BookingScenarios.Customer_books_three_seats_gets_confirmed_booking_with_total_and_reference |
| Booking a taken seat | No partial booking on conflict | Customer A books seats 1+2; customer B books seats 2+3 | Conflict; seat map shows seats 1+2 taken; counts stay (1 booking, 2 seats); no second event | BookingScenarios.Booking_a_taken_seat_is_rejected_and_leaves_no_partial_rows |
| Two parallel bookings of the same seat | Database prevents double booking | Five rounds: two bookings of overlapping seats run at the same time on a file database | Each round: exactly one success and one conflict; the booking and seat row counts grow by one booking and two seats | BookingScenarios.Two_parallel_bookings_of_the_same_seat_give_one_success_and_one_conflict |

### 3.2 Cancellation (`Scenarios.CancellationScenarios`)

| Scenario | Purpose | Steps | Expected result | Test |
|---|---|---|---|---|
| Customer cancels 3 h before start | Cancel in time, seats freed, booking kept | Clock at start − 3 h; owner cancels | Cancelled with `CancelledAt` set, booking row kept, seats inactive, one `BookingCancelled` event; another customer can book the same seats | CancellationScenarios.Customer_cancels_three_hours_before_start_and_the_seats_can_be_booked_again |
| Customer cancels 1 h before start | Reject too-late cancel | Clock at start − 1 h; owner cancels | Rejected (`BookingTooLate`, limit 2 h); booking still Confirmed, seats active, no event | CancellationScenarios.Customer_cannot_cancel_one_hour_before_start |
| Late cancel through the API | HTTP result of the 2 h rule | Clock at start − 1 h; `POST /api/bookings/{id}/cancel` as owner | 409 Conflict; booking unchanged | CancellationScenarios.Customer_cancelling_one_hour_before_start_gets_409_through_the_API |
| Exactly 2 h before start | Inclusive boundary | Clock at start − 2 h; owner cancels | Allowed; status Cancelled | CancellationScenarios.Customer_can_cancel_exactly_two_hours_before_start |
| 1 h 59 min before start | Just inside the limit | Clock at start − 2 h + 1 min; owner cancels | Rejected; booking unchanged | CancellationScenarios.Customer_cannot_cancel_one_minute_inside_the_two_hour_limit |
| Staff with ManageBookings and admin cancel late | Override of the 2 h rule | Clock at start − 1 h; Staff with ManageBookings, and Admin, cancel (theory, two cases) | Allowed; status Cancelled; one event | CancellationScenarios.Staff_with_ManageBookings_and_Admin_cancel_one_hour_before_start |
| Staff without ManageBookings | Permission check | Staff with only ManageMovies calls cancel | 403; booking unchanged | CancellationScenarios.Staff_without_ManageBookings_get_403 |

### 3.3 Showing overlap (`Scenarios.ShowingOverlapScenarios`)

| Scenario | Purpose | Steps | Expected result | Test |
|---|---|---|---|---|
| Overlapping showing | Reject overlap and name the conflict | Validate a slot starting 60 min after an existing showing in the same auditorium | Conflict; detail contains `'Dune' 19:00–20:55 in Salong 1` (display time zone) | ShowingOverlapScenarios.Overlapping_showing_is_rejected_and_names_the_conflicting_showing |
| Start 1 min before the previous end | Boundary | Start = previous end (incl. 15 min cleaning) − 1 min | Rejected | ShowingOverlapScenarios.Showing_starting_one_minute_before_the_previous_one_ends_is_rejected |
| Back to back | Boundary accepted | Start = previous end incl. cleaning | Accepted; end = start + film length + cleaning | ShowingOverlapScenarios.Back_to_back_showing_starting_when_the_previous_one_ends_including_cleaning_is_accepted |
| Other auditorium | Overlap is per auditorium | Same start time in the other auditorium | Accepted | ShowingOverlapScenarios.Showing_at_the_same_time_in_another_auditorium_is_accepted |

### 3.4 Access (`Scenarios.AccessScenarios`)

| Scenario | Purpose | Steps | Expected result | Test |
|---|---|---|---|---|
| Customer reads another customer's booking | Data isolation | Customer B `GET /api/bookings/{A's booking}` | 403 | AccessScenarios.Customer_reading_another_customers_booking_gets_403 |
| Customer reads own booking | Owner access | Customer A reads own booking | 200 with the booking id | AccessScenarios.Customer_reading_their_own_booking_gets_200 |
| No token | Authentication | `GET /api/bookings/{id}` without a token | 401 | AccessScenarios.Unauthenticated_request_for_a_booking_gets_401 |
| Customer lists all bookings | Permission | Customer `GET /api/bookings?showingId=` | 403 | AccessScenarios.Customer_listing_all_bookings_gets_403 |
| Staff with ManageBookings | Staff access | Staff reads a booking and lists a showing's bookings | 200; list holds both bookings | AccessScenarios.Staff_with_ManageBookings_read_any_booking_and_list_them |
| My bookings | Own data only | Customer B `GET /api/bookings/mine?scope=upcoming` | Only B's booking | AccessScenarios.Mine_returns_only_the_callers_own_bookings |

### 3.5 Create booking through the API (`BookingCreateTests`, US-135, temporary file database)

| Scenario | Purpose | Steps | Expected result | Test |
|---|---|---|---|---|
| Customer books | 201, location, DTO | `POST /api/bookings` with a VIP + a Standard seat | 201, `Location` header, status Confirmed, total 340, seats ordered, stored row matches | BookingCreateTests.Ac1_A_customer_gets_201_with_location_and_the_booking_dto |
| Total price | Standard 140 / VIP 200 | Book 2 Standard + 1 VIP | Total 480 in response and database | BookingCreateTests.Ac2_Two_standard_and_one_VIP_seat_cost_480_with_the_default_prices |
| Invalid seat selection (0 seats, 9 seats, duplicate, other auditorium, unknown seat, missing list) | Validation, nothing saved | Post each invalid selection (theory, six cases) | 400 with an error on `SeatIds`; no booking saved | BookingCreateTests.Ac3_An_invalid_seat_selection_returns_400_keyed_by_SeatIds_and_saves_nothing |
| Eight seats | Upper limit allowed | Book 8 seats | 201; 1 booking, 8 seat rows | BookingCreateTests.Ac3_Eight_seats_are_allowed |
| Unknown showing | 404 | Book showing 9999 | 404; nothing saved | BookingCreateTests.Ac4_An_unknown_showing_returns_404 |
| Cancelled or started showing | Reject booking | Book a cancelled showing, and a showing that started yesterday (theory) | 409 "This showing can no longer be booked."; nothing saved | BookingCreateTests.Ac4_A_cancelled_or_started_showing_returns_409_and_saves_nothing |
| Seat already booked | Conflict with fresh seat map | Book seats 1+2, then seats 2+VIP | 409 with message and seat map; counts (1, 2) | BookingCreateTests.Ac5_A_seat_that_is_already_booked_returns_409_with_the_current_seat_map_and_saves_no_part_of_the_booking |
| Seat taken after the pre-check | Unique index is the last guard | An interceptor books a seat just before the save | 409; the whole booking is rolled back; only the rival booking exists | BookingCreateTests.Ac5_The_unique_index_rejects_a_seat_taken_after_the_pre_check_and_rolls_the_whole_booking_back |
| Two simultaneous requests | One wins | Five rounds of two parallel requests for overlapping seats | One 201 and one 409 per round; counts as expected | BookingCreateTests.Ac6_Two_simultaneous_requests_for_the_same_seat_give_exactly_one_201_and_one_409 |
| BookingCreated event | Raised once | Successful booking | One event with booking, user and showing | BookingCreateTests.Ac7_BookingCreated_is_raised_once_after_a_successful_booking |
| No event on failure | Events only on success | Conflict, invalid, unknown showing | No event | BookingCreateTests.Ac7_No_event_is_raised_when_the_booking_fails |
| Failing subscriber | Event errors do not fail the booking | A subscriber throws | 201; other subscriber still called; booking saved | BookingCreateTests.Ac7_A_failing_subscriber_does_not_fail_the_booking_or_stop_the_other_subscribers |
| Who may book | 401 / 403 / 201 | Anonymous, Staff, Admin, Customer (theory) | 401, 403, 403, 201; saved only for the customer | BookingCreateTests.Ac8_Only_a_customer_can_book |
| Reference format | 8 characters from the unambiguous alphabet | Ten bookings | Every reference matches `^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{8}$` and is unique | BookingCreateTests.Ac9_A_reference_has_8_characters_from_the_unambiguous_alphabet |
| Duplicate reference | Regenerated | Reference generator returns an existing reference twice, then a free one | 201 with the free reference; 3 attempts | BookingCreateTests.Ac9_A_duplicate_reference_is_regenerated_and_the_booking_still_succeeds |
| Reference retries exhausted | Fail cleanly | Generator keeps returning an existing reference | 500; 6 attempts (1 + 5 retries); nothing saved; no event | BookingCreateTests.Ac9_After_five_retries_with_a_duplicate_reference_the_request_fails_and_saves_nothing |
| Rebook cancelled seats | Seats freed | Book, cancel, book the same seats | 201; 2 bookings, 4 seat rows, 2 active | BookingCreateTests.Ac10_The_seats_of_a_cancelled_booking_can_be_booked_again |

### 3.6 Cancel a booking through the API (`CancellationTests`, US-138, temporary file database, fake clock)

| Scenario | Purpose | Steps | Expected result | Test |
|---|---|---|---|---|
| Customer cancels in time | 204, seats freed | Clock at start − 3 h; owner cancels; read seat map; another customer rebooks | 204; Cancelled with `CancelledAt`; seats inactive; map all free; rebooking gives 201 | CancellationTests.Ac1_A_customer_cancels_in_time_and_the_seats_can_be_booked_again |
| Too late | 2 h rule | Clock at start − 1 h; owner cancels | 409 with the too-late message; booking unchanged; no event | CancellationTests.Ac2_Cancelling_too_late_returns_409_and_changes_nothing |
| Exactly 2 h | Inclusive | Clock at start − 2 h | 204 | CancellationTests.Ac3_Exactly_two_hours_before_is_allowed |
| 1 h 59 min | Just inside | Clock at start − 2 h + 1 min | 409; unchanged | CancellationTests.Ac3_One_hour_59_minutes_before_is_too_late |
| Another customer's booking | Ownership | Other customer cancels | 403; unchanged | CancellationTests.Ac4_Another_customers_booking_returns_403_and_stays_unchanged |
| Other customer, late booking | No state leak | Clock after start; other customer cancels | 403 (not 409) | CancellationTests.Ac4_Another_customer_learns_nothing_about_the_state_of_a_late_booking |
| Unknown booking / no token | 404 / 401 | Cancel id 9999; cancel without a token | 404; 401; booking unchanged | CancellationTests.Ac4_An_unknown_booking_returns_404_and_no_token_returns_401 |
| Admin and staff with ManageBookings | Any time, also after start | Clock 1 h after start; admin, then staff with ManageBookings (theory) | 204; Cancelled with `CancelledAt`; one event | CancellationTests.Ac5_Admin_and_staff_with_ManageBookings_cancel_at_any_time_including_after_the_start |
| Staff without ManageBookings | Permission | Staff with ManageMovies cancels | 403; unchanged | CancellationTests.Ac5_Staff_without_ManageBookings_get_403 |
| Cancel a cancelled booking | Idempotence guard | Cancel twice as customer, staff or admin (theory) | Second call 409 "Booking is already cancelled."; `CancelledAt` unchanged; one event | CancellationTests.Ac6_Cancelling_a_cancelled_booking_returns_409_without_a_second_event_or_a_new_CancelledAt |
| History keeps cancelled booking | Booking kept | Cancel, then `GET /api/bookings/mine?scope=history` and `upcoming` | History holds it with the same reference and price; upcoming is empty | CancellationTests.Ac7_A_cancelled_booking_stays_in_the_history_with_the_same_reference_and_price |
| BookingCancelled event | Raised after commit | Cancel | One event; handler sees status Cancelled in the database | CancellationTests.Ac8_BookingCancelled_is_raised_once_after_the_commit |
| No event on rejection | Events only on success | Wrong owner, unknown id, no token, too late | No event | CancellationTests.Ac8_No_event_is_raised_on_any_rejection |

### 3.7 Booking lists and details (`BookingReadTests`, US-136)

| Scenario | Purpose | Steps | Expected result | Test |
|---|---|---|---|---|
| Upcoming | Own, confirmed, not started | `GET /api/bookings/mine?scope=upcoming` | Only the caller's confirmed bookings of future showings, earliest first | BookingReadTests.Ac1_Upcoming_holds_only_the_callers_confirmed_bookings_of_showings_that_have_not_started_earliest_first |
| History | Cancelled and started | `scope=history` | Cancelled and started bookings, latest first, with status and UTC `CancelledAt` | BookingReadTests.Ac2_History_holds_cancelled_and_started_bookings_with_status_latest_first |
| Bad scope | Validation | Missing, empty, `all`, `1` (theory) | 400 | BookingReadTests.Ac3_A_missing_or_unknown_scope_returns_400 |
| Scope case and partition | Each booking once | `UPCOMING`, `History` | Union is all four bookings, no duplicates | BookingReadTests.Ac3_Scope_is_case_insensitive_and_upcoming_plus_history_hold_each_booking_once |
| Owner reads a booking | DTO content | `GET /api/bookings/{id}` as owner | 200; reference, movie, auditorium, total, status; UTC times; seats in row order | BookingReadTests.Ac4_The_owner_gets_the_booking_dto_with_utc_times_and_seats_in_row_order |
| Cancelled booking keeps seats | History | Read a cancelled booking | Status Cancelled; one seat listed | BookingReadTests.Ac4_A_cancelled_booking_keeps_its_seats |
| Another customer's booking | 403 without data | Customer A reads B's booking | 403; body has no reference or title | BookingReadTests.Ac5_Another_customers_booking_returns_403_without_booking_data |
| Unknown booking | 404 | Customer and staff read id 9999 | 404 for both | BookingReadTests.Ac5_An_unknown_id_returns_404_for_customers_and_staff |
| Who can read any booking | Permission | Staff with ManageBookings, Admin, Staff with ManageMovies, Staff without permission (theory) | 200, 200, 403, 403 for detail and search | BookingReadTests.Ac6_Only_staff_with_ManageBookings_and_admin_can_read_any_booking_and_search |
| Staff search | Filters | Search by showing, by reference (any case), both | Matching bookings only | BookingReadTests.Ac6_Search_filters_by_showing_and_reference_and_can_combine_them |
| Search without a filter | Validation | No filter, empty or blank reference (theory) | 400 | BookingReadTests.Ac6_Search_without_a_filter_returns_400 |
| Anonymous | 401 | `mine`, detail, search without token (theory) | 401 | BookingReadTests.Ac6_Anonymous_calls_return_401 |
| Staff on `mine` | Customer-only route | Staff calls `/api/bookings/mine` | 403 | BookingReadTests.Ac6_Staff_cannot_use_the_customer_only_mine_route |
| Isolation on shared showing | No cross-user data | Both customers list upcoming and history | Own bookings only | BookingReadTests.Ac7_Mine_never_contains_another_users_bookings_even_on_the_same_showing |

### 3.8 Showings (`ShowingTests`, US-129 and US-130)

| Scenario | Purpose | Steps | Expected result | Test |
|---|---|---|---|---|
| Create showing | 201, end time | `POST /api/showings` by admin | 201, `Location`, Scheduled, end = start + film + 15 min | ShowingTests.Ac1_Create_returns_201_with_location_and_a_scheduled_showing_ending_after_length_plus_cleaning |
| Offset start | UTC conversion | Start `2090-01-15T19:00:00+02:00` | Stored as 17:00 UTC; readable at `Location` | ShowingTests.Ac1_Create_converts_an_offset_start_to_UTC_and_the_location_can_be_read_back |
| Showing in the past | Rejected | Start one minute ago | 400 on `StartTime`; nothing saved | ShowingTests.Ac2_A_start_in_the_past_returns_400_keyed_by_StartTime_and_saves_nothing |
| Start without offset or missing | Validation | No UTC offset, or no start (theory) | 400 on `StartTime` | ShowingTests.Ac2_A_start_without_UTC_offset_or_missing_returns_400_keyed_by_StartTime |
| Overlapping start | Reject, name the conflict | Five start offsets that overlap an existing showing (theory) | 409 "Overlaps with 'Dune' 18:00–20:55 in Salong 1"; nothing saved | ShowingTests.Ac3_An_overlapping_start_returns_409_naming_the_conflicting_showing_and_saves_nothing |
| Daylight saving time | Message uses display zone | Overlap in July | Detail shows 17:00–19:55 | ShowingTests.Ac3_The_conflict_detail_uses_the_display_time_zone_with_daylight_saving_time |
| Other auditorium | Allowed | Same time, other auditorium | 201 | ShowingTests.Ac4_The_same_time_in_another_auditorium_returns_201 |
| Back to back | Allowed | Start = previous end; end = next start (theory) | 201 | ShowingTests.Ac5_Back_to_back_showings_return_201 |
| Cancelled showing does not block | Only scheduled overlaps count | Create over a cancelled showing; then over the new one | 201; then 409 | ShowingTests.Ac6_A_cancelled_overlapping_showing_does_not_block_but_a_scheduled_one_still_does |
| Unknown movie or auditorium | Validation | Bad movie, bad auditorium, both (theory) | 400 with an error per unknown field | ShowingTests.Ac7_Unknown_movie_or_auditorium_returns_400_with_an_error_on_each_unknown_field |
| Create by caller | 401 / 403 / 201 | Anonymous, Customer, Staff (none, ManageMovies, ManageShowings), Admin (theory) | 401, 403, 403, 403, 201, 201 | ShowingTests.Ac8_Create_by_caller |
| Edit | Update and recalculate end | `PUT` new movie, auditorium, start | 204; end recalculated | ShowingTests.Us130_Ac1_Edit_updates_movie_auditorium_and_start_and_recalculates_the_end_time |
| Edit into the past | Rejected | `PUT` start in the past | 400 on `StartTime`; unchanged | ShowingTests.Us130_Ac1_Edit_with_a_start_in_the_past_returns_400_on_StartTime_and_changes_nothing |
| Edit overlap | Ignores itself | `PUT` onto another showing; `PUT` shifting within its own slot | 409; 204 | ShowingTests.Us130_Ac1_Edit_overlapping_another_showing_returns_409_but_the_showing_itself_is_ignored |
| Cancel showing | Cascade to bookings | Cancel a showing with confirmed and cancelled bookings | 204; showing Cancelled; its bookings Cancelled, seats inactive; other showing untouched | ShowingTests.Us130_Ac2_Cancel_cancels_the_showing_and_every_confirmed_booking_and_frees_the_seats |
| Seats rebookable | Filtered unique index | Cancel showing, insert a new booking of the same seat | Insert succeeds | ShowingTests.Us130_Ac2_The_seats_of_a_cancelled_booking_can_be_booked_again_by_the_database_rules |
| Cancel twice | Idempotent | Cancel a cancelled showing | 204; no new events | ShowingTests.Us130_Ac2_Cancelling_an_already_cancelled_showing_returns_204_without_events |
| Cancel started showing | Allowed | Cancel a showing that started 30 min ago | 204; Cancelled | ShowingTests.Us130_Ac2_A_showing_that_has_already_started_can_be_cancelled |
| Delete without bookings | Allowed | `DELETE` | 204; gone | ShowingTests.Us130_Ac3_Delete_removes_a_showing_without_bookings |
| Delete with bookings | Blocked | Delete with a confirmed or cancelled booking (theory) | 409 "Showing has bookings and can only be cancelled."; kept | ShowingTests.Us130_Ac3_Delete_with_any_booking_returns_409_and_keeps_the_showing |
| Failing cancellation | Rollback | Second booking cancel throws | 500; showing and all bookings unchanged; no events | ShowingTests.Us130_Ac4_A_failing_booking_cancellation_rolls_everything_back_and_raises_no_events |
| Edit blocked by booking | Protect bookings | Edit a showing with a confirmed booking | 409 with the blocked-edit message | ShowingTests.Us130_Ac5_Edit_is_blocked_with_409_when_the_showing_has_a_confirmed_booking |
| Edit blocked when started | Protect history | Edit a started showing | 409 | ShowingTests.Us130_Ac5_Edit_is_blocked_with_409_when_the_showing_has_already_started |
| Edit with cancelled bookings only | Allowed | Edit | 204 | ShowingTests.Us130_Ac5_Edit_is_allowed_when_the_only_bookings_are_cancelled |
| Edit cancelled showing | Blocked | Edit | 409 "Cancelled showings cannot be edited." | ShowingTests.Us130_Ac5_A_cancelled_showing_cannot_be_edited |
| One event per booking | Events | Cancel showing with two confirmed and one cancelled booking | Two `BookingCancelled` events | ShowingTests.Us130_Ac6_One_BookingCancelled_event_is_raised_per_cancelled_booking |
| Failing subscriber | Commit stays | Subscriber throws | 204; showing and booking Cancelled | ShowingTests.Us130_Ac6_A_failing_event_subscriber_does_not_undo_the_committed_cancellation |
| Access to edit, cancel, delete, read and list | 401 / 403 / 404 | Unknown showing as each caller (theory) | 401, 403, 403, 403, 404, 404 (list: 200 for permitted callers) | ShowingTests.Us130_Ac7_Edit_cancel_delete_and_read_of_an_unknown_showing_by_caller |
| Get showing | Booking count | Read showing with one confirmed and one cancelled booking | `ActiveBookingCount` = 1 | ShowingTests.Us130_Get_returns_the_showing_with_its_confirmed_booking_count |
| List showings | Filter and order | Period and auditorium filter | Ordered by start; counts per showing | ShowingTests.Us130_List_filters_by_period_and_auditorium_and_orders_by_start_time |
| List with bad period | Validation | Missing `from`, missing `to`, reversed (theory) | 400 on the field | ShowingTests.Us130_List_with_a_missing_or_reversed_period_returns_400 |

### 3.9 Seat map (`SeatMapTests`, US-134)

| Scenario | Purpose | Steps | Expected result | Test |
|---|---|---|---|---|
| Anonymous seat map | Public, ordered, priced | `GET /api/showings/{id}/seats` without token | Every seat ordered by row and number, with type and price (Standard 140, VIP 200) | SeatMapTests.US134_Ac1_Anonymous_request_returns_every_seat_ordered_with_type_and_configured_price |
| No personal data | Privacy | Map of a booked showing | Body has no user id or reference | SeatMapTests.US134_Ac1_The_map_holds_no_user_or_booking_data |
| Taken and freed | Seat state | Confirmed booking, then cancel | Seats taken, then free | SeatMapTests.US134_Ac2_A_seat_in_a_confirmed_booking_is_taken_and_free_again_after_cancel |
| Other showing | Isolation | Seat booked for another showing | Stays free here | SeatMapTests.US134_Ac2_A_seat_booked_for_another_showing_stays_free |
| Own auditorium | Isolation | Showing in the other auditorium | Only that auditorium's seats | SeatMapTests.US134_Ac2_Only_the_seats_of_the_showings_own_auditorium_are_listed |
| Unknown or cancelled showing | 404 | Request both | 404 `application/problem+json` | SeatMapTests.US134_Ac3_Unknown_and_cancelled_showings_return_404_problem_details |
| Started showing | Viewable, not bookable | Started, starting now, starting in 1 s | `IsBookable` false, false, true; seats still listed | SeatMapTests.US134_Ac4_A_showing_that_has_started_is_viewable_but_not_bookable |

### 3.10 Public browsing (`PublicBrowsingTests`, US-132 and US-133, fixed clock)

| Scenario | Purpose | Steps | Expected result | Test |
|---|---|---|---|---|
| Default list | Anonymous, window, order | `GET /api/public/showings` | Showings from now to now + 14 days, by start time, with movie data and free seats | PublicBrowsingTests.Ac1_Default_list_is_anonymous_and_holds_the_showings_in_the_window_sorted_by_start_time |
| Empty schedule | Empty list | No showings | 200 `[]` | PublicBrowsingTests.Ac1_An_empty_schedule_returns_200_with_an_empty_list |
| Genre filter | Case-insensitive | `genre=Horror`, `horror`, `Sci-Fi`, `scifi` (theory) | Only that genre | PublicBrowsingTests.Ac2_Genre_filter_returns_only_that_genre |
| Date filter | Display-zone day | Showings near local midnight | Matched by Stockholm day | PublicBrowsingTests.Ac2_Date_filter_uses_the_display_time_zone_day |
| Auditorium filter | By id | `auditoriumId=` | Only that auditorium | PublicBrowsingTests.Ac2_AuditoriumId_filter_returns_only_that_auditorium |
| Title filter | Partial, any case, Swedish letters | `dun`, `DUN`, `ÅSA`, `RÄKSM`, ... (theory) | Matching movie | PublicBrowsingTests.Ac2_Title_filter_matches_partly_and_ignores_case_including_swedish_letters |
| Title wildcards | No injection of `%` / `_` | `%`, `_une` (theory) | Empty list | PublicBrowsingTests.Ac2_Title_wildcards_are_matched_literally |
| Combined filters | AND | Genre + title + auditorium | Only showings meeting all | PublicBrowsingTests.Ac3_Combined_filters_return_only_showings_that_satisfy_all_of_them |
| Excluded showings | Cancelled, past, beyond 14 days | Seed such showings | Never listed | PublicBrowsingTests.Ac4_Cancelled_past_and_beyond_14_days_showings_never_appear_even_when_the_filters_match |
| Date outside window | Empty | Dates −1, 15, 400 days (theory) | 200 `[]` | PublicBrowsingTests.Ac4_A_date_outside_the_window_returns_200_with_an_empty_list |
| Free seats | Seats − confirmed | Confirmed and cancelled bookings | Cancelled bookings free their seats | PublicBrowsingTests.Ac5_FreeSeats_is_the_seat_count_minus_seats_of_confirmed_bookings_only |
| Bad date or auditorium id | Validation | `date=abc`, `2026-13-45`, `03-11-2026`, `auditoriumId=x` (theory) | 400 `application/problem+json` | PublicBrowsingTests.Ac6_Invalid_date_or_auditoriumId_returns_400_problem_details |
| Long title | Validation | 101 characters | 400 | PublicBrowsingTests.Ac6_A_title_longer_than_100_characters_returns_400 |
| Unknown genre / auditorium | Empty, not error | Unknown genre; unknown auditorium; empty filters | 200 `[]`; empty filters ignored | PublicBrowsingTests.Ac6_Unknown_genre_or_auditorium_returns_200_with_an_empty_list_and_empty_filters_are_ignored |
| Staff route protected | 401 / 403 | `GET /api/showings` as anonymous, Customer (theory) | 401, 403 | PublicBrowsingTests.Ac7_Staff_showings_route_still_requires_ManageShowings |
| Genres and auditoriums | Dropdown data | `GET /api/public/genres`, `/auditoriums` | Anonymous, sorted | PublicBrowsingTests.Public_genres_and_auditoriums_are_anonymous_and_sorted |
| Movie detail | Own upcoming showings | `GET /api/public/movies/{id}` | Movie with only its upcoming scheduled showings | PublicBrowsingTests.US133_Ac3_Movie_detail_is_anonymous_and_holds_the_movie_with_only_its_own_upcoming_showings |
| Movie without showings | Empty list | Movie with no showings | 200, empty `Showings` | PublicBrowsingTests.US133_Ac3_A_movie_without_upcoming_showings_returns_200_with_an_empty_list |
| Unknown movie | 404 | id 9999 | 404 | PublicBrowsingTests.US133_Ac3_An_unknown_movie_id_returns_404 |

### 3.11 Movies (`MovieTests`)

| Scenario | Purpose | Steps | Expected result | Test |
|---|---|---|---|---|
| Create movie | 201, stored values | `POST /api/movies` as staff with ManageMovies | 201, `Location`, trimmed title, stored values equal input | MovieTests.Ac1_Create_returns_201_with_the_movie_and_stored_values_match_input |
| Optional description, order | List by title | Create two movies | Ordered by title; description null | MovieTests.Ac1_Description_is_optional_and_list_is_ordered_by_title |
| Invalid fields | 400 per field | Empty title, length 0, 2001-character description | One message per field; nothing saved | MovieTests.Ac2_Invalid_fields_return_400_keyed_by_field_and_save_nothing |
| Title and length limits | Validation | Whitespace title; length 401 and −5 (theory) | 400 with message | MovieTests.Ac2_Whitespace_title_and_out_of_range_length_are_rejected |
| Title length | Boundary | 201 and 200 characters | 400; 201 | MovieTests.Ac2_Title_over_200_characters_is_rejected_and_200_is_accepted |
| Bad genre / age rating | Strict enums | Unknown, numeric or null values (theory) | 400 keyed by field | MovieTests.Ac2_Unknown_numeric_or_null_genre_and_age_rating_return_400_keyed_by_field |
| Missing genre and age rating | Required | Omit both | "Genre is required", "Age rating is required" | MovieTests.Ac2_Missing_genre_and_age_rating_say_they_are_required |
| Update movie | Route id wins | `PUT` with a different id in the body | 204; the routed movie changed | MovieTests.Ac3_PUT_valid_returns_204_and_persists_and_the_route_id_wins |
| Update invalid | 400 | `PUT` invalid | 400; unchanged | MovieTests.Ac3_PUT_invalid_returns_400_and_changes_nothing |
| Update unknown | 404 | `PUT` id 9999 | 404 | MovieTests.Ac3_PUT_unknown_id_returns_404 |
| Get unknown | 404 | `GET` id 9999 | 404 | MovieTests.Ac3_GET_unknown_id_returns_404 |
| Delete with future showing | Blocked | Delete | 409 "Movie cannot be deleted because it has future showings."; kept | MovieTests.Ac4_Delete_with_a_future_scheduled_showing_returns_409_and_keeps_the_movie |
| Delete without showings | Allowed | Delete | 204; then 404 | MovieTests.Ac5_Delete_movie_without_showings_returns_204_and_it_is_gone |
| Delete unknown | 404 | id 9999 | 404 | MovieTests.Ac5_Delete_unknown_id_returns_404 |
| Delete with past or cancelled showings | History kept | Delete (theory) | 409 "Movie cannot be deleted because it has showings." | MovieTests.Ac5_Movie_with_only_past_or_cancelled_showings_is_kept_with_409_because_history_is_never_cascaded |
| Write access | 401 / 403 / allowed | Anonymous, Customer, wrong staff permission, ManageMovies, Admin (theory) | 401, 403, 403, success, success | MovieTests.Ac6_Write_endpoints_by_caller |
| Anonymous reads | Public | `GET` list and detail | 200 | MovieTests.Ac6_Anonymous_GET_list_and_detail_return_200 |
| Display names | Wire value vs display | `ToDisplayName()`, JSON | "Sci-Fi", "All ages", "7"; JSON `"SciFi"` | MovieTests.Enum_display_names_are_shown_to_users_while_wire_values_stay_enum_names |

### 3.12 Auditoriums (`AuditoriumTests`)

| Scenario | Purpose | Steps | Expected result | Test |
|---|---|---|---|---|
| Create auditorium | Seats generated | `POST` 3 rows × 4 seats as admin | 201, `Location`, capacity 12, 12 Standard seats numbered by row and number | AuditoriumTests.Ac1_Create_returns_201_with_location_capacity_and_exactly_the_generated_standard_seats |
| Boundary sizes | Limits | 1×1 and 26×40 | Both accepted; 1 + 1040 seats | AuditoriumTests.Ac1_Create_accepts_the_boundary_sizes |
| Duplicate name | Case and spaces ignored | Create `  SALONG 1 ` after `Salong 1` | 400 on `Name`; nothing created | AuditoriumTests.Ac2_Create_with_a_name_equal_ignoring_case_and_spaces_returns_400_and_creates_nothing |
| Rename | Own name allowed | `PUT` another's name; own name in other case | 400; 204 | AuditoriumTests.Ac2_Put_rejects_another_auditoriums_name_but_allows_its_own_in_a_different_case |
| Invalid fields | 400 per field | Blank name, rows 0/27/−1, seats per row 0/41 (theory) | 400 keyed by field; nothing created | AuditoriumTests.Ac3_Invalid_fields_return_400_keyed_by_field_and_create_nothing |
| Name length, missing fields | Boundary | 101 and 100 characters; empty body | 400; 201; all missing fields reported | AuditoriumTests.Ac3_Name_over_100_characters_is_rejected_and_100_is_accepted_and_missing_fields_are_all_reported |
| Non-integer numbers | Binding errors | Rows 1.5 / "abc", seats per row 2.5 (theory) | 400 keyed by field | AuditoriumTests.Ac3_Non_integer_numbers_fail_binding_with_400_keyed_by_field |
| Put invalid | 400 | `PUT` invalid | 400; unchanged | AuditoriumTests.Ac3_Put_invalid_returns_400_and_changes_nothing |
| Anonymous list | Order, capacity, VIP rows | `GET /api/auditoriums` | Ordered by name; capacity; sorted VIP rows (partly VIP row excluded) | AuditoriumTests.Ac4_Anonymous_list_is_ordered_by_name_and_shows_capacity_and_sorted_vip_rows_per_auditorium |
| Get by id | 200 / 404 | Known and unknown id | 200 with data; 404 | AuditoriumTests.Ac4_Anonymous_get_by_id_returns_200_and_unknown_id_returns_404 |
| Rename only | Seats untouched | `PUT` name only | 204; same seat ids | AuditoriumTests.Ac5_Put_name_only_returns_204_and_leaves_every_seat_untouched |
| Grow layout | Seats kept, new seats follow row type | Grow 3×4 to 4×6 | Old seats keep ids; new seats take the row's type | AuditoriumTests.Ac5_Growing_the_layout_keeps_existing_seats_and_new_seats_take_their_rows_type |
| Shrink layout | Extra seats removed | Shrink to 2×2 | 4 seats remain, same ids | AuditoriumTests.Ac5_Shrinking_the_layout_deletes_extra_seats_and_keeps_the_rest_with_the_same_ids |
| Put unknown | 404 | id 9999 | 404 | AuditoriumTests.Ac5_Put_unknown_id_returns_404 |
| Layout change with future booking | Protect bookings | Active booking in a future showing; change layout | 409 "Layout cannot be changed because the auditorium has bookings."; unchanged | AuditoriumTests.Ac6_Layout_change_with_an_active_booking_in_a_future_showing_returns_409_and_changes_nothing |
| Remove a booked seat | History kept | Remove a seat that was ever booked | 409 | AuditoriumTests.Ac6_Removing_a_seat_that_was_ever_booked_returns_409_even_for_a_past_showing |
| Allowed layout change | Past, cancelled or inactive bookings | Three cases (theory) | 204; 4 seats | AuditoriumTests.Ac6_Layout_change_is_allowed_when_bookings_are_past_cancelled_or_inactive_and_seats_that_stay |
| Rename with future booking | Allowed | Name-only change | 204 | AuditoriumTests.Ac6_Name_only_change_is_allowed_even_with_future_bookings |
| Booking elsewhere | Scoped | Booking in another auditorium | 204 | AuditoriumTests.Ac6_A_booking_in_another_auditorium_does_not_block_the_change |
| Write access | 401 / 403 / allowed | Anonymous, Customer, Staff with ManageShowings, Admin (theory) | 401, 403, 403, 201 + 204 | AuditoriumTests.Ac7_Write_endpoints_by_caller |
| VIP row | Row type | `PUT .../rows/2/type` VIP, then Standard | 204; only that row changes; `VipRows` updated | AuditoriumTests.Us127_Ac1_Setting_a_row_to_VIP_changes_only_that_rows_seats_and_Standard_sets_it_back |
| Idempotent VIP | Partly VIP row | Repeat VIP | 204 twice; row fully VIP | AuditoriumTests.Us127_Ac1_Repeating_the_same_call_is_idempotent_and_a_partly_VIP_row_becomes_fully_VIP |
| Only addressed auditorium | Isolation | Set a row VIP | Other auditorium unchanged | AuditoriumTests.Us127_Ac1_Only_the_addressed_auditorium_changes |
| Row outside auditorium | 404 | Row 0, 4, −1 (theory) | 404; unchanged | AuditoriumTests.Us127_Ac2_Row_outside_the_auditorium_returns_404_and_changes_nothing |
| Unknown auditorium | 404 | id 9999 | 404 | AuditoriumTests.Us127_Ac2_Unknown_auditorium_returns_404 |
| Bad type | Validation | `Gold`, empty, 1, null (theory) | 400 on `Type`; unchanged | AuditoriumTests.Us127_Ac2_Unknown_or_missing_Type_returns_400_keyed_by_Type_and_changes_nothing |
| Body without type | Validation | `{}` | 400 on `Type` | AuditoriumTests.Us127_Ac2_A_body_without_Type_returns_400 |
| Row type access | 401 / 403 | Anonymous, Customer, Staff (theory) | 401, 403, 403; unchanged | AuditoriumTests.Us127_Ac2_Only_an_admin_may_change_a_row_type |
| Existing bookings | Prices untouched | Change a booked row to VIP | Booking total and seat price unchanged | AuditoriumTests.Us127_Ac6_Changing_a_row_type_leaves_existing_bookings_and_their_prices_untouched |

### 3.13 Authentication (`AuthTests`) and authorization (`AuthorizationTests`)

| Scenario | Purpose | Steps | Expected result | Test |
|---|---|---|---|---|
| Register | 201, Customer role | `POST /api/auth/register` | 201; role Customer; display name stored | AuthTests.Ac1_Register_returns_201_and_assigns_the_Customer_role |
| Role missing | Fail cleanly | Delete the Customer role, register | 500; no user created | AuthTests.Ac1_Register_fails_with_500_and_creates_no_user_when_the_Customer_role_is_missing |
| Short password | Validation | 7 characters | 400 on `Password`; no account | AuthTests.Ac2_Password_shorter_than_8_characters_returns_400_and_creates_no_account |
| Duplicate email | Validation | Same email, other case | 400 on `Email`; one account | AuthTests.Ac2_Duplicate_email_returns_400_on_Email_and_creates_no_second_account |
| Name / email fields | Validation | Empty name, bad email | Errors on `Name`, `Email`, none on `Password` | AuthTests.Ac2_Missing_name_and_invalid_email_return_per_field_errors |
| Login | Signed JWT | Log in with a permission claim | 200; token valid with issuer, audience and key; claims sub, email, name, role, permission; expiry about 60 min | AuthTests.Ac3_Login_returns_200_with_a_signed_JWT_carrying_the_claims |
| Wrong credentials | Same 401 message | Wrong password; unknown email | Both 401 "Invalid email or password." | AuthTests.Ac3_Wrong_password_and_unknown_email_return_the_same_generic_401 |
| Privilege escalation | Role fields ignored | Register with Role Admin and permissions | Customer role; no claims | AuthTests.Ac4_Role_and_permission_fields_in_the_payload_are_ignored |
| Weak JWT key | Startup fails | Key shorter than 32 characters | Startup error mentions `Jwt:Key` | AuthTests.Startup_fails_when_the_JWT_key_is_shorter_than_32_characters |
| Policies registered | All five policies | Look up each policy (theory) | Found | AuthorizationTests.Ac1_Policy_is_registered |
| Status matrix | 401 / 403 / 200 | Probe endpoint per policy for anonymous, Customer, Staff with/without claim, Admin (theory) | 401 anonymous; 403 without role or permission; 200 with it; Admin passes permission policies | AuthorizationTests.Ac2_Status_matrix |
| Bad tokens | 401 | Expired token; token signed with another key | 401; a valid token gives 200 | AuthorizationTests.Ac2_Expired_token_and_token_signed_with_another_key_return_401 |
| 401 body | Problem JSON, no redirect | Anonymous call | 401 `application/problem+json`, title "Authentication required", no `Location` | AuthorizationTests.Ac3_401_returns_problem_json_without_redirect |
| 403 body | Problem JSON, no redirect | Customer on a staff route | 403 `application/problem+json`, title "You do not have access to this resource" | AuthorizationTests.Ac3_403_returns_problem_json_without_redirect |

### 3.14 Admin staff (`AdminStaffTests`)

| Scenario | Purpose | Steps | Expected result | Test |
|---|---|---|---|---|
| Create staff | Role and claims | Admin `POST /api/admin/staff` with permissions (one duplicate) | 201; role Staff; one claim per distinct permission; no password in response; can log in | AdminStaffTests.Ac1_Admin_creates_staff_with_role_Staff_and_one_claim_per_permission |
| No permissions | Allowed | Create without permissions | 201; no claims | AdminStaffTests.Ac1_Staff_can_be_created_with_no_permissions |
| Role missing | Fail cleanly | Delete the Staff role | 500; no account | AdminStaffTests.Ac1_Fails_with_500_and_leaves_no_account_when_the_Staff_role_is_missing |
| POST access | 401 / 403 / 201 | Anonymous, Customer, Staff, Admin (theory) | 401, 403, 403, 201 | AdminStaffTests.Ac2_POST_status_by_caller |
| GET access | 401 / 403 / 200 | Same callers (theory) | 401, 403, 403, 200 | AdminStaffTests.Ac2_GET_status_by_caller |
| Unknown permission | Validation | `Fly`, `managemovies` | 400 naming each invalid value; nothing created | AdminStaffTests.Ac3_Unknown_permission_returns_400_naming_each_invalid_value_and_creates_nothing |
| Short password | Validation | 7 characters | 400 on `Password` | AdminStaffTests.Ac3_Seven_character_password_returns_400_on_Password_and_creates_nothing |
| Duplicate email | Validation | Existing customer's email | 400 on `Email` | AdminStaffTests.Ac3_Duplicate_email_returns_400_on_Email_and_creates_no_second_account |
| Name / email | Validation | Empty name, bad email | Per-field errors | AdminStaffTests.Ac3_Missing_name_and_invalid_email_return_per_field_errors |
| List staff | Only staff, by name | Seed staff, admin, customer | Only staff, ordered by name, with permissions | AdminStaffTests.Ac4_List_returns_only_staff_ordered_by_name_with_their_permissions |
| Empty list | Empty | No staff | `[]` | AdminStaffTests.Ac4_List_is_empty_when_there_is_no_staff |
| Replace permissions | Other claims kept | `PUT .../permissions` | 204; permission set replaced; unrelated claim kept | AdminStaffTests.US122_Ac1_PUT_replaces_the_permission_set_and_leaves_other_claims_alone |
| Same set | Idempotent | `PUT` same set | 204; unchanged | AdminStaffTests.US122_Ac1_PUT_with_the_same_set_is_idempotent |
| Remove all | Effective at next login | `PUT` empty list; log in again | 204; no claims; probe route 403 after re-login | AdminStaffTests.US122_Ac2_Empty_list_removes_all_permissions_and_the_next_login_gets_403_on_ManageMovies |
| Unknown user | 404 | Random id | 404 | AdminStaffTests.US122_Ac3_Unknown_user_returns_404 |
| Admin or Customer target | Only staff editable | Target role Admin or Customer (theory) | 400; claims unchanged | AdminStaffTests.US122_Ac3_Target_in_role_Admin_or_Customer_returns_400_and_claims_are_unchanged |
| Non-admin caller | 401 / 403 | Anonymous, Customer, Staff (theory) | 401, 403, 403; unchanged | AdminStaffTests.US122_Ac3_Non_admin_caller_is_rejected_and_claims_are_unchanged |
| Unknown permission on PUT | Validation | Valid + `Foo` | 400 on `Permissions`; unchanged | AdminStaffTests.US122_Ac3_Unknown_permission_returns_400_on_Permissions_and_claims_are_unchanged |
| Missing list | Validation | `{}` | 400; unchanged | AdminStaffTests.US122_Ac3_Missing_permissions_list_returns_400_and_claims_are_unchanged |

### 3.15 Data model, pricing and health (`DataTests`, `PricingTests`, `HealthTests`)

| Scenario | Purpose | Steps | Expected result | Test |
|---|---|---|---|---|
| Migration tables | Schema | Apply migrations | All entity and Identity tables exist | DataTests.Ac1_Migration_creates_entity_and_identity_tables |
| Enums as strings | Storage | Save a movie | Stored `SciFi\|Age11` | DataTests.Ac1_Enums_are_stored_as_strings |
| Length constraint | Data integrity | Movie length 0 | `DbUpdateException` | DataTests.Ac1_Movie_length_must_be_positive |
| Unique auditorium name | Index | Duplicate name | `DbUpdateException` | DataTests.Ac2_Auditorium_name_index_exists_and_duplicate_name_throws |
| Double booking in the database | Filtered unique index | Two active `BookingSeat` for the same showing and seat | `DbUpdateException` | DataTests.Ac2_Second_active_booking_seat_for_same_showing_and_seat_throws |
| Inactive seat row | Index allows rebook | Cancelled (inactive) row plus a new active row | Saves; 3 rows, 2 active | DataTests.Ac2_Inactive_booking_seat_does_not_block_a_new_active_one |
| Unique reference | Index | Two bookings, same reference | `DbUpdateException` | DataTests.Ac2_Duplicate_booking_reference_throws |
| Generic repository | CRUD | Add, list, filter, update, remove | Works | DataTests.Ac3_Repository_supports_add_get_list_update_and_remove |
| Cancellation token | Honoured | Cancelled token | `OperationCanceledException` | DataTests.Ac3_Repository_honours_a_cancelled_token |
| Repository signature | Async methods take a token | Reflect over `IRepository<>` | 4 async methods, each with `CancellationToken` | DataTests.Ac3_Every_async_repository_method_accepts_a_cancellation_token |
| Open generic registration | DI | Resolve `IRepository<Movie>`, `IRepository<Showing>` | Resolved | DataTests.Ac3_Repository_is_registered_open_generic |
| Prices from configuration | Standard 140 / VIP 200 | Read calculator | 140 and 200 | PricingTests.Ac3_Calculator_returns_the_prices_from_appsettings |
| Changed configuration | No code change | Override both prices | 95 and 310 | PricingTests.Ac3_Changing_configuration_changes_the_prices_without_code_changes |
| Unknown seat type | Failure | Seat type 99 | `ArgumentOutOfRangeException` | PricingTests.Ac3_Calculator_rejects_an_unknown_seat_type |
| Pricing endpoint | Anonymous | `GET /api/pricing` | 200 with configured prices | PricingTests.Ac3_Pricing_endpoint_is_anonymous_and_returns_the_configured_prices |
| Bad price configuration | Startup fails | Zero, negative, empty prices (theory) | Startup error names the key | PricingTests.Ac5_Startup_fails_with_a_clear_message_for_a_missing_or_non_positive_price |
| No price literals | Configuration only | Scan production code | No `140` / `200` literals | PricingTests.Ac4_Production_code_has_no_price_literals |
| Health | Anonymous | `GET /api/health` | 200 `ok` | HealthTests.Ac3_Health_returns_200_with_status_ok_without_authentication |
| Swagger in Development | Docs | `GET /swagger/v1/swagger.json` | 200 with health route and bearer scheme | HealthTests.Ac3_Swagger_is_served_in_Development |
| Swagger in Production | Hidden | Same call | 404 | HealthTests.Ac3_Swagger_is_not_served_in_Production |
| CORS | One origin | Configured origin; other origin | Header only for the configured origin | HealthTests.Ac3_Cors_allows_the_configured_client_origin_only |

### 3.16 Client: typed API clients and helpers (no browser)

| Scenario | Purpose | Steps | Expected result | Test |
|---|---|---|---|---|
| Auth: claims | Parse JWT | `JwtParser.ParseClaims` | Name, role, every permission | ClientAuthTests.Ac1_ParseClaims_maps_name_role_and_every_permission |
| Auth: single permission | String form | One permission as plain string | One claim | ClientAuthTests.Ac1_ParseClaims_reads_a_single_permission_written_as_a_plain_string |
| Auth: bad token | Robust | Empty, malformed (theory) | `null` | ClientAuthTests.Ac1_ParseClaims_returns_null_for_a_malformed_token |
| Auth: expired token | Robust | Expired; no `exp` | `null` | ClientAuthTests.Ac1_ParseClaims_returns_null_for_an_expired_token_or_one_without_exp |
| Auth: login | Store token | `Login` | Token in storage; state change raised; user exposed | ClientAuthTests.Ac1_Login_stores_the_token_notifies_and_exposes_the_user |
| Auth: expired stored token | Anonymous | Expired token in storage | Anonymous; token removed | ClientAuthTests.Ac1_An_expired_stored_token_means_anonymous_and_is_removed |
| Auth: logout | Clear | `Logout` | Token removed; anonymous | ClientAuthTests.Ac3_Logout_removes_the_token_and_the_user_is_anonymous |
| Auth: bearer header | Attach token | Send with and without login | Header only when signed in | ClientAuthTests.Ac1_Handler_attaches_the_Bearer_token_when_present_and_not_when_absent |
| Auth: 401 | Sign out | 401 from a protected route | Token removed; redirect to login with return address | ClientAuthTests.Ac3_Handler_logs_out_and_sends_the_user_to_login_on_401_from_a_protected_route |
| Auth: 401 from login | Left to form | 401 from `/api/auth/login` | No redirect | ClientAuthTests.Ac1_Handler_leaves_a_401_from_the_login_route_to_the_form |
| Auth: 403 | No access page | 403 | Redirect to `no-access`; token kept | ClientAuthTests.Ac3_Handler_shows_the_no_access_page_on_403_and_keeps_the_token |
| Auth: return URL | Open redirect guard | Local and external addresses (theory) | Only in-app addresses allowed | ClientAuthTests.Ac1_SafeReturnUrl_only_allows_addresses_inside_the_app |
| Auth: field errors | Mapping | Validation problem | Mapped to fields, case-insensitive | ClientAuthTests.Ac1_ApiProblem_maps_validation_errors_to_fields_case_insensitively |
| Auth: general errors | Mapping | Errors without a field | Shown as general message | ClientAuthTests.Ac1_ApiProblem_shows_errors_not_tied_to_a_field_in_the_general_message |
| Auth: fallback | Plain language | 401 title; unreadable body | Title; "Something went wrong. Please try again." | ClientAuthTests.Ac1_ApiProblem_uses_the_title_for_a_401_login_and_a_fallback_for_an_unreadable_body |
| Movies: list | Typed client | `GetAllAsync` | Movies with enum names | MovieClientTests.US125_Ac1_GetAllAsync_reads_the_movies_with_enum_names |
| Movies: create / update | Typed client | `CreateAsync`, `UpdateAsync` | POST / PUT with enum names | MovieClientTests.US125_Ac1_CreateAsync_posts_and_UpdateAsync_puts_to_the_row_id_with_enums_as_names |
| Movies: validation | Field mapping | 400 problem | Errors on matching fields | MovieClientTests.US125_Ac2_Validation_errors_map_to_the_matching_fields_case_insensitively |
| Movies: general error | Mapping | Non-field key | General message | MovieClientTests.US125_Ac2_An_error_key_that_is_not_a_form_field_goes_to_the_general_message |
| Movies: 409 | Message | Delete conflict | API detail; not success | MovieClientTests.US125_Ac3_A_409_returns_the_API_detail_and_the_call_is_not_a_success |
| Movies: 204 / 404 | Delete | 204; 404 | Success; not-found flag | MovieClientTests.US125_Ac3_A_204_delete_succeeds_and_a_404_is_flagged_so_the_page_reloads |
| Movies: 403 / offline | Plain messages | 403; dead connection | Permission message; unreachable message | MovieClientTests.US125_Ac1_A_403_shows_the_permission_message_and_a_dead_connection_the_unreachable_message |
| Movies: unreadable body | No throw | HTML body on 200 | Unexpected-error result | MovieClientTests.US125_Ac1_An_unreadable_success_body_is_reported_instead_of_throwing |
| Auditoriums: read | Typed client | `ListAsync`, `GetAsync` | Capacity and VIP rows | AuditoriumClientTests.US128_Ac1_ListAsync_and_GetAsync_read_the_auditoriums_with_capacity_and_vip_rows |
| Auditoriums: write | Typed client | `CreateAsync`, `UpdateAsync` | POST / PUT with name, rows, seats per row | AuditoriumClientTests.US128_Ac1_CreateAsync_posts_and_UpdateAsync_puts_the_name_rows_and_seats_per_row |
| Auditoriums: row type | Typed client | `SetRowTypeAsync` | PUT to the row route with type name | AuditoriumClientTests.US128_Ac2_SetRowTypeAsync_puts_the_type_name_to_the_row_route |
| Auditoriums: failed row change | Revert support | 404; dead connection | Failure reported | AuditoriumClientTests.US128_Ac2_A_failed_row_change_is_reported_so_the_page_can_revert_the_row |
| Auditoriums: prices | Read-only prices | `GetPricesAsync` | 140 / 200 | AuditoriumClientTests.US128_Ac3_GetPricesAsync_reads_the_standard_and_vip_prices |
| Auditoriums: validation | Field mapping | 400 problem | Errors on Name, Rows, SeatsPerRow | AuditoriumClientTests.US128_Ac4_Duplicate_name_and_range_errors_map_to_their_own_fields |
| Auditoriums: layout conflict | Message | 409 | General error | AuditoriumClientTests.US128_Ac4_A_409_layout_conflict_returns_the_API_message_as_a_general_error |
| Auditoriums: 403 | Message | 403 | Permission message | AuditoriumClientTests.US128_Ac5_A_403_shows_the_permission_message |
| Auditoriums: dead or garbled | No throw | Offline; HTML; `null` | Unreachable / unexpected | AuditoriumClientTests.US128_Ac6_A_dead_connection_or_unreadable_body_is_reported_instead_of_throwing |
| Showings: list | Typed client | `ListAsync` with range | Range sent as UTC with `Z`; showings read | ShowingClientTests.US131_Ac1_ListAsync_sends_the_range_as_UTC_with_Z_and_the_auditorium_and_reads_the_showings |
| Showings: settings | Typed client | `GetSettingsAsync` | Cleaning minutes | ShowingClientTests.US131_Ac1_GetSettingsAsync_reads_the_cleaning_minutes |
| Showings: create | Typed client | `CreateAsync` | Start as ISO UTC with `Z` | ShowingClientTests.US131_Ac1_CreateAsync_posts_ids_and_the_start_as_ISO_UTC_with_Z |
| Showings: time helpers | Local to UTC | `ShowingTimes.ToUtc`, `End` | UTC; end = start + film + cleaning | ShowingClientTests.US131_Ac1_Local_entry_converts_to_UTC_and_the_end_preview_adds_film_length_and_cleaning |
| Showings: errors | Mapping | 400 start; 409 overlap | Field error; general message | ShowingClientTests.US131_Ac2_A_400_maps_StartTime_to_the_field_and_a_409_overlap_detail_is_the_general_message |
| Showings: cancel | Typed client | `CancelAsync` | POST cancel route; 404 flagged | ShowingClientTests.US131_Ac3_CancelAsync_posts_to_the_cancel_route_and_a_404_is_flagged |
| Showings: edit / delete | Typed client | `UpdateAsync`, blocked edit, blocked delete | PUT; API messages | ShowingClientTests.US131_Ac4_UpdateAsync_puts_and_a_409_from_edit_or_delete_returns_the_API_message |
| Showings: 403 / offline | Plain messages | 403; dead connection | Permission message; unreachable | ShowingClientTests.US131_Ac5_A_403_shows_the_permission_message_and_a_dead_connection_the_unreachable_message |
| Showings page access | Policy | Page attribute | `ManageShowings` policy | ShowingClientTests.US131_Ac5_The_showings_page_requires_the_ManageShowings_policy |
| Showing settings endpoint | Anonymous | `GET /api/showings/settings`; list without token | 200 with 15; list 401 | ShowingClientTests.US131_Ac1_The_settings_endpoint_is_anonymous_and_the_list_still_needs_a_token |
| Public: filters | Query string | `GetShowingsAsync` with filters | Escaped, empty filters left out | PublicClientTests.Ac2_GetShowingsAsync_sends_the_filters_escaped_and_leaves_out_empty_ones |
| Public: read | Typed client | Showings, genres, auditoriums | Read from their routes | PublicClientTests.Ac1_GetShowingsAsync_reads_the_showings_and_genres_and_auditoriums_are_read_from_their_routes |
| Public: movie | 404 flag | `GetMovieAsync` | Movie read; 404 flagged | PublicClientTests.Ac3_GetMovieAsync_reads_the_movie_and_a_404_is_flagged_as_not_found |
| Public: failures | No throw | 502, garbled, offline | Plain errors | PublicClientTests.Ac2_A_failed_dead_or_unreadable_call_gives_a_plain_error_instead_of_throwing |
| Public: group by day | Display zone | Showings near midnight | Grouped by local day | PublicClientTests.Ac1_Showings_are_grouped_by_the_calendar_day_in_the_display_time_zone |
| Public: time zone fallback | Robust | Null, empty, unknown zone (theory) | Browser's local zone | PublicClientTests.Ac1_A_missing_or_unknown_time_zone_falls_back_to_the_browsers_zone |
| Public: Book link | Login return | `AuthRedirect.BookUrl` | Seat map when signed in; login with return address when not | PublicClientTests.Ac4_Book_goes_to_the_seat_map_when_signed_in_and_to_login_with_a_return_address_when_anonymous |
| Public: routes | No login | Page attributes | Routes set; no `Authorize` | PublicClientTests.Ac1_The_home_and_movie_pages_have_their_routes_and_need_no_login |
| Booking: seat map render | States | Render `SeatGrid` | Labelled buttons; state cues; four-item legend | BookingClientTests.Ac1_Seat_map_has_a_labelled_button_per_seat_with_a_cue_for_each_state_and_a_four_state_legend |
| Booking: not selectable | Read-only | Render non-selectable grid | No buttons | BookingClientTests.Ac1_Seat_grid_that_is_not_selectable_draws_no_seat_buttons |
| Booking: admin grid | Legend | Read `SeatGrid.razor` | Two-item legend and row buttons | BookingClientTests.Ac1_Admin_seat_grid_without_seats_keeps_its_two_item_legend |
| Booking: ninth seat | Max 8 | Select 9 seats | Message "You can select at most 8 seats."; toggle off works | BookingClientTests.Ac1_The_ninth_seat_is_refused_with_a_message_and_a_selected_seat_can_be_toggled_off |
| Booking: taken seat | Not selectable | Toggle a taken seat | Not selected; max constant is 8 | BookingClientTests.Ac1_A_taken_seat_cannot_be_selected |
| Booking: total | Running total | Select seats; render `Book` | Sum of prices; Book disabled with no seats | BookingClientTests.Ac2_The_total_is_the_sum_of_the_selected_seat_prices_and_Book_is_disabled_with_no_seats |
| Booking: create | Typed client | `CreateAsync` | POST with seat ids; booking returned; text helpers | BookingClientTests.Ac3_CreateAsync_posts_the_seats_and_returns_the_booking |
| Booking: confirmation | Content | Read `Book.razor` | Reference, movie, time, seats, total, link to My bookings | BookingClientTests.Ac3_The_confirmation_shows_reference_movie_time_seats_total_and_a_link_to_my_bookings |
| Booking: conflict | Fresh map | 409 with seat map | Message and map; taken seats dropped from selection | BookingClientTests.Ac4_A_seat_conflict_returns_the_message_and_the_fresh_map_and_selected_taken_seats_are_dropped |
| Booking: closed showing | Not a conflict | 409 with detail | Error message | BookingClientTests.Ac4_A_409_for_a_closed_showing_is_an_error_message_not_a_conflict |
| Booking: failures | No throw | 400, garbled, offline | Plain errors | BookingClientTests.Ac4_A_rejected_dead_or_garbled_create_gives_a_plain_error_instead_of_throwing |
| Booking page: closed | Message | Render `Book` for a not bookable map | Closed message; no buttons | BookingClientTests.Ac5_A_map_that_is_not_bookable_shows_the_closed_message_and_no_seat_buttons_or_Book |
| Booking page: unknown showing | Message | 404 | "This showing does not exist" | BookingClientTests.Ac5_An_unknown_showing_shows_a_not_found_message |
| Booking: seat map call | Typed client | `GetSeatMapAsync` | Map read; 404 flagged | BookingClientTests.Ac5_GetSeatMapAsync_reads_the_map_and_flags_a_404 |
| Booking: page access | Customer only | Page attributes, client policy, menu | `CustomerOnly` everywhere | BookingClientTests.Ac5_The_pages_are_customer_only_and_the_client_registers_the_same_policy_as_the_API |
| My bookings | Content | Render `MyBookings` | Tabs; reference, movie, time, seats, total, status | BookingClientTests.Ac6_My_bookings_has_tabs_and_lists_reference_movie_time_seats_total_and_status |
| My bookings: states | Empty and error | Empty list; 500 | Empty message; error message | BookingClientTests.Ac6_My_bookings_shows_an_empty_state_and_an_error_state |
| My bookings: scope | Query | `GetMineAsync` for each tab (theory) | `?scope=upcoming` / `history` | BookingClientTests.Ac6_GetMineAsync_asks_for_the_tab_scope |
| Cancel button | Enabled | Render a booking 3 days ahead | Enabled Cancel; no late message | CancellationClientTests.Ac1_A_confirmed_upcoming_booking_shows_an_enabled_Cancel_button_and_no_late_message |
| Cancel call | Typed client | `CancelAsync` | POST cancel route; success on 204 | CancellationClientTests.Ac1_CancelAsync_posts_to_the_cancel_endpoint_and_reports_success_on_204 |
| Cancel confirmation | UX | Read `MyBookings.razor` | Confirm dialog, then API call, then reload | CancellationClientTests.Ac1_Cancel_asks_for_confirmation_before_calling_the_API_then_reloads_the_list |
| Late cancel button | 2 h rule in UI | Render a booking 1 h ahead | Disabled Cancel with visible explanation | CancellationClientTests.Ac2_Inside_two_hours_the_Cancel_button_is_disabled_with_the_explanation_as_visible_text |
| Two-hour limit in client | Inclusive | `IsTooLateForCustomer` at −3 h, −2 h, −2 h + 1 s | false, false, true | CancellationClientTests.Ac2_The_limit_is_inclusive_exactly_two_hours_before_is_still_allowed |
| Cancel errors | Messages | 409, 403, 404, offline | API message; plain messages | CancellationClientTests.Ac2_A_409_from_the_API_gives_its_own_message_and_other_refusals_get_plain_messages |
| Refused cancel | UX | Read `MyBookings.razor` | Error shown; list reloaded | CancellationClientTests.Ac2_A_refused_cancel_shows_the_API_message_and_reloads_the_list |
| No cancel button | Cancelled / past | Render each | No Cancel button | CancellationClientTests.Ac3_Cancelled_and_past_bookings_show_no_Cancel_button |
| CanCancel | Rule | `BookingText.CanCancel` | Only Confirmed and not started | CancellationClientTests.Ac3_CanCancel_needs_a_Confirmed_booking_whose_showing_has_not_started |
| Staff search call | Typed client | `SearchAsync` | Showing and escaped reference sent | CancellationClientTests.Ac4_SearchAsync_sends_the_showing_and_the_escaped_reference_and_reads_the_bookings |
| Staff page access | Policy | Page attributes, menu | `ManageBookings`; linked from the menu | CancellationClientTests.Ac4_The_staff_page_is_on_staff_bookings_for_the_ManageBookings_policy_and_linked_from_the_menu |
| Staff cancel | Any time | Read `Bookings.razor` | No late rule; confirm panel; refresh | CancellationClientTests.Ac4_Staff_can_cancel_any_Confirmed_booking_at_any_time_after_a_confirm_panel_and_the_list_is_refreshed |
| Staff page start | Filters only | Render page | Filters, no results table yet | CancellationClientTests.Ac4_The_staff_page_starts_with_the_filters_and_no_results_until_a_search_is_made |
| Staff search errors | States | 400; empty; offline | Plain message; empty list; unreachable | CancellationClientTests.Ac5_The_staff_search_reports_a_server_error_as_a_plain_message_and_an_empty_result_as_an_empty_list |
| Loading and empty states | UX | Read both pages | Spinner; "No bookings found" | CancellationClientTests.Ac5_Both_pages_show_a_spinner_while_loading_and_the_staff_page_says_No_bookings_found_when_empty |

### 3.17 Client: design system and page structure (`DesignSystemTests`, source checks)

| Scenario | Purpose | Steps | Expected result | Test |
|---|---|---|---|---|
| Design tokens | Own CSS | Read `app.css` | All tokens on `:root`; no external assets | DesignSystemTests.Ac1_AppCss_defines_design_tokens_on_root |
| Layout | No template look | Read `MainLayout` | Header, nav, main, footer, toggle; no sidebar; responsive rule | DesignSystemTests.Ac1_MainLayout_has_header_top_nav_footer_and_collapsing_toggle_without_sidebar |
| No Bootstrap | Own CSS only | Read `index.html` | Links `app.css`; no Bootstrap | DesignSystemTests.Ac1_IndexHtml_links_own_css_and_no_bootstrap |
| Shared components | Parameters | Read each component (theory, 5 cases) | Expected parameters exist | DesignSystemTests.Ac2_Shared_components_exist_with_their_parameters |
| Form field label | Accessibility | Read `FormField` | `<label for="@Id">` | DesignSystemTests.Ac2_FormField_binds_label_to_input_with_for |
| Template leftovers | Clean | Check files | Gone | DesignSystemTests.Ac3_Home_and_template_leftovers_are_gone |
| Staff page | Admin only | Read `Staff.razor` | Admin-only; all permissions offered | DesignSystemTests.US123_Ac2_Staff_page_is_admin_only_and_offers_all_three_permissions_for_create_and_edit |
| Route guards | Menu by role | Read `App.razor`, layout | Guards and role-based links | DesignSystemTests.US123_Ac3_Router_guards_pages_and_menu_links_depend_on_the_role |
| Movies page | Policy | Read `Movies.razor` | `ManageMovies` guard; menu entry | DesignSystemTests.US125_Ac1_Movies_page_is_guarded_by_ManageMovies_lists_movies_and_the_menu_entry_uses_the_policy |
| Movie form | Fields and limits | Read `MovieForm.razor` | Five fields, full dropdowns, limits | DesignSystemTests.US125_Ac1_Movie_form_has_the_five_fields_with_full_dropdowns_and_html_limits |
| Auditorium pages | Columns and fields | Read pages | Columns; three fields with errors | DesignSystemTests.US128_Ac1_Auditorium_list_shows_the_columns_and_the_form_has_the_three_fields_with_field_errors |
| Seat grid | Accessibility, revert | Read grid and form | Labelled row buttons; failed toggle reverted | DesignSystemTests.US128_Ac2_Seat_grid_has_labelled_focusable_row_buttons_and_the_form_reverts_a_failed_toggle |
| Seat colours | CSS variables | Read `app.css` | Variables; sideways scroll | DesignSystemTests.US128_Ac2_Seat_colours_are_css_variables_and_wide_grids_scroll_sideways |
| Prices on form | Read-only | Read form and client | Prices from pricing endpoint | DesignSystemTests.US128_Ac3_Form_shows_the_read_only_prices_from_the_pricing_endpoint |
| Admin-only pages | Menu placement | Read pages and layout | Admin guard; entry in admin block | DesignSystemTests.US128_Ac5_Pages_are_admin_only_and_the_nav_entry_sits_in_the_admin_block |
| Page states | Spinner, empty, error | Read pages | Present | DesignSystemTests.US128_Ac6_Pages_show_a_spinner_an_empty_state_and_an_error_alert |

## 4. Coverage matrix

| Requirement area | Scenario | Test classes |
|---|---|---|
| Booking 1–8 seats, reference, total (Standard 140 / VIP 200), status Confirmed | Customer books seats | BookingScenarios, BookingCreateTests (Ac1, Ac2, Ac3 eight seats, Ac9) |
| Booking 0 or more than 8 seats | Validation error, nothing saved | BookingCreateTests (`Ac3_An_invalid_seat_selection_...`), BookingClientTests (ninth seat) |
| Double booking | One success, one conflict | BookingScenarios (parallel, taken seat), BookingCreateTests (Ac5, Ac6), DataTests (unique index) |
| Booking a started showing | Rejected | BookingCreateTests (`Ac4_A_cancelled_or_started_showing_...`), SeatMapTests (Ac4) |
| Cancellation, 2 hour rule (exactly 2 h allowed) | Cancelled, seats freed, booking kept | CancellationScenarios, CancellationTests, CancellationClientTests |
| Cancel under 2 hours | Rejected | CancellationScenarios, CancellationTests |
| Staff cancel | ManageBookings or admin, any time | CancellationScenarios, CancellationTests (Ac5), ShowingTests (showing cancel cascade) |
| Showing overlap | Rejected, names the conflicting showing | ShowingOverlapScenarios, ShowingTests (Ac3, Ac5, Ac6) |
| Showing in the past | Rejected | ShowingTests (Ac2, Us130_Ac1 edit into the past) |
| Access 401 / 403 | Unauthenticated or missing permission | AccessScenarios, AuthorizationTests, BookingReadTests, and the "by caller" tests in MovieTests, ShowingTests, AuditoriumTests, AdminStaffTests |
| Seed data (accounts, 3 auditoriums, 10 movies) | **No test yet.** No seeder exists in the code, so `SeedScenarios` was not created in US-141. Add the scenario here when the seed story delivers a seeder. | none |

## 5. Keeping this document correct

- A test added, renamed or removed must be changed here in the same change. Compare with `dotnet test --list-tests`: every test method in the list appears in section 3, and every row names a test that exists.
- Use the test class and method names exactly as in the code.
- Do not record test results in this document.
