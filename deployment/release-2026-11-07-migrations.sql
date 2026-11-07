-- Release 2026-11-07: the tutorial. Adds one column, "TutorialWatchedAt", to "AspNetUsers". Nothing is dropped or rewritten.
-- Every step is skipped when already applied, so the script can be re-run.
-- Apply AFTER deployment/release-2026-11-06-migrations.sql and BEFORE the new image runs (production refuses to start while
-- migrations are pending):
--   1. Back up the database.
--   2. psql "<production connection>" -v ON_ERROR_STOP=1 -f deployment/release-2026-11-07-migrations.sql
--   3. Start a new Azure Container Apps revision on the new image and check the site.
-- Generated with: dotnet ef migrations script 20261106031412_AddImageSkipsAndPixelMasks --idempotent

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261107003418_AddTutorialWatchedAt') THEN
    ALTER TABLE "AspNetUsers" ADD "TutorialWatchedAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261107003418_AddTutorialWatchedAt') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261107003418_AddTutorialWatchedAt', '10.0.14');
    END IF;
END $EF$;
COMMIT;

