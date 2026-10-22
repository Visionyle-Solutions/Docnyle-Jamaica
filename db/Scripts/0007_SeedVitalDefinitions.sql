/* ============================================================================
   0007_SeedVitalDefinitions.sql

   Seeds the built-in vitals (cfg.VitalDefinition) for every hospital that is missing them.
   Without these rows the Nurse Vitals page, the consultation's Vitals card and
   Settings → Clinical Config → Vitals show no vitals at all.

   - Codes, labels, base units and order match DataStore.SeedClinicalCatalogs; ranges match
     ConsultationService.VitalRanges; units/conversions live in VitalUnitService.Standard.
   - Waist and head circumference start switched off (VitalUnitService.OffByDefault);
     head circumference is paediatric only.
   - Safe to re-run: a hospital keeps any vital it already has (UQ_cfg_VD_Hosp_Code).
   - The app caches this list for 5 minutes, so allow that long (or restart the site)
     before the vitals appear.
   ============================================================================ */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Vitals TABLE (
    VitalCode       NVARCHAR(40)  NOT NULL,
    DisplayLabel    NVARCHAR(50)  NOT NULL,
    AllowedUnits    NVARCHAR(20)  NULL,
    MinValue        DECIMAL(10,2) NULL,
    MaxValue        DECIMAL(10,2) NULL,
    IsPediatricOnly BIT           NOT NULL,
    SortOrder       INT           NOT NULL,
    IsEnabled       BIT           NOT NULL
);

INSERT INTO @Vitals (VitalCode, DisplayLabel, AllowedUnits, MinValue, MaxValue, IsPediatricOnly, SortOrder, IsEnabled)
VALUES
    (N'TEMP',       N'Temp',                N'F',           90,   110, 0,  1, 1),
    (N'BP_SYS',     N'BP systolic',         N'mmHg',        50,   300, 0,  2, 1),
    (N'BP_DIA',     N'BP diastolic',        N'mmHg',        20,   200, 0,  3, 1),
    (N'PULSE',      N'Pulse',               N'bpm',         20,   250, 0,  4, 1),
    (N'SPO2',       N'SpO2',                N'%',            0,   100, 0,  5, 1),
    (N'HEIGHT',     N'Height',              N'cm',          20,   250, 0,  6, 1),
    (N'WEIGHT',     N'Weight',              N'kg',         0.5,   500, 0,  7, 1),
    (N'RESP_RATE',  N'Respiratory Rate',    N'breaths/min',  5,    60, 0,  8, 1),
    (N'PAIN_SCALE', N'Pain Scale',          N'0-10',         0,    10, 0,  9, 1),
    (N'WAIST_CIRC', N'Waist Circumference', N'cm',          30,   200, 0, 10, 0),
    (N'HEAD_CIRC',  N'Head Circumference',  N'cm',          20,    70, 1, 11, 0);

INSERT INTO cfg.VitalDefinition
    (HospitalId, VitalCode, DisplayLabel, AllowedUnits, MinValue, MaxValue,
     IsCustom, IsPediatricOnly, SortOrder, IsEnabled, IsActive)
SELECT h.HospitalId, v.VitalCode, v.DisplayLabel, v.AllowedUnits, v.MinValue, v.MaxValue,
       0, v.IsPediatricOnly, v.SortOrder, v.IsEnabled, 1
FROM   cfg.Hospital h
CROSS  JOIN @Vitals v
WHERE  NOT EXISTS (SELECT 1 FROM cfg.VitalDefinition vd
                   WHERE vd.HospitalId = h.HospitalId AND vd.VitalCode = v.VitalCode);

PRINT CONCAT(@@ROWCOUNT, N' vital definition row(s) added.');

COMMIT TRANSACTION;

/* Check:
SELECT h.Name, vd.SortOrder, vd.VitalCode, vd.DisplayLabel, vd.AllowedUnits, vd.IsEnabled
FROM   cfg.VitalDefinition vd JOIN cfg.Hospital h ON h.HospitalId = vd.HospitalId
ORDER  BY h.Name, vd.SortOrder;
*/
