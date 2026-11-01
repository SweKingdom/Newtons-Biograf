# Newtons Biograf User Manual

## Table of contents

1. Introduction
2. Getting started
3. Navigating the app
4. How to register and log in
5. How to create a staff account
6. How to change staff permissions
7. How to manage movies
8. How to manage auditoriums
9. How to set VIP rows and seat prices
10. How to schedule a showing
11. How to search for a showing
12. How to browse showings and movies
13. How to book a showing as a visitor
14. How to choose seats
15. How to view my bookings

## 1. Introduction
This manual describes how cinema staff use Newtons Biograf to manage movies, auditoriums and showings, and how customers browse, book tickets and choose seats. Chapters are added as features are delivered.

## 2. Getting started
The current release contains the data foundation only. There are no screens or actions for end users yet.

> **Note:** Chapters for managing movies, auditoriums and showings, and for booking tickets, will be added when those features are delivered.

## 3. Navigating the app

The app has a header at the top of every page with the Newtons Biograf logo and the main navigation links. The footer is at the bottom of every page.

**On a desktop or tablet (640px wide or more):**
1. Use the links in the top navigation to move between sections.
2. The current page is shown in the header.

**On a phone (below 640px wide):**
1. Tap the **Menu** button in the header to open the navigation.
2. Tap a link to go to that section. The menu closes when you choose a page.
3. Tap **Menu** again to close the menu without choosing a page.

Tip: The **Menu** button tells screen readers whether the menu is open or closed.

## How to register and log in

Use this procedure to create an account, sign in and sign out.

1. Open the app and select **Register** in the header.
2. Enter your details in **Name**, **Email** and **Password**. The password must be at least 8 characters.
3. Select **Register**. If a field is invalid, the message appears under that field. Correct it and try again.
4. After a successful registration you are taken to the **Log in** page with a success notice.
5. Enter your **Email** and **Password** and select **Log in**.
6. The header shows your name and role. The menu shows only the pages your role allows.

**Wrong password:** the message "Invalid email or password." appears in an alert box. Check your details and try again.

**Log out:** select **Log out** in the header. You are signed out and the header shows **Log in** and **Register** again.

**Restricted pages:** if you open a protected page while signed out, the app shows the **Log in** page. After you log in, you return to the page you asked for.

**No access:** if you are signed in but your role does not allow a page, the page "You don't have access to this page" is shown.

**Tip:** Log out when you use a shared computer.

## How to create a staff account

Only administrators can create staff accounts. Customers cannot become staff by themselves.

1. Sign in with an administrator account.
2. Open the staff management area and choose the option to add a new staff member.
3. Enter the employee's name and email address. Each email address can be used only once.
4. Enter a password of at least 8 characters.
5. Select the permissions the employee needs. You can leave all permissions unselected.
6. Save. The new account is created with the Staff role.

Tips:
- If an email address is already in use, the form shows an error next to the Email field and no account is created.
- If the password is shorter than 8 characters, the form shows an error next to the Password field.
- Permissions are chosen at creation. Changing them later is described in the staff permissions chapter.
- Staff members can sign in with the email and password you set.

## How to change staff permissions

Admins can replace or remove the permissions of a Staff account. This changes what that employee can do.

**Before you start**
- You must be signed in as an Admin.
- The target account must have the role Staff. Admin and Customer accounts cannot be changed this way.

**Steps**
1. Identify the staff member's user id.
2. Send the new permission list for that user. The list replaces the whole existing set.
3. To remove all permissions, send an empty list.

**What you will see**
- A successful change is confirmed with no content (HTTP 204).
- The new permissions are stored immediately, but the staff member only gets them on their next login.

**Tips and warnings**
- The change is a full replacement. Include every permission the employee should keep.
- A staff member who is already signed in keeps their old permissions until their session token expires (default 60 minutes). Ask them to sign out and sign in again to apply the change at once.
- Unknown permission names are rejected, and nothing is changed.
- Sending the same list twice is safe.

## How to manage movies

