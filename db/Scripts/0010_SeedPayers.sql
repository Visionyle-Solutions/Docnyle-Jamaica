/* ============================================================================
   0010_SeedPayers.sql

   Seeds cfg.Payer for Jamaica (country JM), which feeds Coverage & Insurance and billing checkout:
     Scheme             → GOVT_SCHEME  National Health Fund (NHF)
     Insurance Company  → PRIVATE      Sagicor Life, Guardian Life, Medecus, Canopy Insurance

   - Values match the app's demo data (DataStore.Seed); more insurers can be added from Settings.
   - Payers are listed by cfg.Hospital.Country, so the hospital must be set to 'JM' (checked below).
   - Safe to re-run: existing rows (same country + name) are left alone.
   ============================================================================ */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Payers TABLE (Name NVARCHAR(150), Category NVARCHAR(15));
INSERT INTO @Payers VALUES
    (N'National Health Fund (NHF)', N'GOVT_SCHEME'),
    (N'Sagicor Life', N'PRIVATE'), (N'Guardian Life', N'PRIVATE'), (N'Medecus', N'PRIVATE'), (N'Canopy Insurance', N'PRIVATE');

INSERT INTO cfg.Payer (Name, Category, Country, IsActive)
SELECT p.Name, p.Category, N'JM', 1
FROM   @Payers p
WHERE  NOT EXISTS (SELECT 1 FROM cfg.Payer x WHERE x.Country = N'JM' AND x.Name = p.Name);
PRINT CONCAT(@@ROWCOUNT, N' payer row(s) added.');

/* Hospitals that will not see these payers (country other than JM) */
SELECT HospitalId, Country FROM cfg.Hospital WHERE ISNULL(Country, N'') <> N'JM';

COMMIT TRANSACTION;
