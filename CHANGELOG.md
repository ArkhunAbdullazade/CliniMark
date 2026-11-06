# Changelog

What changed in CliniMark and why, newest first. Git holds every line of every change; this file explains them in plain language, with the commit that delivered each one. The researcher's requests behind these changes, word for word, are in [docs/requests-log.md](docs/requests-log.md).

Database changes are in `src/CliniMark.Presentation/Migrations`, and each release that changes the database has a script in `deployment/`.

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