Use the Movies screen to keep the movie programme up to date. You need the **Manage movies** permission (or the Admin role) to see the Movies menu entry.

1. Click **Movies** in the top menu. The table lists every movie with its **Title**, **Genre**, **Age rating** and **Length**.
2. To add a movie, click **Add movie**.
3. Fill in **Title**, **Genre**, **Age rating**, **Length** and **Description**. Pick the genre and age rating from the dropdowns; both start at "Select…".
4. Click **Save**. The movie appears in the list with a confirmation message.
5. To change a movie, click **Edit** on its row, change the fields, and click **Save**.
6. To delete a movie, click **Delete** on its row and confirm in the panel that opens.

**Tips and warnings**
- If a field is invalid, a message appears under that field. Correct it and save again. Nothing is saved until all errors are fixed.
- You cannot delete a movie that has future showings. The screen shows the reason (for example, "Movie cannot be deleted because it has future showings."), and the movie stays in the list.
- If you do not see the Movies menu entry, ask an administrator to check your staff permissions.

## How to manage auditoriums

Admins create and edit auditoriums. All staff and customers can view them. Only admins can open the auditorium screens; other users do not see the **Auditoriums** menu entry.

**Create an auditorium**
1. Sign in as an admin.
2. Open **Auditoriums** in the menu. The list shows each auditorium with its name, rows, seats per row and capacity.
3. Choose the option to add a new auditorium.
4. Enter a **Name**. The name must be unique and may be up to 100 characters. Names are not case-sensitive, so "Salon 1" and "salon 1" count as the same name.
5. Enter **Rows** (1 to 26).
6. Enter **Seats per row** (1 to 40).
7. Save. You return to the list, which shows the new auditorium. **Capacity** is calculated automatically as Rows × Seats per row. All seats are created as standard seats.

**Edit an auditorium**
1. Sign in as an admin and open **Auditoriums** from the menu.
2. Choose the auditorium you want to change. The edit page shows its prices and its seat grid.
3. Change the **Name**, the **Rows** or the **Seats per row**.
4. Save. You return to the list, which shows the new data.

**Read the seat grid**
- Each row is labelled with its number and its type, **VIP** or **Standard**. VIP rows are gold; Standard rows use the standard seat colour.
- To change a row's type, see [How to set VIP rows and seat prices](#how-to-set-vip-rows-and-seat-prices).

Tips and warnings:
- Changing only the name is always allowed.
- Changing **Rows** or **Seats per row** is blocked when the auditorium has future showings with bookings, or when a seat that would be removed has already been booked. The message "Layout cannot be changed because the auditorium has bookings." is shown in a red message box and nothing is changed. Your typed values stay on the form.
- When you add rows or seats, existing seats keep their position. New seats in an existing row take that row's seat type. New rows are standard.
- If a name is already in use, the save fails and the **Name** field shows an error.
- If the list or the edit page cannot load, a red message box shows the error. Try again later or contact support.
- If there are no auditoriums, the list shows "No auditoriums yet. Add the first one."

**View auditoriums**
- Anyone can view the auditorium list and each auditorium's details, without signing in.
- The list and details show Name, Rows, Seats per row, Capacity and VIP rows.

## How to set VIP rows and seat prices

Admins can mark a whole row of an auditorium as VIP, or set it back to Standard. Seat prices are set by the system configuration, not in the app.

1. Sign in as an admin and open **Auditoriums** from the menu.
2. Choose the auditorium, then open **Edit**.
3. Click the row label of the row you want to change (for example "Row 2").
4. The row changes to **VIP** (gold, labelled "VIP") or back to **Standard**. The change is saved immediately. There is no Save button for this step.
5. Reload the page to confirm the change is kept. Doing the same change twice has no further effect.

If the change cannot be saved, the row returns to its previous type and a red message box shows the error. Try again.

**Prices**
- The edit page shows the current **Standard** and **VIP** prices. The prices are read-only.
- Current prices are 140 SEK for a Standard seat and 200 SEK for a VIP seat. Customers see these prices when they book.

