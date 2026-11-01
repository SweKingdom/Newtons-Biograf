# Newtons Biograf User Manual

## Table of contents

1. Introduction
2. Getting started
3. Navigating the app
4. How to register and log in

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

Admins create and edit auditoriums. All staff and customers can view them.

**Create an auditorium**
1. Sign in as an admin.
2. Open the auditorium list and choose the option to add a new auditorium.
3. Enter a **Name**. The name must be unique and may be up to 100 characters. Names are not case-sensitive, so "Salon 1" and "salon 1" count as the same name.
4. Enter **Rows** (1 to 26).
5. Enter **Seats per row** (1 to 40).
6. Save. The **Capacity** is calculated automatically as Rows × Seats per row. All seats are created as standard seats.

**Edit an auditorium**
1. Sign in as an admin and open the auditorium.
2. Change the **Name**, the **Rows** or the **Seats per row**.
3. Save.

Tips and warnings:
- Changing only the name is always allowed.
- Changing **Rows** or **Seats per row** is blocked when the auditorium has future showings with bookings, or when a seat that would be removed has already been booked. The message "Layout cannot be changed because the auditorium has bookings." is shown and nothing is changed.
- When you add rows or seats, existing seats keep their position. New seats in an existing row take that row's seat type. New rows are standard.
- If a name is already in use, the save fails and the **Name** field shows an error.

**View auditoriums**
- The auditorium list and each auditorium's details show Name, Rows, Seats per row, Capacity and VIP rows. Anyone can view them, without signing in.

## How to set VIP rows and seat prices

Admins can mark a whole row of an auditorium as VIP, or set it back to Standard. Seat prices are set by the system configuration, not in the app.

1. Open the auditorium you want to change under **Auditoriums**.
2. Find the row you want to change.
3. Set the row's seat type to **VIP** or **Standard**. Every seat in that row takes the new type. Other rows are not changed.
4. Save. Doing the same change twice has no further effect.

**Tips**
- Seat type applies to a whole row only. You cannot mark single seats as VIP.
- Current prices are 140 SEK for a Standard seat and 200 SEK for a VIP seat. Customers see these prices when they book.

**Warnings**
- Changing a row to VIP or Standard does not change the price of bookings that already exist.
- Only Admins can change seat types. Staff and Customer accounts receive an access error.
- Price changes are made by the system administrator in the configuration, not in the app.
