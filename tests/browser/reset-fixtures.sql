-- Clears ONLY the disposable local browser audit database created by this audit.
DO $$ BEGIN
  IF current_database() <> 'clinimark_audit_20261022' THEN
    RAISE EXCEPTION 'This reset is restricted to the disposable browser audit database';
  END IF;
END $$;
TRUNCATE "AnnotatedMedDatas", "AnnotatedByStudentsMedDatas", "AspNetUsers" RESTART IDENTITY CASCADE;
UPDATE "MedDatas" SET "IsAnnotated" = false, "IsAnnotatedByStudent" = false,
 "LockedByUserId" = NULL, "LockedByStudentUserId" = NULL, "KeywordStates" = NULL;
