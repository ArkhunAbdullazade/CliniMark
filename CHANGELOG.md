# Changelog

What changed in CliniMark and why, newest first. Git holds every line of every change; this file explains them in plain language, with the commit that delivered each one. The researcher's requests behind these changes, word for word, are in [docs/requests-log.md](docs/requests-log.md).

Database changes are in `src/CliniMark.Presentation/Migrations`, and each release that changes the database has a script in `deployment/`.

## 2026-11-24: the label check always shows in full, and a shorter sign-up page

- "Please confirm for this image" never scrolls now, whatever the window size. Before, it was capped at 45% of the column's height (72% while a label search was open). On laptop screens that cut it off, and images with sex, age or skin tone made it scroll. It now always takes its full height; the Saved terms and custom-annotation lists below it give way instead. Shorter screens get slightly tighter spacing, and the label search list is a little shorter there. Checked at 1920×1080, 1536×864, 1440×900, 1366×768, 1280×720, 1366×768 at 125% zoom and 1280×600, with the search closed and open.
  - With the search open on a window 614 px tall or less, the right column itself scrolls a little; the box is never cut.
  - The column also keeps 64 px clear at the bottom, up from 48, so Skip the case no longer covers the bottom of the lists.
- Sign-up page:
  - The two sentences under the title are gone ("Register with your institutional or workplace email address…" and "Your account password is personal…").
  - So is the line naming personal email services under the email field. A personal address is still refused, with that explanation in the error message.
  - Position and Workplace now say "(required)". Both were already required by the browser and the server.

## 2026-11-24: specialists can change a term they already answered

- Clinicians asked to be able to go back to a term after Save and Next Term. Selecting a saved term in the description now opens it to be changed, not just to look at. Its shapes and comment come back and can be edited. The answer can also change, between a drawing, Uncertain/Skip and Not Visible/Abstract; to make a drawn term Uncertain or Not visible, its shapes are deleted first. **Save changes** keeps the change and returns to the term they were on (or to Next Image when all are saved). **Cancel**, a new button that only shows then, leaves the term as it was. Leaving with unsaved changes asks first. The open term is outlined in the text, and saved terms say on hover that they can be changed.
- Saved terms are easy to find again:
  - Terms that can be selected (saved terms, and the current term while another is open) grow slightly and light up in their own colour under the pointer. Terms not reached yet stay as they are, because selecting them does nothing.
  - A **Saved terms** list under the Comments and Report buttons fills in as the specialist goes. It shows each saved term in the order of the text, with its answer (Outlined, Uncertain or Not visible), an Edit mark, and a count such as "3 of 5". Selecting a row opens that term, like selecting it in the text, and the open row says "Editing".
  - The open term is shown with a red tint and a thick underline.
  - Selecting the term already being drawn now does nothing. Before, it offered to throw the drawing away.
- Changes are possible until Next Image. After that the image is finished, and a change is refused as "no longer assigned".
- Nothing is lost. The answer row takes the new version, so every count, table and export uses the corrected answer. The version it replaced goes to a new table, `AnswerRevisions`, with when it was saved and when it was changed. The answer keeps its first save time, drawing timings and technical note. A repeated request (a retry) changes nothing.
- Admin image page: an answer that was changed shows "Changed n times by the specialist", which opens the earlier versions. Export: a new `answer_revisions` table, explained in the archive's README. Skip the case also copies earlier versions into its copy of the discarded work.
- Email notifications: a comment added or changed while changing an answer is announced like any other comment. A comment is never announced twice.
- Server: `POST /MedData/CorrectAnnotatedMedData` (`CorrectProfessionalAsync`) runs the same checks as a new answer, under the same lock.

Database script: `deployment/release-2026-11-24-migrations.sql` (adds one table; nothing is removed).

## 2026-11-24: Previous and Next through one participant's images

- An image opened from a participant's Images worked on list now has a bar above and below it: "Images worked on by <name> · Image 3 of 577", with Previous image and Next image buttons. They follow the same order as the list (current locks first, then the most recent saves), so all of one annotator's work can be checked without going back each time. The left and right arrow keys do the same, except while typing, in a dropdown or on the "Colour by" switch. The participant's name links back to their page.
- The bar stays after Remove from study or Restore. Images opened from All images look as before. An image that isn't in the participant's list says so.
- The previous and next image come from a light query that reads only that participant's answers and locks, so each step is quick. The participant page's list now breaks ties by image number, so both always agree.

## 2026-11-24: no false "Page error" reports from the end-of-series page

- Leaving the specialist page while it had no image (the end-of-series page, or "No images are available in your filters right now") logged a "Page error": "Cannot access 'custom' before initialization". The check that warns about unsaved work read the custom annotations, which are only set up when there is an image. Participants saw nothing and lost nothing, because there was no image to save and the browser went on to the next page. But each exit added a problem report and a notification email. The first 7 reports came on 2026-11-23, from two specialists choosing new filters. The check now works on pages without an image. The fault had been there since custom annotations were added on 2026-11-02. It surfaced now because participants were reaching the end of their filters.

