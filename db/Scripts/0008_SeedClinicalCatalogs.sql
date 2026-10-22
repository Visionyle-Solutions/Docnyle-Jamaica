/* ============================================================================
   0008_SeedClinicalCatalogs.sql

   Seeds the pick lists the consultation page searches, for every hospital missing them:
     Complaints      → cfg.SymptomCatalog   (per hospital; quick picks shown as chips)
     Diagnosis       → cfg.ICD10Catalog     (global)
     Labs / Imaging  → cfg.LabTestCatalog   (per hospital)
     Prescriptions   → cfg.DrugFormulary    (per hospital)

   - Values match the app's demo data (DataStore.SeedClinicalCatalogs) — a starter set to extend
     from Settings (formulary) or with a fuller ICD-10 import.
   - Lab tests are not linked to specialties (cfg.LabTestSpecialty): the hospital has none yet.
   - Safe to re-run: existing rows (same code / name) are left alone.
   ============================================================================ */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

/* ── Complaints: symptom catalog ── */
DECLARE @Symptoms TABLE (SymptomCode NVARCHAR(40), DisplayLabel NVARCHAR(100), IsQuickPick BIT);
INSERT INTO @Symptoms VALUES
    (N'FEVER', N'Fever', 1), (N'COUGH', N'Cough', 1), (N'HEADACHE', N'Headache', 1), (N'RIGORS', N'Rigors / chills', 1),
    (N'NAUSEA', N'Nausea', 1), (N'FATIGUE', N'Fatigue', 1), (N'VOMITING', N'Vomiting', 1), (N'CHEST_PAIN', N'Chest pain', 1),
    (N'BREATHLESSNESS', N'Breathlessness', 0), (N'DIARRHEA', N'Diarrhea', 0), (N'ABDOMINAL_PAIN', N'Abdominal pain', 0),
    (N'JOINT_PAIN', N'Joint pain', 0), (N'BACK_PAIN', N'Back pain', 0), (N'DIZZINESS', N'Dizziness', 0),
    (N'WEIGHT_LOSS', N'Weight loss', 0), (N'SKIN_RASH', N'Skin rash', 0), (N'SWELLING', N'Swelling', 0),
    (N'BURNING_URINE', N'Burning urination', 0);

INSERT INTO cfg.SymptomCatalog (HospitalId, SymptomCode, DisplayLabel, IsQuickPick, IsActive)
SELECT h.HospitalId, s.SymptomCode, s.DisplayLabel, s.IsQuickPick, 1
FROM   cfg.Hospital h CROSS JOIN @Symptoms s
WHERE  NOT EXISTS (SELECT 1 FROM cfg.SymptomCatalog x WHERE x.HospitalId = h.HospitalId AND x.SymptomCode = s.SymptomCode);
PRINT CONCAT(@@ROWCOUNT, N' symptom row(s) added.');

/* ── Diagnosis: ICD-10 (global) ── */
DECLARE @Icd TABLE (Icd10Code NVARCHAR(10), [Description] NVARCHAR(300), SynonymTerms NVARCHAR(500));
INSERT INTO @Icd VALUES
    (N'R50.9', N'Fever, unspecified', N'fever,pyrexia,high temperature'),
    (N'J00',   N'Acute nasopharyngitis (common cold)', N'cold,cough,runny nose,common cold'),
    (N'I10',   N'Essential (primary) hypertension', N'high bp,hypertension,bp'),
    (N'E11.9', N'Type 2 diabetes mellitus without complications', N'diabetes,sugar,high sugar,type 2'),
    (N'A90',   N'Dengue fever', N'dengue'),
    (N'G43.9', N'Migraine, unspecified', N'migraine,headache');

INSERT INTO cfg.ICD10Catalog (Icd10Code, [Description], SynonymTerms, IsActive)
SELECT i.Icd10Code, i.[Description], i.SynonymTerms, 1
FROM   @Icd i
WHERE  NOT EXISTS (SELECT 1 FROM cfg.ICD10Catalog x WHERE x.Icd10Code = i.Icd10Code);
PRINT CONCAT(@@ROWCOUNT, N' ICD-10 row(s) added.');

/* ── Labs / Imaging: lab test catalog ── */
DECLARE @Tests TABLE (TestName NVARCHAR(100), TestType NVARCHAR(20), Category NVARCHAR(50));
INSERT INTO @Tests VALUES
    (N'Complete Blood Count (CBC)', N'LAB', N'Hematology'), (N'HbA1c', N'LAB', N'Biochemistry'),
    (N'Fasting Blood Sugar (FBS)', N'LAB', N'Biochemistry'), (N'Post Prandial Blood Sugar (PPBS)', N'LAB', N'Biochemistry'),
    (N'Lipid Profile', N'LAB', N'Biochemistry'), (N'Liver Function Test (LFT)', N'LAB', N'Biochemistry'),
    (N'Renal Function Test (RFT/KFT)', N'LAB', N'Biochemistry'), (N'Thyroid Profile (TSH, T3, T4)', N'LAB', N'Endocrinology'),
    (N'Urine Routine & Microscopy', N'LAB', N'Microbiology'), (N'Dengue NS1 Antigen', N'LAB', N'Serology'),
    (N'Malarial Smear (MP/MF)', N'LAB', N'Parasitology'), (N'ECG (12-lead)', N'LAB', N'Cardiology'),
    (N'Chest X-ray (PA view)', N'IMAGING', N'Radiology'), (N'Ultrasound Abdomen', N'IMAGING', N'Radiology'),
    (N'Echocardiography (2D Echo)', N'IMAGING', N'Cardiology'), (N'ESR', N'LAB', N'Hematology'),
    (N'CRP (C-Reactive Protein)', N'LAB', N'Biochemistry'), (N'Blood Culture & Sensitivity', N'LAB', N'Microbiology');