**Tips**
- Seat type applies to a whole row only. You cannot mark single seats as VIP.

**Warnings**
- Changing a row to VIP or Standard does not change the price of bookings that already exist.
- Only Admins can change seat types. Staff and Customer accounts do not see the screens and receive an access error from the system.
- Price changes are made by the system administrator in the configuration, not in the app.

## How to schedule a showing

Showings are managed from **Showings** in the navigation menu. This menu entry and page are available only to Admins and staff with the **Manage showings** permission. Other users see a message that they are not authorised.

### Scheduling a new showing
1. Open **Showings**.
2. Select **New showing**.
3. Choose a **Movie**.
4. Choose an **Auditorium**.
5. Enter the **Date** and **Start time**.
6. Check the **Ends about …** line. It shows the start time plus the film length plus 15 minutes for cleaning.
7. Select **Save**. The new showing appears in the list.

Tips:
- The end time is an estimate. The system makes the final check when you save.
- If the date is in the past, the **Date** field shows an error. Choose a later date.
- If the showing overlaps another showing in the same auditorium, a red message appears above the form. Change the time or auditorium. Your entries are kept.

### Filtering the list
- Use the **From** and **To** dates to choose a period. The list starts with today and the next 14 days.
- Use the **Auditorium** filter to show one auditorium only.

### Editing a showing
1. In the list, select **Edit** on the showing.
2. Change the details and select **Save**.

Tips:
- **Edit** is greyed out when the showing already has bookings or has started. Hover over the button to see the reason.
- If the change conflicts with another showing, a red message appears above the form.

### Cancelling a showing
1. In the list, select **Cancel** on a showing with the status **Scheduled**.
2. Read the confirmation. It states how many active bookings will be cancelled.
3. Select **Confirm** to cancel the showing. The status changes to **Cancelled**.

Warning: cancelling a showing cancels its active bookings. This cannot be undone from the screen.

### Deleting a showing
1. In the list, select **Delete** on the showing. The button appears only when the showing has no active bookings.
2. Confirm the deletion.

If the system refuses the deletion, a red message explains why.

## How to search for a showing

Visitors do not need to log in to see which movies are showing.

1. Open the showings page.
2. Optionally choose a **Genre** from the dropdown.
3. Optionally pick a **Date**. Dates outside the next 14 days show no results.
4. Optionally choose an **Auditorium** from the dropdown.
5. Optionally type part of a movie title in **Title**. Upper and lower case do not matter, and letters such as Å, Ä and Ö are matched too.
6. The list shows only showings that start within the next 14 days and are not cancelled. It is sorted by start time.
7. Each showing shows the number of **Free seats**.

Tips:
- All filters can be combined. A showing must match every filter you set.
- If the list is empty, remove a filter or choose another date.
- A date in the wrong format or an auditorium that is not a valid choice shows an error message. Pick the value again from the dropdown.

## How to browse showings and movies

You do not need an account to browse. Open the home page to see what is on.

1. Open the home page (`/`). Showings for the next 14 days are listed, grouped under each day (for example "Tuesday 5 November 2030").
2. Each row shows the movie, its age rating, the start time, the auditorium and the number of free seats.
3. To narrow the list, use the filters at the top:
   1. **Genre** — choose a genre.
   2. **Date** — choose a day within the next 14 days.
   3. **Auditorium** — choose an auditorium.
   4. **Title** — type part of a movie title. The list updates a moment after you stop typing.
4. The filters work together. Select **Clear filters** to reset them all.
5. If no showing matches, the page shows "No showings match your filters".
6. To see a movie's details, select its title. The movie page shows the description, length, genre, age rating and its upcoming showings. If the movie cannot be found, the page shows "Movie not found".

**Tip:** Times are shown in cinema local time (Stockholm).

## How to book a showing as a visitor
1. On the home page or a movie page, select **Book** on the showing you want.
2. If you are logged in, the seat selection page opens.
3. If you are not logged in, the login page opens. After you log in, you are returned to the booking page for that showing.
4. A showing with no free seats shows **Sold out**. The **Book** button is disabled.