## 2026-11-23: Progress by category tooltip shows the right row

- On the admin Overview, hovering over a bar in Progress by category showed the figures of another category. The chart lists categories one under the other, but the chart library looked for the bar nearest the pointer from left to right, so it picked whichever row's bar ended closest sideways. It now follows the row under the pointer. Checked in a browser on every row of all three lists, for specialists and students.

## 2026-11-23: sign-up asks for a workplace email, and speciality is required

- Sign-up accepts only institutional or workplace email addresses. Addresses at personal email services such as Gmail, Outlook, Hotmail, Yahoo, iCloud, GMX, web.de, ukr.net, mail.ru or Yandex are refused, both on the page before the confirmation and by the server. A list can't cover every service, so new registrations are still worth a glance. Existing accounts and accounts the study team creates are not affected.
- The "University or institution" field is now called Workplace, on the sign-up page, in the admin panel and in the registration email. The data and the export column keep the name `University`.
- The two yellow warnings on the sign-up page (write down your password; check your email carefully) are gone. The "Is this your correct email?" confirmation stays.
- Speciality is now required, on the sign-up page and on the Filters page, like image modality. Body region stays optional. Accounts saved before this with no speciality still see every speciality until they next save their filters.
- Fixed on the way: Razor always writes `data-` attributes, so the filter script now reads which lists are required from the attribute's value.

## 2026-11-23: time spent by each participant

- The Participants page has a new Time spent column, and each participant's page a Time spent tile. Selecting the time opens their time on each image: status, time, page openings, first opened and last worked, with a total that is the sum of the rows. It can be sorted by most recent or longest.
- The annotation page measures active time only. It counts while the page is the visible browser tab and the person has used the mouse, keyboard or touch screen in the last 3 minutes, so a page left open unattended stops counting. It pauses while the tutorial plays. Specialist and student pages both measure.
- The page reports its running total after the first second, every half minute, before Next Image, Skip the case, submitting or logging out, and when it is hidden or closed. Each report repeats the whole total, so a lost or repeated report never loses or doubles time. The server only starts counting on an image assigned to that person, and a total can never run faster than the clock.
- Work saved before measuring began shows an estimate, marked ≈. For each term it is the time from the term appearing to its save, at most 10 minutes a term, as saved with the answer. Terms answered without drawing aren't included.
- Last active now also counts time on image pages, including images that were skipped or opened without saving.

Database script: `deployment/release-2026-11-23-migrations.sql` (adds one table; nothing is removed).

## 2026-11-09: email notifications

- The study team gets an email for every new registration, with everything the person entered at sign-up.
- Every technical problem and every comment is emailed within about a minute: problems reported by participants, errors their pages logged automatically, and comments or technical notes saved with a term.
- An email when specialists have finished 100, 1,000, 2,500, 5,000, 7,500 and 10,000 images. Skipped cases and student work don't count toward these targets.
- A weekly report every Monday at 08:00 Berlin time. It covers overall progress, each clinician's sign-ins, active days, answers and finished images, progress by image modality, and the images finished that week.
- Sign-ins are now recorded (account id and time only), so the weekly report can say how often each clinician came in. They are counted from the day notifications are switched on.
- Emails go out through Azure Communication Services. Nothing is announced twice, and anything waiting is merged into the next email when the hourly sending limit is reached. A failed email is tried again until it goes through.

Database script: `deployment/release-2026-11-09-migrations.sql` (adds two tables; nothing is removed).

## 2026-11-07: tutorial video

- A new 3-minute tutorial, with a calm British male voice, covers the annotation workflow from the workspace onwards. It leaves out signing up and the student page. The file is `src/CliniMark.Presentation/wwwroot/videos/clinimark-tutorial.mp4`.
- It opens by itself the first time a participant reaches their annotation page, and they watch it to the end before starting. If it can't load, they can carry on and it opens again next time.
- A new Tutorial button, between Progress and Log Out, replays it at any time.

Database script: `deployment/release-2026-11-07-migrations.sql` (adds one column; nothing is removed).

## 2026-11-07: the project is now called CliniMark

The earlier name was already a trademark, so the project took a new name and logo. It changed everywhere in the app, its pages and exports, the code and the public addresses. Nothing about how the app works changed.

## 2026-11-06: ready for clinicians

Everything below went live together on 2026-11-06, the first version clinicians use.

**Signing up and choosing images**
- Self sign-up is open again, and no longer asks for clinical experience. Accounts from earlier imports keep their value; exports leave it empty when it is unknown.
- Image modality is required on the sign-up and Filters pages. Speciality and body region stay optional. Reason: modality matches a clinician's reading skill and is the most reliable label.
- The shared research access page and its password are gone. People go straight to sign-in; per-account lockout and rate limits stay.