INSERT INTO cfg.LabTestCatalog (HospitalId, TestName, TestType, Category, IsActive)
SELECT h.HospitalId, t.TestName, t.TestType, t.Category, 1
FROM   cfg.Hospital h CROSS JOIN @Tests t
WHERE  NOT EXISTS (SELECT 1 FROM cfg.LabTestCatalog x WHERE x.HospitalId = h.HospitalId AND x.TestName = t.TestName);
PRINT CONCAT(@@ROWCOUNT, N' lab test row(s) added.');

/* ── Prescriptions: drug formulary ── */
DECLARE @Drugs TABLE (BrandName NVARCHAR(200), GenericName NVARCHAR(200), Composition NVARCHAR(500), TherapeuticClass NVARCHAR(300),
                      GenericClass NVARCHAR(200), DrugForm NVARCHAR(30), DefaultRoute NVARCHAR(30), DefaultDosage NVARCHAR(20),
                      DefaultFrequency NVARCHAR(20), DefaultDurationDays INT, DefaultInstructions NVARCHAR(300));
INSERT INTO @Drugs VALUES
    (N'Crocin', N'Paracetamol', N'Paracetamol 500mg', N'Analgesic / Antipyretic', N'Paracetamol', N'Tablet', N'Oral', N'500mg', N'1-1-1', 3, N'After food'),
    (N'Augmentin', N'Amoxicillin + Clavulanate', N'Amoxicillin 500mg + Clavulanate 125mg', N'Antibiotic', N'Penicillin combination', N'Tablet', N'Oral', N'625mg', N'1-0-1', 5, N'After food'),
    (N'Amlodac', N'Amlodipine', N'Amlodipine 5mg', N'Antihypertensive', N'Calcium channel blocker', N'Tablet', N'Oral', N'5mg', N'1-0-0', 30, N'Morning, before food'),
    (N'Glycomet', N'Metformin', N'Metformin 500mg', N'Antidiabetic', N'Biguanide', N'Tablet', N'Oral', N'500mg', N'1-0-1', 30, N'After food'),
    (N'Cetzine', N'Cetirizine', N'Cetirizine 10mg', N'Antihistamine', N'Cetirizine', N'Tablet', N'Oral', N'10mg', N'0-0-1', 5, N'At night'),
    (N'Omez', N'Omeprazole', N'Omeprazole 20mg', N'Proton Pump Inhibitor', N'PPI', N'Capsule', N'Oral', N'20mg', N'1-0-0', 14, N'Before breakfast, empty stomach'),
    (N'Ascoril-D', N'Dextromethorphan + Phenylephrine + Chlorpheniramine', N'DM+PE+CPM', N'Cough Suppressant', N'Antitussive combination', N'Syrup', N'Oral', N'5ml', N'1-1-1', 5, N'After food'),
    (N'Otrivin', N'Xylometazoline', N'Xylometazoline 0.1%', N'Nasal Decongestant', N'Sympathomimetic', N'Nasal Drops', N'Nasal', N'2 drops each nostril', N'1-0-1', 5, N'Not more than 5 days'),
    (N'Monocef', N'Ceftriaxone', N'Ceftriaxone 1g', N'Antibiotic (Cephalosporin)', N'3rd gen Cephalosporin', N'Injection', N'IV/IM', N'1g', N'1-0-1', 5, N'To be administered by healthcare professional'),
    (N'Candid Mouth Paint', N'Clotrimazole', N'Clotrimazole 1%', N'Antifungal', N'Azole antifungal', N'Topical Paint', N'Topical', N'Apply thin layer', N'1-1-1', 7, N'Apply to affected area');

INSERT INTO cfg.DrugFormulary (HospitalId, BrandName, GenericName, Composition, TherapeuticClass, GenericClass, DrugForm,
                               DefaultRoute, DefaultDosage, DefaultFrequency, DefaultDurationDays, DefaultInstructions,
                               IsControlledSubstance, IsActive)
SELECT h.HospitalId, d.BrandName, d.GenericName, d.Composition, d.TherapeuticClass, d.GenericClass, d.DrugForm,
       d.DefaultRoute, d.DefaultDosage, d.DefaultFrequency, d.DefaultDurationDays, d.DefaultInstructions, 0, 1
FROM   cfg.Hospital h CROSS JOIN @Drugs d
WHERE  NOT EXISTS (SELECT 1 FROM cfg.DrugFormulary x WHERE x.HospitalId = h.HospitalId AND x.BrandName = d.BrandName);
PRINT CONCAT(@@ROWCOUNT, N' drug row(s) added.');

COMMIT TRANSACTION;

/* Check (counts per table):
SELECT 'Symptoms' AS List, COUNT(*) FROM cfg.SymptomCatalog
UNION ALL SELECT 'ICD-10',   COUNT(*) FROM cfg.ICD10Catalog
UNION ALL SELECT 'LabTests', COUNT(*) FROM cfg.LabTestCatalog
UNION ALL SELECT 'Drugs',    COUNT(*) FROM cfg.DrugFormulary;
*/
