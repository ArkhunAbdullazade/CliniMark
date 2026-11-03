-- Release 2026-11-03: image attribution (source licence + credit line shown under each image).
-- Adds two nullable text columns to "MedDatas": "SourceLicense" and "Attribution". Nothing is dropped or rewritten.
-- Every step is skipped when already applied, so the script can be re-run safely.
-- Apply AFTER deployment/release-2026-11-02-migrations.sql and BEFORE the new image runs (production refuses to start
-- while migrations are pending):
--   1. Back up the database.
--   2. psql "<production connection>" -v ON_ERROR_STOP=1 -f deployment/release-2026-11-03-migrations.sql
--   3. Start a new Azure Container Apps revision on the new image and check the site.
-- A new, empty database instead gets the full script: dotnet ef migrations script --idempotent
-- Generated with: dotnet ef migrations script 20261101220046_AddCustomAnnotations --idempotent

﻿START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103001626_AddImageAttribution') THEN
    ALTER TABLE "MedDatas" ADD "Attribution" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103001626_AddImageAttribution') THEN
    ALTER TABLE "MedDatas" ADD "SourceLicense" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103001626_AddImageAttribution') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261103001626_AddImageAttribution', '10.0.14');
    END IF;
END $EF$;
COMMIT;

