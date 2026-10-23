/* ============================================================================
   0009_SeedMedicalHistoryLists.sql

   Seeds the Health Record → Medical History pick lists, for every hospital missing them:
     Chronic Condition  → cfg.MedicalHistoryItem  (per hospital; ListMedicalHistoryItems)
     Surgery / Event    → cfg.MedicalEventType    (per hospital; ListMedicalEventItems)

   - Values match the app's demo data (DataStore.Seed) — a starter set; conditions can be
     extended or switched off from Settings.
   - Safe to re-run: existing rows (same name) are left alone.
   ============================================================================ */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

/* ── Chronic conditions ── */
DECLARE @Conditions TABLE (ItemName NVARCHAR(100), SortOrder INT);
INSERT INTO @Conditions VALUES
    (N'Diabetes', 1), (N'Heart Disease', 2), (N'Hypertension', 3), (N'Hyperlipidemias', 4), (N'Osteoarthritis', 5),
    (N'Asthma / COPD / Bronchitis', 6), (N'Cancer', 7), (N'Alcohol or Drug Abuse', 8), (N'HIV / STD / Related Ailments', 9),
    (N'Hypothyroidism', 10), (N'CKD', 11), (N'Epilepsy', 12), (N'Depression / Anxiety', 13), (N'Rheumatoid Arthritis', 14);

INSERT INTO cfg.MedicalHistoryItem (HospitalId, ItemName, IsQuickPick, SortOrder, IsActive)
SELECT h.HospitalId, c.ItemName, 1, c.SortOrder, 1
FROM   cfg.Hospital h CROSS JOIN @Conditions c
WHERE  NOT EXISTS (SELECT 1 FROM cfg.MedicalHistoryItem x WHERE x.HospitalId = h.HospitalId AND x.ItemName = c.ItemName);
PRINT CONCAT(@@ROWCOUNT, N' chronic condition row(s) added.');

/* ── Surgeries / medical events ── */
DECLARE @Events TABLE (EventName NVARCHAR(100), SortOrder INT);
INSERT INTO @Events VALUES
    (N'CABG', 1), (N'Angioplasty / Stent', 2), (N'Appendectomy', 3), (N'Cholecystectomy', 4), (N'Joint Replacement', 5),
    (N'C-Section', 6), (N'Hysterectomy', 7), (N'Stroke', 8), (N'Myocardial Infarction', 9), (N'Fracture (specify)', 10);

INSERT INTO cfg.MedicalEventType (HospitalId, EventName, SortOrder, IsActive)
SELECT h.HospitalId, e.EventName, e.SortOrder, 1
FROM   cfg.Hospital h CROSS JOIN @Events e
WHERE  NOT EXISTS (SELECT 1 FROM cfg.MedicalEventType x WHERE x.HospitalId = h.HospitalId AND x.EventName = e.EventName);
PRINT CONCAT(@@ROWCOUNT, N' surgery / event row(s) added.');

COMMIT TRANSACTION;
