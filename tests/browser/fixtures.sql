-- Synthetic local browser-audit data. Run only against a new clinimark_audit_* database.
INSERT INTO "MedDatas" ("Id", "ImageUrl", "ImageDescription", "Diagnosis", "Speciality", "BodyRegion", "Modality", "IsAnnotated", "IsAnnotatedByStudent") VALUES
(1, 'http://127.0.0.1:5211/tests/browser/fixture.svg', 'Synthetic image: lesion and boundary. Not patient data.', 'Synthetic lesion', 'cardiology', 'Chest and Thorax', 'ct', false, false),
(2, 'http://127.0.0.1:5211/tests/browser/fixture.svg', 'Synthetic second image: lesion. Not patient data.', 'Synthetic lesion', 'dermatology', 'head and neck', 'mri', false, false),
(3, 'http://127.0.0.1:5211/tests/browser/fixture.svg', 'Excluded by body region.', 'Synthetic', 'cardiology', 'abdomen', 'ct', false, false),
(4, 'http://127.0.0.1:5211/tests/browser/fixture.svg', 'Excluded by modality.', 'Synthetic', 'cardiology', 'Chest and Thorax', 'x-ray', false, false),
(5, 'http://127.0.0.1:5211/tests/browser/fixture.svg', 'Unknown metadata must not match.', 'Synthetic', NULL, NULL, NULL, false, false);
INSERT INTO "MedDataKeywords" ("MedDataId", "Keyword") VALUES (1, 'lesion'), (1, 'boundary'), (2, 'lesion'), (3, 'lesion'), (4, 'lesion'), (5, 'lesion');
UPDATE "MedDatas" SET "SourceUrl" = 'https://example.org/synthetic-article', "SourceLicense" = 'CC BY 4.0',
  "Attribution" = 'Synthetic credit line for image 1. Not a real article. Licensed under CC BY 4.0.' WHERE "Id" = 1;
