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

No change in this story. Note: if you call a protected page or function without being logged in, you will be asked to log in again. If your account does not have the access needed for an action, the action is refused with a "Forbidden" message.

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