## How to choose seats

Use the seat map to pick the seats for your booking. You must be logged in as a customer.

1. Open a showing and select **Book**. The seat map opens at the showing's booking page.
2. Read the **Legend** at the top of the map:
   - **Free Standard**: available seat, standard price.
   - **Free VIP** (marked ★): available VIP seat, higher price.
   - **Taken** (marked ✕): already booked. You cannot select it.
   - **Selected** (marked ✓): a seat you have picked.
3. Select a free seat to add it to your selection. Select it again to remove it. You can also use the Tab key to move between seats and Enter or Space to select one.
4. Repeat for each seat you want. You can select at most **8 seats** per booking. If you try to select a 9th seat, the message "You can select at most 8 seats." is shown.
5. Check the **selected seat count** and the **total in SEK** below the map. The total updates each time you select or remove a seat.
6. Select **Book** to confirm. The Book button is unavailable when no seat is selected, and while your booking is being sent.
7. When the booking succeeds, a confirmation shows your **reference**, the **movie**, the **time**, your **seats** and the **total price**. Select **My bookings** to see it later.

**Tips and warnings**
- The total shown is for information. The final price is calculated when you book.
- If the message "One or more seats are no longer available" appears, another customer booked one or more of your seats first. The map is refreshed, the taken seats are removed from your selection, and your other seats stay selected. Check the selection and select **Book** again.
- If the message "This showing can no longer be booked" appears, the showing is closed for booking. No seats can be selected.
- If the message "Showing not found" appears, the link may be out of date. Go back and choose the showing again.
- If you are not logged in, you are sent to the login page and returned to the seat map after logging in.

## How to view my bookings

Use **My bookings** to see the bookings you have made. This page is available to customers only.

1. Select **My bookings** in the menu. This link appears when you are logged in as a customer.
2. Select the **Upcoming** tab to see bookings for showings that have not started yet.
3. Select the **History** tab to see bookings for showings that have already taken place.
4. For each booking, check the **reference**, **movie**, **time**, **seats**, **total** and **status**.
5. Switching tabs loads the list for that tab.

**Tips and warnings**
- If a tab shows "You have no bookings" (or similar empty text), you have no bookings in that list yet.
- If the list cannot be loaded, an error message is shown. Refresh the page and try again.
- Staff accounts cannot open My bookings. Anonymous visitors are sent to the login page.

## How to cancel a booking

You can cancel your own confirmed booking up to 2 hours before the showing starts.

1. Log in and open **My bookings**.
2. Find the booking under **Upcoming**. It must have the status **Confirmed**.
3. Click **Cancel**. A confirmation panel opens below the booking.
4. To cancel, click **Yes, cancel booking**. To keep the booking, click **Keep booking**. The panel closes and nothing changes.
5. A success message appears. The booking moves to **History** with the status **Cancelled**.

**Tips and warnings**
- If the showing starts in less than 2 hours, **Cancel** is greyed out. The message "Bookings can only be cancelled up to 2 hours before the showing starts." is shown. Contact cinema staff for help.
- If you leave the page open until the 2-hour limit passes and then click **Cancel**, the system shows the reason and reloads the list.
- Cancelled and past bookings have no **Cancel** button.

## How to manage bookings as staff

Staff with the **Manage bookings** permission (and administrators) can find and cancel any customer booking. Open **Bookings** in the navigation menu.

1. Open **Bookings**. The page opens at `/staff/bookings`.
2. Enter a **Showing id**, a **Reference**, or both. At least one field is required.
3. Click **Search**. A spinner shows while the search runs.
4. Check the results. Each row shows the booking status.
5. To cancel a **Confirmed** booking, click **Cancel**, then confirm in the panel. The booking shows **Cancelled**. Staff can cancel at any time, including within 2 hours of the showing.

**Tips and warnings**
- If no booking matches, the page shows "No bookings found".
- An empty search shows a validation message. Enter a showing id or reference first.
- If you do not have the permission, the page shows an access-denied message. Ask an administrator to grant **Manage bookings**.