**Annotating**
- New button, **Skip the case**, in the page's bottom right corner, level with the other buttons. A case outside the specialist's field reopens fresh for the next person and is never offered to them again. Anything they had saved on it moves out of the results into a kept copy (`ImageSkips.DiscardedWork`), so no annotation is destroyed.
- The three filter values in the top right box are bold red.
- When every term is saved but the labels aren't confirmed, the box turns yellow and the status line says so in bold red, because Next Image gave no clue why it was locked.
- Confirmed that an image is only ever held by one specialist at a time, with a test of 20 specialists asking at the same instant.

**Research data**
- Every drawn annotation is now also stored as a pixel mask (1 inside the shape, 0 outside), so segmentation models can be scored with Dice and IoU. Coordinates stay the original record. The mask is computed on the server: a pixel is inside when its centre is, shapes of one term are merged, and a contour that crosses itself is filled throughout. Masks are stored as COCO run-length encoding and download as PNG files from the Export page.
- New admin page, **Full data**: one row per image-text pair with everything known and collected, downloadable as CSV or JSON. Each label has its original value, the verdict and the submitted value in separate columns.

**Project**
- This changelog and the requests log.
- The image workflow moves the `latest` tag from master only, so a test image can be built from a branch.

Commits `8cfb5bc` and the commit adding this file. Database script: `deployment/release-2026-11-06-migrations.sql` (adds a table and columns; nothing is removed). For annotations older than masks, run the operator command `backfill-masks`.

## 2026-11-05: label check (`e1a7a47`)

Specialists confirm or correct each image's modality, speciality and body region (Yes, No, Can't tell). No opens a search of the study's labels, and a missing value can be added. Next Image waits until all three are answered. The dataset's own labels are never overwritten: `MetadataChecks` records what was shown and what the specialist said. Reason: the researcher wants this metadata to be reliable, and the labels were machine-predicted.

## 2026-11-03: series flow and first deployment

- `b1a2d61`: image modality comes first in the filters; clinicians are thanked at the end of a series and invited to choose new filters; the Filters page shows how many images a selection offers.
- `af7e60a`: filters use the dataset's display labels; each image shows its source and licence; saves retry automatically during a restart; the study team can create accounts from a CSV; sign-up can be closed. First version deployed on Azure.

## 2026-11-02: custom annotations (`b78a770`)

- New tool, Custom annotation: a specialist outlines a finding that isn't among the highlighted terms and names it, as many as they like.
- Every filter list became optional, so "microscopy" alone is enough. (Modality became required again on 2026-11-06.)
- Hovering on a button for two seconds shows a longer description.

## 2026-11-01: comments, problem reports and new look

- `46e5779`: Comments and Report a technical problem became panels that slide open beside the image. Problems can be sent at any time, even when the image or tools fail, and failed loads and saves are logged automatically. Session time limits were removed, so nobody is timed out while annotating. Each term is highlighted once, so no term is answered twice.
- `9642828`: drawing instructions moved into the term prompt; the original logo was added.
- `ccc08c4`: the app's look was refreshed, the sign-in pages rebuilt, and a show-password toggle added.
- `ba06ba1`: tests run before any image is built.

## 2026-10-31: first round of researcher changes

- `516ad06`: logo in the header, larger description box, wider side boxes, more visible rounded corners, a clearer delete mark, and a rotate cursor on the rotation handle.
- `f46f78c`: Filters gained search, Select all and Cancel and Return. Progress and Log Out moved to the top right, the drawing tools were renamed Boundary Box and Freehand Contour, tool names slide out on hover, and the magnifier was removed.

## 2026-10: admin panel and Azure readiness

Admin panel with progress charts, lock management and result export (`500528e`, `f634def`, `0721655`). Zoom to area and description reader (`b316022`). Export with every image file (`868b3dc`). Sign-up warnings and removal of stuck images (`2d71f24`). Images copied to storage at launch (`cc49960`). Upgrade to .NET 10 (`d530a5e`) and security updates (`9c77027`). Password reset command (`b1d67d6`). Azure fixes and container image build (`f54f1a8`, `ce4307e`). Sign-in rate limits (`8b76153`, `cbc119b`, `98418a0`). Faster annotation loading (`a930b79`, `61d2e3f`, `41ee4e3`, `c6fbf62`).

## 2026-07: tidying and documentation

Code clean-up and refactoring (`b8eb001`, `b27edf5`). README documentation and demo video (`743777f` to `333bb3c`).

## 2024-10 to 2025-03: the original app

Sign-in and registration with ASP.NET Identity, image and keyword data, the annotation page, cropping and the magnifier tool (`205c805` to `7529864`). Docker setup and the Excel data loader (`e29f102`, `134f5cb`). Shared access page (`6c8412c`). ORCID iD on accounts (`6072d11`). Student workspace (`3e9afe2`, `cf53aed`).
