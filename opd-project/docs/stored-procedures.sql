/* ============================================================
   Consult360 — Stored Procedures
   SQL Server 2019 T-SQL

   SCOPE: This file is derived 1:1 from js/api.js (150 public
   methods) + js/data.js (in-memory implementation) + docs/schema.sql
   (51-table SQL Server 2019 schema), per the binding rules in
   docs/DATA-LAYER-ARCHITECTURE.md. Every method exported on
   window.API has exactly one (or one small logical group of)
   stored procedure(s) below that reproduces its data.js business
   logic against the real schema tables/columns.

   Version: 2.1 — supersedes the v2.0 draft (152 SPs / 143 methods).
   v2.1 adds 5 stored procedures (cfg.usp_ToggleSpecialty,
   cfg.usp_CreateVitalConfig, cfg.usp_CreateDrug, cfg.usp_UpdateDrugStatus,
   cfg.usp_UpdateMessageTemplate) covering 7 api.js methods that were
   added or corrected during a Phase-1 functional-test bug-fix pass
   (toggleSpecialty, createVitalConfig, createDrug, updateDrugStatus,
   updateMessageTemplate, exportTableCsv — the last reuses the existing
   sec.usp_ListExportData_* procedures, no new SP needed — and
   updatePermission, whose SP already existed in v2.0 but had never
   been wired to a documented endpoint since api.js didn't call it yet).
   Extends coverage to all 150 current api.js methods (157 procedures
   + 3 views).

   Version 3.0 (2026-10-15) — aligned with docs/schema.sql v4.0, whose
   column names follow the C# models in Models/. Every procedure was
   rewritten for the renamed columns (e.g. cfg.Hospital LegalAddress /
   ContactNumber / PrimaryEmail, cfg.DrugFormulary DrugId /
   IsControlledSubstance, cfg.PhraseBank Scope / Section / TriggerKeyword,
   cln.InvestigationOrder OrderId, cln.LabResult ResultId, bil.Payment
   TransactionId, sec.AuditLog [Timestamp]) and the v4.0 CHECK values
   (order ORDERED/SENT/RESULTS_READY, lab result PENDING_REVIEW/REVIEWED,
   symptom UNSET/YES/NO). Parameters that fed dropped columns were
   removed; where a procedure's signature changed, a "v4.0:" comment says
   why. User roles moved to sec.UserRole (@Roles is comma-separated).
   sec.usp_CreateUserSession / sec.usp_CheckSessionValid use a reserved
   table (schema.sql section G) and are flagged RESERVED. Verified: all
   procedures compile on SQL Server against schema v4.0 with no
   unresolved references.

   Version 3.6 (2026-10-17) — closes API-SPECIFICATION.md Known Gaps #4–#7:
   opd.usp_CreatePatient / opd.usp_UpdatePatient take every registration
   field and RegisteredVia, and MRNs follow MRN_PREFIX / MRN_FORMAT /
   MRN_SEQ (cfg.fn_FormatMrn, allocated under a lock); sec.usp_GetUserCredential
   and sec.usp_RecordLoginAttempt support the salted password check and
   lockout (sec.LoginAttempt is no longer reserved); opd.usp_CreateAppointment
   re-checks the slot under the per-doctor-day lock; opd.usp_ListAvailableSlots
   rejects slot lengths that would never end its loop. Error numbers 50205, 50206.

   Version 3.5 (2026-10-17) — section 22 closes the Phase 4 gaps: 52
   procedures and 2 helper functions for the features the Web Forms app
   ran only in memory (duplicate check, medical event / family history
   edits, social history, consent documents, CSV import, payer and lab
   partner admin, single appointment / patient history / range lists and
   the dashboard, reschedule, recurring booking, patient-portal booking,
   in-progress consultation, medication history, outside medicines,
   removing an order, editing a drug, service charge on/off, lab claim by
   mobile, lab attachments, in-app notifications and the consultation form
   designer). Needs schema.sql v4.1 (sec.LabClaimAttempt). Error numbers
   50200–50249.

   Naming: {SqlSchema}.usp_{Verb}{Entity}{Qualifier}
   Verbs (14, per architecture doc §2): Get, List, Search, Check,
     Create, Save, Update, Delete, Submit, Confirm, Cancel,
     Approve, Reject, Record.

   Conventions applied throughout:
   - SET NOCOUNT ON in every procedure.
   - Every hospital-scoped list/search/get procedure accepts
     @HospitalId and filters WHERE HospitalId = @HospitalId.
   - Every table access uses the external UNIQUEIDENTIFIER
     {Table}Id column, never the internal Id — callers
     never see Id (see schema.sql header, dual-key pattern).
   - New rows: INSERT ... OUTPUT inserted.* — NEWSEQUENTIALID()
     default supplies the id (see DF_{schema}_{tbl}_Id defaults
     in schema.sql), matching data.js's nextId(prefix) pattern.
   - Soft delete: tables with IsActive/Status columns
     are updated, never hard-deleted, matching data.js behavior
     (e.g. deleteAllergy sets Status='INACTIVE'). Where data.js
     itself performs an in-memory splice() AND the schema table
     has no IsActive/Status column to soft-delete on
     (e.g. bil.InvoiceLineItem, cfg.PhraseBank via deletePhrase),
     a real DELETE is used and flagged with a comment.
   - Audit-relevant actions (patient create, sign-off, alert
     override, export, print, permission/role change, login) are
     flagged with a comment: "AUDIT: caller must also invoke
     sec.usp_RecordAuditEntry" — no auto-firing triggers, matching
     the style already established in the v1.0 draft.
   - TODO comments flag any data.js field/behavior that has no
     matching column in schema.sql (schema is source of truth;
     these are noted rather than inventing incompatible columns).
   ============================================================ */

-- ============================================================
-- 1. SECURITY, USERS & SESSIONS (sec)
-- ============================================================
-- 0. TIME ZONE HELPERS
--    Every point in time is stored as UTC (DATETIMEOFFSET +00:00). "Today", report date ranges and the
--    calendar date of a visit or appointment are taken in the hospital's timezone (cfg.Hospital.Timezone →
--    cfg.TimeZone), never the database server's clock: a Jamaican clinic's day ends at local midnight
--    (05:00 UTC), whatever timezone the server runs in.
-- ============================================================
CREATE OR ALTER FUNCTION cfg.fn_HospitalWindowsTz (@HospitalId UNIQUEIDENTIFIER)
RETURNS NVARCHAR(100) AS
BEGIN
    RETURN COALESCE((SELECT tz.WindowsTimeZoneId FROM cfg.Hospital h
                     INNER JOIN cfg.TimeZone tz ON tz.TimeZoneId = h.Timezone
                     WHERE h.HospitalId = @HospitalId), N'UTC');
END;
GO

-- The hospital's current calendar date.
CREATE OR ALTER FUNCTION cfg.fn_HospitalToday (@HospitalId UNIQUEIDENTIFIER)
RETURNS DATE AS
BEGIN
    RETURN CAST(SYSDATETIMEOFFSET() AT TIME ZONE cfg.fn_HospitalWindowsTz(@HospitalId) AS DATE);
END;
GO

-- Timezones offered for hospitals (and, at the API layer, for mapping a browser's IANA zone).
CREATE OR ALTER PROCEDURE cfg.usp_ListTimeZones
AS BEGIN
    SET NOCOUNT ON;
    SELECT TimeZoneId, WindowsTimeZoneId, DisplayName FROM cfg.TimeZone WHERE IsActive = 1 ORDER BY DisplayName;
END;
GO

-- ============================================================

CREATE OR ALTER PROCEDURE sec.usp_AuthenticateUser
    @Email          NVARCHAR(100),
    @PasswordHash   NVARCHAR(200)
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='LOGIN' or 'LOGIN_FAIL').
    -- v4.0: roles come from sec.UserRole (AppUser.Roles, comma-joined here). The v3.0 insert into
    -- sec.LoginAttempt was removed — that table is reserved (schema.sql section G); failed logins
    -- are only audited.
    SELECT u.UserId, u.HospitalId, u.FullName, u.Email, u.Phone,
           (SELECT STRING_AGG(r.[Role], ',') FROM sec.UserRole r WHERE r.UserId = u.UserId) AS Roles,
           u.SpecialtyId, u.LicenseNumber, u.LabPartnerId, u.IsActive
    FROM sec.ApplicationUser u
    INNER JOIN cfg.Hospital h ON h.HospitalId = u.HospitalId AND h.IsActive = 1
    WHERE u.Email = @Email AND u.PasswordHash = @PasswordHash
      AND u.IsActive = 1;
END;
GO
-- v3.6: salted password check (WCF-MIGRATION-GUIDE.md §7.5). A PBKDF2 hash cannot be recomputed from the
-- password alone, so the service first reads the stored hash for the e-mail here, verifies the password in C#,
-- then calls sec.usp_AuthenticateUser with that stored hash. PasswordHash format (written by the service):
--   PBKDF2$SHA256${iterations}${salt base64}${hash base64}
-- Returns nothing for an unknown e-mail, an inactive user or an inactive hospital — the service still runs one
-- dummy PBKDF2 verification then, so response time does not reveal which e-mails exist.
-- RecentFailures: failed sign-ins for this e-mail in the last 15 minutes since the last success
-- (sec.LoginAttempt); 5 or more = locked out until LockedUntil.
CREATE OR ALTER PROCEDURE sec.usp_GetUserCredential
    @Email NVARCHAR(100)
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Since DATETIMEOFFSET(3) = DATEADD(MINUTE, -15, SYSUTCDATETIME());
    DECLARE @LastSuccess DATETIMEOFFSET(3) = (SELECT MAX(CreatedAt) FROM sec.LoginAttempt WHERE EmailAttempted = @Email AND IsSuccess = 1);
    DECLARE @Failures INT, @LastFailure DATETIMEOFFSET(3);
    SELECT @Failures = COUNT(*), @LastFailure = MAX(CreatedAt)
    FROM sec.LoginAttempt
    WHERE EmailAttempted = @Email AND IsSuccess = 0 AND CreatedAt > @Since
      AND (@LastSuccess IS NULL OR CreatedAt > @LastSuccess);

    SELECT u.UserId, u.PasswordHash, @Failures AS RecentFailures,
           CASE WHEN @Failures >= 5 THEN DATEADD(MINUTE, 15, @LastFailure) END AS LockedUntil
    FROM sec.ApplicationUser u
    INNER JOIN cfg.Hospital h ON h.HospitalId = u.HospitalId AND h.IsActive = 1
    WHERE u.Email = @Email AND u.IsActive = 1;
END;
GO

-- v3.6: one row per sign-in attempt (success or failure), for the lockout above. Called by the service after it
-- has verified (or failed to verify) the password; the LOGIN / LOGIN_FAIL audit entry is written separately.
CREATE OR ALTER PROCEDURE sec.usp_RecordLoginAttempt
    @Email     NVARCHAR(100),
    @IsSuccess BIT,
    @IpAddress NVARCHAR(45) = NULL
AS BEGIN
    SET NOCOUNT ON;
    INSERT INTO sec.LoginAttempt (EmailAttempted, IsSuccess, IpAddress) VALUES (@Email, @IsSuccess, @IpAddress);
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_CreateUser
    @HospitalId     UNIQUEIDENTIFIER,
    @FullName       NVARCHAR(200),
    @Email          NVARCHAR(100),
    @PasswordHash   NVARCHAR(200),
    @Phone          NVARCHAR(20) = NULL,
    @Roles          NVARCHAR(200),              -- comma-separated, e.g. 'DOCTOR,ADMIN' (rows in sec.UserRole)
    @SpecialtyId    UNIQUEIDENTIFIER = NULL,
    @LicenseNumber  NVARCHAR(50) = NULL,
    @LabPartnerId   UNIQUEIDENTIFIER = NULL,
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @New TABLE (UserId UNIQUEIDENTIFIER);
    BEGIN TRAN;
    INSERT INTO sec.ApplicationUser (HospitalId, FullName, Email, PasswordHash, Phone,
                                     SpecialtyId, LicenseNumber, LabPartnerId, CreatedBy)
    OUTPUT inserted.UserId INTO @New
    VALUES (@HospitalId, @FullName, @Email, @PasswordHash, @Phone,
            @SpecialtyId, @LicenseNumber, @LabPartnerId, ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));

    INSERT INTO sec.UserRole (UserId, [Role], CreatedBy)
    SELECT DISTINCT n.UserId, LTRIM(RTRIM(s.value)), ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000')
    FROM @New n CROSS JOIN STRING_SPLIT(@Roles, ',') s
    WHERE LTRIM(RTRIM(s.value)) <> '';
    COMMIT;

    SELECT u.UserId, u.HospitalId, u.FullName, u.Email, u.Phone,
           (SELECT STRING_AGG(r.[Role], ',') FROM sec.UserRole r WHERE r.UserId = u.UserId) AS Roles,
           u.SpecialtyId, u.LicenseNumber, u.IsActive, u.CreatedAt
    FROM sec.ApplicationUser u INNER JOIN @New n ON n.UserId = u.UserId;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_UpdateUser
    @UserId         UNIQUEIDENTIFIER,
    @FullName       NVARCHAR(200) = NULL,
    @Phone          NVARCHAR(20) = NULL,
    @Roles          NVARCHAR(200) = NULL,       -- comma-separated; NULL = leave roles unchanged
    @SpecialtyId    UNIQUEIDENTIFIER = NULL,
    @LicenseNumber  NVARCHAR(50) = NULL,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='UPDATE') if Roles changed
    BEGIN TRAN;
    UPDATE sec.ApplicationUser SET
        FullName = ISNULL(@FullName, FullName),
        Phone = ISNULL(@Phone, Phone),
        SpecialtyId = ISNULL(@SpecialtyId, SpecialtyId),
        LicenseNumber = ISNULL(@LicenseNumber, LicenseNumber),
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE UserId = @UserId;

    IF @Roles IS NOT NULL BEGIN
        DELETE FROM sec.UserRole WHERE UserId = @UserId;
        INSERT INTO sec.UserRole (UserId, [Role], CreatedBy)
        SELECT DISTINCT @UserId, LTRIM(RTRIM(value)), ISNULL(@ModifiedBy, '00000000-0000-0000-0000-000000000000')
        FROM STRING_SPLIT(@Roles, ',') WHERE LTRIM(RTRIM(value)) <> '';
    END;
    COMMIT;

    SELECT u.UserId, u.HospitalId, u.FullName, u.Email, u.Phone,
           (SELECT STRING_AGG(r.[Role], ',') FROM sec.UserRole r WHERE r.UserId = u.UserId) AS Roles,
           u.SpecialtyId, u.LicenseNumber, u.IsActive
    FROM sec.ApplicationUser u WHERE u.UserId = @UserId;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_UpdateUserActive
    @UserId         UNIQUEIDENTIFIER,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- data.js toggles the current value (no explicit flag passed by api.js) — mirrored here
    UPDATE sec.ApplicationUser
    SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE UserId = @UserId;

    SELECT UserId, IsActive FROM sec.ApplicationUser WHERE UserId = @UserId;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListUsers
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT u.UserId, u.FullName, u.Email, u.Phone,
           (SELECT STRING_AGG(r.[Role], ',') FROM sec.UserRole r WHERE r.UserId = u.UserId) AS Roles,
           u.SpecialtyId, u.LicenseNumber, u.LabPartnerId, u.IsActive, u.CreatedAt
    FROM sec.ApplicationUser u
    WHERE u.HospitalId = @HospitalId
    ORDER BY u.FullName;
END;
GO

-- RESERVED (schema.sql section G): sec.UserSession is not used by the application yet (sessions are
-- ASP.NET session state). usp_CreateUserSession / usp_CheckSessionValid are kept for when the
-- feature is built; nothing should call them today.
CREATE OR ALTER PROCEDURE sec.usp_CreateUserSession
    @UserId         UNIQUEIDENTIFIER,
    @SessionToken   NVARCHAR(100),
    @Browser        NVARCHAR(100) = NULL,
    @IpAddress      NVARCHAR(45) = NULL,
    @ExpiresAt      DATETIMEOFFSET(3)
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (UserSessionId UNIQUEIDENTIFIER, SessionToken NVARCHAR(100), CreatedAt DATETIMEOFFSET(3), ExpiresAt DATETIMEOFFSET(3));
    UPDATE sec.UserSession
    SET IsActive = 0, InvalidatedAt = SYSUTCDATETIME(), InvalidationReason = 'NEW_LOGIN'
    WHERE UserId = @UserId AND IsActive = 1;

    INSERT INTO sec.UserSession (UserId, SessionToken, Browser, IpAddress, ExpiresAt)
    OUTPUT inserted.UserSessionId, inserted.SessionToken, inserted.CreatedAt, inserted.ExpiresAt
    INTO @Out
    VALUES (@UserId, @SessionToken, @Browser, @IpAddress, @ExpiresAt);
    SELECT UserSessionId, SessionToken, CreatedAt, ExpiresAt FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_CheckSessionValid
    @SessionToken   NVARCHAR(100)
AS BEGIN
    SET NOCOUNT ON;
    SELECT UserSessionId, UserId, IsActive, ExpiresAt
    FROM sec.UserSession
    WHERE SessionToken = @SessionToken AND IsActive = 1 AND ExpiresAt > SYSUTCDATETIME();
END;
GO

-- ============================================================
-- 2. PERMISSIONS (sec.PermissionMatrix)
-- ============================================================

CREATE OR ALTER PROCEDURE sec.usp_GetPermissionMatrix
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- Returned as a flat grid (Role, Feature, IsGranted); the API layer pivots
    -- into the {ROLE: {feature: bool}} shape data.js/api.js consumers expect.
    SELECT [Role], Feature, IsGranted
    FROM sec.PermissionMatrix
    WHERE HospitalId = @HospitalId
    ORDER BY [Role], Feature;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListRoles
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT DISTINCT [Role] FROM sec.PermissionMatrix WHERE HospitalId = @HospitalId ORDER BY [Role];
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListPermissionFeatures
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT DISTINCT Feature FROM sec.PermissionMatrix WHERE HospitalId = @HospitalId ORDER BY Feature;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_UpdatePermission
    @HospitalId     UNIQUEIDENTIFIER,
    @Role           NVARCHAR(20),
    @Feature        NVARCHAR(30),
    @IsGranted      BIT
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='UPDATE', EntityType='Permission')
    MERGE sec.PermissionMatrix AS tgt
    USING (SELECT @HospitalId AS HospitalId, @Role AS [Role], @Feature AS Feature) AS src
        ON tgt.HospitalId = src.HospitalId AND tgt.[Role] = src.[Role] AND tgt.Feature = src.Feature
    WHEN MATCHED THEN UPDATE SET IsGranted = @IsGranted
    WHEN NOT MATCHED THEN INSERT (HospitalId, [Role], Feature, IsGranted)
        VALUES (@HospitalId, @Role, @Feature, @IsGranted);
END;
GO

-- ============================================================
-- 3. CONFIGURATION — HOSPITAL, SPECIALTY, PAYMENT METHODS (cfg)
-- ============================================================

CREATE OR ALTER PROCEDURE cfg.usp_GetHospital
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT HospitalId, Name, LegalAddress, ContactNumber, PrimaryEmail, Country, Currency, Timezone,
           LicenseNumber, LogoBase64, PatientIdLabel, NationalHealthIdLabel, NationalHealthIdMandatory,
           GrowthChartStandard, DefaultTempUnit, DefaultRxValidityDays,
           RxPrefix, ReceiptPrefix, InvoicePrefix, LabChargePerTest, IsActive
    FROM cfg.Hospital WHERE HospitalId = @HospitalId;
END;
GO

-- Settings → Hospital profile. A NULL parameter keeps the current value, so callers send only what changed.
-- @Timezone must be a cfg.TimeZone id (IANA); it decides the hospital's "today", report dates and appointment times.
-- @RemoveLogo = 1 clears the logo (a new @LogoBase64 replaces it).
CREATE OR ALTER PROCEDURE cfg.usp_UpdateHospital
    @HospitalId                UNIQUEIDENTIFIER,
    @Name                      NVARCHAR(200) = NULL,
    @LegalAddress              NVARCHAR(500) = NULL,
    @ContactNumber             NVARCHAR(20)  = NULL,
    @PrimaryEmail              NVARCHAR(100) = NULL,
    @Country                   NVARCHAR(5)   = NULL,
    @Currency                  NVARCHAR(5)   = NULL,
    @Timezone                  NVARCHAR(50)  = NULL,
    @LicenseNumber             NVARCHAR(50)  = NULL,
    @PatientIdLabel            NVARCHAR(30)  = NULL,
    @NationalHealthIdLabel     NVARCHAR(50)  = NULL,
    @NationalHealthIdMandatory BIT           = NULL,
    @GrowthChartStandard       NVARCHAR(5)   = NULL,
    @DefaultTempUnit           NVARCHAR(1)   = NULL,
    @DefaultRxValidityDays     INT           = NULL,
    @RxPrefix                  NVARCHAR(10)  = NULL,
    @ReceiptPrefix             NVARCHAR(10)  = NULL,
    @InvoicePrefix             NVARCHAR(10)  = NULL,
    @LabChargePerTest          DECIMAL(10,2) = NULL,
    @LogoBase64                NVARCHAR(MAX) = NULL,
    @RemoveLogo                BIT           = 0,
    @ModifiedBy                UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM cfg.Hospital WHERE HospitalId = @HospitalId) THROW 50070, N'Hospital not found.', 1;
    IF @Timezone IS NOT NULL AND NOT EXISTS (SELECT 1 FROM cfg.TimeZone WHERE TimeZoneId = @Timezone AND IsActive = 1)
        THROW 50071, N'Unknown time zone: choose one from cfg.usp_ListTimeZones.', 1;
    IF @Name IS NOT NULL AND LTRIM(RTRIM(@Name)) = N'' THROW 50072, N'Hospital name is required.', 1;
    IF @DefaultRxValidityDays IS NOT NULL AND @DefaultRxValidityDays < 1 THROW 50073, N'Prescription validity must be at least 1 day.', 1;

    UPDATE cfg.Hospital SET
        Name                      = COALESCE(LTRIM(RTRIM(@Name)), Name),
        LegalAddress              = COALESCE(@LegalAddress, LegalAddress),
        ContactNumber             = COALESCE(@ContactNumber, ContactNumber),
        PrimaryEmail              = COALESCE(@PrimaryEmail, PrimaryEmail),
        Country                   = COALESCE(@Country, Country),
        Currency                  = COALESCE(@Currency, Currency),
        Timezone                  = COALESCE(@Timezone, Timezone),
        LicenseNumber             = COALESCE(@LicenseNumber, LicenseNumber),
        PatientIdLabel            = COALESCE(NULLIF(LTRIM(RTRIM(@PatientIdLabel)), N''), PatientIdLabel),
        NationalHealthIdLabel     = COALESCE(NULLIF(LTRIM(RTRIM(@NationalHealthIdLabel)), N''), NationalHealthIdLabel),
        NationalHealthIdMandatory = COALESCE(@NationalHealthIdMandatory, NationalHealthIdMandatory),
        GrowthChartStandard       = COALESCE(@GrowthChartStandard, GrowthChartStandard),
        DefaultTempUnit           = COALESCE(@DefaultTempUnit, DefaultTempUnit),
        DefaultRxValidityDays     = COALESCE(@DefaultRxValidityDays, DefaultRxValidityDays),
        RxPrefix                  = COALESCE(@RxPrefix, RxPrefix),
        ReceiptPrefix             = COALESCE(@ReceiptPrefix, ReceiptPrefix),
        InvoicePrefix             = COALESCE(@InvoicePrefix, InvoicePrefix),
        LabChargePerTest          = COALESCE(@LabChargePerTest, LabChargePerTest),
        LogoBase64                = CASE WHEN @LogoBase64 IS NOT NULL THEN @LogoBase64 WHEN @RemoveLogo = 1 THEN NULL ELSE LogoBase64 END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE HospitalId = @HospitalId;
    EXEC cfg.usp_GetHospital @HospitalId = @HospitalId;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_ListSpecialties
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT SpecialtyId, HospitalId, SpecialtyName, IsActive
    FROM cfg.Specialty
    WHERE HospitalId = @HospitalId AND IsActive = 1
    ORDER BY SpecialtyName;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_ListSpecialtyConfig
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- Unlike usp_ListSpecialties, returns inactive rows too — admin config screen
    SELECT SpecialtyId, HospitalId, SpecialtyName, IsActive
    FROM cfg.Specialty
    WHERE HospitalId = @HospitalId
    ORDER BY SpecialtyName;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_CreateSpecialty
    @HospitalId     UNIQUEIDENTIFIER,
    @SpecialtyName  NVARCHAR(100),
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (SpecialtyId UNIQUEIDENTIFIER, SpecialtyName NVARCHAR(100), IsActive BIT);
    INSERT INTO cfg.Specialty (HospitalId, SpecialtyName, CreatedBy)
    OUTPUT inserted.SpecialtyId, inserted.SpecialtyName, inserted.IsActive
    INTO @Out
    VALUES (@HospitalId, @SpecialtyName, ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));
    SELECT SpecialtyId, SpecialtyName, IsActive FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_ToggleSpecialty
    @SpecialtyId    UNIQUEIDENTIFIER,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- data.js toggles the current value (no explicit flag passed by api.js)
    UPDATE cfg.Specialty
    SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE SpecialtyId = @SpecialtyId;

    SELECT SpecialtyId, HospitalId, SpecialtyName, IsActive
    FROM cfg.Specialty WHERE SpecialtyId = @SpecialtyId;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_ListPaymentMethods
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT PaymentMethodId, HospitalId, MethodName, IsActive
    FROM cfg.PaymentMethod
    WHERE HospitalId = @HospitalId AND IsActive = 1
    ORDER BY MethodName;
END;
GO

-- ---- Vitals config ----

CREATE OR ALTER PROCEDURE cfg.usp_ListVitalsConfig
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT VitalDefinitionId, HospitalId, VitalCode, DisplayLabel, AllowedUnits,
           MinValue, MaxValue, SortOrder, IsEnabled
    FROM cfg.VitalDefinition
    WHERE HospitalId = @HospitalId
    ORDER BY SortOrder;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_ListEnabledVitalsConfig
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT VitalDefinitionId, HospitalId, VitalCode, DisplayLabel, AllowedUnits,
           MinValue, MaxValue, SortOrder
    FROM cfg.VitalDefinition
    WHERE HospitalId = @HospitalId AND IsEnabled = 1
    ORDER BY SortOrder;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_UpdateVitalEnabled
    @VitalCode      NVARCHAR(20),
    @HospitalId     UNIQUEIDENTIFIER,
    @IsEnabled      BIT,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    UPDATE cfg.VitalDefinition
    SET IsEnabled = @IsEnabled, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE VitalCode = @VitalCode AND HospitalId = @HospitalId;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_CreateVitalConfig
    @HospitalId     UNIQUEIDENTIFIER,
    @VitalCode      NVARCHAR(20),
    @DisplayLabel   NVARCHAR(50),
    @AllowedUnits   NVARCHAR(20),
    @MinValue       DECIMAL(8,2) = NULL,
    @MaxValue       DECIMAL(8,2) = NULL,
    @SortOrder      INT = 0,
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (VitalDefinitionId UNIQUEIDENTIFIER, HospitalId UNIQUEIDENTIFIER, VitalCode NVARCHAR(40), DisplayLabel NVARCHAR(50), AllowedUnits NVARCHAR(20), MinValue DECIMAL(10,2), MaxValue DECIMAL(10,2), SortOrder INT, IsEnabled BIT);
    INSERT INTO cfg.VitalDefinition (HospitalId, VitalCode, DisplayLabel, AllowedUnits, MinValue, MaxValue, SortOrder, CreatedBy)
    OUTPUT inserted.VitalDefinitionId, inserted.HospitalId, inserted.VitalCode, inserted.DisplayLabel,
           inserted.AllowedUnits, inserted.MinValue, inserted.MaxValue, inserted.SortOrder, inserted.IsEnabled
    INTO @Out
    VALUES (@HospitalId, @VitalCode, @DisplayLabel, @AllowedUnits, @MinValue, @MaxValue, @SortOrder, ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));
    SELECT VitalDefinitionId, HospitalId, VitalCode, DisplayLabel, AllowedUnits, MinValue, MaxValue, SortOrder, IsEnabled FROM @Out;
END;
GO

-- ---- Medical history / event config (quick-pick master lists) ----

CREATE OR ALTER PROCEDURE cfg.usp_ListMedicalHistoryItems
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT ItemId, HospitalId, ItemName, IsQuickPick, SortOrder
    FROM cfg.MedicalHistoryItem
    WHERE HospitalId = @HospitalId AND IsActive = 1
    ORDER BY SortOrder;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_ListMedicalHistoryConfig
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- Admin config view — includes inactive items, matches data.js listMedicalHistoryConfig
    SELECT ItemId, HospitalId, ItemName, IsQuickPick, SortOrder, IsActive
    FROM cfg.MedicalHistoryItem
    WHERE HospitalId = @HospitalId
    ORDER BY SortOrder;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_CreateMedicalHistoryConfigItem
    @HospitalId     UNIQUEIDENTIFIER,
    @ItemName       NVARCHAR(100),
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (ItemId UNIQUEIDENTIFIER, ItemName NVARCHAR(100), SortOrder INT);
    DECLARE @Sort INT;
    SELECT @Sort = ISNULL(MAX(SortOrder), 0) + 1 FROM cfg.MedicalHistoryItem WHERE HospitalId = @HospitalId;
    INSERT INTO cfg.MedicalHistoryItem (HospitalId, ItemName, SortOrder, CreatedBy)
    OUTPUT inserted.ItemId, inserted.ItemName, inserted.SortOrder
    INTO @Out
    VALUES (@HospitalId, @ItemName, @Sort, ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));
    SELECT ItemId, ItemName, SortOrder FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_UpdateMedicalHistoryItemStatus
    @HospitalId     UNIQUEIDENTIFIER,
    @ItemName       NVARCHAR(100),
    @IsActive       BIT,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    UPDATE cfg.MedicalHistoryItem
    SET IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE HospitalId = @HospitalId AND ItemName = @ItemName;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_ListMedicalEventItems
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT EventId, HospitalId, EventName, SortOrder
    FROM cfg.MedicalEventType
    WHERE HospitalId = @HospitalId AND IsActive = 1
    ORDER BY SortOrder, EventName;
END;
GO

-- ============================================================
-- 4. PATIENTS + SUB-RESOURCES (opd)
-- ============================================================

CREATE OR ALTER PROCEDURE opd.usp_GetPatient
    @PatientId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='READ', EntityType='Patient')
    SELECT PatientId, HospitalId, Mrn, FullName, Gender, Dob, Phone, Email,
           [Address], NationalHealthId, InsuranceProvider, InsurancePolicyNo,
           EmergencyContactName, EmergencyContactPhone, BloodGroup, RegisteredVia,
           Prefix, FirstName, MiddleName, LastName, Trn, CountryCode, Mobile, PreferredLanguage,
           AddrLine1, AddrLine2, City, Region, Country, MaritalStatus, ReferralSource, PreferredDoctorId,
           SmokingStatus, SmokingPackYears, AlcoholUse, Occupation, PhysicalActivity, Diet,
           IsActive, CreatedAt
    FROM opd.Patient
    WHERE PatientId = @PatientId AND IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_SearchPatients
    @HospitalId     UNIQUEIDENTIFIER,
    @Term           NVARCHAR(100) = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- data.js: term < 2 chars returns the unfiltered hospital list
    IF @Term IS NULL OR LEN(LTRIM(RTRIM(@Term))) < 2 BEGIN
        SELECT PatientId, Mrn, FullName, Gender, Dob, Phone, Email, IsActive
        FROM opd.Patient WHERE HospitalId = @HospitalId AND IsActive = 1
        ORDER BY FullName;
        RETURN;
    END;
    SELECT PatientId, Mrn, FullName, Gender, Dob, Phone, Email, IsActive
    FROM opd.Patient
    WHERE HospitalId = @HospitalId AND IsActive = 1
      AND (FullName LIKE '%' + @Term + '%'
           OR Mrn LIKE '%' + @Term + '%'
           OR Phone LIKE '%' + @Term + '%'
           OR NationalHealthId LIKE '%' + @Term + '%')
    ORDER BY FullName;
END;
GO

-- MRN from the hospital's settings — HospitalService.FormatMrn. Tokens: {prefix} (MRN_PREFIX, default 'MRN'),
-- {year}, {seq:N} (zero-padded to N, never truncated), {seq}. Default format '{prefix}-{seq:5}'.
-- v3.6: {year} is the hospital's year (memory used the UTC year).
CREATE OR ALTER FUNCTION cfg.fn_FormatMrn (@Format NVARCHAR(200), @Seq INT, @Prefix NVARCHAR(200), @Year INT)
RETURNS NVARCHAR(400) AS
BEGIN
    DECLARE @s NVARCHAR(400) = COALESCE(NULLIF(@Format, N''), N'{prefix}-{seq:5}');
    DECLARE @SeqText NVARCHAR(12) = CAST(@Seq AS NVARCHAR(12)), @i INT, @j INT, @n INT, @Token NVARCHAR(20);
    SET @s = REPLACE(REPLACE(@s, N'{prefix}', COALESCE(NULLIF(@Prefix, N''), N'MRN')), N'{year}', CAST(@Year AS NVARCHAR(4)));
    SET @i = CHARINDEX(N'{seq:', @s);
    WHILE @i > 0
    BEGIN
        SET @j = CHARINDEX(N'}', @s, @i);
        SET @n = CASE WHEN @j > @i + 5 THEN TRY_CAST(SUBSTRING(@s, @i + 5, @j - @i - 5) AS INT) END;
        IF @n IS NULL OR @n < 0 OR @n > 12 BREAK;                       -- not a {seq:N} token: leave the rest as typed
        SET @Token = SUBSTRING(@s, @i, @j - @i + 1);
        SET @s = REPLACE(@s, @Token, CASE WHEN LEN(@SeqText) >= @n THEN @SeqText ELSE RIGHT(REPLICATE(N'0', @n) + @SeqText, @n) END);
        SET @i = CHARINDEX(N'{seq:', @s);
    END;
    RETURN REPLACE(@s, N'{seq}', @SeqText);
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_CreatePatient
    @HospitalId     UNIQUEIDENTIFIER,
    @FullName       NVARCHAR(200),
    @Gender         NVARCHAR(10),
    @Dob            DATE,
    @Phone          NVARCHAR(20),
    @Email          NVARCHAR(100) = NULL,
    @Address        NVARCHAR(500) = NULL,
    @NationalHealthId NVARCHAR(50) = NULL,
    @InsuranceProvider NVARCHAR(100) = NULL,
    @InsurancePolicyNo NVARCHAR(50) = NULL,
    @EmergencyContactName NVARCHAR(200) = NULL,
    @EmergencyContactPhone NVARCHAR(20) = NULL,
    @CreatedBy      UNIQUEIDENTIFIER = NULL,
    -- v3.6: the structured registration fields (Patient Registration form) and the channel
    @RegisteredVia  NVARCHAR(20) = 'FRONT_DESK',   -- FRONT_DESK | PATIENT_PORTAL | CSV_IMPORT
    @Prefix         NVARCHAR(10) = NULL,
    @FirstName      NVARCHAR(60) = NULL,
    @MiddleName     NVARCHAR(60) = NULL,
    @LastName       NVARCHAR(60) = NULL,
    @Trn            NVARCHAR(20) = NULL,
    @CountryCode    NVARCHAR(5) = NULL,
    @Mobile         NVARCHAR(16) = NULL,
    @PreferredLanguage NVARCHAR(40) = NULL,
    @AddrLine1      NVARCHAR(150) = NULL,
    @AddrLine2      NVARCHAR(150) = NULL,
    @City           NVARCHAR(80) = NULL,
    @Region         NVARCHAR(80) = NULL,
    @Country        NVARCHAR(60) = NULL,
    @MaritalStatus  NVARCHAR(10) = NULL,
    @ReferralSource NVARCHAR(80) = NULL,
    @PreferredDoctorId UNIQUEIDENTIFIER = NULL,
    @BloodGroup     NVARCHAR(5) = NULL,
    @Occupation     NVARCHAR(80) = NULL
AS BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Out TABLE (PatientId UNIQUEIDENTIFIER, Mrn NVARCHAR(20), FullName NVARCHAR(200), CreatedAt DATETIMEOFFSET(3));
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='CREATE', EntityType='Patient')

    -- v3.6: the MRN follows the hospital's MRN_PREFIX / MRN_FORMAT and takes the next MRN_SEQ
    -- (HospitalService.IssueMrnUnlocked). The counter row is locked for the transaction, so two
    -- registrations never get the same number; a number already used (e.g. after the format changed) is skipped.
    DECLARE @Seq INT, @Mrn NVARCHAR(400), @Tries INT = 0,
            @Format NVARCHAR(200) = (SELECT [Value] FROM cfg.HospitalSetting WHERE HospitalId = @HospitalId AND SettingKey = 'MRN_FORMAT'),
            @Prefix2 NVARCHAR(200) = (SELECT [Value] FROM cfg.HospitalSetting WHERE HospitalId = @HospitalId AND SettingKey = 'MRN_PREFIX'),
            @Year INT = YEAR(cfg.fn_HospitalToday(@HospitalId));

    BEGIN TRAN;
    SELECT @Seq = ISNULL(TRY_CAST([Value] AS INT), 0)
    FROM cfg.HospitalSetting WITH (UPDLOCK, HOLDLOCK)
    WHERE HospitalId = @HospitalId AND SettingKey = 'MRN_SEQ';
    IF @@ROWCOUNT = 0
    BEGIN
        SET @Seq = 0;
        INSERT INTO cfg.HospitalSetting (HospitalId, SettingKey, [Value], CreatedBy)
        VALUES (@HospitalId, 'MRN_SEQ', '0', ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));
    END;

    WHILE 1 = 1
    BEGIN
        SET @Seq += 1; SET @Tries += 1;
        SET @Mrn = cfg.fn_FormatMrn(@Format, @Seq, @Prefix2, @Year);
        IF LEN(@Mrn) > 20
        BEGIN   -- XACT_ABORT rolls back (no ROLLBACK here: the CSV import calls this inside INSERT … EXEC)
            THROW 50205, N'The MRN format makes numbers longer than 20 characters. Shorten it in Settings → Formats.', 1;
        END;
        IF NOT EXISTS (SELECT 1 FROM opd.Patient WHERE HospitalId = @HospitalId AND Mrn = @Mrn) BREAK;
        IF @Tries >= 1000
        BEGIN   -- XACT_ABORT rolls back (no ROLLBACK here: the CSV import calls this inside INSERT … EXEC)
            THROW 50206, N'No free MRN could be issued with the current format. Check Settings → Formats.', 1;
        END;
    END;

    UPDATE cfg.HospitalSetting SET [Value] = CAST(@Seq AS NVARCHAR(200)), ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @CreatedBy
    WHERE HospitalId = @HospitalId AND SettingKey = 'MRN_SEQ';

    INSERT INTO opd.Patient (HospitalId, Mrn, FullName, Gender, Dob, Phone, Email,
                             [Address], NationalHealthId, InsuranceProvider, InsurancePolicyNo,
                             EmergencyContactName, EmergencyContactPhone, RegisteredVia,
                             Prefix, FirstName, MiddleName, LastName, Trn, CountryCode, Mobile, PreferredLanguage,
                             AddrLine1, AddrLine2, City, Region, Country, MaritalStatus, ReferralSource, PreferredDoctorId,
                             BloodGroup, Occupation, CreatedBy)
    OUTPUT inserted.PatientId, inserted.Mrn, inserted.FullName, inserted.CreatedAt
    INTO @Out
    VALUES (@HospitalId, @Mrn, @FullName, @Gender, @Dob, @Phone, @Email,
            @Address, @NationalHealthId, @InsuranceProvider, @InsurancePolicyNo,
            @EmergencyContactName, @EmergencyContactPhone, @RegisteredVia,
            NULLIF(@Prefix, N''), NULLIF(@FirstName, N''), NULLIF(@MiddleName, N''), NULLIF(@LastName, N''), NULLIF(@Trn, N''),
            NULLIF(@CountryCode, N''), NULLIF(@Mobile, N''), NULLIF(@PreferredLanguage, N''),
            NULLIF(@AddrLine1, N''), NULLIF(@AddrLine2, N''), NULLIF(@City, N''), NULLIF(@Region, N''), NULLIF(@Country, N''),
            NULLIF(@MaritalStatus, N''), NULLIF(@ReferralSource, N''), @PreferredDoctorId,
            NULLIF(@BloodGroup, N''), NULLIF(@Occupation, N''), ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));
    COMMIT;
    SELECT PatientId, Mrn, FullName, CreatedAt FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_UpdatePatient
    @PatientId      UNIQUEIDENTIFIER,
    @FullName       NVARCHAR(200) = NULL,
    @Phone          NVARCHAR(20) = NULL,
    @Email          NVARCHAR(100) = NULL,
    @Address        NVARCHAR(500) = NULL,
    @NationalHealthId NVARCHAR(50) = NULL,
    @InsuranceProvider NVARCHAR(100) = NULL,
    @InsurancePolicyNo NVARCHAR(50) = NULL,
    @EmergencyContactName NVARCHAR(200) = NULL,
    @EmergencyContactPhone NVARCHAR(20) = NULL,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL,
    -- v3.6: every field the registration form edits (PatientService.Update). NULL keeps the stored value;
    -- '' clears an optional text field.
    @Gender         NVARCHAR(10) = NULL,
    @Dob            DATE = NULL,
    @Prefix         NVARCHAR(10) = NULL,
    @FirstName      NVARCHAR(60) = NULL,
    @MiddleName     NVARCHAR(60) = NULL,
    @LastName       NVARCHAR(60) = NULL,
    @Trn            NVARCHAR(20) = NULL,
    @CountryCode    NVARCHAR(5) = NULL,
    @Mobile         NVARCHAR(16) = NULL,
    @PreferredLanguage NVARCHAR(40) = NULL,
    @AddrLine1      NVARCHAR(150) = NULL,
    @AddrLine2      NVARCHAR(150) = NULL,
    @City           NVARCHAR(80) = NULL,
    @Region         NVARCHAR(80) = NULL,
    @Country        NVARCHAR(60) = NULL,
    @MaritalStatus  NVARCHAR(10) = NULL,
    @ReferralSource NVARCHAR(80) = NULL,
    @PreferredDoctorId UNIQUEIDENTIFIER = NULL,
    @BloodGroup     NVARCHAR(5) = NULL,
    @Occupation     NVARCHAR(80) = NULL
AS BEGIN
    SET NOCOUNT ON;
    UPDATE opd.Patient SET
        FullName = ISNULL(@FullName, FullName),
        Phone = ISNULL(@Phone, Phone),
        Email = CASE WHEN @Email IS NULL THEN Email ELSE NULLIF(@Email, N'') END,
        [Address] = CASE WHEN @Address IS NULL THEN [Address] ELSE NULLIF(@Address, N'') END,
        NationalHealthId = CASE WHEN @NationalHealthId IS NULL THEN NationalHealthId ELSE NULLIF(@NationalHealthId, N'') END,
        InsuranceProvider = ISNULL(@InsuranceProvider, InsuranceProvider),
        InsurancePolicyNo = ISNULL(@InsurancePolicyNo, InsurancePolicyNo),
        EmergencyContactName = CASE WHEN @EmergencyContactName IS NULL THEN EmergencyContactName ELSE NULLIF(@EmergencyContactName, N'') END,
        EmergencyContactPhone = CASE WHEN @EmergencyContactPhone IS NULL THEN EmergencyContactPhone ELSE NULLIF(@EmergencyContactPhone, N'') END,
        Gender = ISNULL(@Gender, Gender),
        Dob = ISNULL(@Dob, Dob),
        Prefix = CASE WHEN @Prefix IS NULL THEN Prefix ELSE NULLIF(@Prefix, N'') END,
        FirstName = CASE WHEN @FirstName IS NULL THEN FirstName ELSE NULLIF(@FirstName, N'') END,
        MiddleName = CASE WHEN @MiddleName IS NULL THEN MiddleName ELSE NULLIF(@MiddleName, N'') END,
        LastName = CASE WHEN @LastName IS NULL THEN LastName ELSE NULLIF(@LastName, N'') END,
        Trn = CASE WHEN @Trn IS NULL THEN Trn ELSE NULLIF(@Trn, N'') END,
        CountryCode = CASE WHEN @CountryCode IS NULL THEN CountryCode ELSE NULLIF(@CountryCode, N'') END,
        Mobile = CASE WHEN @Mobile IS NULL THEN Mobile ELSE NULLIF(@Mobile, N'') END,
        PreferredLanguage = CASE WHEN @PreferredLanguage IS NULL THEN PreferredLanguage ELSE NULLIF(@PreferredLanguage, N'') END,
        AddrLine1 = CASE WHEN @AddrLine1 IS NULL THEN AddrLine1 ELSE NULLIF(@AddrLine1, N'') END,
        AddrLine2 = CASE WHEN @AddrLine2 IS NULL THEN AddrLine2 ELSE NULLIF(@AddrLine2, N'') END,
        City = CASE WHEN @City IS NULL THEN City ELSE NULLIF(@City, N'') END,
        Region = CASE WHEN @Region IS NULL THEN Region ELSE NULLIF(@Region, N'') END,
        Country = CASE WHEN @Country IS NULL THEN Country ELSE NULLIF(@Country, N'') END,
        MaritalStatus = CASE WHEN @MaritalStatus IS NULL THEN MaritalStatus ELSE NULLIF(@MaritalStatus, N'') END,
        ReferralSource = CASE WHEN @ReferralSource IS NULL THEN ReferralSource ELSE NULLIF(@ReferralSource, N'') END,
        PreferredDoctorId = ISNULL(@PreferredDoctorId, PreferredDoctorId),
        BloodGroup = CASE WHEN @BloodGroup IS NULL THEN BloodGroup ELSE NULLIF(@BloodGroup, N'') END,
        Occupation = CASE WHEN @Occupation IS NULL THEN Occupation ELSE NULLIF(@Occupation, N'') END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE PatientId = @PatientId AND IsActive = 1;

    -- v3.6: returns the full record (opd.usp_GetPatient's shape), as UpdatePatient's `Patient` result says
    EXEC opd.usp_GetPatient @PatientId = @PatientId;
END;
GO

-- ---- Allergies ----

CREATE OR ALTER PROCEDURE opd.usp_ListAllergies
    @PatientId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT AllergyId, HospitalId, PatientId, AllergyType, AllergyName,
           ReactionType, ReactionDescription, Severity, Status, IsTreated, IsFamilyHistory,
           SinceWhen, Notes
    FROM opd.PatientAllergy
    WHERE PatientId = @PatientId AND IsActive = 1
    ORDER BY CreatedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_CreateAllergy
    @HospitalId     UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER,
    @AllergyType    NVARCHAR(20),
    @AllergyName    NVARCHAR(100),
    @ReactionType   NVARCHAR(30) = NULL,
    @ReactionDescription NVARCHAR(200) = NULL,
    @Severity       NVARCHAR(10),
    @IsTreated      BIT = 0,
    @IsFamilyHistory BIT = 0,
    @SinceWhen      DATE = NULL,
    @Notes          NVARCHAR(500) = NULL,
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (AllergyId UNIQUEIDENTIFIER, AllergyName NVARCHAR(100), Severity NVARCHAR(10), Status NVARCHAR(10));
    INSERT INTO opd.PatientAllergy (HospitalId, PatientId, AllergyType, AllergyName,
                                     ReactionType, ReactionDescription, Severity, IsTreated,
                                     IsFamilyHistory, SinceWhen, Notes, CreatedBy)
    OUTPUT inserted.AllergyId, inserted.AllergyName, inserted.Severity, inserted.Status
    INTO @Out
    VALUES (@HospitalId, @PatientId, @AllergyType, @AllergyName, @ReactionType, @ReactionDescription,
            @Severity, @IsTreated, @IsFamilyHistory, @SinceWhen, @Notes, ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));
    SELECT AllergyId, AllergyName, Severity, Status FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_UpdateAllergy
    @AllergyId      UNIQUEIDENTIFIER,
    @ReactionType   NVARCHAR(30) = NULL,
    @ReactionDescription NVARCHAR(200) = NULL,
    @Severity       NVARCHAR(10) = NULL,
    @Status         NVARCHAR(10) = NULL,
    @IsTreated      BIT = NULL,
    @SinceWhen      DATE = NULL,
    @Notes          NVARCHAR(500) = NULL,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    UPDATE opd.PatientAllergy SET
        ReactionType = ISNULL(@ReactionType, ReactionType),
        ReactionDescription = ISNULL(@ReactionDescription, ReactionDescription),
        Severity = ISNULL(@Severity, Severity),
        Status = ISNULL(@Status, Status),
        IsTreated = ISNULL(@IsTreated, IsTreated),
        SinceWhen = ISNULL(@SinceWhen, SinceWhen),
        Notes = ISNULL(@Notes, Notes),
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE AllergyId = @AllergyId AND IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_DeleteAllergy
    @AllergyId      UNIQUEIDENTIFIER,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- Soft delete: Status = 'INACTIVE' (allowed by CK_opd_PA_Status since schema v4.0)
    UPDATE opd.PatientAllergy
    SET Status = 'INACTIVE', ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE AllergyId = @AllergyId;
END;
GO

-- ---- Patient medical history ----

CREATE OR ALTER PROCEDURE opd.usp_ListPatientMedicalHistory
    @PatientId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT PatientMedHistoryId, HospitalId, PatientId, ItemId, ConditionName,
           OnsetMonth, OnsetYear, Status, Notes, RecordedAt, RecordedByUserId
    FROM opd.PatientMedicalHistory
    WHERE PatientId = @PatientId AND IsActive = 1
    ORDER BY RecordedAt DESC;
END;
GO

-- Alias covering both data.js getPatientMedicalHistory and listPatientMedicalHistory
-- (same underlying table/shape; kept as two procs to match the two distinct api.js
-- method names 1:1).
CREATE OR ALTER PROCEDURE opd.usp_GetPatientMedicalHistory
    @PatientId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT PatientMedHistoryId, HospitalId, PatientId, ItemId, ConditionName,
           OnsetMonth, OnsetYear, Status, Notes, RecordedAt, RecordedByUserId
    FROM opd.PatientMedicalHistory
    WHERE PatientId = @PatientId AND IsActive = 1
    ORDER BY RecordedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_CreatePatientMedicalHistory
    @HospitalId     UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER,
    @ItemId         UNIQUEIDENTIFIER = NULL,   -- cfg.MedicalHistoryItem; NULL = free text
    @ConditionName  NVARCHAR(100),
    @OnsetMonth     INT = NULL,
    @OnsetYear      INT = NULL,
    @Status         NVARCHAR(12) = 'ACTIVE',
    @Notes          NVARCHAR(500) = NULL,
    @RecordedByUserId UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (PatientMedHistoryId UNIQUEIDENTIFIER, ConditionName NVARCHAR(100), RecordedAt DATETIMEOFFSET(3));
    INSERT INTO opd.PatientMedicalHistory (HospitalId, PatientId, ItemId, ConditionName, OnsetMonth, OnsetYear,
                                           Status, Notes, RecordedByUserId)
    OUTPUT inserted.PatientMedHistoryId, inserted.ConditionName, inserted.RecordedAt
    INTO @Out
    VALUES (@HospitalId, @PatientId, @ItemId, @ConditionName, @OnsetMonth, @OnsetYear,
            @Status, @Notes, @RecordedByUserId);
    SELECT PatientMedHistoryId, ConditionName, RecordedAt FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_SavePatientMedicalHistory
    @HospitalId     UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER,
    @ItemId         UNIQUEIDENTIFIER = NULL,   -- cfg.MedicalHistoryItem; NULL = free text
    @ConditionName  NVARCHAR(100),
    @OnsetMonth     INT = NULL,
    @OnsetYear      INT = NULL,
    @Status         NVARCHAR(12) = 'ACTIVE',
    @Notes          NVARCHAR(500) = NULL,
    @RecordedByUserId UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- Upsert by (PatientId, ConditionName) — matches data.js checklist auto-save behavior
    MERGE opd.PatientMedicalHistory AS tgt
    USING (SELECT @PatientId AS PatientId, @ConditionName AS ConditionName) AS src
        ON tgt.PatientId = src.PatientId AND tgt.ConditionName = src.ConditionName AND tgt.IsActive = 1
    WHEN MATCHED THEN UPDATE SET OnsetMonth = @OnsetMonth, OnsetYear = @OnsetYear,
                                 Status = @Status, Notes = @Notes
    WHEN NOT MATCHED THEN
        INSERT (HospitalId, PatientId, ItemId, ConditionName, OnsetMonth, OnsetYear, Status, Notes, RecordedByUserId)
        VALUES (@HospitalId, @PatientId, @ItemId, @ConditionName, @OnsetMonth, @OnsetYear, @Status, @Notes, @RecordedByUserId);

    SELECT PatientMedHistoryId, ItemId, ConditionName, OnsetMonth, OnsetYear, Status, Notes
    FROM opd.PatientMedicalHistory WHERE PatientId = @PatientId AND ConditionName = @ConditionName AND IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_DeletePatientMedicalHistory
    @PatientId      UNIQUEIDENTIFIER,
    @ConditionName  NVARCHAR(100)
AS BEGIN
    SET NOCOUNT ON;
    -- data.js performs a hard splice(); schema has IsActive, so soft delete (IsActive = 0) is used here
    -- (preferred per architecture doc §4.8) rather than a physical DELETE.
    UPDATE opd.PatientMedicalHistory SET IsActive = 0
    WHERE PatientId = @PatientId AND ConditionName = @ConditionName;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_UpdatePatientMedicalHistory
    @PatientId      UNIQUEIDENTIFIER,
    @ConditionName  NVARCHAR(100),
    @OnsetMonth     INT = NULL,
    @OnsetYear      INT = NULL,
    @Status         NVARCHAR(12) = NULL,
    @Notes          NVARCHAR(500) = NULL
AS BEGIN
    SET NOCOUNT ON;
    UPDATE opd.PatientMedicalHistory SET
        OnsetMonth = ISNULL(@OnsetMonth, OnsetMonth),
        OnsetYear = ISNULL(@OnsetYear, OnsetYear),
        Status = ISNULL(@Status, Status),
        Notes = ISNULL(@Notes, Notes)
    WHERE PatientId = @PatientId AND ConditionName = @ConditionName AND IsActive = 1;
END;
GO

-- ---- Patient medical events ----

CREATE OR ALTER PROCEDURE opd.usp_ListPatientMedicalEvents
    @PatientId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT PatientMedEventId, HospitalId, PatientId, EventId, EventName, EventMonth, EventYear,
           Notes, RecordedAt, RecordedByUserId
    FROM opd.PatientMedicalEvent
    WHERE PatientId = @PatientId AND IsActive = 1
    ORDER BY RecordedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_CreatePatientMedicalEvent
    @HospitalId     UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER,
    @EventId        UNIQUEIDENTIFIER = NULL,   -- cfg.MedicalEventType; NULL = free text
    @EventName      NVARCHAR(100),
    @EventMonth     INT = NULL,
    @EventYear      INT = NULL,
    @Notes          NVARCHAR(500) = NULL,
    @RecordedByUserId UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (PatientMedEventId UNIQUEIDENTIFIER, EventName NVARCHAR(100), RecordedAt DATETIMEOFFSET(3));
    INSERT INTO opd.PatientMedicalEvent (HospitalId, PatientId, EventId, EventName, EventMonth, EventYear,
                                         Notes, RecordedByUserId)
    OUTPUT inserted.PatientMedEventId, inserted.EventName, inserted.RecordedAt
    INTO @Out
    VALUES (@HospitalId, @PatientId, @EventId, @EventName, @EventMonth, @EventYear, @Notes, @RecordedByUserId);
    SELECT PatientMedEventId, EventName, RecordedAt FROM @Out;
END;
GO

-- ---- Patient family history ----

CREATE OR ALTER PROCEDURE opd.usp_ListPatientFamilyHistory
    @PatientId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT FamilyHistoryId, HospitalId, PatientId, RelationshipType, ConditionName, Notes, RecordedAt
    FROM opd.PatientFamilyHistory
    WHERE PatientId = @PatientId AND IsActive = 1
    ORDER BY RecordedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_CreatePatientFamilyHistory
    @HospitalId     UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER,
    @RelationshipType NVARCHAR(30),
    @ConditionName  NVARCHAR(100),
    @Notes          NVARCHAR(300) = NULL,
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (FamilyHistoryId UNIQUEIDENTIFIER, RelationshipType NVARCHAR(30), ConditionName NVARCHAR(100));
    INSERT INTO opd.PatientFamilyHistory (HospitalId, PatientId, RelationshipType, ConditionName, Notes, CreatedBy)
    OUTPUT inserted.FamilyHistoryId, inserted.RelationshipType, inserted.ConditionName
    INTO @Out
    VALUES (@HospitalId, @PatientId, @RelationshipType, @ConditionName, @Notes, ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));
    SELECT FamilyHistoryId, RelationshipType, ConditionName FROM @Out;
END;
GO

-- ---- Past visits & previous prescriptions (Consultation 360 Zone A) ----

CREATE OR ALTER PROCEDURE opd.usp_ListPatientPastVisits
    @PatientId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- Replicates data.js: LOCKED consultations only, newest first, with symptom
    -- chief-complaint rollup and primary diagnosis.
    SELECT c.ConsultationId,
           c.VisitLocalDate AS VisitDate, c.VisitDate AS VisitAt, c.TimeZoneId,
           u.FullName AS DoctorName,
           STUFF((
               SELECT ', ' + ISNULL(sc.DisplayLabel, cs.SymptomCode)
               FROM cln.ConsultationSymptom cs
               LEFT JOIN cfg.SymptomCatalog sc ON sc.HospitalId = cs.HospitalId AND sc.SymptomCode = cs.SymptomCode
               WHERE cs.ConsultationId = c.ConsultationId AND cs.[State] = 'YES'
               FOR XML PATH('')), 1, 2, '') AS ChiefComplaint,
           (SELECT TOP (1) dx.Icd10Code + ' ' + dx.Icd10Description
              FROM cln.ConsultationDiagnosis dx
              WHERE dx.ConsultationId = c.ConsultationId AND dx.IsPrimary = 1 AND dx.IsActive = 1
              ORDER BY dx.SequenceOrder) AS PrimaryDiagnosis
    FROM cln.Consultation c
    INNER JOIN sec.ApplicationUser u ON u.UserId = c.DoctorId
    WHERE c.PatientId = @PatientId AND c.Status = 'LOCKED' AND c.IsActive = 1
    ORDER BY c.VisitDate DESC;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_ListPreviousPrescriptionsByDoctor
    @PatientId      UNIQUEIDENTIFIER,
    @DoctorId       UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- Two result sets: (1) the most recent LOCKED consultation between this
    -- patient/doctor, (2) its ACTIVE prescriptions. Empty result set 1 means
    -- "found: false" at the API layer, matching data.js's { found, consultation, prescriptions }.
    DECLARE @ConsultationId UNIQUEIDENTIFIER;
    SELECT TOP (1) @ConsultationId = ConsultationId
    FROM cln.Consultation
    WHERE PatientId = @PatientId AND DoctorId = @DoctorId AND Status = 'LOCKED' AND IsActive = 1
    ORDER BY VisitDate DESC;

    SELECT ConsultationId, VisitDate FROM cln.Consultation WHERE ConsultationId = @ConsultationId;

    SELECT PrescriptionId, MedicineName, Strength, DrugForm, DeliveryRoute, Category,
           TakeInQty, TimesPerDay, DurationValue, DurationUnit, Instructions, Prn
    FROM cln.Prescription
    WHERE ConsultationId = @ConsultationId AND Status = 'ACTIVE' AND IsActive = 1;
END;
GO

-- ============================================================
-- 5. SCHEDULING — APPOINTMENTS & SLOTS (opd)
-- ============================================================

CREATE OR ALTER PROCEDURE opd.usp_ListTodayAppointmentsForDoctor
    @DoctorId       UNIQUEIDENTIFIER,
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT a.AppointmentId, a.PatientId, p.FullName AS PatientName, p.Mrn,
           a.SlotStart, a.SlotEnd, a.TimeZoneId,
           a.SlotStart AT TIME ZONE tz.WindowsTimeZoneId AS SlotStartLocal,
           a.SlotEnd AT TIME ZONE tz.WindowsTimeZoneId AS SlotEndLocal,
           a.[Type], a.Status, a.Notes
    FROM opd.Appointment a
    INNER JOIN opd.Patient p ON p.PatientId = a.PatientId
    INNER JOIN cfg.TimeZone tz ON tz.TimeZoneId = a.TimeZoneId
    WHERE a.DoctorId = @DoctorId AND a.HospitalId = @HospitalId
      AND a.AppointmentDate = cfg.fn_HospitalToday(@HospitalId)
      AND a.IsActive = 1
    ORDER BY a.SlotStart;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_ListAppointmentsForDate
    @DoctorId       UNIQUEIDENTIFIER,
    @HospitalId     UNIQUEIDENTIFIER,
    @AppointmentDate DATE
AS BEGIN
    SET NOCOUNT ON;
    SELECT a.AppointmentId, a.PatientId, p.FullName AS PatientName, p.Mrn,
           a.SlotStart, a.SlotEnd, a.TimeZoneId,
           a.SlotStart AT TIME ZONE tz.WindowsTimeZoneId AS SlotStartLocal,
           a.SlotEnd AT TIME ZONE tz.WindowsTimeZoneId AS SlotEndLocal,
           a.[Type], a.Status, a.Notes,
           a.CheckedInAt, a.ConsultStartedAt, a.CompletedAt
    FROM opd.Appointment a
    INNER JOIN opd.Patient p ON p.PatientId = a.PatientId
    INNER JOIN cfg.TimeZone tz ON tz.TimeZoneId = a.TimeZoneId
    WHERE a.DoctorId = @DoctorId AND a.HospitalId = @HospitalId
      AND a.AppointmentDate = @AppointmentDate AND a.IsActive = 1
    ORDER BY a.SlotStart;
END;
GO

-- Front desk (receptionist) day list: every doctor's appointments for one date.
CREATE OR ALTER PROCEDURE opd.usp_ListAppointmentsForHospitalDate
    @HospitalId     UNIQUEIDENTIFIER,
    @AppointmentDate DATE
AS BEGIN
    SET NOCOUNT ON;
    SELECT a.AppointmentId, a.PatientId, p.FullName AS PatientName, p.Mrn,
           a.DoctorId, u.FullName AS DoctorName, u.Room AS DoctorRoom,
           a.SlotStart, a.SlotEnd, a.TimeZoneId,
           a.SlotStart AT TIME ZONE tz.WindowsTimeZoneId AS SlotStartLocal,
           a.SlotEnd AT TIME ZONE tz.WindowsTimeZoneId AS SlotEndLocal,
           a.[Type], a.Status, a.Notes,
           a.CheckedInAt, a.ConsultStartedAt, a.CompletedAt
    FROM opd.Appointment a
    INNER JOIN opd.Patient p ON p.PatientId = a.PatientId
    INNER JOIN sec.ApplicationUser u ON u.UserId = a.DoctorId
    INNER JOIN cfg.TimeZone tz ON tz.TimeZoneId = a.TimeZoneId
    WHERE a.HospitalId = @HospitalId
      AND a.AppointmentDate = @AppointmentDate AND a.IsActive = 1
    ORDER BY a.SlotStart, u.FullName;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_ListDoctorSlotConfig
    @DoctorId       UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT SlotConfigId, HospitalId, DoctorId, DayOfWeek, StartTime, EndTime
    FROM opd.DoctorSlotConfig
    WHERE DoctorId = @DoctorId AND IsActive = 1
    ORDER BY DayOfWeek;
END;
GO

-- ── Schedule Settings (MediSlot model) ─────────────────────────────────────
-- Helper used by the slot/charge procedures: doctor override → hospital value.
CREATE OR ALTER FUNCTION cfg.fn_ResolveSetting (@HospitalId UNIQUEIDENTIFIER, @DoctorId UNIQUEIDENTIFIER, @Key NVARCHAR(40))
RETURNS NVARCHAR(200) AS
BEGIN
    RETURN COALESCE(
        (SELECT [Value] FROM opd.DoctorSetting WHERE HospitalId = @HospitalId AND DoctorId = @DoctorId AND SettingKey = @Key),
        (SELECT [Value] FROM cfg.HospitalSetting WHERE HospitalId = @HospitalId AND SettingKey = @Key));
END;
GO

-- A doctor's display name (sec.ApplicationUser.FullName): DisplayNameOverride if set, else DOCTOR_NAME_TEMPLATE with
-- tokens {prefix} {first} {middle} {last} {finitial} {minitial} {suffix} {suffixComma} (suffix = Qualifications),
-- spaces collapsed. No first or last name → @CurrentName unchanged. Same rules as SchedulingService.RenderDoctorName.
CREATE OR ALTER FUNCTION cfg.fn_RenderDoctorName (
    @Template NVARCHAR(200), @Override NVARCHAR(200), @Prefix NVARCHAR(20), @FirstName NVARCHAR(100),
    @MiddleName NVARCHAR(100), @LastName NVARCHAR(100), @Qualifications NVARCHAR(200), @CurrentName NVARCHAR(200))
RETURNS NVARCHAR(200) AS
BEGIN
    IF NULLIF(LTRIM(RTRIM(@Override)), N'') IS NOT NULL RETURN LTRIM(RTRIM(@Override));
    IF NULLIF(@FirstName, N'') IS NULL AND NULLIF(@LastName, N'') IS NULL RETURN @CurrentName;
    DECLARE @q NVARCHAR(200) = ISNULL(@Qualifications, N'');
    DECLARE @s NVARCHAR(400) = COALESCE(NULLIF(@Template, N''), N'{prefix} {first} {last}');
    SET @s = REPLACE(@s, N'{prefix}', ISNULL(@Prefix, N''));
    SET @s = REPLACE(@s, N'{first}', ISNULL(@FirstName, N''));
    SET @s = REPLACE(@s, N'{middle}', ISNULL(@MiddleName, N''));
    SET @s = REPLACE(@s, N'{last}', ISNULL(@LastName, N''));
    SET @s = REPLACE(@s, N'{finitial}', LEFT(ISNULL(@FirstName, N''), 1));
    SET @s = REPLACE(@s, N'{minitial}', LEFT(ISNULL(@MiddleName, N''), 1));
    SET @s = REPLACE(@s, N'{suffixComma}', CASE WHEN LEN(@q) > 0 THEN N', ' + @q ELSE N'' END);
    SET @s = REPLACE(@s, N'{suffix}', @q);
    WHILE CHARINDEX(N'  ', @s) > 0 SET @s = REPLACE(@s, N'  ', N' ');
    SET @s = REPLACE(@s, N' ,', N',');
    RETURN LEFT(LTRIM(RTRIM(@s)), 200);
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_ListAvailableSlots
    @DoctorId        UNIQUEIDENTIFIER,
    @HospitalId      UNIQUEIDENTIFIER,
    @AppointmentDate DATE,
    @DurationMinutes INT = NULL        -- appointment type duration; NULL = doctor's slot
AS BEGIN
    SET NOCOUNT ON;
    -- Mirrors data.js listAvailableSlots (MediSlot engine):
    --   leave day → no slots; weekday with no DoctorSlotConfig windows → closed;
    --   slot = @DurationMinutes or APPT_SLOT_MINS (doctor → hospital); step = slot + APPT_BUFFER_MINS;
    --   IsBooked = overlaps a non-cancelled appointment extended by the buffer.
    -- Slot windows (DoctorSlotConfig) are hospital-local wall-clock times, so booked appointments (UTC) are
    -- converted to the hospital's timezone before comparing. Returned SlotStart / SlotEnd are hospital-local
    -- wall-clock times — pass them unchanged to opd.usp_CreateAppointment (@SlotStartLocal / @SlotEndLocal).
    DECLARE @Tz NVARCHAR(100) = cfg.fn_HospitalWindowsTz(@HospitalId);
    IF EXISTS (SELECT 1 FROM opd.DoctorLeave WHERE DoctorId = @DoctorId AND HospitalId = @HospitalId AND LeaveDate = @AppointmentDate) RETURN;

    DECLARE @Dow NVARCHAR(3) = (SELECT code FROM (VALUES
        (1,'MON'),(2,'TUE'),(3,'WED'),(4,'THU'),(5,'FRI'),(6,'SAT'),(7,'SUN')) AS d(n, code)
        WHERE d.n = ((DATEPART(WEEKDAY, @AppointmentDate) + @@DATEFIRST - 2) % 7) + 1);
    DECLARE @Slot INT = COALESCE(@DurationMinutes, TRY_CAST(cfg.fn_ResolveSetting(@HospitalId, @DoctorId, 'APPT_SLOT_MINS') AS INT), 15);
    DECLARE @Buffer INT = COALESCE(TRY_CAST(cfg.fn_ResolveSetting(@HospitalId, NULL, 'APPT_BUFFER_MINS') AS INT), 0);
    -- v3.6: a slot of under 5 minutes or over 12 hours, or a negative buffer, would make the loop below never end
    IF @Slot < 5 OR @Slot > 720 RETURN;
    IF @Buffer < 0 SET @Buffer = 0;

    DECLARE @Grid TABLE (StartMin INT NOT NULL);
    DECLARE @WStart INT, @WEnd INT, @M INT;
    DECLARE w CURSOR LOCAL FAST_FORWARD FOR
        SELECT DATEDIFF(MINUTE, '00:00', CAST(StartTime AS TIME)), DATEDIFF(MINUTE, '00:00', CAST(EndTime AS TIME))
        FROM opd.DoctorSlotConfig
        WHERE DoctorId = @DoctorId AND HospitalId = @HospitalId AND DayOfWeek = @Dow AND IsActive = 1
        ORDER BY StartTime;
    OPEN w; FETCH NEXT FROM w INTO @WStart, @WEnd;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @M = @WStart;
        WHILE @M + @Slot <= @WEnd BEGIN INSERT INTO @Grid VALUES (@M); SET @M += @Slot + @Buffer; END;
        FETCH NEXT FROM w INTO @WStart, @WEnd;
    END;
    CLOSE w; DEALLOCATE w;

    ;WITH Taken AS (
        SELECT DATEDIFF(MINUTE, '00:00', CAST(a.SlotStart AT TIME ZONE @Tz AS TIME)) AS S,
               DATEDIFF(MINUTE, '00:00', CAST(COALESCE(a.SlotEnd, a.SlotStart) AT TIME ZONE @Tz AS TIME)) + @Buffer AS E
        FROM opd.Appointment a
        WHERE a.DoctorId = @DoctorId AND a.AppointmentDate = @AppointmentDate AND a.Status <> 'CANCELLED' AND a.IsActive = 1
    )
    SELECT CONVERT(NVARCHAR(10), @AppointmentDate, 23) + 'T' + FORMAT(DATEADD(MINUTE, g.StartMin, CAST('00:00' AS TIME)), 'hh\:mm') + ':00' AS SlotStart,
           CONVERT(NVARCHAR(10), @AppointmentDate, 23) + 'T' + FORMAT(DATEADD(MINUTE, g.StartMin + @Slot, CAST('00:00' AS TIME)), 'hh\:mm') + ':00' AS SlotEnd,
           CASE WHEN EXISTS (SELECT 1 FROM Taken t WHERE g.StartMin < t.E AND g.StartMin + @Slot + @Buffer > t.S)
                THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsBooked,
           (SELECT Timezone FROM cfg.Hospital WHERE HospitalId = @HospitalId) AS TimeZoneId
    FROM @Grid g ORDER BY g.StartMin;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_GetDoctorScheduleForDate
    @DoctorId UNIQUEIDENTIFIER, @HospitalId UNIQUEIDENTIFIER, @AppointmentDate DATE
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Dow NVARCHAR(3) = (SELECT code FROM (VALUES
        (1,'MON'),(2,'TUE'),(3,'WED'),(4,'THU'),(5,'FRI'),(6,'SAT'),(7,'SUN')) AS d(n, code)
        WHERE d.n = ((DATEPART(WEEKDAY, @AppointmentDate) + @@DATEFIRST - 2) % 7) + 1);
    -- result set 1: summary
    SELECT @DoctorId AS DoctorId, @HospitalId AS HospitalId, @AppointmentDate AS [Date], @Dow AS DayOfWeek,
           COALESCE(TRY_CAST(cfg.fn_ResolveSetting(@HospitalId, @DoctorId, 'APPT_SLOT_MINS') AS INT), 15) AS SlotMinutes,
           COALESCE(TRY_CAST(cfg.fn_ResolveSetting(@HospitalId, NULL, 'APPT_BUFFER_MINS') AS INT), 0) AS BufferMinutes,
           CASE WHEN l.LeaveId IS NULL THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END AS OnLeave, l.Reason AS LeaveReason,
           (SELECT STRING_AGG(DayOfWeek, ',') FROM (SELECT DISTINCT DayOfWeek FROM opd.DoctorSlotConfig
               WHERE DoctorId = @DoctorId AND HospitalId = @HospitalId AND IsActive = 1) x) AS WorkingDays
    FROM (SELECT 1 AS one) d
    LEFT JOIN opd.DoctorLeave l ON l.DoctorId = @DoctorId AND l.HospitalId = @HospitalId AND l.LeaveDate = @AppointmentDate;
    -- result set 2: that day's windows
    SELECT StartTime, EndTime FROM opd.DoctorSlotConfig
    WHERE DoctorId = @DoctorId AND HospitalId = @HospitalId AND DayOfWeek = @Dow AND IsActive = 1
    ORDER BY StartTime;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_SaveDoctorSlotConfig
    @DoctorId UNIQUEIDENTIFIER, @HospitalId UNIQUEIDENTIFIER,
    @WindowsJson NVARCHAR(MAX)          -- [{ "DayOfWeek":"MON","StartTime":"09:00","EndTime":"13:00" }, …]
AS BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRAN;
        UPDATE opd.DoctorSlotConfig SET IsActive = 0
        WHERE DoctorId = @DoctorId AND HospitalId = @HospitalId AND IsActive = 1;
        INSERT INTO opd.DoctorSlotConfig (HospitalId, DoctorId, DayOfWeek, StartTime, EndTime)
        SELECT @HospitalId, @DoctorId, j.DayOfWeek, j.StartTime, j.EndTime
        FROM OPENJSON(@WindowsJson) WITH (DayOfWeek NVARCHAR(3), StartTime NVARCHAR(5), EndTime NVARCHAR(5)) j;
    COMMIT;
    EXEC opd.usp_ListDoctorSlotConfig @DoctorId = @DoctorId;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_GetHospitalSettings
    @HospitalId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT SettingKey, [Value] FROM cfg.HospitalSetting WHERE HospitalId = @HospitalId;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_SaveHospitalSettings
    @HospitalId UNIQUEIDENTIFIER,
    @ValuesJson NVARCHAR(MAX),          -- { "APPT_SLOT_MINS": "15", … }  (MRN_SEQ is ignored — system-managed)
    @ModifiedBy UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    MERGE cfg.HospitalSetting AS t
    USING (SELECT [key] COLLATE DATABASE_DEFAULT AS SettingKey, [value] COLLATE DATABASE_DEFAULT AS [Value]
           FROM OPENJSON(@ValuesJson) WHERE [key] <> 'MRN_SEQ') AS s
       ON t.HospitalId = @HospitalId AND t.SettingKey = s.SettingKey
    WHEN MATCHED THEN UPDATE SET [Value] = s.[Value], ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHEN NOT MATCHED THEN INSERT (HospitalId, SettingKey, [Value], ModifiedAt, ModifiedBy)
        VALUES (@HospitalId, s.SettingKey, s.[Value], SYSUTCDATETIME(), @ModifiedBy);
    -- A new DOCTOR_NAME_TEMPLATE re-renders the display name of every doctor of the hospital.
    IF EXISTS (SELECT 1 FROM OPENJSON(@ValuesJson) WHERE [key] = 'DOCTOR_NAME_TEMPLATE')
        UPDATE u SET FullName = cfg.fn_RenderDoctorName(cfg.fn_ResolveSetting(@HospitalId, NULL, 'DOCTOR_NAME_TEMPLATE'),
                                    u.DisplayNameOverride, u.Prefix, u.FirstName, u.MiddleName, u.LastName, u.Qualifications, u.FullName),
                     ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
        FROM sec.ApplicationUser u
        WHERE u.HospitalId = @HospitalId AND EXISTS (SELECT 1 FROM sec.UserRole r WHERE r.UserId = u.UserId AND r.[Role] = 'DOCTOR');
    EXEC cfg.usp_GetHospitalSettings @HospitalId = @HospitalId;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_GetDoctorSettings
    @HospitalId UNIQUEIDENTIFIER, @DoctorId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT k.SettingKey,
           COALESCE(d.[Value], h.[Value]) AS [Value],
           CASE WHEN d.[Value] IS NOT NULL THEN 'DOCTOR' WHEN h.[Value] IS NOT NULL THEN 'HOSPITAL' ELSE 'NONE' END AS Source
    FROM (VALUES ('APPT_SLOT_MINS'),('FREE_FOLLOWUP_ALLOWED'),('FREE_FOLLOWUP_DAYS'),('CHARGE_MODEL'),('CHARGE_UNIT_MINS'),('CHARGE_UNIT_RATE')) k(SettingKey)
    LEFT JOIN opd.DoctorSetting d ON d.HospitalId = @HospitalId AND d.DoctorId = @DoctorId AND d.SettingKey = k.SettingKey
    LEFT JOIN cfg.HospitalSetting h ON h.HospitalId = @HospitalId AND h.SettingKey = k.SettingKey;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_SaveDoctorSettings
    @HospitalId UNIQUEIDENTIFIER, @DoctorId UNIQUEIDENTIFIER,
    @ValuesJson NVARCHAR(MAX),          -- { "APPT_SLOT_MINS": "20", "FREE_FOLLOWUP_DAYS": "" } — "" / null = inherit
    @ModifiedBy UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DELETE d FROM opd.DoctorSetting d
    JOIN OPENJSON(@ValuesJson) j ON j.[key] COLLATE DATABASE_DEFAULT = d.SettingKey
    WHERE d.HospitalId = @HospitalId AND d.DoctorId = @DoctorId AND NULLIF(j.[value], '') IS NULL;
    MERGE opd.DoctorSetting AS t
    USING (SELECT [key] COLLATE DATABASE_DEFAULT AS SettingKey, [value] COLLATE DATABASE_DEFAULT AS [Value]
           FROM OPENJSON(@ValuesJson) WHERE NULLIF([value], '') IS NOT NULL) AS s
       ON t.HospitalId = @HospitalId AND t.DoctorId = @DoctorId AND t.SettingKey = s.SettingKey
    WHEN MATCHED THEN UPDATE SET [Value] = s.[Value], ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHEN NOT MATCHED THEN INSERT (HospitalId, DoctorId, SettingKey, [Value], ModifiedAt, ModifiedBy)
        VALUES (@HospitalId, @DoctorId, s.SettingKey, s.[Value], SYSUTCDATETIME(), @ModifiedBy);
    EXEC opd.usp_GetDoctorSettings @HospitalId = @HospitalId, @DoctorId = @DoctorId;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_ListDoctorLeave
    @DoctorId UNIQUEIDENTIFIER, @HospitalId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT LeaveId, HospitalId, DoctorId, LeaveDate, Reason FROM opd.DoctorLeave
    WHERE DoctorId = @DoctorId AND HospitalId = @HospitalId ORDER BY LeaveDate;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_CreateDoctorLeave
    @DoctorId UNIQUEIDENTIFIER, @HospitalId UNIQUEIDENTIFIER, @LeaveDate DATE, @Reason NVARCHAR(200) = 'Leave', @CreatedBy UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM opd.DoctorLeave WHERE DoctorId = @DoctorId AND HospitalId = @HospitalId AND LeaveDate = @LeaveDate)
        INSERT INTO opd.DoctorLeave (HospitalId, DoctorId, LeaveDate, Reason, CreatedBy) VALUES (@HospitalId, @DoctorId, @LeaveDate, @Reason, ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));
    SELECT LeaveId, HospitalId, DoctorId, LeaveDate, Reason FROM opd.DoctorLeave
    WHERE DoctorId = @DoctorId AND HospitalId = @HospitalId AND LeaveDate = @LeaveDate;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_DeleteDoctorLeave
    @LeaveId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    DELETE FROM opd.DoctorLeave WHERE LeaveId = @LeaveId;
    SELECT CAST(1 AS BIT) AS Success;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_ListAppointmentTypes
    @HospitalId UNIQUEIDENTIFIER, @IncludeInactive BIT = 0
AS BEGIN
    SET NOCOUNT ON;
    SELECT AppointmentTypeId, HospitalId, Code, Name, DurationMinutes, Color, IsChargeable, FeeCategory, SortOrder, IsActive
    FROM cfg.AppointmentType
    WHERE HospitalId = @HospitalId AND (@IncludeInactive = 1 OR IsActive = 1)
    ORDER BY SortOrder;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_SaveAppointmentType
    @AppointmentTypeId UNIQUEIDENTIFIER = NULL, @HospitalId UNIQUEIDENTIFIER, @Name NVARCHAR(100),
    @DurationMinutes INT = NULL, @Color NVARCHAR(7) = '#0f766e', @IsChargeable BIT = 1, @FeeCategory NVARCHAR(10) = 'NEW',
    @ModifiedBy UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    IF @AppointmentTypeId IS NOT NULL
        UPDATE cfg.AppointmentType SET Name = @Name, DurationMinutes = @DurationMinutes, Color = @Color, IsChargeable = @IsChargeable,
               FeeCategory = @FeeCategory, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
        WHERE AppointmentTypeId = @AppointmentTypeId;
    ELSE
    BEGIN
        -- Code = upper-cased name, de-duplicated within the hospital (data.js does the same)
        DECLARE @Base NVARCHAR(20) = LEFT(UPPER(REPLACE(REPLACE(@Name, ' ', '_'), '/', '_')), 20), @Code NVARCHAR(20), @N INT = 1;
        SET @Code = @Base;
        WHILE EXISTS (SELECT 1 FROM cfg.AppointmentType WHERE HospitalId = @HospitalId AND Code = @Code)
            BEGIN SET @N += 1; SET @Code = LEFT(@Base, 17) + '_' + CAST(@N AS NVARCHAR(2)); END;
        INSERT INTO cfg.AppointmentType (HospitalId, Code, Name, DurationMinutes, Color, IsChargeable, FeeCategory, SortOrder)
        VALUES (@HospitalId, @Code, @Name, @DurationMinutes, @Color, @IsChargeable, @FeeCategory,
                (SELECT COUNT(*) + 1 FROM cfg.AppointmentType WHERE HospitalId = @HospitalId));
        SET @AppointmentTypeId = (SELECT AppointmentTypeId FROM cfg.AppointmentType WHERE HospitalId = @HospitalId AND Code = @Code);
    END;
    SELECT * FROM cfg.AppointmentType WHERE AppointmentTypeId = @AppointmentTypeId;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_UpdateAppointmentTypeActive
    @AppointmentTypeId UNIQUEIDENTIFIER, @IsActive BIT
AS BEGIN
    SET NOCOUNT ON;
    UPDATE cfg.AppointmentType SET IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME() WHERE AppointmentTypeId = @AppointmentTypeId;
    SELECT * FROM cfg.AppointmentType WHERE AppointmentTypeId = @AppointmentTypeId;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListDoctorProfiles
    @HospitalId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT u.UserId, u.HospitalId, u.FullName AS DisplayName, u.Prefix, u.FirstName, u.MiddleName, u.LastName, u.Qualifications,
           u.DisplayNameOverride, u.LicenseNumber, u.Email, u.Phone, u.Room, u.Languages, u.Bio, u.SpecialtyId,
           (SELECT STRING_AGG(sp.SpecialtyName, ', ') FROM sec.DoctorSpecialty ds JOIN cfg.Specialty sp ON sp.SpecialtyId = ds.SpecialtyId
             WHERE ds.DoctorId = u.UserId) AS SpecialtyNames
    FROM sec.ApplicationUser u
    WHERE u.HospitalId = @HospitalId AND EXISTS (SELECT 1 FROM sec.UserRole r WHERE r.UserId = u.UserId AND r.[Role] = 'DOCTOR') AND u.IsActive = 1
    ORDER BY u.FullName;   -- PasswordHash is never returned
END;
GO

-- Create (@DoctorId NULL) or update a doctor: sec.ApplicationUser, its 'DOCTOR' role, and its specialties.
-- @SpecialtyIdsJson is an ordered JSON array of SpecialtyIds — the first valid one becomes the primary
-- (ApplicationUser.SpecialtyId); ids of another hospital are ignored. FullName is rendered here
-- (cfg.fn_RenderDoctorName with the hospital's DOCTOR_NAME_TEMPLATE). A new doctor needs @PasswordHash and
-- gets zero-amount NEW / FOLLOWUP fee rows effective from the hospital's today, in its currency.
CREATE OR ALTER PROCEDURE sec.usp_SaveDoctorProfile
    @DoctorId            UNIQUEIDENTIFIER = NULL,
    @HospitalId          UNIQUEIDENTIFIER,
    @Email               NVARCHAR(100),
    @PasswordHash        NVARCHAR(200) = NULL,       -- required when creating; ignored on update
    @Prefix              NVARCHAR(20)  = NULL,
    @FirstName           NVARCHAR(100) = NULL,
    @MiddleName          NVARCHAR(100) = NULL,
    @LastName            NVARCHAR(100) = NULL,
    @Qualifications      NVARCHAR(200) = NULL,
    @DisplayNameOverride NVARCHAR(200) = NULL,
    @LicenseNumber       NVARCHAR(50)  = NULL,
    @Phone               NVARCHAR(20)  = NULL,
    @Room                NVARCHAR(50)  = NULL,
    @Languages           NVARCHAR(200) = NULL,
    @Bio                 NVARCHAR(1000) = NULL,
    @SpecialtyIdsJson    NVARCHAR(MAX) = N'[]',      -- ["<SpecialtyId>", …], primary first
    @UserId              UNIQUEIDENTIFIER = NULL     -- who is saving (CreatedBy / ModifiedBy)
AS BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Actor UNIQUEIDENTIFIER = ISNULL(@UserId, '00000000-0000-0000-0000-000000000000');
    SET @Email = LTRIM(RTRIM(@Email));
    IF NULLIF(@Email, N'') IS NULL THROW 50060, N'Email is required.', 1;
    IF NULLIF(@FirstName, N'') IS NULL AND NULLIF(@LastName, N'') IS NULL AND NULLIF(@DisplayNameOverride, N'') IS NULL
        THROW 50061, N'Enter the doctor''s name.', 1;
    IF EXISTS (SELECT 1 FROM sec.ApplicationUser WHERE Email = @Email AND (@DoctorId IS NULL OR UserId <> @DoctorId))
        THROW 50062, N'Another user already has this email.', 1;
    IF ISJSON(ISNULL(@SpecialtyIdsJson, N'[]')) = 0 THROW 50063, N'@SpecialtyIdsJson must be a JSON array.', 1;

    DECLARE @Spec TABLE (Ord INT PRIMARY KEY, SpecialtyId UNIQUEIDENTIFIER);
    INSERT INTO @Spec (Ord, SpecialtyId)
    SELECT CAST(j.[key] AS INT), s.SpecialtyId
    FROM OPENJSON(ISNULL(@SpecialtyIdsJson, N'[]')) j
    INNER JOIN cfg.Specialty s ON s.SpecialtyId = TRY_CAST(j.[value] AS UNIQUEIDENTIFIER) AND s.HospitalId = @HospitalId;
    DECLARE @Primary UNIQUEIDENTIFIER = (SELECT TOP (1) SpecialtyId FROM @Spec ORDER BY Ord);
    DECLARE @Template NVARCHAR(200) = cfg.fn_ResolveSetting(@HospitalId, NULL, 'DOCTOR_NAME_TEMPLATE');

    BEGIN TRAN;
    IF @DoctorId IS NULL
    BEGIN
        IF NULLIF(@PasswordHash, N'') IS NULL THROW 50064, N'A new doctor needs @PasswordHash.', 1;
        DECLARE @New TABLE (UserId UNIQUEIDENTIFIER);
        INSERT INTO sec.ApplicationUser (HospitalId, FullName, Email, PasswordHash, Phone, SpecialtyId, LicenseNumber,
                                         Prefix, FirstName, MiddleName, LastName, Qualifications, DisplayNameOverride,
                                         Room, Languages, Bio, CreatedBy)
        OUTPUT inserted.UserId INTO @New
        VALUES (@HospitalId,
                cfg.fn_RenderDoctorName(@Template, @DisplayNameOverride, @Prefix, @FirstName, @MiddleName, @LastName, @Qualifications, N''),
                @Email, @PasswordHash, @Phone, @Primary, @LicenseNumber,
                @Prefix, @FirstName, @MiddleName, @LastName, @Qualifications, @DisplayNameOverride,
                @Room, @Languages, @Bio, @Actor);
        SELECT @DoctorId = UserId FROM @New;

        DECLARE @Today DATE = cfg.fn_HospitalToday(@HospitalId);
        INSERT INTO bil.DoctorFeeSchedule (HospitalId, DoctorId, ConsultationType, FeeAmount, Currency, EffectiveFrom, CreatedBy)
        SELECT @HospitalId, @DoctorId, t.ConsultationType, 0, h.Currency, @Today, @Actor
        FROM (VALUES ('NEW'), ('FOLLOWUP')) t(ConsultationType)
        CROSS JOIN cfg.Hospital h WHERE h.HospitalId = @HospitalId;
    END
    ELSE
    BEGIN
        UPDATE sec.ApplicationUser SET
            Email = @Email, Phone = @Phone, SpecialtyId = @Primary, LicenseNumber = @LicenseNumber,
            Prefix = @Prefix, FirstName = @FirstName, MiddleName = @MiddleName, LastName = @LastName,
            Qualifications = @Qualifications, DisplayNameOverride = @DisplayNameOverride,
            Room = @Room, Languages = @Languages, Bio = @Bio,
            FullName = cfg.fn_RenderDoctorName(@Template, @DisplayNameOverride, @Prefix, @FirstName, @MiddleName, @LastName, @Qualifications, FullName),
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE UserId = @DoctorId AND HospitalId = @HospitalId AND IsActive = 1;
        IF @@ROWCOUNT = 0 THROW 50065, N'Doctor not found in this hospital.', 1;
    END;

    IF NOT EXISTS (SELECT 1 FROM sec.UserRole WHERE UserId = @DoctorId AND [Role] = 'DOCTOR')
        INSERT INTO sec.UserRole (UserId, [Role], CreatedBy) VALUES (@DoctorId, 'DOCTOR', @Actor);

    DELETE FROM sec.DoctorSpecialty WHERE DoctorId = @DoctorId;
    INSERT INTO sec.DoctorSpecialty (DoctorId, SpecialtyId, CreatedBy)
    SELECT DISTINCT @DoctorId, SpecialtyId, @Actor FROM @Spec;
    COMMIT;

    -- 1: the profile (PasswordHash is never returned); 2: its specialties, primary first
    SELECT u.UserId AS DoctorId, u.HospitalId, u.FullName, u.Email, u.Phone, u.SpecialtyId, u.LicenseNumber,
           u.Prefix, u.FirstName, u.MiddleName, u.LastName, u.Qualifications, u.DisplayNameOverride,
           u.Room, u.Languages, u.Bio, u.IsActive
    FROM sec.ApplicationUser u WHERE u.UserId = @DoctorId;
    SELECT ds.SpecialtyId, s.SpecialtyName, CAST(CASE WHEN ds.SpecialtyId = @Primary THEN 1 ELSE 0 END AS BIT) AS IsPrimary
    FROM sec.DoctorSpecialty ds INNER JOIN cfg.Specialty s ON s.SpecialtyId = ds.SpecialtyId
    WHERE ds.DoctorId = @DoctorId
    ORDER BY CASE WHEN ds.SpecialtyId = @Primary THEN 0 ELSE 1 END, s.SpecialtyName;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_GetConsultationCharge
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- Mirrors data.js getConsultationCharge:
    -- appointment (same patient + doctor + day) → cfg.AppointmentType (chargeable? FeeCategory, duration)
    -- → CHARGE_MODEL FLAT (bil.DoctorFeeSchedule for FeeCategory) | UNIT (CEILING(duration / unit) × rate)
    -- → free follow-up if a PAID (fee > 0, not cancelled) consultation with the same doctor is within FREE_FOLLOWUP_DAYS.
    DECLARE @HospitalId UNIQUEIDENTIFIER, @PatientId UNIQUEIDENTIFIER, @DoctorId UNIQUEIDENTIFIER, @VisitDate DATE;
    SELECT @HospitalId = HospitalId, @PatientId = PatientId, @DoctorId = DoctorId, @VisitDate = VisitLocalDate
    FROM cln.Consultation WHERE ConsultationId = @ConsultationId;

    DECLARE @TypeCode NVARCHAR(20), @Duration INT;
    SELECT TOP (1) @TypeCode = a.[Type],
           @Duration = DATEDIFF(MINUTE, a.SlotStart, COALESCE(a.SlotEnd, a.SlotStart))
    FROM opd.Appointment a
    WHERE a.PatientId = @PatientId AND a.DoctorId = @DoctorId AND a.AppointmentDate = @VisitDate AND a.Status <> 'CANCELLED' AND a.IsActive = 1
    ORDER BY CASE a.Status WHEN 'IN_CONSULT' THEN 0 WHEN 'CHECKED_IN' THEN 1 WHEN 'BOOKED' THEN 2 ELSE 3 END;

    DECLARE @FeeCategory NVARCHAR(10) = 'NEW', @Chargeable BIT = 1, @TypeName NVARCHAR(100) = 'New Consultation';
    SELECT @FeeCategory = FeeCategory, @Chargeable = IsChargeable, @TypeName = Name
    FROM cfg.AppointmentType WHERE HospitalId = @HospitalId AND Code = @TypeCode;
    IF NULLIF(@Duration, 0) IS NULL SET @Duration = COALESCE(TRY_CAST(cfg.fn_ResolveSetting(@HospitalId, @DoctorId, 'APPT_SLOT_MINS') AS INT), 15);

    DECLARE @Model NVARCHAR(10) = COALESCE(cfg.fn_ResolveSetting(@HospitalId, @DoctorId, 'CHARGE_MODEL'), 'FLAT');
    DECLARE @Base DECIMAL(10,2) = 0;
    IF @Chargeable = 1
    BEGIN
        IF @Model = 'UNIT'
        BEGIN
            DECLARE @Unit INT = COALESCE(TRY_CAST(cfg.fn_ResolveSetting(@HospitalId, @DoctorId, 'CHARGE_UNIT_MINS') AS INT), 15);
            DECLARE @Rate DECIMAL(10,2) = COALESCE(TRY_CAST(cfg.fn_ResolveSetting(@HospitalId, @DoctorId, 'CHARGE_UNIT_RATE') AS DECIMAL(10,2)), 0);
            SET @Base = CEILING(@Duration * 1.0 / @Unit) * @Rate;
        END
        ELSE
            SELECT TOP (1) @Base = FeeAmount FROM bil.DoctorFeeSchedule
            WHERE DoctorId = @DoctorId AND ConsultationType = @FeeCategory AND EffectiveFrom <= @VisitDate AND (EffectiveTo IS NULL OR EffectiveTo >= @VisitDate);
    END;

    DECLARE @Allowed BIT = CASE WHEN cfg.fn_ResolveSetting(@HospitalId, @DoctorId, 'FREE_FOLLOWUP_ALLOWED') = 'true' THEN 1 ELSE 0 END;
    DECLARE @Window INT = COALESCE(TRY_CAST(cfg.fn_ResolveSetting(@HospitalId, @DoctorId, 'FREE_FOLLOWUP_DAYS') AS INT), 0);
    DECLARE @Anchor DATE = (
        SELECT MAX(c.VisitLocalDate) FROM bil.Invoice i
        JOIN cln.Consultation c ON c.ConsultationId = i.ConsultationId
        WHERE i.PatientId = @PatientId AND i.DoctorId = @DoctorId AND i.Status <> 'CANCELLED' AND i.IsFreeFollowup = 0
          AND i.ConsultationId <> @ConsultationId AND c.VisitLocalDate <= @VisitDate
          AND EXISTS (SELECT 1 FROM bil.InvoiceLineItem l WHERE l.InvoiceId = i.InvoiceId AND l.Category = 'CONSULTATION' AND l.UnitAmount > 0));
    DECLARE @IsFree BIT = CASE WHEN @Chargeable = 1 AND @Allowed = 1 AND @Anchor IS NOT NULL
                                    AND DATEDIFF(DAY, @Anchor, @VisitDate) BETWEEN 0 AND @Window THEN 1 ELSE 0 END;

    SELECT @FeeCategory AS ConsultationType, @TypeCode AS AppointmentTypeCode, @TypeName AS AppointmentTypeName,
           @Duration AS DurationMinutes, @Model AS ChargeModel, @Base AS BaseFee,
           CASE WHEN @IsFree = 1 THEN 0 ELSE @Base END AS FeeAmount, @IsFree AS IsFreeFollowup, @Anchor AS AnchorDate;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_ListIntakeQuestions
    @HospitalId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT QuestionId, HospitalId, Label, QuestionType, Options, SortOrder FROM cfg.IntakeQuestion
    WHERE HospitalId = @HospitalId AND IsActive = 1 ORDER BY SortOrder;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_CreateIntakeQuestion
    @HospitalId UNIQUEIDENTIFIER, @Label NVARCHAR(200), @QuestionType NVARCHAR(10) = 'TEXT', @Options NVARCHAR(500) = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (QuestionId UNIQUEIDENTIFIER, HospitalId UNIQUEIDENTIFIER, Label NVARCHAR(200), QuestionType NVARCHAR(10), Options NVARCHAR(500), SortOrder INT);
    INSERT INTO cfg.IntakeQuestion (HospitalId, Label, QuestionType, Options, SortOrder)
    OUTPUT inserted.QuestionId, inserted.HospitalId, inserted.Label, inserted.QuestionType, inserted.Options, inserted.SortOrder
    INTO @Out
    VALUES (@HospitalId, @Label, @QuestionType, @Options, (SELECT COUNT(*) + 1 FROM cfg.IntakeQuestion WHERE HospitalId = @HospitalId));
    SELECT QuestionId, HospitalId, Label, QuestionType, Options, SortOrder FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_DeleteIntakeQuestion
    @QuestionId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    UPDATE cfg.IntakeQuestion SET IsActive = 0 WHERE QuestionId = @QuestionId;   -- soft delete
    SELECT CAST(1 AS BIT) AS Success;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_GetConsentForm
    @HospitalId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT TOP (1) ConsentFormId, HospitalId, Title, BodyText, Version FROM cfg.ConsentForm WHERE HospitalId = @HospitalId AND IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_SaveConsentForm
    @HospitalId UNIQUEIDENTIFIER, @Title NVARCHAR(200), @BodyText NVARCHAR(MAX), @ModifiedBy UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM cfg.ConsentForm WHERE HospitalId = @HospitalId AND IsActive = 1)
        UPDATE cfg.ConsentForm
        SET Version = Version + CASE WHEN Title <> @Title OR BodyText <> @BodyText THEN 1 ELSE 0 END,
            Title = @Title, BodyText = @BodyText, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
        WHERE HospitalId = @HospitalId AND IsActive = 1;
    ELSE
        INSERT INTO cfg.ConsentForm (HospitalId, Title, BodyText, ModifiedAt, ModifiedBy) VALUES (@HospitalId, @Title, @BodyText, SYSUTCDATETIME(), @ModifiedBy);
    EXEC cfg.usp_GetConsentForm @HospitalId = @HospitalId;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_SubmitIntakeResponse
    @HospitalId UNIQUEIDENTIFIER, @AppointmentId UNIQUEIDENTIFIER, @PatientId UNIQUEIDENTIFIER, @Answers NVARCHAR(MAX)   -- JSON List<IntakeAnswer>
AS BEGIN
    SET NOCOUNT ON;
    MERGE opd.IntakeResponse AS t USING (SELECT @AppointmentId AS AppointmentId) AS s ON t.AppointmentId = s.AppointmentId
    WHEN MATCHED THEN UPDATE SET Answers = @Answers, SubmittedAt = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN INSERT (HospitalId, AppointmentId, PatientId, Answers) VALUES (@HospitalId, @AppointmentId, @PatientId, @Answers);
    SELECT * FROM opd.IntakeResponse WHERE AppointmentId = @AppointmentId;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_RecordConsent
    @HospitalId UNIQUEIDENTIFIER, @ConsentFormId UNIQUEIDENTIFIER, @PatientId UNIQUEIDENTIFIER,
    @AppointmentId UNIQUEIDENTIFIER = NULL, @ConsultationId UNIQUEIDENTIFIER = NULL,
    @SignedByName NVARCHAR(200), @Method NVARCHAR(10) = 'IN_CLINIC'
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (Id BIGINT, ConsentRecordId UNIQUEIDENTIFIER, HospitalId UNIQUEIDENTIFIER, ConsentFormId UNIQUEIDENTIFIER, PatientId UNIQUEIDENTIFIER, AppointmentId UNIQUEIDENTIFIER, ConsultationId UNIQUEIDENTIFIER, SignedByName NVARCHAR(200), Method NVARCHAR(10), FormVersion INT, TitleSnapshot NVARCHAR(200), BodySnapshot NVARCHAR(MAX), SignedAt DATETIMEOFFSET(3), CreatedAt DATETIMEOFFSET(3), CreatedBy UNIQUEIDENTIFIER, ModifiedAt DATETIMEOFFSET(3), ModifiedBy UNIQUEIDENTIFIER);
    -- v4.0: RecordedBy column dropped (ConsentRecord model has no such property).
    -- append-only: snapshot the current form text so later edits never change this record
    INSERT INTO opd.ConsentRecord (HospitalId, ConsentFormId, PatientId, AppointmentId, ConsultationId, SignedByName, Method,
                                   FormVersion, TitleSnapshot, BodySnapshot)
    OUTPUT inserted.*
    INTO @Out
    SELECT @HospitalId, f.ConsentFormId, @PatientId, @AppointmentId, @ConsultationId, @SignedByName, @Method,
           f.Version, f.Title, f.BodyText
    FROM cfg.ConsentForm f WHERE f.ConsentFormId = @ConsentFormId;
    SELECT Id, ConsentRecordId, HospitalId, ConsentFormId, PatientId, AppointmentId, ConsultationId, SignedByName, Method, FormVersion, TitleSnapshot, BodySnapshot, SignedAt, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_GetVisitIntakeAndConsent
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @HospitalId UNIQUEIDENTIFIER, @PatientId UNIQUEIDENTIFIER, @DoctorId UNIQUEIDENTIFIER, @VisitDate DATE, @AppointmentId UNIQUEIDENTIFIER;
    SELECT @HospitalId = HospitalId, @PatientId = PatientId, @DoctorId = DoctorId, @VisitDate = VisitLocalDate
    FROM cln.Consultation WHERE ConsultationId = @ConsultationId;
    SELECT TOP (1) @AppointmentId = AppointmentId FROM opd.Appointment
    WHERE PatientId = @PatientId AND DoctorId = @DoctorId AND AppointmentDate = @VisitDate AND Status <> 'CANCELLED' AND IsActive = 1
    ORDER BY CASE Status WHEN 'IN_CONSULT' THEN 0 WHEN 'CHECKED_IN' THEN 1 WHEN 'BOOKED' THEN 2 ELSE 3 END;
    SELECT * FROM opd.Appointment WHERE AppointmentId = @AppointmentId;                       -- 1: appointment
    SELECT * FROM opd.IntakeResponse WHERE AppointmentId = @AppointmentId;                    -- 2: intake answers
    EXEC cfg.usp_GetConsentForm @HospitalId = @HospitalId;                                    -- 3: current form
    SELECT TOP (1) * FROM opd.ConsentRecord                                                   -- 4: latest signature
    WHERE ConsultationId = @ConsultationId OR (@AppointmentId IS NOT NULL AND AppointmentId = @AppointmentId)
    ORDER BY SignedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_ListClinicalConditions
    @HospitalId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT ConditionId, HospitalId, Name, Icd10Prefixes FROM cfg.ClinicalCondition WHERE HospitalId = @HospitalId AND IsActive = 1 ORDER BY Name;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_CreateClinicalCondition
    @HospitalId UNIQUEIDENTIFIER, @Name NVARCHAR(100), @Icd10Prefixes NVARCHAR(200) = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (ConditionId UNIQUEIDENTIFIER, HospitalId UNIQUEIDENTIFIER, Name NVARCHAR(100), Icd10Prefixes NVARCHAR(200));
    INSERT INTO cfg.ClinicalCondition (HospitalId, Name, Icd10Prefixes)
    OUTPUT inserted.ConditionId, inserted.HospitalId, inserted.Name, inserted.Icd10Prefixes
    INTO @Out
    VALUES (@HospitalId, @Name, UPPER(@Icd10Prefixes));
    SELECT ConditionId, HospitalId, Name, Icd10Prefixes FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_DeleteClinicalCondition
    @ConditionId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    UPDATE cfg.ClinicalCondition SET IsActive = 0 WHERE ConditionId = @ConditionId;   -- soft delete; linked phrases stop matching
    SELECT CAST(1 AS BIT) AS Success;
END;
GO

-- Business event. @SlotStartLocal / @SlotEndLocal are wall-clock times in the hospital's timezone (what
-- opd.usp_ListAvailableSlots returns, what the receptionist picks). Stored as UTC instants + TimeZoneId, so
-- "9:00 AM Jamaica time" stays 9:00 AM Jamaica time for a user viewing it from another timezone.
CREATE OR ALTER PROCEDURE opd.usp_CreateAppointment
    @HospitalId     UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER,
    @DoctorId       UNIQUEIDENTIFIER,
    @SlotStartLocal DATETIME2(0),
    @SlotEndLocal   DATETIME2(0),
    @Type           NVARCHAR(20) = 'NEW',      -- cfg.AppointmentType.Code
    @Notes          NVARCHAR(300) = NULL,
    @CreatedBy      UNIQUEIDENTIFIER = NULL,
    @SkipSlotCheck  BIT = 0                    -- v3.6: 1 only from procedures that already checked under the lock
-- v3.6: with @SkipSlotCheck = 0 the slot check runs INSERT … EXEC internally, so this procedure cannot itself be
-- called inside INSERT … EXEC (SQL error 8164). DataRepository calls it through ADO.NET; the procedures here that
-- capture its result (recurring booking) pass @SkipSlotCheck = 1 after checking under the lock themselves.
AS BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Out TABLE (AppointmentId UNIQUEIDENTIFIER, AppointmentDate DATE, SlotStart DATETIMEOFFSET(0), SlotEnd DATETIMEOFFSET(0),
                        TimeZoneId NVARCHAR(50), Status NVARCHAR(15));
    DECLARE @TimeZoneId NVARCHAR(50), @Tz NVARCHAR(100);
    SELECT @TimeZoneId = h.Timezone, @Tz = tz.WindowsTimeZoneId
    FROM cfg.Hospital h INNER JOIN cfg.TimeZone tz ON tz.TimeZoneId = h.Timezone WHERE h.HospitalId = @HospitalId;
    IF @Tz IS NULL THROW 50050, N'Hospital not found or has no timezone.', 1;

    BEGIN TRAN;
    -- v3.6: re-check that the slot is still one of the doctor's free slots, under the same per-doctor-day lock as
    -- reschedule / recurring / portal booking, so two front-desk users cannot book the same time. Front-desk
    -- bookings may be for a slot that has already started today (walk-ins), so past times are allowed here.
    IF @SkipSlotCheck = 0
    BEGIN
        DECLARE @LockResult INT, @Free BIT,
                @Lock NVARCHAR(255) = N'opd.Appointment:' + CONVERT(NVARCHAR(36), @DoctorId) + N':' + CONVERT(NVARCHAR(10), CAST(@SlotStartLocal AS DATE), 23);
        EXEC @LockResult = sp_getapplock @Resource = @Lock, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @LockResult < 0 THROW 50220, N'The doctor''s schedule is being changed by someone else. Try again.', 1;
        EXEC opd.usp_CheckSlotAvailable @HospitalId = @HospitalId, @DoctorId = @DoctorId,
             @SlotStartLocal = @SlotStartLocal, @SlotEndLocal = @SlotEndLocal, @IsAvailable = @Free OUTPUT, @AllowPast = 1;
        IF @Free = 0
        BEGIN
            ROLLBACK;
            THROW 50214, N'That slot was just taken — choose another time.', 1;
        END;
    END;

    INSERT INTO opd.Appointment (HospitalId, PatientId, DoctorId, SlotStart, SlotEnd, TimeZoneId, AppointmentDate,
                                  [Type], Notes, CreatedBy)
    OUTPUT inserted.AppointmentId, inserted.AppointmentDate, inserted.SlotStart, inserted.SlotEnd, inserted.TimeZoneId, inserted.Status
    INTO @Out
    VALUES (@HospitalId, @PatientId, @DoctorId,
            (@SlotStartLocal AT TIME ZONE @Tz) AT TIME ZONE 'UTC',
            (@SlotEndLocal AT TIME ZONE @Tz) AT TIME ZONE 'UTC',
            @TimeZoneId, CAST(@SlotStartLocal AS DATE),
            @Type, @Notes, ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));
    COMMIT;
    SELECT o.AppointmentId, o.AppointmentDate, o.SlotStart, o.SlotEnd, o.TimeZoneId,
           o.SlotStart AT TIME ZONE @Tz AS SlotStartLocal, o.SlotEnd AT TIME ZONE @Tz AS SlotEndLocal, o.Status
    FROM @Out o;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_UpdateAppointmentStatus
    @AppointmentId  UNIQUEIDENTIFIER,
    @Status         NVARCHAR(15),
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (AppointmentId UNIQUEIDENTIFIER, Status NVARCHAR(15), CheckedInAt DATETIMEOFFSET(3), ConsultStartedAt DATETIMEOFFSET(3), CompletedAt DATETIMEOFFSET(3));
    -- Lifecycle: BOOKED → CHECKED_IN → IN_CONSULT → COMPLETED;
    -- CANCELLED / NO_SHOW only from BOOKED or CHECKED_IN. Same status = no-op.
    -- Any other move is rejected (no row returned) — mirrors data.js APPOINTMENT_TRANSITIONS.
    UPDATE opd.Appointment
    SET Status           = @Status,
        CheckedInAt      = CASE WHEN @Status = 'CHECKED_IN' THEN SYSUTCDATETIME() ELSE CheckedInAt END,
        CheckedInBy      = CASE WHEN @Status = 'CHECKED_IN' THEN @ModifiedBy ELSE CheckedInBy END,
        ConsultStartedAt = CASE WHEN @Status = 'IN_CONSULT' THEN SYSUTCDATETIME() ELSE ConsultStartedAt END,
        CompletedAt      = CASE WHEN @Status = 'COMPLETED'  THEN SYSUTCDATETIME() ELSE CompletedAt END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    OUTPUT inserted.AppointmentId, inserted.Status, inserted.CheckedInAt, inserted.ConsultStartedAt, inserted.CompletedAt
    INTO @Out
    WHERE AppointmentId = @AppointmentId AND IsActive = 1
      AND (   (Status = 'BOOKED'     AND @Status IN ('CHECKED_IN','CANCELLED','NO_SHOW'))
           OR (Status = 'CHECKED_IN' AND @Status IN ('IN_CONSULT','CANCELLED','NO_SHOW'))
           OR (Status = 'IN_CONSULT' AND @Status = 'COMPLETED'));
    SELECT AppointmentId, Status, CheckedInAt, ConsultStartedAt, CompletedAt FROM @Out;

    IF @@ROWCOUNT = 0
        SELECT AppointmentId, Status, CheckedInAt, ConsultStartedAt, CompletedAt
        FROM opd.Appointment
        WHERE AppointmentId = @AppointmentId AND IsActive = 1 AND Status = @Status;  -- same-status no-op; empty = rejected
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_ListNoShowReport
    @HospitalId     UNIQUEIDENTIFIER,
    @FromDate       DATE,
    @ToDate         DATE
AS BEGIN
    SET NOCOUNT ON;
    SELECT
        COUNT(*) AS Total,
        SUM(CASE WHEN Status = 'NO_SHOW' THEN 1 ELSE 0 END) AS NoShows,
        CASE WHEN COUNT(*) > 0
             THEN CAST(ROUND(100.0 * SUM(CASE WHEN Status = 'NO_SHOW' THEN 1 ELSE 0 END) / COUNT(*), 0) AS INT)
             ELSE 0 END AS NoShowRate
    FROM opd.Appointment
    WHERE HospitalId = @HospitalId AND IsActive = 1
      AND AppointmentDate BETWEEN @FromDate AND @ToDate;

    SELECT Status, COUNT(*) AS StatusCount
    FROM opd.Appointment
    WHERE HospitalId = @HospitalId AND IsActive = 1
      AND AppointmentDate BETWEEN @FromDate AND @ToDate
    GROUP BY Status;
END;
GO

-- ============================================================
-- 6. CONSULTATIONS CORE (cln)
-- ============================================================

CREATE OR ALTER PROCEDURE cln.usp_GetOrCreateConsultation
    @HospitalId     UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER,
    @DoctorId       UNIQUEIDENTIFIER,
    @AppointmentId  UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (ConsultationId UNIQUEIDENTIFIER, HospitalId UNIQUEIDENTIFIER, PatientId UNIQUEIDENTIFIER, DoctorId UNIQUEIDENTIFIER, AppointmentId UNIQUEIDENTIFIER, VisitDate DATETIMEOFFSET(0), TimeZoneId NVARCHAR(50), VisitLocalDate DATE, Status NVARCHAR(15), IsLocked BIT);
    DECLARE @ConsultationId UNIQUEIDENTIFIER;
    SELECT @ConsultationId = ConsultationId
    FROM cln.Consultation
    WHERE PatientId = @PatientId AND DoctorId = @DoctorId
      AND Status = 'IN_PROGRESS' AND IsActive = 1;

    IF @ConsultationId IS NOT NULL BEGIN
        SELECT ConsultationId, HospitalId, PatientId, DoctorId, AppointmentId,
               VisitDate, TimeZoneId, VisitLocalDate, Status, IsLocked,
               SubjectiveText, ObjectiveText, PlanText,
               SubjectivePersonal, ObjectivePersonal, PlanPersonal,
               FollowupDate, FollowupNotes, SkippedSteps
        FROM cln.Consultation WHERE ConsultationId = @ConsultationId;
    END ELSE BEGIN
        INSERT INTO cln.Consultation (HospitalId, PatientId, DoctorId, AppointmentId,
                                       VisitDate, TimeZoneId, VisitLocalDate, CreatedBy)
        OUTPUT inserted.ConsultationId, inserted.HospitalId, inserted.PatientId,
               inserted.DoctorId, inserted.AppointmentId, inserted.VisitDate,
               inserted.TimeZoneId, inserted.VisitLocalDate, inserted.Status, inserted.IsLocked
        INTO @Out
        SELECT @HospitalId, @PatientId, @DoctorId, @AppointmentId,
               SYSUTCDATETIME(), h.Timezone, cfg.fn_HospitalToday(@HospitalId), @DoctorId
        FROM cfg.Hospital h WHERE h.HospitalId = @HospitalId;
        SELECT ConsultationId, HospitalId, PatientId, DoctorId, AppointmentId, VisitDate, TimeZoneId, VisitLocalDate, Status, IsLocked FROM @Out;
    END;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_GetConsultationById
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='READ', EntityType='Consultation')
    SELECT ConsultationId, HospitalId, PatientId, DoctorId, AppointmentId,
           VisitDate, TimeZoneId, VisitLocalDate, Status, IsLocked,
           SubjectiveText, ObjectiveText, PlanText,
           SubjectivePersonal, ObjectivePersonal, PlanPersonal,
           FollowupDate, FollowupNotes, SkippedSteps
    FROM cln.Consultation WHERE ConsultationId = @ConsultationId AND IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_SaveSoapNotes
    @ConsultationId UNIQUEIDENTIFIER,
    @SubjectiveText NVARCHAR(MAX) = NULL,
    @ObjectiveText  NVARCHAR(MAX) = NULL,
    @PlanText       NVARCHAR(MAX) = NULL,
    @SubjectivePersonal NVARCHAR(MAX) = NULL,
    @ObjectivePersonal  NVARCHAR(MAX) = NULL,
    @PlanPersonal       NVARCHAR(MAX) = NULL,
    @SkippedSteps   NVARCHAR(1000) = NULL,     -- JSON array of section keys
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- v4.0: ChiefComplaint / AssessmentText columns dropped — the chief complaint is the
    -- YES symptoms (cln.ConsultationSymptom) and the assessment is cln.ConsultationDiagnosis.
    UPDATE cln.Consultation SET
        SubjectiveText = ISNULL(@SubjectiveText, SubjectiveText),
        ObjectiveText = ISNULL(@ObjectiveText, ObjectiveText),
        PlanText = ISNULL(@PlanText, PlanText),
        SubjectivePersonal = ISNULL(@SubjectivePersonal, SubjectivePersonal),
        ObjectivePersonal = ISNULL(@ObjectivePersonal, ObjectivePersonal),
        PlanPersonal = ISNULL(@PlanPersonal, PlanPersonal),
        SkippedSteps = ISNULL(@SkippedSteps, SkippedSteps),
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE ConsultationId = @ConsultationId AND IsLocked = 0;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_SaveFollowup
    @ConsultationId UNIQUEIDENTIFIER,
    @FollowupDate   DATE = NULL,
    @FollowupNotes  NVARCHAR(500) = NULL,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    UPDATE cln.Consultation
    SET FollowupDate = @FollowupDate, FollowupNotes = @FollowupNotes,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE ConsultationId = @ConsultationId;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListConsultationsForDate
    @HospitalId     UNIQUEIDENTIFIER,
    @VisitDate      DATE
AS BEGIN
    SET NOCOUNT ON;
    SELECT ConsultationId, PatientId, DoctorId, AppointmentId, VisitDate, Status
    FROM cln.Consultation
    WHERE HospitalId = @HospitalId AND VisitLocalDate = @VisitDate AND IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ConfirmSignOff
    @ConsultationId UNIQUEIDENTIFIER,
    @DoctorId       UNIQUEIDENTIFIER,
    @SignatureHash  NVARCHAR(128)              -- computed by the API layer over the signed content
AS BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Out TABLE (AttestationId UNIQUEIDENTIFIER, SignedAt DATETIMEOFFSET(3));
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='SIGNOFF', EntityType='Consultation')
    BEGIN TRAN;
    -- Stocked items from Services & Procedures are deducted first, earliest-expiry batch first. If any ran out
    -- since they were added, this raises 50040 and the whole sign-off rolls back (stock never goes negative).
    EXEC inv.usp_RecordStockConsumption @ConsultationId = @ConsultationId, @UserId = @DoctorId;

    UPDATE cln.Consultation
    SET Status = 'LOCKED', IsLocked = 1, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @DoctorId
    WHERE ConsultationId = @ConsultationId AND IsLocked = 0;

    -- v4.0: the attestation is DoctorId + SignatureHash (the per-section Is*Checked flags were dropped)
    INSERT INTO cln.ConsultationAttestation (HospitalId, ConsultationId, DoctorId, SignatureHash)
    OUTPUT inserted.AttestationId, inserted.SignedAt
    INTO @Out
    SELECT HospitalId, ConsultationId, @DoctorId, @SignatureHash
    FROM cln.Consultation WHERE ConsultationId = @ConsultationId;
    SELECT AttestationId, SignedAt FROM @Out;

    -- Signing off completes the visit: linked appointment IN_CONSULT → COMPLETED
    UPDATE a
    SET a.Status = 'COMPLETED', a.CompletedAt = SYSUTCDATETIME(),
        a.ModifiedAt = SYSUTCDATETIME(), a.ModifiedBy = @DoctorId
    FROM opd.Appointment a
    INNER JOIN cln.Consultation c
        ON c.AppointmentId = a.AppointmentId
        OR (c.AppointmentId IS NULL AND a.PatientId = c.PatientId AND a.DoctorId = c.DoctorId AND a.AppointmentDate = c.VisitLocalDate)
    WHERE c.ConsultationId = @ConsultationId AND a.Status = 'IN_CONSULT' AND a.IsActive = 1;
    COMMIT;
END;
GO

-- ============================================================
-- 7. VITALS (cln.ConsultationVital)
-- ============================================================

CREATE OR ALTER PROCEDURE cln.usp_GetConsultationVitals
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT VitalId, ConsultationId, TempValue, BpSystolic, BpDiastolic,
           PulseBpm, SpO2, RespRate, HeightCm, WeightKg, Bmi, PainScale,
           WaistCircCm, HeadCircCm, RecordedAt, RecordedBy
    FROM cln.ConsultationVital WHERE ConsultationId = @ConsultationId;

    -- ConsultationVitals.CustomValues (custom vitals, keyed by VitalCode)
    SELECT VitalCode, VitalValue
    FROM cln.ConsultationCustomVital WHERE ConsultationId = @ConsultationId;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_SaveVitals
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER,
    @TempValue      DECIMAL(5,1) = NULL,
    @BpSystolic     DECIMAL(5,1) = NULL,
    @BpDiastolic    DECIMAL(5,1) = NULL,
    @PulseBpm       DECIMAL(5,1) = NULL,
    @SpO2           DECIMAL(5,1) = NULL,
    @RespRate       DECIMAL(5,1) = NULL,
    @HeightCm       DECIMAL(5,1) = NULL,
    @WeightKg       DECIMAL(6,2) = NULL,
    @PainScale      DECIMAL(4,1) = NULL,
    @WaistCircCm    DECIMAL(5,1) = NULL,
    @HeadCircCm     DECIMAL(5,1) = NULL,
    @CustomValuesJson NVARCHAR(MAX) = NULL,   -- { "VITAL_CODE": 12.5, … } → cln.ConsultationCustomVital
    @RecordedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- BMI is computed at read/write time (kg / m^2), never stored redundantly per
    -- architecture doc §4.6 — computed here only when both Height and Weight are known.
    DECLARE @Bmi DECIMAL(4,1) = CASE WHEN @HeightCm > 0 AND @WeightKg > 0
        THEN ROUND(@WeightKg / POWER(@HeightCm / 100.0, 2), 1) ELSE NULL END;

    MERGE cln.ConsultationVital AS tgt
    USING (SELECT @ConsultationId AS ConsultationId) AS src
        ON tgt.ConsultationId = src.ConsultationId
    WHEN MATCHED THEN UPDATE SET
        TempValue = ISNULL(@TempValue, TempValue), BpSystolic = ISNULL(@BpSystolic, BpSystolic),
        BpDiastolic = ISNULL(@BpDiastolic, BpDiastolic), PulseBpm = ISNULL(@PulseBpm, PulseBpm),
        SpO2 = ISNULL(@SpO2, SpO2), RespRate = ISNULL(@RespRate, RespRate),
        HeightCm = ISNULL(@HeightCm, HeightCm), WeightKg = ISNULL(@WeightKg, WeightKg),
        Bmi = ISNULL(@Bmi, Bmi), PainScale = ISNULL(@PainScale, PainScale),
        WaistCircCm = ISNULL(@WaistCircCm, WaistCircCm), HeadCircCm = ISNULL(@HeadCircCm, HeadCircCm),
        RecordedAt = SYSUTCDATETIME(), RecordedBy = ISNULL(@RecordedBy, RecordedBy)
    WHEN NOT MATCHED THEN
        INSERT (HospitalId, ConsultationId, TempValue, BpSystolic, BpDiastolic, PulseBpm,
                SpO2, RespRate, HeightCm, WeightKg, Bmi, PainScale, WaistCircCm, HeadCircCm, RecordedBy)
        VALUES (@HospitalId, @ConsultationId, @TempValue, @BpSystolic, @BpDiastolic, @PulseBpm,
                @SpO2, @RespRate, @HeightCm, @WeightKg, @Bmi, @PainScale, @WaistCircCm, @HeadCircCm, @RecordedBy);

    IF @CustomValuesJson IS NOT NULL
        MERGE cln.ConsultationCustomVital AS tgt
        USING (SELECT [key] COLLATE DATABASE_DEFAULT AS VitalCode, TRY_CAST([value] AS DECIMAL(10,2)) AS VitalValue
               FROM OPENJSON(@CustomValuesJson)) AS src
            ON tgt.ConsultationId = @ConsultationId AND tgt.VitalCode = src.VitalCode
        WHEN MATCHED THEN UPDATE SET VitalValue = src.VitalValue, ModifiedAt = SYSUTCDATETIME()
        WHEN NOT MATCHED THEN INSERT (HospitalId, ConsultationId, VitalCode, VitalValue)
            VALUES (@HospitalId, @ConsultationId, src.VitalCode, src.VitalValue);

    EXEC cln.usp_GetConsultationVitals @ConsultationId = @ConsultationId;
END;
GO

-- ============================================================
-- 8. SYMPTOMS (cfg.SymptomCatalog, cln.ConsultationSymptom)
-- ============================================================
-- v4.0: cfg.SymptomCatalog is per hospital (HospitalId, SymptomCode, DisplayLabel,
-- IsQuickPick); cln.ConsultationSymptom stores the SymptomCode. The v3.0 global catalog
-- (SymptomName + Category QUICK_PICK/OTHER) is gone.

CREATE OR ALTER PROCEDURE cfg.usp_ListSymptomCatalog
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT SymptomCatalogId, HospitalId, SymptomCode, DisplayLabel, IsQuickPick
    FROM cfg.SymptomCatalog
    WHERE HospitalId = @HospitalId AND IsActive = 1
    ORDER BY DisplayLabel;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_ListSymptomQuickPicks
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT SymptomCatalogId, HospitalId, SymptomCode, DisplayLabel, IsQuickPick
    FROM cfg.SymptomCatalog
    WHERE HospitalId = @HospitalId AND IsActive = 1 AND IsQuickPick = 1
    ORDER BY DisplayLabel;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_ListSymptomOthers
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT SymptomCatalogId, HospitalId, SymptomCode, DisplayLabel, IsQuickPick
    FROM cfg.SymptomCatalog
    WHERE HospitalId = @HospitalId AND IsActive = 1 AND IsQuickPick = 0
    ORDER BY DisplayLabel;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_CreateCustomSymptom
    @HospitalId     UNIQUEIDENTIFIER,
    @DisplayLabel   NVARCHAR(100)
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (SymptomCatalogId UNIQUEIDENTIFIER, HospitalId UNIQUEIDENTIFIER, SymptomCode NVARCHAR(40), DisplayLabel NVARCHAR(100), IsQuickPick BIT);
    -- Code = upper-cased label (spaces → '_'); an existing code/label is returned instead of a duplicate.
    DECLARE @Code NVARCHAR(40) = LEFT(UPPER(REPLACE(LTRIM(RTRIM(@DisplayLabel)), ' ', '_')), 40);
    IF EXISTS (SELECT 1 FROM cfg.SymptomCatalog
               WHERE HospitalId = @HospitalId AND (SymptomCode = @Code OR DisplayLabel = @DisplayLabel)) BEGIN
        SELECT SymptomCatalogId, HospitalId, SymptomCode, DisplayLabel, IsQuickPick FROM cfg.SymptomCatalog
        WHERE HospitalId = @HospitalId AND (SymptomCode = @Code OR DisplayLabel = @DisplayLabel);
        RETURN;
    END;
    INSERT INTO cfg.SymptomCatalog (HospitalId, SymptomCode, DisplayLabel, IsQuickPick)
    OUTPUT inserted.SymptomCatalogId, inserted.HospitalId, inserted.SymptomCode, inserted.DisplayLabel, inserted.IsQuickPick
    INTO @Out
    VALUES (@HospitalId, @Code, @DisplayLabel, 0);
    SELECT SymptomCatalogId, HospitalId, SymptomCode, DisplayLabel, IsQuickPick FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListConsultationSymptoms
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT cs.SymptomId, cs.ConsultationId, cs.SymptomCode, sc.DisplayLabel, cs.[State]
    FROM cln.ConsultationSymptom cs
    LEFT JOIN cfg.SymptomCatalog sc ON sc.HospitalId = cs.HospitalId AND sc.SymptomCode = cs.SymptomCode
    WHERE cs.ConsultationId = @ConsultationId;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_UpdateSymptomState
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER,
    @SymptomCode    NVARCHAR(40),
    @State          NVARCHAR(10)               -- UNSET | YES | NO
AS BEGIN
    SET NOCOUNT ON;
    -- Upsert (insert on first toggle, update thereafter) — tri-state cycle UNSET → YES → NO → UNSET
    MERGE cln.ConsultationSymptom AS tgt
    USING (SELECT @ConsultationId AS ConsultationId, @SymptomCode AS SymptomCode) AS src
        ON tgt.ConsultationId = src.ConsultationId AND tgt.SymptomCode = src.SymptomCode
    WHEN MATCHED THEN UPDATE SET [State] = @State
    WHEN NOT MATCHED THEN
        INSERT (HospitalId, ConsultationId, SymptomCode, [State])
        VALUES (@HospitalId, @ConsultationId, @SymptomCode, @State);
END;
GO

-- ============================================================
-- 9. PHRASES (cfg.PhraseBank)
-- ============================================================

CREATE OR ALTER PROCEDURE cfg.usp_ListPhrases
    @HospitalId     UNIQUEIDENTIFIER,
    @Section        NVARCHAR(5),               -- S | O | P (rows with Section 'ANY' always match)
    @SpecialtyId    UNIQUEIDENTIFIER = NULL,
    @DoctorId       UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- Scope precedence DOCTOR > SPECIALTY > GLOBAL.
    SELECT PhraseId, HospitalId, Scope, SpecialtyId, DoctorId, ConditionId, Section,
           TriggerKeyword, PhraseText, SortOrder
    FROM cfg.PhraseBank
    WHERE HospitalId = @HospitalId AND Section IN (@Section, 'ANY') AND IsActive = 1
      AND (Scope = 'GLOBAL'
           OR (Scope = 'SPECIALTY' AND SpecialtyId = @SpecialtyId)
           OR (Scope = 'DOCTOR' AND DoctorId = @DoctorId))
    ORDER BY CASE Scope WHEN 'DOCTOR' THEN 0 WHEN 'SPECIALTY' THEN 1 ELSE 2 END, SortOrder, PhraseText;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_SearchPhrases
    @Term           NVARCHAR(100),
    @HospitalId     UNIQUEIDENTIFIER,
    @Section        NVARCHAR(5),
    @SpecialtyId    UNIQUEIDENTIFIER = NULL,
    @DoctorId       UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SELECT TOP (5) PhraseId, HospitalId, Scope, SpecialtyId, DoctorId, ConditionId, Section,
           TriggerKeyword, PhraseText
    FROM cfg.PhraseBank
    WHERE HospitalId = @HospitalId AND Section IN (@Section, 'ANY') AND IsActive = 1
      AND (Scope = 'GLOBAL'
           OR (Scope = 'SPECIALTY' AND SpecialtyId = @SpecialtyId)
           OR (Scope = 'DOCTOR' AND DoctorId = @DoctorId))
      AND (TriggerKeyword LIKE '%' + @Term + '%' OR PhraseText LIKE '%' + @Term + '%')
    ORDER BY CASE Scope WHEN 'DOCTOR' THEN 0 WHEN 'SPECIALTY' THEN 1 ELSE 2 END, SortOrder;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_CreatePhrase
    @HospitalId     UNIQUEIDENTIFIER,
    @Scope          NVARCHAR(10) = 'DOCTOR',   -- GLOBAL | SPECIALTY | DOCTOR
    @SpecialtyId    UNIQUEIDENTIFIER = NULL,   -- required for SPECIALTY scope
    @DoctorId       UNIQUEIDENTIFIER = NULL,   -- required for DOCTOR scope
    @ConditionId    UNIQUEIDENTIFIER = NULL,   -- NULL = any condition
    @Section        NVARCHAR(5) = 'ANY',
    @TriggerKeyword NVARCHAR(30) = NULL,
    @PhraseText     NVARCHAR(500),
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (PhraseId UNIQUEIDENTIFIER, Scope NVARCHAR(10), Section NVARCHAR(5), TriggerKeyword NVARCHAR(30), PhraseText NVARCHAR(500));
    INSERT INTO cfg.PhraseBank (HospitalId, Scope, SpecialtyId, DoctorId, ConditionId, Section,
                                TriggerKeyword, PhraseText, CreatedBy)
    OUTPUT inserted.PhraseId, inserted.Scope, inserted.Section, inserted.TriggerKeyword, inserted.PhraseText
    INTO @Out
    VALUES (@HospitalId, @Scope, @SpecialtyId, @DoctorId, @ConditionId, @Section,
            @TriggerKeyword, @PhraseText, ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));
    SELECT PhraseId, Scope, Section, TriggerKeyword, PhraseText FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_DeletePhrase
    @PhraseId       UNIQUEIDENTIFIER,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- data.js performs a hard splice(); schema has IsActive, so soft delete (IsActive = 0) is
    -- used (preferred per architecture §4.8) rather than a physical DELETE.
    UPDATE cfg.PhraseBank SET IsActive = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE PhraseId = @PhraseId;
END;
GO

-- ============================================================
-- 10. DIAGNOSES / ICD10 (cfg.ICD10Catalog, cln.ConsultationDiagnosis)
-- ============================================================

CREATE OR ALTER PROCEDURE cfg.usp_SearchIcd10Catalog
    @Term           NVARCHAR(100)
AS BEGIN
    SET NOCOUNT ON;
    SELECT Icd10CatalogId, Icd10Code, [Description], SynonymTerms
    FROM cfg.ICD10Catalog
    WHERE IsActive = 1
      AND (Icd10Code LIKE @Term + '%' OR [Description] LIKE '%' + @Term + '%' OR SynonymTerms LIKE '%' + @Term + '%')
    ORDER BY Icd10Code;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListConsultationDiagnoses
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT DiagnosisId, ConsultationId, Icd10Code, Icd10Description, ClinicalNotes,
           IsPrimary, Laterality, AcuteChronic, SequenceOrder
    FROM cln.ConsultationDiagnosis
    WHERE ConsultationId = @ConsultationId AND IsActive = 1
    ORDER BY SequenceOrder;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_CreateDiagnosis
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER,
    @Icd10Code      NVARCHAR(10),
    @Icd10Description NVARCHAR(300),
    @ClinicalNotes  NVARCHAR(500) = NULL,
    @Laterality     NVARCHAR(20) = NULL,
    @AcuteChronic   NVARCHAR(20) = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (DiagnosisId UNIQUEIDENTIFIER, Icd10Code NVARCHAR(10), IsPrimary BIT);
    -- First diagnosis on a consultation defaults to IsPrimary=1, matching data.js
    DECLARE @ExistingCount INT;
    SELECT @ExistingCount = COUNT(*) FROM cln.ConsultationDiagnosis
    WHERE ConsultationId = @ConsultationId AND IsActive = 1;

    INSERT INTO cln.ConsultationDiagnosis (HospitalId, ConsultationId, Icd10Code, Icd10Description, ClinicalNotes,
                                            IsPrimary, Laterality, AcuteChronic, SequenceOrder)
    OUTPUT inserted.DiagnosisId, inserted.Icd10Code, inserted.IsPrimary
    INTO @Out
    VALUES (@HospitalId, @ConsultationId, @Icd10Code, @Icd10Description, @ClinicalNotes,
            CASE WHEN @ExistingCount = 0 THEN 1 ELSE 0 END, @Laterality, @AcuteChronic, @ExistingCount);
    SELECT DiagnosisId, Icd10Code, IsPrimary FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_DeleteDiagnosis
    @DiagnosisId    UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    UPDATE cln.ConsultationDiagnosis SET IsActive = 0 WHERE DiagnosisId = @DiagnosisId;
END;
GO

-- ============================================================
-- 11. FORMULARY / PRESCRIPTIONS (cfg.DrugFormulary, cln.Prescription)
-- ============================================================
-- v4.0: cfg.DrugFormulary is per hospital and matches the Drug model (DrugId, DefaultRoute,
-- DefaultDurationDays, GenericClass, IsControlledSubstance). Strength lives on the
-- prescription line only (cln.Prescription.Strength), not on the formulary row.

CREATE OR ALTER PROCEDURE cfg.usp_SearchFormulary
    @Term           NVARCHAR(100),
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT DrugId, HospitalId, BrandName, GenericName, Composition, TherapeuticClass, GenericClass,
           DrugForm, DefaultRoute, DefaultDosage, DefaultFrequency, DefaultDurationDays,
           DefaultInstructions, IsControlledSubstance
    FROM cfg.DrugFormulary
    WHERE HospitalId = @HospitalId AND IsActive = 1
      AND (BrandName LIKE '%' + @Term + '%' OR GenericName LIKE '%' + @Term + '%')
    ORDER BY BrandName;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_GetDrugByName
    @HospitalId     UNIQUEIDENTIFIER,
    @BrandName      NVARCHAR(200)
AS BEGIN
    SET NOCOUNT ON;
    SELECT TOP (1) DrugId, HospitalId, BrandName, GenericName, Composition, TherapeuticClass, GenericClass,
           DrugForm, DefaultRoute, DefaultDosage, DefaultFrequency, DefaultDurationDays,
           DefaultInstructions, IsControlledSubstance
    FROM cfg.DrugFormulary
    WHERE HospitalId = @HospitalId AND IsActive = 1 AND BrandName = @BrandName;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_CreateDrug
    @HospitalId             UNIQUEIDENTIFIER,
    @BrandName              NVARCHAR(200),
    @GenericName            NVARCHAR(200) = NULL,
    @Composition            NVARCHAR(500) = NULL,
    @TherapeuticClass       NVARCHAR(300) = NULL,
    @GenericClass           NVARCHAR(200) = NULL,
    @DrugForm               NVARCHAR(30) = NULL,
    @DefaultRoute           NVARCHAR(30) = NULL,
    @DefaultDosage          NVARCHAR(20) = NULL,
    @DefaultFrequency       NVARCHAR(20) = NULL,
    @DefaultDurationDays    INT = NULL,
    @DefaultInstructions    NVARCHAR(300) = NULL,
    @IsControlledSubstance  BIT = 0,
    @CreatedBy              UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (DrugId UNIQUEIDENTIFIER, HospitalId UNIQUEIDENTIFIER, BrandName NVARCHAR(200), GenericName NVARCHAR(200), Composition NVARCHAR(500), TherapeuticClass NVARCHAR(300), GenericClass NVARCHAR(200), DrugForm NVARCHAR(30), DefaultRoute NVARCHAR(30), DefaultDosage NVARCHAR(20), DefaultFrequency NVARCHAR(20), DefaultDurationDays INT, DefaultInstructions NVARCHAR(300), IsControlledSubstance BIT, IsActive BIT);
    INSERT INTO cfg.DrugFormulary (HospitalId, BrandName, GenericName, Composition, TherapeuticClass, GenericClass,
           DrugForm, DefaultRoute, DefaultDosage, DefaultFrequency, DefaultDurationDays,
           DefaultInstructions, IsControlledSubstance, CreatedBy)
    OUTPUT inserted.DrugId, inserted.HospitalId, inserted.BrandName, inserted.GenericName, inserted.Composition,
           inserted.TherapeuticClass, inserted.GenericClass, inserted.DrugForm, inserted.DefaultRoute,
           inserted.DefaultDosage, inserted.DefaultFrequency, inserted.DefaultDurationDays,
           inserted.DefaultInstructions, inserted.IsControlledSubstance, inserted.IsActive
    INTO @Out
    VALUES (@HospitalId, @BrandName, @GenericName, @Composition, @TherapeuticClass, @GenericClass,
            @DrugForm, @DefaultRoute, @DefaultDosage, @DefaultFrequency, @DefaultDurationDays,
            @DefaultInstructions, @IsControlledSubstance, ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));
    SELECT DrugId, HospitalId, BrandName, GenericName, Composition, TherapeuticClass, GenericClass, DrugForm, DefaultRoute, DefaultDosage, DefaultFrequency, DefaultDurationDays, DefaultInstructions, IsControlledSubstance, IsActive FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_UpdateDrugStatus
    @DrugId             UNIQUEIDENTIFIER,
    @ModifiedBy          UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- data.js toggles the current value (no explicit flag passed by api.js)
    UPDATE cfg.DrugFormulary
    SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE DrugId = @DrugId;

    SELECT DrugId, BrandName, IsActive
    FROM cfg.DrugFormulary WHERE DrugId = @DrugId;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListPrescriptions
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT PrescriptionId, ConsultationId, PatientId, MedicineName, Strength, DrugForm,
           DeliveryRoute, Category, TakeInQty, TimesPerDay, DurationValue, DurationUnit,
           Instructions, DynamicInstruction, Prn, TimingBf, TimingAn, TimingNi, TimingEm,
           ValidityDays, ValidUntil, TotalDispenseQty, Status
    FROM cln.Prescription
    WHERE ConsultationId = @ConsultationId AND Status = 'ACTIVE' AND IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListActiveMedicationsForPatient
    @PatientId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT PrescriptionId, ConsultationId, Source, SourceDetail, MedicineName, Strength, DrugForm,
           TakeInQty, TimesPerDay, DurationValue, DurationUnit, Instructions, Status
    FROM cln.Prescription
    WHERE PatientId = @PatientId AND Status = 'ACTIVE' AND IsActive = 1
    ORDER BY CreatedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_CreatePrescription
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER = NULL,   -- NULL only for an outside medicine (@Source = 'OUTSIDE')
    @PatientId      UNIQUEIDENTIFIER,
    @Source         NVARCHAR(10) = NULL,
    @SourceDetail   NVARCHAR(120) = NULL,
    @MedicineName   NVARCHAR(200),
    @Strength       NVARCHAR(50) = NULL,
    @GenericClass   NVARCHAR(200) = NULL,
    @TherapeuticClass NVARCHAR(300) = NULL,
    @DrugForm       NVARCHAR(30) = NULL,
    @DeliveryRoute  NVARCHAR(30) = NULL,
    @Category       NVARCHAR(20) = NULL,
    @TakeInQty      DECIMAL(6,2) = 1,
    @TimesPerDay    INT = 1,
    @DurationValue  INT = 0,
    @DurationUnit   NVARCHAR(10) = NULL,
    @Instructions   NVARCHAR(500) = NULL,
    @Prn            BIT = 0,
    @TimingBf       BIT = 0,
    @TimingAn       BIT = 0,
    @TimingNi       BIT = 0,
    @TimingEm       BIT = 0,
    @ValidityDays   INT = 30,
    @RepeatedFromConsultationId UNIQUEIDENTIFIER = NULL,
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (PrescriptionId UNIQUEIDENTIFIER, MedicineName NVARCHAR(200), Status NVARCHAR(15), CreatedAt DATETIMEOFFSET(3));
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='CREATE', EntityType='Prescription')
    INSERT INTO cln.Prescription (HospitalId, ConsultationId, PatientId, Source, SourceDetail, MedicineName,
        Strength, GenericClass, TherapeuticClass, DrugForm, DeliveryRoute, Category, TakeInQty, TimesPerDay,
        DurationValue, DurationUnit, Instructions, Prn,
        TimingBf, TimingAn, TimingNi, TimingEm, ValidityDays, ValidUntil,
        RepeatedFromConsultationId, CreatedBy)
    OUTPUT inserted.PrescriptionId, inserted.MedicineName, inserted.Status, inserted.CreatedAt
    INTO @Out
    VALUES (@HospitalId, @ConsultationId, @PatientId, @Source, @SourceDetail, @MedicineName,
        @Strength, @GenericClass, @TherapeuticClass, @DrugForm, @DeliveryRoute, @Category, @TakeInQty, @TimesPerDay,
        ISNULL(@DurationValue, 0), @DurationUnit, @Instructions, @Prn,
        @TimingBf, @TimingAn, @TimingNi, @TimingEm, @ValidityDays,
        DATEADD(DAY, @ValidityDays, cfg.fn_HospitalToday(@HospitalId)),
        @RepeatedFromConsultationId, ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));
    SELECT PrescriptionId, MedicineName, Status, CreatedAt FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_CancelPrescription
    @PrescriptionId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    UPDATE cln.Prescription SET Status = 'DISCONTINUED', ModifiedAt = SYSUTCDATETIME()
    WHERE PrescriptionId = @PrescriptionId;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_CheckDrugAllergyConflicts
    @PatientId      UNIQUEIDENTIFIER,
    @MedicineName   NVARCHAR(200),
    @Composition    NVARCHAR(500) = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: if the caller proceeds despite a CRITICAL alert, it must also invoke
    -- sec.usp_RecordAuditEntry (Action='OVERRIDE', Details naming the AlertType/reason)
    DECLARE @DrugText NVARCHAR(700) = LOWER(@MedicineName + ' ' + ISNULL(@Composition, ''));
    SELECT
        'CRITICAL' AS Severity, a.AllergyName, a.Severity AS AllergySeverity,
        CASE
            WHEN LOWER(a.AllergyName) LIKE '%penicillin%'
                 AND (@DrugText LIKE '%amoxicillin%' OR @DrugText LIKE '%ampicillin%'
                      OR @DrugText LIKE '%penicillin%' OR @DrugText LIKE '%augmentin%')
                THEN 'CRITICAL: ' + @MedicineName + ' belongs to the Penicillin class. Patient has Penicillin allergy (' + a.Severity + ').'
            ELSE 'CRITICAL: ' + @MedicineName + ' may conflict with known allergy to ' + a.AllergyName + ' (' + a.Severity + ').'
        END AS Message
    FROM opd.PatientAllergy a
    WHERE a.PatientId = @PatientId AND a.Status = 'ACTIVE' AND a.IsActive = 1
      AND (
        @DrugText LIKE '%' + LOWER(a.AllergyName) + '%'
        OR LOWER(a.AllergyName) LIKE '%' + LOWER(LEFT(@MedicineName, CHARINDEX(' ', @MedicineName + ' ') - 1)) + '%'
        OR (LOWER(a.AllergyName) LIKE '%penicillin%'
            AND (@DrugText LIKE '%amoxicillin%' OR @DrugText LIKE '%ampicillin%'
                 OR @DrugText LIKE '%penicillin%' OR @DrugText LIKE '%augmentin%'))
      );
END;
GO

-- ============================================================
-- 12. LAB / INVESTIGATIONS (cfg.LabTestCatalog, cln.DoctorLabFavorite,
--     cln.InvestigationOrder, cln.LabResult)
-- ============================================================
-- v4.0: cfg.LabTestCatalog is per hospital (TestId, TestType, Category); the specialties a
-- test is suggested for are in cfg.LabTestSpecialty. @SpecialtyId filters through that table
-- (NULL = no filter). Order status ORDERED → SENT → RESULTS_READY; result status
-- PENDING_REVIEW → REVIEWED.

CREATE OR ALTER PROCEDURE cln.usp_ListDoctorLabFavorites
    @DoctorId       UNIQUEIDENTIFIER,
    @SpecialtyId    UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SELECT f.FavoriteId, t.TestId, t.TestName, t.TestType, t.Category
    FROM cln.DoctorLabFavorite f
    INNER JOIN cfg.LabTestCatalog t ON t.TestId = f.TestId
    WHERE f.DoctorId = @DoctorId AND t.IsActive = 1
      AND (@SpecialtyId IS NULL
           OR EXISTS (SELECT 1 FROM cfg.LabTestSpecialty ls WHERE ls.TestId = t.TestId AND ls.SpecialtyId = @SpecialtyId));
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_SearchLabTests
    @HospitalId     UNIQUEIDENTIFIER,
    @Term           NVARCHAR(100),
    @SpecialtyId    UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SELECT t.TestId, t.TestName, t.TestType, t.Category
    FROM cfg.LabTestCatalog t
    WHERE t.HospitalId = @HospitalId AND t.IsActive = 1 AND t.TestName LIKE '%' + @Term + '%'
      AND (@SpecialtyId IS NULL
           OR EXISTS (SELECT 1 FROM cfg.LabTestSpecialty ls WHERE ls.TestId = t.TestId AND ls.SpecialtyId = @SpecialtyId))
    ORDER BY t.TestName;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_CreateLabTest
    @HospitalId     UNIQUEIDENTIFIER,
    @TestName       NVARCHAR(100),
    @TestType       NVARCHAR(20) = 'LAB',
    @Category       NVARCHAR(50) = NULL,
    @SpecialtyIds   NVARCHAR(MAX) = NULL       -- comma-separated SpecialtyIds → cfg.LabTestSpecialty
AS BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @New TABLE (TestId UNIQUEIDENTIFIER, TestName NVARCHAR(100));
    BEGIN TRAN;
    INSERT INTO cfg.LabTestCatalog (HospitalId, TestName, TestType, Category)
    OUTPUT inserted.TestId, inserted.TestName INTO @New
    VALUES (@HospitalId, @TestName, @TestType, @Category);

    INSERT INTO cfg.LabTestSpecialty (TestId, SpecialtyId)
    SELECT DISTINCT n.TestId, TRY_CAST(LTRIM(RTRIM(x.value)) AS UNIQUEIDENTIFIER)
    FROM @New n CROSS JOIN STRING_SPLIT(@SpecialtyIds, ',') x
    WHERE TRY_CAST(LTRIM(RTRIM(x.value)) AS UNIQUEIDENTIFIER) IS NOT NULL;
    COMMIT;

    SELECT TestId, TestName FROM @New;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_CreateLabFavorite
    @DoctorId       UNIQUEIDENTIFIER,
    @TestId         UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM cln.DoctorLabFavorite WHERE DoctorId = @DoctorId AND TestId = @TestId)
        INSERT INTO cln.DoctorLabFavorite (DoctorId, TestId) VALUES (@DoctorId, @TestId);

    SELECT FavoriteId, DoctorId, TestId
    FROM cln.DoctorLabFavorite WHERE DoctorId = @DoctorId AND TestId = @TestId;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_DeleteLabFavorite
    @DoctorId       UNIQUEIDENTIFIER,
    @TestId         UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- No IsActive on this junction table — hard delete matches data.js splice()
    DELETE FROM cln.DoctorLabFavorite WHERE DoctorId = @DoctorId AND TestId = @TestId;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_CreateInvestigationOrder
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER,
    @TestId         UNIQUEIDENTIFIER = NULL,   -- cfg.LabTestCatalog; NULL for a free-text test
    @TestName       NVARCHAR(100),
    @TestType       NVARCHAR(20) = 'LAB',
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (OrderId UNIQUEIDENTIFIER, TestName NVARCHAR(100), Status NVARCHAR(15), OrderedAt DATETIMEOFFSET(3));
    -- v4.0: Priority / ClinicalNotes / IsFasting columns dropped (not on the InvestigationOrder model).
    INSERT INTO cln.InvestigationOrder (HospitalId, ConsultationId, PatientId,
                                        TestId, TestName, TestType, CreatedBy)
    OUTPUT inserted.OrderId, inserted.TestName, inserted.Status, inserted.OrderedAt
    INTO @Out
    VALUES (@HospitalId, @ConsultationId, @PatientId,
            @TestId, @TestName, @TestType, ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));
    SELECT OrderId, TestName, Status, OrderedAt FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListInvestigationOrders
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT OrderId, ConsultationId, PatientId, TestId, TestName, TestType,
           Status, OrderedAt, LabPartnerId
    FROM cln.InvestigationOrder
    WHERE ConsultationId = @ConsultationId AND IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListPendingLabOrders
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- Orders still awaiting a result: ORDERED (not yet claimed) or SENT (claimed by a lab partner)
    SELECT o.OrderId, o.PatientId, p.FullName AS PatientName, p.Mrn AS PatientMrn,
           o.TestId, o.TestName, o.TestType, o.Status, o.OrderedAt, o.LabPartnerId
    FROM cln.InvestigationOrder o
    INNER JOIN opd.Patient p ON p.PatientId = o.PatientId
    WHERE o.HospitalId = @HospitalId AND o.Status IN ('ORDERED','SENT') AND o.IsActive = 1
    ORDER BY o.OrderedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_UpdateOrderStatus
    @OrderId        UNIQUEIDENTIFIER,
    @Status         NVARCHAR(15),              -- ORDERED | SENT | RESULTS_READY
    @LabPartnerId   UNIQUEIDENTIFIER = NULL    -- set when a lab partner claims the order (→ SENT)
AS BEGIN
    SET NOCOUNT ON;
    UPDATE cln.InvestigationOrder
    SET Status = @Status, LabPartnerId = ISNULL(@LabPartnerId, LabPartnerId)
    WHERE OrderId = @OrderId;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_SubmitLabResult
    @OrderId        UNIQUEIDENTIFIER,
    @HospitalId     UNIQUEIDENTIFIER,
    @LabPartnerId   UNIQUEIDENTIFIER = NULL,
    @ResultSummary  NVARCHAR(500) = NULL,
    @ResultValue    NVARCHAR(100) = NULL,
    @ReferenceRange NVARCHAR(100) = NULL,
    @Interpretation NVARCHAR(10) = NULL,
    @Technician     NVARCHAR(100) = NULL,
    @Notes          NVARCHAR(500) = NULL,
    @UploadedByUserId UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (ResultId UNIQUEIDENTIFIER, Status NVARCHAR(15), UploadedAt DATETIMEOFFSET(3));
    SET XACT_ABORT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='SUBMIT', EntityType='LabResult')
    -- Patient / consultation / test name are copied from the order (the LabResult model carries them).
    BEGIN TRAN;
    INSERT INTO cln.LabResult (OrderId, HospitalId, PatientId, ConsultationId, TestName,
                                ResultSummary, ResultValue, ReferenceRange, Interpretation,
                                Technician, Notes, UploadedByUserId, LabPartnerId)
    OUTPUT inserted.ResultId, inserted.Status, inserted.UploadedAt
    INTO @Out
    SELECT o.OrderId, @HospitalId, o.PatientId, o.ConsultationId, o.TestName,
           @ResultSummary, @ResultValue, @ReferenceRange, @Interpretation,
           @Technician, @Notes, @UploadedByUserId, ISNULL(@LabPartnerId, o.LabPartnerId)
    FROM cln.InvestigationOrder o
    WHERE o.OrderId = @OrderId AND o.HospitalId = @HospitalId;
    SELECT ResultId, Status, UploadedAt FROM @Out;

    UPDATE cln.InvestigationOrder SET Status = 'RESULTS_READY'
    WHERE OrderId = @OrderId;
    COMMIT;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListLabResultsForPatient
    @PatientId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT r.ResultId, r.OrderId, r.ConsultationId, r.TestName, r.ResultSummary, r.ResultValue,
           r.ReferenceRange, r.Interpretation, r.Technician, r.Notes, r.Status, r.UploadedAt,
           r.ReviewedAt, r.ReviewNotes
    FROM cln.LabResult r
    WHERE r.PatientId = @PatientId AND r.IsActive = 1
    ORDER BY r.UploadedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListPendingReviewResults
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT r.ResultId, r.OrderId, r.PatientId, p.FullName AS PatientName, p.Mrn AS PatientMrn,
           r.TestName, r.ResultSummary, r.ResultValue, r.Interpretation, r.Status, r.UploadedAt
    FROM cln.LabResult r
    INNER JOIN opd.Patient p ON p.PatientId = r.PatientId
    WHERE r.HospitalId = @HospitalId AND r.Status = 'PENDING_REVIEW' AND r.IsActive = 1
    ORDER BY r.UploadedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_UpdateLabResultStatus
    @ResultId       UNIQUEIDENTIFIER,
    @ReviewNotes    NVARCHAR(300) = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='APPROVE', EntityType='LabResult').
    -- v4.0: ReviewedBy column dropped (not on the LabResult model) — the reviewer is in the audit entry.
    UPDATE cln.LabResult
    SET Status = 'REVIEWED', ReviewedAt = SYSUTCDATETIME(), ReviewNotes = @ReviewNotes
    WHERE ResultId = @ResultId;
END;
GO

-- ============================================================
-- 13. REFERRALS (cln.Referral)
-- ============================================================

CREATE OR ALTER PROCEDURE cln.usp_CreateReferral
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER,
    @Specialty      NVARCHAR(100),
    @DoctorName     NVARCHAR(200) = NULL,
    @Urgency        NVARCHAR(20) = 'ROUTINE',
    @Reason         NVARCHAR(500) = NULL,
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (ReferralId UNIQUEIDENTIFIER, Specialty NVARCHAR(100), Urgency NVARCHAR(20));
    -- v4.0: ReferToSpecialty → Specialty, ReferToDoctorName → DoctorName; Status column dropped.
    INSERT INTO cln.Referral (HospitalId, ConsultationId, PatientId, Specialty,
                               DoctorName, Urgency, Reason, CreatedBy)
    OUTPUT inserted.ReferralId, inserted.Specialty, inserted.Urgency
    INTO @Out
    VALUES (@HospitalId, @ConsultationId, @PatientId, @Specialty,
            @DoctorName, @Urgency, @Reason, ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));
    SELECT ReferralId, Specialty, Urgency FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListReferrals
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT ReferralId, ConsultationId, PatientId, Specialty, DoctorName,
           Urgency, Reason, CreatedAt
    FROM cln.Referral WHERE ConsultationId = @ConsultationId AND IsActive = 1;
END;
GO

-- ============================================================
-- 14. SKETCHES / DRAWING PAD (cln.ConsultationSketch)
-- ============================================================

CREATE OR ALTER PROCEDURE cln.usp_SaveSketch
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER,
    @DoctorId       UNIQUEIDENTIFIER,
    @Title          NVARCHAR(100) = NULL,
    @ImageData      NVARCHAR(MAX)              -- JPEG data URL (v4.0: StrokesJson dropped)
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (SketchId UNIQUEIDENTIFIER, CreatedAt DATETIMEOFFSET(3));
    -- data.js always inserts a new row (never updates) despite the "save" verb;
    -- mirrored here as an INSERT-only procedure.
    INSERT INTO cln.ConsultationSketch (HospitalId, ConsultationId, PatientId, DoctorId,
                                         Title, ImageData)
    OUTPUT inserted.SketchId, inserted.CreatedAt
    INTO @Out
    VALUES (@HospitalId, @ConsultationId, @PatientId, @DoctorId, @Title, @ImageData);
    SELECT SketchId, CreatedAt FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListSketches
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT SketchId, ConsultationId, PatientId, DoctorId, Title, ImageData, CreatedAt
    FROM cln.ConsultationSketch WHERE ConsultationId = @ConsultationId
    ORDER BY CreatedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListPatientSketches
    @PatientId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT SketchId, ConsultationId, PatientId, DoctorId, Title, ImageData, CreatedAt
    FROM cln.ConsultationSketch WHERE PatientId = @PatientId
    ORDER BY CreatedAt DESC;
END;
GO

-- ============================================================
-- 15. CONFIDENTIAL NOTES & ADDENDUMS (cln)
-- ============================================================

CREATE OR ALTER PROCEDURE cln.usp_GetConfidentialNote
    @ConsultationId UNIQUEIDENTIFIER,
    @DoctorId       UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- Author-only: WHERE clause enforces the note is only ever returned to its
    -- own author (DoctorId = @DoctorId), matching data.js's DoctorId match.
    SELECT ConfidentialNoteId, ConsultationId, DoctorId, NoteText, CreatedAt, UpdatedAt
    FROM cln.ConfidentialNote
    WHERE ConsultationId = @ConsultationId AND DoctorId = @DoctorId;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_SaveConfidentialNote
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER,
    @DoctorId       UNIQUEIDENTIFIER,
    @NoteText       NVARCHAR(MAX)
AS BEGIN
    SET NOCOUNT ON;
    MERGE cln.ConfidentialNote AS tgt
    USING (SELECT @ConsultationId AS ConsultationId) AS src
        ON tgt.ConsultationId = src.ConsultationId AND tgt.DoctorId = @DoctorId
    WHEN MATCHED THEN UPDATE SET NoteText = @NoteText, UpdatedAt = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (HospitalId, ConsultationId, DoctorId, NoteText)
        VALUES (@HospitalId, @ConsultationId, @DoctorId, @NoteText);

    SELECT ConfidentialNoteId, NoteText, UpdatedAt
    FROM cln.ConfidentialNote WHERE ConsultationId = @ConsultationId AND DoctorId = @DoctorId;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_CreateAddendum
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER,
    @DoctorId       UNIQUEIDENTIFIER,
    @Category       NVARCHAR(30) = NULL,
    @AddendumText   NVARCHAR(MAX)
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (AddendumId UNIQUEIDENTIFIER, CreatedAt DATETIMEOFFSET(3));
    -- Append-only, matching schema comment on cln.ConsultationAddendum — never updated.
    INSERT INTO cln.ConsultationAddendum (HospitalId, ConsultationId, DoctorId, Category, AddendumText)
    OUTPUT inserted.AddendumId, inserted.CreatedAt
    INTO @Out
    VALUES (@HospitalId, @ConsultationId, @DoctorId, @Category, @AddendumText);
    SELECT AddendumId, CreatedAt FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListAddendums
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT AddendumId, ConsultationId, DoctorId, Category, AddendumText, CreatedAt
    FROM cln.ConsultationAddendum WHERE ConsultationId = @ConsultationId
    ORDER BY CreatedAt;
END;
GO

-- ============================================================
-- 16. BILLING (bil)
-- ============================================================

-- ---- Fee schedule ----

CREATE OR ALTER PROCEDURE bil.usp_CheckEffectiveFee
    @DoctorId       UNIQUEIDENTIFIER,
    @ConsultationType NVARCHAR(10),
    @OnDate         DATE = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- Backs both api.js checkEffectiveFee and getDoctorFee (same data.js impl,
    -- getEffectiveFee, called with different argument sets).
    DECLARE @Date DATE = ISNULL(@OnDate, cfg.fn_HospitalToday((SELECT HospitalId FROM sec.ApplicationUser WHERE UserId = @DoctorId)));
    SELECT TOP (1) FeeScheduleId, DoctorId, ConsultationType, FeeAmount, Currency,
           EffectiveFrom, EffectiveTo
    FROM bil.DoctorFeeSchedule
    WHERE DoctorId = @DoctorId AND ConsultationType = @ConsultationType
      AND EffectiveFrom <= @Date AND (EffectiveTo IS NULL OR EffectiveTo >= @Date)
      AND IsActive = 1
    ORDER BY EffectiveFrom DESC;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_ListDoctorFees
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT FeeScheduleId, DoctorId, ConsultationType, FeeAmount, Currency, EffectiveFrom, EffectiveTo
    FROM bil.DoctorFeeSchedule
    WHERE HospitalId = @HospitalId AND IsActive = 1;
END;
GO

-- ---- Service charge master ----

CREATE OR ALTER PROCEDURE bil.usp_ListServiceCharges
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT sc.ServiceChargeId, sc.ServiceCode, sc.ServiceName, sc.Category, sc.DefaultAmount, sc.TaxPercent,
           h.Currency   -- v4.0: priced in the hospital's currency
    FROM bil.ServiceChargeMaster sc
    INNER JOIN cfg.Hospital h ON h.HospitalId = sc.HospitalId
    WHERE sc.HospitalId = @HospitalId AND sc.IsActive = 1
    ORDER BY sc.ServiceName;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_GetServiceCharge
    @ServiceChargeId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT sc.ServiceChargeId, sc.ServiceCode, sc.ServiceName, sc.Category, sc.DefaultAmount, sc.TaxPercent,
           h.Currency   -- v4.0: priced in the hospital's currency
    FROM bil.ServiceChargeMaster sc
    INNER JOIN cfg.Hospital h ON h.HospitalId = sc.HospitalId WHERE sc.ServiceChargeId = @ServiceChargeId;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_SearchServiceCharges
    @Term           NVARCHAR(100),
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT sc.ServiceChargeId, sc.ServiceCode, sc.ServiceName, sc.Category, sc.DefaultAmount, sc.TaxPercent,
           h.Currency   -- v4.0: priced in the hospital's currency
    FROM bil.ServiceChargeMaster sc
    INNER JOIN cfg.Hospital h ON h.HospitalId = sc.HospitalId
    WHERE sc.HospitalId = @HospitalId AND sc.IsActive = 1
      AND (sc.ServiceName LIKE '%' + @Term + '%' OR sc.ServiceCode LIKE '%' + @Term + '%')
    ORDER BY sc.ServiceName;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_CreateServiceCharge
    @HospitalId     UNIQUEIDENTIFIER,
    @ServiceCode    NVARCHAR(20),
    @ServiceName    NVARCHAR(200),
    @Category       NVARCHAR(20),
    @DefaultAmount  DECIMAL(10,2),
    @TaxPercent     DECIMAL(5,2) = 0,
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (ServiceChargeId UNIQUEIDENTIFIER, ServiceName NVARCHAR(200), DefaultAmount DECIMAL(10,2));
    INSERT INTO bil.ServiceChargeMaster (HospitalId, ServiceCode, ServiceName, Category,
                                          DefaultAmount, TaxPercent, CreatedBy)
    OUTPUT inserted.ServiceChargeId, inserted.ServiceName, inserted.DefaultAmount
    INTO @Out
    VALUES (@HospitalId, @ServiceCode, @ServiceName, @Category, @DefaultAmount, @TaxPercent, ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));
    SELECT ServiceChargeId, ServiceName, DefaultAmount FROM @Out;
END;
GO

-- ---- Invoices ----

CREATE OR ALTER PROCEDURE bil.usp_CreateInvoice
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER = NULL,
    @PatientId      UNIQUEIDENTIFIER,
    @DoctorId       UNIQUEIDENTIFIER = NULL,
    @ConsultationType NVARCHAR(10) = NULL,
    @IsFreeFollowup BIT = 0,
    @ChargeNote     NVARCHAR(300) = NULL,
    @Currency       NVARCHAR(5) = 'USD',
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (InvoiceId UNIQUEIDENTIFIER, InvoiceNumber NVARCHAR(30), Status NVARCHAR(10), CreatedAt DATETIMEOFFSET(3));
    DECLARE @InvoiceNumber NVARCHAR(30);
    SELECT @InvoiceNumber = 'INV-' + RIGHT('000000' + CAST(ISNULL(MAX(Id), 0) + 1 AS NVARCHAR), 6)
    FROM bil.Invoice WHERE HospitalId = @HospitalId;

    INSERT INTO bil.Invoice (HospitalId, InvoiceNumber, ConsultationId, PatientId,
                              DoctorId, ConsultationType, IsFreeFollowup, ChargeNote, Currency, TotalAmount, CreatedBy)
    OUTPUT inserted.InvoiceId, inserted.InvoiceNumber, inserted.Status, inserted.CreatedAt
    INTO @Out
    VALUES (@HospitalId, @InvoiceNumber, @ConsultationId, @PatientId,
            @DoctorId, @ConsultationType, @IsFreeFollowup, @ChargeNote, @Currency, 0, ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));
    SELECT InvoiceId, InvoiceNumber, Status, CreatedAt FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_GetInvoiceForConsultation
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT TOP (1) InvoiceId, InvoiceNumber, PatientId, DoctorId, Currency, SubTotal,
           DiscountAmount, TaxAmount, TotalAmount, PaidAmount, Status, CreatedAt
    FROM bil.Invoice
    WHERE ConsultationId = @ConsultationId AND Status <> 'CANCELLED' AND IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_ListInvoices
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT InvoiceId, InvoiceNumber, ConsultationId, PatientId, DoctorId, ConsultationType,
           IsFreeFollowup, ChargeNote, Currency, SubTotal, DiscountAmount, TaxAmount, TotalAmount,
           PaidAmount, CoveredAmount, PatientLiability, PatientPaidAmount, Status, CreatedAt
    FROM bil.Invoice
    WHERE HospitalId = @HospitalId AND IsActive = 1
    ORDER BY CreatedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_GetInvoice
    @InvoiceId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT InvoiceId, InvoiceNumber, ConsultationId, PatientId, DoctorId, ConsultationType,
           IsFreeFollowup, ChargeNote, Currency, SubTotal, DiscountAmount, TaxAmount, TotalAmount,
           PaidAmount, CoveredAmount, PatientLiability, PatientPaidAmount, Status, CreatedAt
    FROM bil.Invoice WHERE InvoiceId = @InvoiceId AND IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_UpdateInvoice
    @InvoiceId      UNIQUEIDENTIFIER,
    @Status         NVARCHAR(10) = NULL,
    @SubTotal       DECIMAL(10,2) = NULL,
    @DiscountAmount DECIMAL(10,2) = NULL,
    @TaxAmount      DECIMAL(10,2) = NULL,
    @TotalAmount    DECIMAL(10,2) = NULL,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    UPDATE bil.Invoice SET
        Status = ISNULL(@Status, Status), SubTotal = ISNULL(@SubTotal, SubTotal),
        DiscountAmount = ISNULL(@DiscountAmount, DiscountAmount), TaxAmount = ISNULL(@TaxAmount, TaxAmount),
        TotalAmount = ISNULL(@TotalAmount, TotalAmount), ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE InvoiceId = @InvoiceId AND IsActive = 1;

    SELECT InvoiceId, Status, TotalAmount, PaidAmount FROM bil.Invoice WHERE InvoiceId = @InvoiceId;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_CancelInvoice
    @InvoiceId      UNIQUEIDENTIFIER,
    @Reason         NVARCHAR(300),
    @IssuedBy       UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (CreditNoteId UNIQUEIDENTIFIER, CreditNoteNumber NVARCHAR(30), Amount DECIMAL(10,2));
    DECLARE @HospitalId UNIQUEIDENTIFIER, @Amount DECIMAL(10,2);
    SELECT @HospitalId = HospitalId, @Amount = TotalAmount
    FROM bil.Invoice WHERE InvoiceId = @InvoiceId;

    UPDATE bil.Invoice SET Status = 'CANCELLED', ModifiedAt = SYSUTCDATETIME()
    WHERE InvoiceId = @InvoiceId;

    DECLARE @CnNumber NVARCHAR(30);
    SELECT @CnNumber = 'CN-' + RIGHT('000000' + CAST(ISNULL(MAX(Id), 0) + 1 AS NVARCHAR), 6)
    FROM bil.CreditNote WHERE HospitalId = @HospitalId;

    INSERT INTO bil.CreditNote (HospitalId, CreditNoteNumber, InvoiceId, Amount, Reason, IssuedBy)
    OUTPUT inserted.CreditNoteId, inserted.CreditNoteNumber, inserted.Amount
    INTO @Out
    VALUES (@HospitalId, @CnNumber, @InvoiceId, @Amount, @Reason, @IssuedBy);
    SELECT CreditNoteId, CreditNoteNumber, Amount FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_ListCreditNotes
    @InvoiceId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT CreditNoteId, CreditNoteNumber, InvoiceId, Amount, Reason, IssuedBy, IssuedAt
    FROM bil.CreditNote WHERE InvoiceId = @InvoiceId;
END;
GO

-- ---- Invoice line items ----

CREATE OR ALTER PROCEDURE bil.usp_CreateInvoiceLineItem
    @InvoiceId      UNIQUEIDENTIFIER,
    @ServiceChargeId UNIQUEIDENTIFIER = NULL,
    @InventoryItemId UNIQUEIDENTIFIER = NULL,      -- set instead for a stocked item (inv.InventoryItem)
    @Description    NVARCHAR(200),
    @Category       NVARCHAR(20),
    @Qty            INT = 1,
    @UnitAmount     DECIMAL(10,2),
    @DiscountPercent DECIMAL(5,2) = 0,
    @TaxPercent     DECIMAL(5,2) = 0
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (LineItemId UNIQUEIDENTIFIER, [Description] NVARCHAR(200), LineTotal DECIMAL(10,2));
    DECLARE @Subtotal DECIMAL(10,2) = @Qty * @UnitAmount;
    DECLARE @DiscAmt DECIMAL(10,2) = ROUND(@Subtotal * @DiscountPercent / 100, 2);
    DECLARE @AfterDisc DECIMAL(10,2) = @Subtotal - @DiscAmt;
    DECLARE @TaxAmt DECIMAL(10,2) = ROUND(@AfterDisc * @TaxPercent / 100, 2);
    DECLARE @LineTotal DECIMAL(10,2) = @AfterDisc + @TaxAmt;
    DECLARE @Sort INT;
    SELECT @Sort = ISNULL(MAX(SortOrder), 0) + 1 FROM bil.InvoiceLineItem WHERE InvoiceId = @InvoiceId;

    INSERT INTO bil.InvoiceLineItem (InvoiceId, ServiceChargeId, InventoryItemId, [Description], Category,
                                      Qty, UnitAmount, DiscountPercent, DiscountAmount,
                                      TaxPercent, TaxAmount, LineTotal, SortOrder)
    OUTPUT inserted.LineItemId, inserted.[Description], inserted.LineTotal
    INTO @Out
    VALUES (@InvoiceId, @ServiceChargeId, @InventoryItemId, @Description, @Category,
            @Qty, @UnitAmount, @DiscountPercent, @DiscAmt,
            @TaxPercent, @TaxAmt, @LineTotal, @Sort);
    SELECT LineItemId, [Description], LineTotal FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_ListInvoiceLineItems
    @InvoiceId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT LineItemId, InvoiceId, ServiceChargeId, InventoryItemId, [Description], Category, Qty, UnitAmount,
           DiscountPercent, DiscountAmount, TaxPercent, TaxAmount, LineTotal, SortOrder
    FROM bil.InvoiceLineItem WHERE InvoiceId = @InvoiceId ORDER BY SortOrder;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_DeleteInvoiceLineItem
    @LineItemId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- bil.InvoiceLineItem has no IsActive column — hard delete matches
    -- data.js splice() and is schema-correct (no soft-delete column to set).
    DELETE FROM bil.InvoiceLineItem WHERE LineItemId = @LineItemId;
END;
GO

-- Defined here, ahead of their callers (usp_RecalculateInvoiceTotals, usp_CreatePayment,
-- usp_UpdatePaymentVoid and the coverage-line procedures below), so the script runs without
-- "depends on the missing object" warnings.

-- Re-derives coverage liabilities (priority order, capped to the charge), the patient share,
-- paid amounts and status. Called after any change to line items, coverage lines or payments.
CREATE OR ALTER PROCEDURE bil.usp_RecalculateInvoiceSplit
    @InvoiceId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Charge DECIMAL(10,2) = (SELECT TotalAmount FROM bil.Invoice WHERE InvoiceId = @InvoiceId);
    IF @Charge IS NULL RETURN;

    -- Liability per line in priority order: PERCENT follows the charge, AMOUNT (or a manual
    -- override) stays fixed; each is capped to what earlier lines left of the charge.
    ;WITH raw AS (
        SELECT CoverageLineId, Priority, Id,
               CASE WHEN ApprovedType = 'PERCENT' AND IsManualAmount = 0 THEN ROUND(@Charge * ApprovedValue / 100, 2)
                    WHEN IsManualAmount = 1 THEN Liability
                    ELSE ApprovedValue END AS Wanted
        FROM bil.InvoiceCoverageLine WHERE InvoiceId = @InvoiceId AND Status = 'VERIFIED'
    ), ordered AS (
        SELECT CoverageLineId, Wanted,
               ISNULL(SUM(Wanted) OVER (ORDER BY Priority, Id ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS WantedBefore
        FROM raw
    )
    UPDATE l SET Liability = CASE WHEN o.WantedBefore >= @Charge THEN 0
                                  WHEN o.WantedBefore + o.Wanted > @Charge THEN @Charge - o.WantedBefore
                                  ELSE o.Wanted END
    FROM bil.InvoiceCoverageLine l INNER JOIN ordered o ON o.CoverageLineId = l.CoverageLineId;

    UPDATE l SET SettledAmount = ISNULL((SELECT SUM(p.Amount) FROM bil.Payment p
                                         WHERE p.CoverageLineId = l.CoverageLineId AND p.IsVoided = 0), 0)
    FROM bil.InvoiceCoverageLine l WHERE l.InvoiceId = @InvoiceId;

    DECLARE @Covered DECIMAL(10,2) = ISNULL((SELECT SUM(Liability) FROM bil.InvoiceCoverageLine WHERE InvoiceId = @InvoiceId AND Status = 'VERIFIED'), 0);
    DECLARE @PatientPaid DECIMAL(10,2) = ISNULL((SELECT SUM(Amount) FROM bil.Payment WHERE InvoiceId = @InvoiceId AND IsVoided = 0 AND ISNULL(ComponentType, 'PATIENT') = 'PATIENT'), 0);
    DECLARE @Paid DECIMAL(10,2) = ISNULL((SELECT SUM(Amount) FROM bil.Payment WHERE InvoiceId = @InvoiceId AND IsVoided = 0), 0);
    DECLARE @PatientLiability DECIMAL(10,2) = CASE WHEN @Charge > @Covered THEN @Charge - @Covered ELSE 0 END;
    DECLARE @HasLines BIT = CASE WHEN EXISTS (SELECT 1 FROM bil.InvoiceLineItem WHERE InvoiceId = @InvoiceId) THEN 1 ELSE 0 END;

    UPDATE bil.Invoice SET
        CoveredAmount = @Covered, PatientLiability = @PatientLiability,
        PatientPaidAmount = @PatientPaid, PaidAmount = @Paid,
        Status = CASE
            WHEN Status = 'CANCELLED' THEN 'CANCELLED'
            WHEN @Charge = 0 AND @HasLines = 1 THEN 'PAID'
            WHEN @Charge > 0 AND (@Paid >= @Charge OR (@PatientPaid >= @PatientLiability AND (@Covered > 0 OR @PatientPaid > 0))) THEN 'PAID'
            WHEN @Covered > 0 OR @Paid > 0 THEN 'PARTIAL'
            ELSE 'PENDING' END,
        ModifiedAt = SYSUTCDATETIME()
    WHERE InvoiceId = @InvoiceId;
END;
GO

-- Result set 1: totals (charge, insurance, govt scheme, patient share / paid / due, payer outstanding).
-- Result set 2: coverage lines.
CREATE OR ALTER PROCEDURE bil.usp_GetInvoiceBillingSplit
    @InvoiceId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT i.InvoiceId, i.Currency, i.Status, i.TotalAmount AS GrossAmount,
           ISNULL((SELECT SUM(Liability) FROM bil.InvoiceCoverageLine WHERE InvoiceId = i.InvoiceId AND CoverageType = 'PRIVATE_INSURANCE'), 0) AS InsuranceLiability,
           ISNULL((SELECT SUM(Liability) FROM bil.InvoiceCoverageLine WHERE InvoiceId = i.InvoiceId AND CoverageType = 'GOVT_SCHEME'), 0) AS GovtLiability,
           i.CoveredAmount, i.PatientLiability, i.PatientPaidAmount AS PatientPaid,
           CASE WHEN i.PatientLiability > i.PatientPaidAmount THEN i.PatientLiability - i.PatientPaidAmount ELSE 0 END AS PatientDue,
           CASE WHEN i.PatientPaidAmount > i.PatientLiability THEN i.PatientPaidAmount - i.PatientLiability ELSE 0 END AS PatientOverpaid,
           ISNULL((SELECT SUM(CASE WHEN Liability > SettledAmount THEN Liability - SettledAmount ELSE 0 END)
                   FROM bil.InvoiceCoverageLine WHERE InvoiceId = i.InvoiceId), 0) AS PayerOutstanding
    FROM bil.Invoice i WHERE i.InvoiceId = @InvoiceId;

    SELECT l.CoverageLineId, l.InvoiceId, l.CoverageId, l.PayerId, py.Name AS PayerName, l.CoverageType, l.Priority,
           l.AuthorizationNumber, l.ApprovedType, l.ApprovedValue, l.IsManualAmount, l.Liability, l.SettledAmount, l.Status
    FROM bil.InvoiceCoverageLine l INNER JOIN cfg.Payer py ON py.PayerId = l.PayerId
    WHERE l.InvoiceId = @InvoiceId ORDER BY l.Priority;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_RecalculateInvoiceTotals
    @InvoiceId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    UPDATE bil.Invoice SET
        SubTotal = (SELECT ISNULL(SUM(Qty * UnitAmount), 0) FROM bil.InvoiceLineItem WHERE InvoiceId = @InvoiceId),
        DiscountAmount = (SELECT ISNULL(SUM(DiscountAmount), 0) FROM bil.InvoiceLineItem WHERE InvoiceId = @InvoiceId),
        TaxAmount = (SELECT ISNULL(SUM(TaxAmount), 0) FROM bil.InvoiceLineItem WHERE InvoiceId = @InvoiceId),
        TotalAmount = (SELECT ISNULL(SUM(LineTotal), 0) FROM bil.InvoiceLineItem WHERE InvoiceId = @InvoiceId),
        ModifiedAt = SYSUTCDATETIME()
    WHERE InvoiceId = @InvoiceId;

    -- percent coverages follow the new total; fixed amounts are re-capped; status re-derived
    EXEC bil.usp_RecalculateInvoiceSplit @InvoiceId = @InvoiceId;

    SELECT InvoiceId, SubTotal, DiscountAmount, TaxAmount, TotalAmount,
           CoveredAmount, PatientLiability, PatientPaidAmount, PaidAmount, Status
    FROM bil.Invoice WHERE InvoiceId = @InvoiceId;
END;
GO

-- ---- Payments & receipts ----

CREATE OR ALTER PROCEDURE bil.usp_CreatePayment
    @HospitalId     UNIQUEIDENTIFIER,
    @InvoiceId      UNIQUEIDENTIFIER,
    @ComponentType  NVARCHAR(15) = 'PATIENT',      -- PATIENT | INSURANCE (payer settlement)
    @CoverageLineId UNIQUEIDENTIFIER = NULL,       -- required for INSURANCE: the claim being settled
    @PaymentMethod  NVARCHAR(30),
    @Amount         DECIMAL(10,2),
    @Reference      NVARCHAR(100) = NULL,
    @ReceivedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (TransactionId UNIQUEIDENTIFIER, TransactionNumber NVARCHAR(30), Amount DECIMAL(10,2), PaidAt DATETIMEOFFSET(3));
    IF @ComponentType = 'INSURANCE' AND NOT EXISTS (
        SELECT 1 FROM bil.InvoiceCoverageLine WHERE CoverageLineId = @CoverageLineId AND InvoiceId = @InvoiceId)
        THROW 50120, 'An insurance settlement must reference a coverage line on this invoice.', 1;

    INSERT INTO bil.Payment (HospitalId, InvoiceId, TransactionNumber, ComponentType, CoverageLineId,
                              PaymentMethod, Amount, Reference, ReceivedBy)
    OUTPUT inserted.TransactionId, inserted.TransactionNumber, inserted.Amount, inserted.PaidAt
    INTO @Out
    SELECT @HospitalId, @InvoiceId,
           'TXN-' + RIGHT('000000' + CAST(ISNULL(MAX(Id), 0) + 1 AS NVARCHAR), 6),
           @ComponentType, CASE WHEN @ComponentType = 'INSURANCE' THEN @CoverageLineId END,
           @PaymentMethod, @Amount, @Reference, @ReceivedBy
    FROM bil.Payment WHERE HospitalId = @HospitalId;
    SELECT TransactionId, TransactionNumber, Amount, PaidAt FROM @Out;

    EXEC bil.usp_RecalculateInvoiceSplit @InvoiceId = @InvoiceId;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_ListPaymentsForInvoice
    @InvoiceId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT TransactionId, InvoiceId, TransactionNumber, ComponentType, PaymentMethod,
           Amount, Reference, PaidAt, IsVoided, VoidReason
    FROM bil.Payment WHERE InvoiceId = @InvoiceId ORDER BY PaidAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_ListPayments
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT TransactionId, InvoiceId, TransactionNumber, ComponentType, PaymentMethod,
           Amount, Reference, PaidAt, IsVoided
    FROM bil.Payment WHERE HospitalId = @HospitalId ORDER BY PaidAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_UpdatePaymentVoid
    @TransactionId  UNIQUEIDENTIFIER,
    @VoidReason     NVARCHAR(300),
    @VoidedBy       UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @InvoiceId UNIQUEIDENTIFIER;
    SELECT @InvoiceId = InvoiceId FROM bil.Payment WHERE TransactionId = @TransactionId;

    UPDATE bil.Payment SET IsVoided = 1, VoidedAt = SYSUTCDATETIME(),
                           VoidedBy = @VoidedBy, VoidReason = @VoidReason
    WHERE TransactionId = @TransactionId AND IsVoided = 0;

    IF @InvoiceId IS NOT NULL
        EXEC bil.usp_RecalculateInvoiceSplit @InvoiceId = @InvoiceId;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_CreateReceipt
    @HospitalId     UNIQUEIDENTIFIER,
    @InvoiceId      UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER = NULL,
    @TotalAmount    DECIMAL(10,2),
    @Currency       NVARCHAR(5) = 'USD'
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (ReceiptId UNIQUEIDENTIFIER, ReceiptNumber NVARCHAR(30), GeneratedAt DATETIMEOFFSET(3));
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='PRINT', EntityType='Receipt') on print
    -- v4.0: Receipt.PaymentId dropped — a receipt belongs to the invoice (Receipt model).
    DECLARE @ReceiptNumber NVARCHAR(30);
    SELECT @ReceiptNumber = 'RCP-' + RIGHT('000000' + CAST(ISNULL(MAX(Id), 0) + 1 AS NVARCHAR), 6)
    FROM bil.Receipt WHERE HospitalId = @HospitalId;

    INSERT INTO bil.Receipt (HospitalId, ReceiptNumber, InvoiceId, PatientId, TotalAmount, Currency)
    OUTPUT inserted.ReceiptId, inserted.ReceiptNumber, inserted.GeneratedAt
    INTO @Out
    VALUES (@HospitalId, @ReceiptNumber, @InvoiceId, @PatientId, @TotalAmount, @Currency);
    SELECT ReceiptId, ReceiptNumber, GeneratedAt FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_GetReceiptForInvoice
    @InvoiceId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT ReceiptId, ReceiptNumber, InvoiceId, PatientId, TotalAmount, Currency, GeneratedAt
    FROM bil.Receipt WHERE InvoiceId = @InvoiceId AND IsActive = 1;
END;
GO

-- ---- Coverage & N-way bill split (ported from MediSlot billing) ----
-- Charge (Invoice.TotalAmount) = Σ verified coverage liabilities + Patient share.
-- Self-Pay (no coverage), Private only, Govt scheme only, and Private + Govt together.


CREATE OR ALTER PROCEDURE cfg.usp_ListPayers
    @Country        NVARCHAR(5),
    @Category       NVARCHAR(15) = NULL      -- PRIVATE | GOVT_SCHEME | NULL = both
AS BEGIN
    SET NOCOUNT ON;
    SELECT PayerId, Name, Category, Country
    FROM cfg.Payer
    WHERE Country = @Country AND (@Category IS NULL OR Category = @Category) AND IsActive = 1
    ORDER BY Category, Name;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_ListPatientCoverages
    @PatientId       UNIQUEIDENTIFIER,
    @IncludeInactive BIT = 0
AS BEGIN
    SET NOCOUNT ON;
    SELECT c.CoverageId, c.HospitalId, c.PatientId, c.CoverageType, c.PayerId, py.Name AS PayerName, py.Category AS PayerCategory,
           c.PlanName, c.MemberId, c.PolicyNumber, c.GroupNumber, c.PolicyHolderName, c.Relationship, c.ClaimMode,
           c.GovtCardNumber, c.EffectiveDate, c.ExpiryDate, c.Status, c.Priority, c.IsActive,
           v.Status AS LastVerificationStatus, v.VerifiedAt AS LastVerifiedAt
    FROM opd.PatientCoverage c
    INNER JOIN cfg.Payer py ON py.PayerId = c.PayerId
    OUTER APPLY (SELECT TOP 1 Status, VerifiedAt FROM bil.EligibilityVerification ev
                 WHERE ev.CoverageId = c.CoverageId ORDER BY ev.VerifiedAt DESC) v
    WHERE c.PatientId = @PatientId AND (@IncludeInactive = 1 OR c.IsActive = 1)
    ORDER BY c.Priority;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_SavePatientCoverage
    @CoverageId       UNIQUEIDENTIFIER = NULL,   -- NULL = add
    @HospitalId       UNIQUEIDENTIFIER,
    @PatientId        UNIQUEIDENTIFIER,
    @PayerId          UNIQUEIDENTIFIER,
    @CoverageType     NVARCHAR(20),
    @PlanName         NVARCHAR(100) = NULL,
    @MemberId         NVARCHAR(50) = NULL,
    @PolicyNumber     NVARCHAR(50) = NULL,
    @GroupNumber      NVARCHAR(50) = NULL,
    @PolicyHolderName NVARCHAR(200) = NULL,
    @Relationship     NVARCHAR(20) = NULL,
    @ClaimMode        NVARCHAR(15) = NULL,
    @GovtCardNumber   NVARCHAR(50) = NULL,
    @EffectiveDate    DATE = NULL,
    @ExpiryDate       DATE = NULL,
    @Status           NVARCHAR(10) = 'ACTIVE',
    @UserId           UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (CoverageId UNIQUEIDENTIFIER, Priority INT);
    DECLARE @Out2 TABLE (CoverageId UNIQUEIDENTIFIER, Priority INT);
    IF @CoverageId IS NOT NULL
        BEGIN
            UPDATE opd.PatientCoverage SET PayerId = @PayerId, CoverageType = @CoverageType, PlanName = @PlanName,
                MemberId = @MemberId, PolicyNumber = @PolicyNumber, GroupNumber = @GroupNumber,
                PolicyHolderName = @PolicyHolderName, Relationship = @Relationship, ClaimMode = @ClaimMode,
                GovtCardNumber = @GovtCardNumber,
                EffectiveDate = @EffectiveDate, ExpiryDate = @ExpiryDate, Status = @Status,
                ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
            OUTPUT inserted.CoverageId, inserted.Priority
            INTO @Out
            WHERE CoverageId = @CoverageId;
            SELECT CoverageId, Priority FROM @Out;
        END
        
    ELSE
        BEGIN
            INSERT INTO opd.PatientCoverage (HospitalId, PatientId, PayerId, CoverageType, PlanName, MemberId, PolicyNumber,
                GroupNumber, PolicyHolderName, Relationship, ClaimMode, GovtCardNumber,
                EffectiveDate, ExpiryDate, Status, Priority, CreatedBy)
            OUTPUT inserted.CoverageId, inserted.Priority
            INTO @Out2
            SELECT @HospitalId, @PatientId, @PayerId, @CoverageType, @PlanName, @MemberId, @PolicyNumber,
                @GroupNumber, @PolicyHolderName, @Relationship, @ClaimMode, @GovtCardNumber,
                @EffectiveDate, @ExpiryDate, @Status,
                (SELECT COUNT(*) + 1 FROM opd.PatientCoverage WHERE PatientId = @PatientId), ISNULL(@UserId, '00000000-0000-0000-0000-000000000000');
            SELECT CoverageId, Priority FROM @Out2;
        END
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_DeactivatePatientCoverage
    @CoverageId     UNIQUEIDENTIFIER,
    @UserId         UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    UPDATE opd.PatientCoverage SET IsActive = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE CoverageId = @CoverageId;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_ListEligibilityVerifications
    @CoverageId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT VerificationId, CoverageId, InvoiceId, Status, AuthorizationNumber, ApprovedType, ApprovedValue,
           Notes, VerifiedBy, VerifiedAt
    FROM bil.EligibilityVerification WHERE CoverageId = @CoverageId
    ORDER BY VerifiedAt;
END;
GO

-- Records the payer's eligibility response for an invoice. VERIFIED applies (or re-applies) the
-- coverage as a liability line; PENDING / REJECTED removes it (refused once the payer has settled).
CREATE OR ALTER PROCEDURE bil.usp_ApplyCoverageToInvoice
    @InvoiceId           UNIQUEIDENTIFIER,
    @CoverageId          UNIQUEIDENTIFIER,
    @Status              NVARCHAR(10),            -- VERIFIED | PENDING | REJECTED
    @AuthorizationNumber NVARCHAR(50) = NULL,
    @ApprovedType        NVARCHAR(10) = 'PERCENT', -- PERCENT | AMOUNT
    @ApprovedValue       DECIMAL(10,2) = 0,
    @Notes               NVARCHAR(300) = NULL,
    @VerifiedBy          UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM bil.Invoice WHERE InvoiceId = @InvoiceId AND Status = 'CANCELLED')
        THROW 50121, 'Invoice is cancelled.', 1;
    IF @Status <> 'VERIFIED' AND EXISTS (SELECT 1 FROM bil.InvoiceCoverageLine
                                         WHERE InvoiceId = @InvoiceId AND CoverageId = @CoverageId AND SettledAmount > 0)
        THROW 50122, 'The payer has already settled against this coverage.', 1;

    BEGIN TRAN;
    INSERT INTO bil.EligibilityVerification (CoverageId, InvoiceId, Status, AuthorizationNumber, ApprovedType, ApprovedValue, Notes, VerifiedBy)
    VALUES (@CoverageId, @InvoiceId, @Status, @AuthorizationNumber, @ApprovedType, @ApprovedValue, @Notes, @VerifiedBy);

    IF @Status = 'VERIFIED'
        MERGE bil.InvoiceCoverageLine AS t
        USING (SELECT c.CoverageId, c.PayerId, c.CoverageType, c.Priority FROM opd.PatientCoverage c WHERE c.CoverageId = @CoverageId) AS src
           ON t.InvoiceId = @InvoiceId AND t.CoverageId = src.CoverageId
        WHEN MATCHED THEN UPDATE SET AuthorizationNumber = @AuthorizationNumber, ApprovedType = @ApprovedType,
             ApprovedValue = @ApprovedValue, IsManualAmount = 0, Priority = src.Priority,
             ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @VerifiedBy
        WHEN NOT MATCHED THEN INSERT (InvoiceId, CoverageId, PayerId, CoverageType, Priority, AuthorizationNumber, ApprovedType, ApprovedValue, CreatedBy)
             VALUES (@InvoiceId, src.CoverageId, src.PayerId, src.CoverageType, src.Priority, @AuthorizationNumber, @ApprovedType, @ApprovedValue, @VerifiedBy);
    ELSE
        DELETE FROM bil.InvoiceCoverageLine WHERE InvoiceId = @InvoiceId AND CoverageId = @CoverageId;

    EXEC bil.usp_RecalculateInvoiceSplit @InvoiceId = @InvoiceId;
    COMMIT;
    EXEC bil.usp_GetInvoiceBillingSplit @InvoiceId = @InvoiceId;
END;
GO

-- Staff override of an applied liability (still capped to the charge by the recalculation).
CREATE OR ALTER PROCEDURE bil.usp_EditInvoiceCoverageLine
    @CoverageLineId UNIQUEIDENTIFIER,
    @Liability      DECIMAL(10,2),
    @UserId         UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @InvoiceId UNIQUEIDENTIFIER = (SELECT InvoiceId FROM bil.InvoiceCoverageLine WHERE CoverageLineId = @CoverageLineId);
    UPDATE bil.InvoiceCoverageLine
    SET Liability = CASE WHEN @Liability < 0 THEN 0 ELSE @Liability END, IsManualAmount = 1,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE CoverageLineId = @CoverageLineId;
    EXEC bil.usp_RecalculateInvoiceSplit @InvoiceId = @InvoiceId;
    EXEC bil.usp_GetInvoiceBillingSplit @InvoiceId = @InvoiceId;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_RemoveInvoiceCoverageLine
    @CoverageLineId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @InvoiceId UNIQUEIDENTIFIER = (SELECT InvoiceId FROM bil.InvoiceCoverageLine WHERE CoverageLineId = @CoverageLineId);
    IF EXISTS (SELECT 1 FROM bil.InvoiceCoverageLine WHERE CoverageLineId = @CoverageLineId AND SettledAmount > 0)
        THROW 50122, 'The payer has already settled against this coverage.', 1;
    DELETE FROM bil.InvoiceCoverageLine WHERE CoverageLineId = @CoverageLineId;
    EXEC bil.usp_RecalculateInvoiceSplit @InvoiceId = @InvoiceId;
    EXEC bil.usp_GetInvoiceBillingSplit @InvoiceId = @InvoiceId;
END;
GO


-- ============================================================
-- 17. REPORTS
-- ============================================================

CREATE OR ALTER PROCEDURE cln.usp_ListConsultationVolumeReport
    @HospitalId     UNIQUEIDENTIFIER,
    @FromDate       DATE,
    @ToDate         DATE
AS BEGIN
    SET NOCOUNT ON;
    SELECT VisitLocalDate AS [Date], COUNT(*) AS ConsultationCount
    FROM cln.Consultation
    WHERE HospitalId = @HospitalId AND VisitLocalDate BETWEEN @FromDate AND @ToDate AND IsActive = 1
    GROUP BY VisitLocalDate
    ORDER BY VisitLocalDate;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListDiagnosisFrequencyReport
    @HospitalId     UNIQUEIDENTIFIER,
    @FromDate       DATE,
    @ToDate         DATE
AS BEGIN
    SET NOCOUNT ON;
    SELECT dx.Icd10Code, dx.Icd10Description, COUNT(*) AS DiagnosisCount
    FROM cln.ConsultationDiagnosis dx
    INNER JOIN cln.Consultation c ON c.ConsultationId = dx.ConsultationId
    WHERE c.HospitalId = @HospitalId AND c.VisitLocalDate BETWEEN @FromDate AND @ToDate
      AND dx.IsActive = 1 AND c.IsActive = 1
    GROUP BY dx.Icd10Code, dx.Icd10Description
    ORDER BY DiagnosisCount DESC;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListPrescribingReport
    @HospitalId     UNIQUEIDENTIFIER,
    @FromDate       DATE,
    @ToDate         DATE
AS BEGIN
    SET NOCOUNT ON;
    SELECT p.MedicineName, COUNT(*) AS PrescriptionCount
    FROM cln.Prescription p
    INNER JOIN cln.Consultation c ON c.ConsultationId = p.ConsultationId
    WHERE c.HospitalId = @HospitalId AND c.VisitLocalDate BETWEEN @FromDate AND @ToDate
      AND p.IsActive = 1 AND c.IsActive = 1
    GROUP BY p.MedicineName
    ORDER BY PrescriptionCount DESC;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_ListRevenueReport
    @HospitalId     UNIQUEIDENTIFIER,
    @FromDate       DATE,
    @ToDate         DATE
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Tz NVARCHAR(100) = cfg.fn_HospitalWindowsTz(@HospitalId);   -- @FromDate / @ToDate are hospital-local dates
    SELECT DoctorId, COUNT(*) AS InvoiceCount, SUM(TotalAmount) AS TotalInvoiced, SUM(PaidAmount) AS TotalPaid
    FROM bil.Invoice
    WHERE HospitalId = @HospitalId AND CAST(CreatedAt AT TIME ZONE @Tz AS DATE) BETWEEN @FromDate AND @ToDate AND IsActive = 1
    GROUP BY DoctorId;
END;
GO

-- ---- Reporting views (performance) ----

CREATE OR ALTER VIEW bil.vw_RevenueByServiceCategory AS
    SELECT i.HospitalId, i.Currency, li.Category,
           SUM(li.LineTotal) AS TotalRevenue,
           COUNT(DISTINCT i.InvoiceId) AS InvoiceCount,
           CAST(i.CreatedAt AT TIME ZONE tz.WindowsTimeZoneId AS DATE) AS InvoiceDate      -- hospital-local date
    FROM bil.Invoice i
    INNER JOIN bil.InvoiceLineItem li ON li.InvoiceId = i.InvoiceId
    INNER JOIN cfg.Hospital h ON h.HospitalId = i.HospitalId
    INNER JOIN cfg.TimeZone tz ON tz.TimeZoneId = h.Timezone
    WHERE i.Status IN ('PAID','PARTIAL') AND i.IsActive = 1
    GROUP BY i.HospitalId, i.Currency, li.Category, CAST(i.CreatedAt AT TIME ZONE tz.WindowsTimeZoneId AS DATE);
GO

CREATE OR ALTER VIEW bil.vw_DayEndSummary AS
    SELECT i.HospitalId, CAST(i.CreatedAt AT TIME ZONE tz.WindowsTimeZoneId AS DATE) AS BillingDate,   -- hospital-local date
           i.Currency,
           COUNT(DISTINCT i.InvoiceId) AS InvoiceCount,
           SUM(i.TotalAmount) AS TotalBilled,
           SUM(i.PaidAmount) AS TotalCollected,
           SUM(i.TotalAmount - i.PaidAmount) AS TotalOutstanding
    FROM bil.Invoice i
    INNER JOIN cfg.Hospital h ON h.HospitalId = i.HospitalId
    INNER JOIN cfg.TimeZone tz ON tz.TimeZoneId = h.Timezone
    WHERE i.Status <> 'CANCELLED' AND i.IsActive = 1
    GROUP BY i.HospitalId, CAST(i.CreatedAt AT TIME ZONE tz.WindowsTimeZoneId AS DATE), i.Currency;
GO

CREATE OR ALTER VIEW bil.vw_PaymentsByComponent AS
    SELECT p.HospitalId, CAST(p.PaidAt AT TIME ZONE tz.WindowsTimeZoneId AS DATE) AS PaymentDate,   -- hospital-local date
           p.ComponentType, p.PaymentMethod,
           SUM(p.Amount) AS TotalAmount, COUNT(*) AS PaymentCount
    FROM bil.Payment p
    INNER JOIN cfg.Hospital h ON h.HospitalId = p.HospitalId
    INNER JOIN cfg.TimeZone tz ON tz.TimeZoneId = h.Timezone
    WHERE p.IsVoided = 0 AND p.IsActive = 1
    GROUP BY p.HospitalId, CAST(p.PaidAt AT TIME ZONE tz.WindowsTimeZoneId AS DATE), p.ComponentType, p.PaymentMethod;
GO

-- ============================================================
-- 18. COMPLIANCE (cfg.Compliance, cfg.HospitalCompliance)
-- ============================================================

CREATE OR ALTER PROCEDURE cfg.usp_ListCompliances
    @Country        NVARCHAR(5)
AS BEGIN
    SET NOCOUNT ON;
    SELECT ComplianceId, ComplianceName, Country, IsMandatory, DisplayLabel, PrintLabel, SortOrder
    FROM cfg.Compliance
    WHERE IsActive = 1 AND (Country = @Country OR Country = 'ALL')
    ORDER BY SortOrder;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_ListHospitalCompliances
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT hc.HospitalComplianceId, hc.HospitalId, hc.ComplianceId, c.ComplianceName,
           c.DisplayLabel, c.PrintLabel, c.IsMandatory,
           hc.RegistrationNumber, hc.IssuedDate, hc.ExpiryDate
    FROM cfg.HospitalCompliance hc
    INNER JOIN cfg.Compliance c ON c.ComplianceId = hc.ComplianceId
    WHERE hc.HospitalId = @HospitalId AND hc.IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_SaveHospitalCompliance
    @HospitalId     UNIQUEIDENTIFIER,
    @ComplianceId   UNIQUEIDENTIFIER,
    @RegistrationNumber NVARCHAR(100),
    @IssuedDate     DATE = NULL,
    @ExpiryDate     DATE = NULL,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='UPDATE', EntityType='HospitalCompliance')
    MERGE cfg.HospitalCompliance AS tgt
    USING (SELECT @HospitalId AS HospitalId, @ComplianceId AS ComplianceId) AS src
        ON tgt.HospitalId = src.HospitalId AND tgt.ComplianceId = src.ComplianceId
    WHEN MATCHED THEN UPDATE SET
        RegistrationNumber = @RegistrationNumber, IssuedDate = @IssuedDate, ExpiryDate = @ExpiryDate,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHEN NOT MATCHED THEN
        INSERT (HospitalId, ComplianceId, RegistrationNumber, IssuedDate, ExpiryDate, CreatedBy)
        VALUES (@HospitalId, @ComplianceId, @RegistrationNumber, @IssuedDate, @ExpiryDate, @ModifiedBy);

    SELECT HospitalComplianceId, RegistrationNumber, IssuedDate, ExpiryDate
    FROM cfg.HospitalCompliance WHERE HospitalId = @HospitalId AND ComplianceId = @ComplianceId;
END;
GO

-- ============================================================
-- 19. NOTIFICATIONS / MESSAGING (cfg.MessageTemplate, msg.*)
-- ============================================================

CREATE OR ALTER PROCEDURE cfg.usp_ListMessageTemplates
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT TemplateId, HospitalId, Name, [Trigger], Channel, [Text], IsEnabled
    FROM cfg.MessageTemplate
    WHERE HospitalId = @HospitalId AND IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_UpdateMessageTemplateStatus
    @TemplateId     UNIQUEIDENTIFIER,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- data.js toggles the current value (no explicit flag passed by api.js)
    UPDATE cfg.MessageTemplate
    SET IsEnabled = CASE WHEN IsEnabled = 1 THEN 0 ELSE 1 END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE TemplateId = @TemplateId;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_UpdateMessageTemplate
    @TemplateId     UNIQUEIDENTIFIER,
    @Name           NVARCHAR(100) = NULL,
    @Trigger        NVARCHAR(30) = NULL,
    @Channel        NVARCHAR(10) = NULL,
    @Text           NVARCHAR(1000) = NULL,
    @IsEnabled      BIT = NULL,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- Partial update — mirrors data.js's Object.assign(template, patch): only
    -- overwrites fields the caller actually supplied (non-NULL). Distinct from
    -- usp_UpdateMessageTemplateStatus above, which only ever toggles IsEnabled.
    UPDATE cfg.MessageTemplate
    SET Name = COALESCE(@Name, Name),
        [Trigger] = COALESCE(@Trigger, [Trigger]),
        Channel = COALESCE(@Channel, Channel),
        [Text] = COALESCE(@Text, [Text]),
        IsEnabled = COALESCE(@IsEnabled, IsEnabled),
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE TemplateId = @TemplateId;

    SELECT TemplateId, HospitalId, Name, [Trigger], Channel, [Text], IsEnabled
    FROM cfg.MessageTemplate WHERE TemplateId = @TemplateId;
END;
GO

CREATE OR ALTER PROCEDURE msg.usp_GetNotificationConfig
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- ApiKey (ENCRYPTION: RANDOMIZED) is deliberately excluded from the read path.
    -- v4.0: ConfigJson replaced by typed columns (WHATSAPP: PhoneNumberId / BusinessAccountId;
    -- EMAIL: Smtp* / From*); IsEnabled → Enabled.
    SELECT Channel, Provider, PhoneNumberId, BusinessAccountId,
           SmtpHost, SmtpPort, FromEmail, FromName, Enabled,
           CAST(CASE WHEN ApiKey IS NULL THEN 0 ELSE 1 END AS BIT) AS HasApiKey
    FROM msg.NotificationConfig
    WHERE HospitalId = @HospitalId;
END;
GO

CREATE OR ALTER PROCEDURE msg.usp_SaveNotificationConfig
    @HospitalId     UNIQUEIDENTIFIER,
    @Channel        NVARCHAR(10),
    @Provider       NVARCHAR(20) = NULL,
    @ApiKey         NVARCHAR(500) = NULL,      -- NULL = keep the stored key
    @PhoneNumberId  NVARCHAR(50) = NULL,
    @BusinessAccountId NVARCHAR(50) = NULL,
    @SmtpHost       NVARCHAR(200) = NULL,
    @SmtpPort       INT = NULL,
    @FromEmail      NVARCHAR(100) = NULL,
    @FromName       NVARCHAR(100) = NULL,
    @Enabled        BIT = 0,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='UPDATE', EntityType='NotificationConfig')
    MERGE msg.NotificationConfig AS tgt
    USING (SELECT @HospitalId AS HospitalId, @Channel AS Channel) AS src
        ON tgt.HospitalId = src.HospitalId AND tgt.Channel = src.Channel
    WHEN MATCHED THEN UPDATE SET
        Provider = ISNULL(@Provider, Provider), ApiKey = ISNULL(@ApiKey, ApiKey),
        PhoneNumberId = @PhoneNumberId, BusinessAccountId = @BusinessAccountId,
        SmtpHost = @SmtpHost, SmtpPort = @SmtpPort, FromEmail = @FromEmail, FromName = @FromName,
        Enabled = @Enabled,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHEN NOT MATCHED THEN
        INSERT (HospitalId, Channel, Provider, ApiKey, PhoneNumberId, BusinessAccountId,
                SmtpHost, SmtpPort, FromEmail, FromName, Enabled, ModifiedAt, ModifiedBy)
        VALUES (@HospitalId, @Channel, @Provider, @ApiKey, @PhoneNumberId, @BusinessAccountId,
                @SmtpHost, @SmtpPort, @FromEmail, @FromName, @Enabled, SYSUTCDATETIME(), @ModifiedBy);
END;
GO

CREATE OR ALTER PROCEDURE msg.usp_CreateQueuedMessage
    @HospitalId     UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER = NULL,
    @Channel        NVARCHAR(10),
    @TemplateName   NVARCHAR(100) = NULL,
    @PatientName    NVARCHAR(200) = NULL,
    @PatientPhone   NVARCHAR(20) = NULL,
    @MessageText    NVARCHAR(MAX) = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (MessageId UNIQUEIDENTIFIER, Status NVARCHAR(15), QueuedAt DATETIMEOFFSET(3));
    -- Status defaults to QUEUED if the channel is enabled in msg.NotificationConfig,
    -- else SIMULATED (Phase-1 sandbox behavior preserved for demo hospitals).
    DECLARE @IsEnabled BIT;
    SELECT @IsEnabled = Enabled FROM msg.NotificationConfig
    WHERE HospitalId = @HospitalId AND Channel = @Channel;

    INSERT INTO msg.MessageQueue (HospitalId, PatientId, Channel, TemplateName,
                                   PatientName, PatientPhone, MessageText, Status)
    OUTPUT inserted.MessageId, inserted.Status, inserted.QueuedAt
    INTO @Out
    VALUES (@HospitalId, @PatientId, @Channel, @TemplateName, @PatientName, @PatientPhone,
            @MessageText, CASE WHEN ISNULL(@IsEnabled, 0) = 1 THEN 'QUEUED' ELSE 'SIMULATED' END);
    SELECT MessageId, Status, QueuedAt FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE msg.usp_SubmitQueuedMessages
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    UPDATE msg.MessageQueue
    SET Status = 'SENT', SentAt = SYSUTCDATETIME()
    WHERE HospitalId = @HospitalId AND Status IN ('QUEUED', 'SIMULATED');
END;
GO

CREATE OR ALTER PROCEDURE msg.usp_ListMessageLog
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT MessageId, PatientId, PatientName, Channel, TemplateName, Status, QueuedAt, SentAt
    FROM msg.MessageQueue WHERE HospitalId = @HospitalId
    ORDER BY QueuedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE msg.usp_GetMessageStats
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT COUNT(*) AS Total,
           SUM(CASE WHEN Status = 'SENT' THEN 1 ELSE 0 END) AS Sent,
           SUM(CASE WHEN Status = 'SIMULATED' THEN 1 ELSE 0 END) AS Simulated,
           SUM(CASE WHEN Status = 'FAILED' THEN 1 ELSE 0 END) AS Failed
    FROM msg.MessageQueue WHERE HospitalId = @HospitalId;
END;
GO

-- ============================================================
-- 20. AUDIT & DATA EXPORT (sec)
-- ============================================================

CREATE OR ALTER PROCEDURE sec.usp_RecordAuditEntry
    @HospitalId     UNIQUEIDENTIFIER,
    @UserId         UNIQUEIDENTIFIER = NULL,   -- NULL for a failed login of an unknown user
    @Action         NVARCHAR(20),
    @EntityType     NVARCHAR(50),
    @EntityId       NVARCHAR(50) = NULL,
    @Details        NVARCHAR(500) = NULL,
    @IpAddress      NVARCHAR(45) = NULL,
    @UserAgent      NVARCHAR(200) = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (AuditLogId UNIQUEIDENTIFIER, Timestamp DATETIMEOFFSET(3));
    -- Append-only (sec.AuditLog has no IsActive/ModifiedAt per architecture §9)
    -- Details must never contain PII — see architecture doc §8 rule 6 / §9 "What NOT to log"
    -- v4.0: SessionToken / Browser columns dropped (AuditLogEntry model); CreatedAt → [Timestamp].
    INSERT INTO sec.AuditLog (HospitalId, UserId, [Action], EntityType, EntityId,
                              Details, IpAddress, UserAgent)
    OUTPUT inserted.AuditLogId, inserted.[Timestamp]
    INTO @Out
    VALUES (@HospitalId, @UserId, @Action, @EntityType, @EntityId,
            @Details, @IpAddress, @UserAgent);
    SELECT AuditLogId, Timestamp FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListAuditLogs
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT AuditLogId, UserId, [Action], EntityType, EntityId, Details, IpAddress, [Timestamp]
    FROM sec.AuditLog WHERE HospitalId = @HospitalId
    ORDER BY [Timestamp] DESC;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListAuditLogsFiltered
    @HospitalId     UNIQUEIDENTIFIER,
    @Action         NVARCHAR(20) = NULL,
    @EntityType     NVARCHAR(50) = NULL,
    @UserId         UNIQUEIDENTIFIER = NULL,
    @FromDate       DATE = NULL,
    @ToDate         DATE = NULL,
    @Top            INT = 200
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Tz NVARCHAR(100) = cfg.fn_HospitalWindowsTz(@HospitalId);   -- @FromDate / @ToDate are hospital-local dates
    SELECT TOP (@Top) AuditLogId, UserId, [Action], EntityType, EntityId, Details, IpAddress, [Timestamp]
    FROM sec.AuditLog
    WHERE HospitalId = @HospitalId
      AND (@Action IS NULL OR [Action] = @Action)
      AND (@EntityType IS NULL OR EntityType = @EntityType)
      AND (@UserId IS NULL OR UserId = @UserId)
      AND (@FromDate IS NULL OR CAST([Timestamp] AT TIME ZONE @Tz AS DATE) >= @FromDate)
      AND (@ToDate IS NULL OR CAST([Timestamp] AT TIME ZONE @Tz AS DATE) <= @ToDate)
    ORDER BY [Timestamp] DESC;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListExportTables
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- Row counts for the export-picker screen — allow-listed table set only
    -- (never expose arbitrary table names to the client, per architecture §8.5).
    SELECT 'patients' AS TableKey, COUNT(*) AS RowCount_ FROM opd.Patient WHERE HospitalId = @HospitalId AND IsActive = 1
    UNION ALL SELECT 'appointments', COUNT(*) FROM opd.Appointment WHERE HospitalId = @HospitalId AND IsActive = 1
    UNION ALL SELECT 'consultations', COUNT(*) FROM cln.Consultation WHERE HospitalId = @HospitalId AND IsActive = 1
    UNION ALL SELECT 'prescriptions', COUNT(*) FROM cln.Prescription WHERE HospitalId = @HospitalId AND IsActive = 1
    UNION ALL SELECT 'invoices', COUNT(*) FROM bil.Invoice WHERE HospitalId = @HospitalId AND IsActive = 1
    UNION ALL SELECT 'paymentTransactions', COUNT(*) FROM bil.Payment WHERE HospitalId = @HospitalId AND IsActive = 1
    UNION ALL SELECT 'receipts', COUNT(*) FROM bil.Receipt WHERE HospitalId = @HospitalId AND IsActive = 1
    UNION ALL SELECT 'auditLogs', COUNT(*) FROM sec.AuditLog WHERE HospitalId = @HospitalId;
END;
GO

-- Individual export procedures — one per allow-listed table (see usp_ListExportTables).
-- data.js's listExportData(tableName) is generic/dynamic over an in-memory object;
-- the SQL layer deliberately does NOT use dynamic SQL against an arbitrary table
-- name from client input (SQL-injection surface) — instead the API layer picks
-- one of these fixed procedures by tableKey, per architecture §8.5 rule 5.

CREATE OR ALTER PROCEDURE sec.usp_ListExportData_Patients
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='EXPORT', Details='patients')
    SELECT PatientId, Mrn, FullName, Gender, Dob, Phone, Email, IsActive, CreatedAt
    FROM opd.Patient WHERE HospitalId = @HospitalId AND IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListExportData_Appointments
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='EXPORT', Details='appointments')
    SELECT AppointmentId, PatientId, DoctorId, AppointmentDate, SlotStart, SlotEnd, TimeZoneId, [Type], Status
    FROM opd.Appointment WHERE HospitalId = @HospitalId AND IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListExportData_Consultations
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='EXPORT', Details='consultations')
    SELECT ConsultationId, PatientId, DoctorId, AppointmentId, VisitDate, TimeZoneId, VisitLocalDate, Status
    FROM cln.Consultation WHERE HospitalId = @HospitalId AND IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListExportData_Prescriptions
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='EXPORT', Details='prescriptions')
    SELECT PrescriptionId, ConsultationId, PatientId, MedicineName, Status, CreatedAt
    FROM cln.Prescription WHERE HospitalId = @HospitalId AND IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListExportData_Invoices
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='EXPORT', Details='invoices')
    SELECT InvoiceId, InvoiceNumber, PatientId, TotalAmount, PaidAmount, Status, CreatedAt
    FROM bil.Invoice WHERE HospitalId = @HospitalId AND IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListExportData_PaymentTransactions
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='EXPORT', Details='paymentTransactions')
    SELECT TransactionId, InvoiceId, TransactionNumber, Amount, PaymentMethod, PaidAt, IsVoided
    FROM bil.Payment WHERE HospitalId = @HospitalId AND IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListExportData_Receipts
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='EXPORT', Details='receipts')
    SELECT ReceiptId, ReceiptNumber, InvoiceId, TotalAmount, Currency, GeneratedAt
    FROM bil.Receipt WHERE HospitalId = @HospitalId AND IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListExportData_AuditLogs
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='EXPORT', Details='auditLogs')
    SELECT AuditLogId, UserId, [Action], EntityType, EntityId, [Timestamp]
    FROM sec.AuditLog WHERE HospitalId = @HospitalId;
END;
GO

-- ============================================================
-- 21. CLINIC INVENTORY (inv.InventoryItem, inv.StockBatch, inv.StockMovement)
--     and stocked items in Services & Procedures (cln.ConsultationServiceItem)
--     Mirrors Services/InventoryService.cs. A batch is usable through its ExpiryDate
--     (the hospital's calendar date, cfg.fn_HospitalToday). Stock never goes negative: consumption is all-or-nothing.
-- ============================================================
CREATE OR ALTER PROCEDURE inv.usp_ListInventoryItems
    @HospitalId      UNIQUEIDENTIFIER,
    @IncludeInactive BIT = 0
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Today DATE = cfg.fn_HospitalToday(@HospitalId);
    SELECT i.InventoryItemId, i.ItemCode, i.ItemName, i.Category, i.Unit, i.UnitPrice, i.TaxPercent, i.ReorderLevel, i.IsActive,
           Available  = ISNULL(SUM(CASE WHEN b.ExpiryDate >= @Today THEN b.QtyOnHand END), 0),
           Expired    = ISNULL(SUM(CASE WHEN b.ExpiryDate <  @Today THEN b.QtyOnHand END), 0),
           NextExpiry = MIN(CASE WHEN b.ExpiryDate >= @Today THEN b.ExpiryDate END)
    FROM inv.InventoryItem i
    LEFT JOIN inv.StockBatch b ON b.InventoryItemId = i.InventoryItemId AND b.QtyOnHand > 0
    WHERE i.HospitalId = @HospitalId AND (@IncludeInactive = 1 OR i.IsActive = 1)
    GROUP BY i.InventoryItemId, i.ItemCode, i.ItemName, i.Category, i.Unit, i.UnitPrice, i.TaxPercent, i.ReorderLevel, i.IsActive
    ORDER BY i.ItemName;
END;
GO

CREATE OR ALTER PROCEDURE inv.usp_GetInventoryItem
    @InventoryItemId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT InventoryItemId, HospitalId, ItemCode, ItemName, Category, Unit, UnitPrice, TaxPercent, ReorderLevel, IsActive
    FROM inv.InventoryItem WHERE InventoryItemId = @InventoryItemId;
END;
GO

-- Insert when @InventoryItemId is NULL, else update. A duplicate ItemCode fails on UQ_inv_Itm_Code.
CREATE OR ALTER PROCEDURE inv.usp_SaveInventoryItem
    @InventoryItemId UNIQUEIDENTIFIER = NULL,
    @HospitalId      UNIQUEIDENTIFIER,
    @ItemCode        NVARCHAR(30),
    @ItemName        NVARCHAR(100),
    @Category        NVARCHAR(20),
    @Unit            NVARCHAR(20),
    @UnitPrice       DECIMAL(10,2),
    @TaxPercent      DECIMAL(5,2) = 0,
    @ReorderLevel    INT = 0,
    @UserId          UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (InventoryItemId UNIQUEIDENTIFIER, ItemName NVARCHAR(100));
    DECLARE @Out2 TABLE (InventoryItemId UNIQUEIDENTIFIER, ItemName NVARCHAR(100));
    IF @InventoryItemId IS NULL
        BEGIN
            INSERT INTO inv.InventoryItem (HospitalId, ItemCode, ItemName, Category, Unit, UnitPrice, TaxPercent, ReorderLevel, CreatedBy)
            OUTPUT inserted.InventoryItemId, inserted.ItemName
            INTO @Out
            VALUES (@HospitalId, @ItemCode, @ItemName, @Category, @Unit, @UnitPrice, @TaxPercent, @ReorderLevel,
                    ISNULL(@UserId, '00000000-0000-0000-0000-000000000000'));
            SELECT InventoryItemId, ItemName FROM @Out;
        END
        
    ELSE
        BEGIN
            UPDATE inv.InventoryItem
            SET ItemCode = @ItemCode, ItemName = @ItemName, Category = @Category, Unit = @Unit, UnitPrice = @UnitPrice,
                TaxPercent = @TaxPercent, ReorderLevel = @ReorderLevel, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
            OUTPUT inserted.InventoryItemId, inserted.ItemName
            INTO @Out2
            WHERE InventoryItemId = @InventoryItemId AND HospitalId = @HospitalId;
            SELECT InventoryItemId, ItemName FROM @Out2;
        END
END;
GO

-- Services an item is used with ("Used with").
CREATE OR ALTER PROCEDURE inv.usp_ListItemServices
    @InventoryItemId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT l.ServiceChargeId, sc.ServiceName
    FROM inv.InventoryItemService l
    INNER JOIN bil.ServiceChargeMaster sc ON sc.ServiceChargeId = l.ServiceChargeId
    WHERE l.InventoryItemId = @InventoryItemId
    ORDER BY sc.ServiceName;
END;
GO

-- Replaces the item's "Used with" list. @ServiceChargeIds: comma-separated ids (empty = linked to nothing).
CREATE OR ALTER PROCEDURE inv.usp_SaveItemServices
    @InventoryItemId  UNIQUEIDENTIFIER,
    @ServiceChargeIds NVARCHAR(MAX),
    @UserId           UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRAN;
        DELETE FROM inv.InventoryItemService WHERE InventoryItemId = @InventoryItemId;
        INSERT INTO inv.InventoryItemService (InventoryItemId, ServiceChargeId, CreatedBy)
        SELECT DISTINCT @InventoryItemId, sc.ServiceChargeId, ISNULL(@UserId, '00000000-0000-0000-0000-000000000000')
        FROM STRING_SPLIT(ISNULL(@ServiceChargeIds, N''), N',') v
        INNER JOIN bil.ServiceChargeMaster sc ON sc.ServiceChargeId = TRY_CAST(LTRIM(RTRIM(v.value)) AS UNIQUEIDENTIFIER)
        INNER JOIN inv.InventoryItem i ON i.InventoryItemId = @InventoryItemId AND i.HospitalId = sc.HospitalId;
    COMMIT;
    EXEC inv.usp_ListItemServices @InventoryItemId = @InventoryItemId;
END;
GO

-- The Item field in Services & Procedures: active items linked to the service, with usable stock.
CREATE OR ALTER PROCEDURE inv.usp_ListItemsForService
    @HospitalId      UNIQUEIDENTIFIER,
    @ServiceChargeId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT i.InventoryItemId, i.ItemName, i.Category, i.Unit,
           Available = ISNULL((SELECT SUM(b.QtyOnHand) FROM inv.StockBatch b
                               WHERE b.InventoryItemId = i.InventoryItemId AND b.QtyOnHand > 0
                                 AND b.ExpiryDate >= cfg.fn_HospitalToday(@HospitalId)), 0)
    FROM inv.InventoryItem i
    INNER JOIN inv.InventoryItemService l ON l.InventoryItemId = i.InventoryItemId AND l.ServiceChargeId = @ServiceChargeId
    WHERE i.HospitalId = @HospitalId AND i.IsActive = 1
    ORDER BY i.ItemName;
END;
GO

-- Activate / deactivate an item: the new value is passed explicitly (DATA-LAYER-ARCHITECTURE §2: no toggle).
CREATE OR ALTER PROCEDURE inv.usp_UpdateInventoryItemActive
    @InventoryItemId UNIQUEIDENTIFIER,
    @IsActive        BIT,
    @UserId          UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (InventoryItemId UNIQUEIDENTIFIER, IsActive BIT);
    UPDATE inv.InventoryItem SET IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    OUTPUT inserted.InventoryItemId, inserted.IsActive
    INTO @Out
    WHERE InventoryItemId = @InventoryItemId;
    SELECT InventoryItemId, IsActive FROM @Out;
END;
GO

CREATE OR ALTER PROCEDURE inv.usp_ListStockBatches
    @InventoryItemId UNIQUEIDENTIFIER,
    @IncludeEmpty    BIT = 0
AS BEGIN
    SET NOCOUNT ON;
    SELECT StockBatchId, InventoryItemId, BatchNo, ExpiryDate, QtyReceived, QtyOnHand, UnitCost, Supplier, ReceivedByUserId, ReceivedAt,
           IsExpired = CAST(CASE WHEN ExpiryDate < cfg.fn_HospitalToday(HospitalId) THEN 1 ELSE 0 END AS BIT)
    FROM inv.StockBatch
    WHERE InventoryItemId = @InventoryItemId AND (@IncludeEmpty = 1 OR QtyOnHand > 0)
    ORDER BY ExpiryDate, ReceivedAt;
END;
GO

CREATE OR ALTER PROCEDURE inv.usp_ListStockMovements
    @InventoryItemId UNIQUEIDENTIFIER,
    @Take            INT = 50
AS BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Take) m.StockMovementId, m.MovementType, m.Qty, m.Reason, m.ConsultationId, m.UserId, m.CreatedAt,
           b.StockBatchId, b.BatchNo
    FROM inv.StockMovement m
    INNER JOIN inv.StockBatch b ON b.StockBatchId = m.StockBatchId
    WHERE m.InventoryItemId = @InventoryItemId
    ORDER BY m.CreatedAt DESC, m.Id DESC;
END;
GO

-- A BatchNo already on file for the item is topped up, provided the expiry matches.
CREATE OR ALTER PROCEDURE inv.usp_RecordStockReceipt
    @HospitalId      UNIQUEIDENTIFIER,
    @InventoryItemId UNIQUEIDENTIFIER,
    @BatchNo         NVARCHAR(40),
    @ExpiryDate      DATE,
    @Qty             INT,
    @UnitCost        DECIMAL(10,2) = 0,
    @Supplier        NVARCHAR(100) = NULL,
    @UserId          UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF @Qty < 1 THROW 50020, N'Quantity must be at least 1.', 1;
    IF NOT EXISTS (SELECT 1 FROM inv.InventoryItem WHERE InventoryItemId = @InventoryItemId AND HospitalId = @HospitalId)
        THROW 50021, N'Item not found.', 1;
    BEGIN TRAN;
        DECLARE @BatchId UNIQUEIDENTIFIER, @Expiry DATE;
        SELECT @BatchId = StockBatchId, @Expiry = ExpiryDate
        FROM inv.StockBatch WITH (UPDLOCK, HOLDLOCK)
        WHERE InventoryItemId = @InventoryItemId AND BatchNo = @BatchNo;

        IF @BatchId IS NOT NULL AND @Expiry <> @ExpiryDate
        BEGIN
            DECLARE @Msg NVARCHAR(200) = N'Batch ' + @BatchNo + N' is already on file with expiry ' + CONVERT(NVARCHAR(10), @Expiry, 23) + N'.';
            THROW 50022, @Msg, 1;
        END;

        IF @BatchId IS NULL
        BEGIN
            DECLARE @New TABLE (StockBatchId UNIQUEIDENTIFIER);
            INSERT INTO inv.StockBatch (HospitalId, InventoryItemId, BatchNo, ExpiryDate, QtyReceived, QtyOnHand, UnitCost, Supplier, ReceivedByUserId, CreatedBy)
            OUTPUT inserted.StockBatchId INTO @New
            VALUES (@HospitalId, @InventoryItemId, @BatchNo, @ExpiryDate, @Qty, @Qty, @UnitCost, @Supplier, @UserId,
                    ISNULL(@UserId, '00000000-0000-0000-0000-000000000000'));
            SELECT @BatchId = StockBatchId FROM @New;
        END
        ELSE
            UPDATE inv.StockBatch
            SET QtyReceived = QtyReceived + @Qty, QtyOnHand = QtyOnHand + @Qty, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
            WHERE StockBatchId = @BatchId;

        INSERT INTO inv.StockMovement (HospitalId, InventoryItemId, StockBatchId, MovementType, Qty, Reason, UserId, CreatedBy)
        VALUES (@HospitalId, @InventoryItemId, @BatchId, 'RECEIPT', @Qty,
                CASE WHEN @Supplier IS NULL OR @Supplier = N'' THEN N'Received' ELSE N'Received from ' + @Supplier END,
                @UserId, ISNULL(@UserId, '00000000-0000-0000-0000-000000000000'));
    COMMIT;
    EXEC inv.usp_ListStockBatches @InventoryItemId = @InventoryItemId;
END;
GO

-- Signed correction of one batch (count correction, damage, expiry write-off). Refuses to go below zero.
CREATE OR ALTER PROCEDURE inv.usp_RecordStockAdjustment
    @StockBatchId UNIQUEIDENTIFIER,
    @Delta        INT,
    @Reason       NVARCHAR(200),
    @UserId       UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF @Delta = 0 THROW 50023, N'Enter a non-zero quantity.', 1;
    BEGIN TRAN;
        UPDATE inv.StockBatch
        SET QtyOnHand = QtyOnHand + @Delta, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE StockBatchId = @StockBatchId AND QtyOnHand + @Delta >= 0;
        IF @@ROWCOUNT = 0 THROW 50024, N'Adjustment would take the batch below zero.', 1;

        INSERT INTO inv.StockMovement (HospitalId, InventoryItemId, StockBatchId, MovementType, Qty, Reason, UserId, CreatedBy)
        SELECT HospitalId, InventoryItemId, StockBatchId, 'ADJUSTMENT', @Delta, @Reason, @UserId,
               ISNULL(@UserId, '00000000-0000-0000-0000-000000000000')
        FROM inv.StockBatch WHERE StockBatchId = @StockBatchId;
    COMMIT;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListConsultationServiceItems
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT ConsultationServiceId, ConsultationId, PatientId, ServiceChargeId, ServiceName, Category, Qty,
           InventoryItemId, ItemName, ItemUnit, ItemQty, Notes, AddedByUserId, CreatedAt
    FROM cln.ConsultationServiceItem WHERE ConsultationId = @ConsultationId ORDER BY CreatedAt;
END;
GO

-- A service / procedure, optionally with a stocked item linked to it (inv.InventoryItemService) and its own quantity.
-- The item is only checked here (available, less what this consultation already lists); it is deducted at sign-off
-- by inv.usp_RecordStockConsumption.
CREATE OR ALTER PROCEDURE cln.usp_CreateConsultationServiceItem
    @ConsultationId  UNIQUEIDENTIFIER,
    @ServiceChargeId UNIQUEIDENTIFIER,
    @Qty             INT = 1,
    @InventoryItemId UNIQUEIDENTIFIER = NULL,
    @ItemQty         INT = 1,
    @Notes           NVARCHAR(300) = NULL,
    @UserId          UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Out TABLE (ConsultationServiceId UNIQUEIDENTIFIER, ServiceName NVARCHAR(200), Qty INT, ItemName NVARCHAR(100), ItemQty INT);
    DECLARE @HospitalId UNIQUEIDENTIFIER, @PatientId UNIQUEIDENTIFIER, @Name NVARCHAR(200), @Category NVARCHAR(20),
            @ItemName NVARCHAR(100), @ItemUnit NVARCHAR(20);
    SELECT @HospitalId = HospitalId, @PatientId = PatientId FROM cln.Consultation WHERE ConsultationId = @ConsultationId AND IsLocked = 0;
    IF @HospitalId IS NULL THROW 50030, N'Consultation not found or already signed off.', 1;

    SELECT @Name = ServiceName, @Category = Category FROM bil.ServiceChargeMaster
    WHERE ServiceChargeId = @ServiceChargeId AND HospitalId = @HospitalId AND IsActive = 1 AND Category <> 'CONSULTATION';
    IF @Name IS NULL THROW 50032, N'Service not found.', 1;

    IF @InventoryItemId IS NOT NULL
    BEGIN
        SELECT @ItemName = i.ItemName, @ItemUnit = i.Unit
        FROM inv.InventoryItem i
        INNER JOIN inv.InventoryItemService l ON l.InventoryItemId = i.InventoryItemId AND l.ServiceChargeId = @ServiceChargeId
        WHERE i.InventoryItemId = @InventoryItemId AND i.HospitalId = @HospitalId AND i.IsActive = 1;
        IF @ItemName IS NULL THROW 50033, N'That item is not used with this service.', 1;

        DECLARE @Left INT =
            (SELECT ISNULL(SUM(QtyOnHand), 0) FROM inv.StockBatch
             WHERE InventoryItemId = @InventoryItemId AND QtyOnHand > 0 AND ExpiryDate >= cfg.fn_HospitalToday(@HospitalId))
          - (SELECT ISNULL(SUM(ItemQty), 0) FROM cln.ConsultationServiceItem
             WHERE ConsultationId = @ConsultationId AND InventoryItemId = @InventoryItemId);
        IF @ItemQty > @Left
        BEGIN
            DECLARE @Msg NVARCHAR(200) = N'Only ' + CAST(CASE WHEN @Left < 0 THEN 0 ELSE @Left END AS NVARCHAR(10)) + N' ' + @ItemUnit + N' available.';
            THROW 50031, @Msg, 1;
        END;
    END;

    INSERT INTO cln.ConsultationServiceItem (HospitalId, ConsultationId, PatientId, ServiceChargeId, ServiceName, Category, Qty,
                                             InventoryItemId, ItemName, ItemUnit, ItemQty, Notes, AddedByUserId, CreatedBy)
    OUTPUT inserted.ConsultationServiceId, inserted.ServiceName, inserted.Qty, inserted.ItemName, inserted.ItemQty
    INTO @Out
    VALUES (@HospitalId, @ConsultationId, @PatientId, @ServiceChargeId, @Name, @Category, @Qty,
            @InventoryItemId, @ItemName, @ItemUnit, CASE WHEN @InventoryItemId IS NOT NULL THEN @ItemQty END,
            @Notes, @UserId, ISNULL(@UserId, '00000000-0000-0000-0000-000000000000'));
    SELECT ConsultationServiceId, ServiceName, Qty, ItemName, ItemQty FROM @Out;
END;
GO

-- Before sign-off only; nothing has been deducted yet, so removal needs no stock reversal.
CREATE OR ALTER PROCEDURE cln.usp_DeleteConsultationServiceItem
    @ConsultationId        UNIQUEIDENTIFIER,
    @ConsultationServiceId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    DELETE si FROM cln.ConsultationServiceItem si
    INNER JOIN cln.Consultation c ON c.ConsultationId = si.ConsultationId AND c.IsLocked = 0
    WHERE si.ConsultationId = @ConsultationId AND si.ConsultationServiceId = @ConsultationServiceId;
END;
GO

-- Stocked items the consultation lists more of than is available (empty = sign-off can proceed).
CREATE OR ALTER PROCEDURE inv.usp_ListStockShortages
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT i.InventoryItemId, i.ItemName, i.Unit, n.Needed, ISNULL(a.Available, 0) AS Available
    FROM (SELECT InventoryItemId, SUM(ItemQty) AS Needed FROM cln.ConsultationServiceItem
          WHERE ConsultationId = @ConsultationId AND InventoryItemId IS NOT NULL GROUP BY InventoryItemId) n
    INNER JOIN inv.InventoryItem i ON i.InventoryItemId = n.InventoryItemId
    OUTER APPLY (SELECT SUM(QtyOnHand) AS Available FROM inv.StockBatch
                 WHERE InventoryItemId = n.InventoryItemId AND QtyOnHand > 0 AND ExpiryDate >= cfg.fn_HospitalToday(HospitalId)) a
    WHERE n.Needed > ISNULL(a.Available, 0);
END;
GO

-- Deducts the consultation's stocked items earliest-expiry first. All or nothing: if any item is short, raises
-- 50040 listing the shortages and deducts nothing. Called once, by cln.usp_ConfirmSignOff; a second call is a no-op.
CREATE OR ALTER PROCEDURE inv.usp_RecordStockConsumption
    @ConsultationId UNIQUEIDENTIFIER,
    @UserId         UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF EXISTS (SELECT 1 FROM inv.StockMovement WHERE ConsultationId = @ConsultationId AND MovementType = 'CONSUMPTION') RETURN;
    DECLARE @Today DATE = cfg.fn_HospitalToday((SELECT HospitalId FROM cln.Consultation WHERE ConsultationId = @ConsultationId));
    BEGIN TRAN;
        -- Lock the usable batches of every item involved, then take from each in expiry order until the need is met.
        SELECT b.StockBatchId, b.HospitalId, b.InventoryItemId, b.QtyOnHand, n.Needed,
               RunTotal = SUM(b.QtyOnHand) OVER (PARTITION BY b.InventoryItemId ORDER BY b.ExpiryDate, b.ReceivedAt, b.Id ROWS UNBOUNDED PRECEDING)
        INTO #Batches
        FROM inv.StockBatch b WITH (UPDLOCK, HOLDLOCK)
        INNER JOIN (SELECT InventoryItemId, SUM(ItemQty) AS Needed FROM cln.ConsultationServiceItem
                    WHERE ConsultationId = @ConsultationId AND InventoryItemId IS NOT NULL GROUP BY InventoryItemId) n
            ON n.InventoryItemId = b.InventoryItemId
        WHERE b.QtyOnHand > 0 AND b.ExpiryDate >= @Today;

        DECLARE @Short NVARCHAR(2000) = (
            SELECT STRING_AGG(CAST(i.ItemName + N': ' + CAST(n.Needed AS NVARCHAR(10)) + N' ' + i.Unit + N' needed, '
                              + CAST(ISNULL(a.Available, 0) AS NVARCHAR(10)) + N' in stock' AS NVARCHAR(MAX)), N'; ')
            FROM (SELECT InventoryItemId, SUM(ItemQty) AS Needed FROM cln.ConsultationServiceItem
                  WHERE ConsultationId = @ConsultationId AND InventoryItemId IS NOT NULL GROUP BY InventoryItemId) n
            INNER JOIN inv.InventoryItem i ON i.InventoryItemId = n.InventoryItemId
            OUTER APPLY (SELECT SUM(QtyOnHand) AS Available FROM #Batches WHERE InventoryItemId = n.InventoryItemId) a
            WHERE n.Needed > ISNULL(a.Available, 0));
        IF @Short IS NOT NULL
        BEGIN
            SET @Short = N'Not enough stock: ' + @Short;
            THROW 50040, @Short, 1;
        END;

        SELECT StockBatchId, HospitalId, InventoryItemId,
               Take = CASE WHEN RunTotal <= Needed THEN QtyOnHand
                           WHEN RunTotal - QtyOnHand < Needed THEN Needed - (RunTotal - QtyOnHand)
                           ELSE 0 END
        INTO #Take FROM #Batches;
        DELETE FROM #Take WHERE Take = 0;

        UPDATE b SET b.QtyOnHand = b.QtyOnHand - t.Take, b.ModifiedAt = SYSUTCDATETIME(), b.ModifiedBy = @UserId
        FROM inv.StockBatch b INNER JOIN #Take t ON t.StockBatchId = b.StockBatchId;

        INSERT INTO inv.StockMovement (HospitalId, InventoryItemId, StockBatchId, MovementType, Qty, Reason, ConsultationId, UserId, CreatedBy)
        SELECT HospitalId, InventoryItemId, StockBatchId, 'CONSUMPTION', -Take, N'Used in consultation', @ConsultationId, @UserId,
               ISNULL(@UserId, '00000000-0000-0000-0000-000000000000')
        FROM #Take;
    COMMIT;
END;
GO

-- ============================================================
-- 22. PHASE 4 GAP CLOSURE (v3.5)
--     Procedures for the features the Web Forms app runs in memory but which had no
--     procedure, so no WCF operation (API-SPECIFICATION.md Known Gaps #1 and
--     API-INTEGRATION-GUIDE.md §8, gaps G-1 … G-17). Each one ports the matching
--     Services/*Service.cs method; differences from the in-memory behavior are
--     commented "v3.5:". Error numbers 50200–50249.
-- ============================================================

-- ---- 22.0 Helpers (internal) ----

-- Digits only: "+1 (876) 555-0101" → "18765550101". NULL → ''.
CREATE OR ALTER FUNCTION cfg.fn_DigitsOnly (@Value NVARCHAR(50))
RETURNS NVARCHAR(50) WITH SCHEMABINDING AS
BEGIN
    DECLARE @Out NVARCHAR(50) = N'', @i INT = 1, @c NCHAR(1);
    WHILE @i <= LEN(@Value)
    BEGIN
        SET @c = SUBSTRING(@Value, @i, 1);
        IF @c LIKE N'[0-9]' SET @Out += @c;
        SET @i += 1;
    END;
    RETURN @Out;
END;
GO

-- The last 10 digits of a phone number — LabService.MobileKey. A key shorter than 10 is not a full mobile number.
-- TODO (encryption): opd.Patient.Phone is marked "ENCRYPTION: DETERMINISTIC". Once Always Encrypted is applied the
-- server cannot strip digits from it; store a computed MobileKey column (written by the service) before then.
CREATE OR ALTER FUNCTION cfg.fn_MobileKey (@Phone NVARCHAR(50))
RETURNS NVARCHAR(10) WITH SCHEMABINDING AS
BEGIN
    DECLARE @d NVARCHAR(50) = cfg.fn_DigitsOnly(@Phone);
    RETURN CASE WHEN LEN(@d) > 10 THEN RIGHT(@d, 10) ELSE @d END;
END;
GO

-- ---- 22.1 Patients — duplicate check (G-1) ----

-- PatientService.FindDuplicate: the first other patient in the hospital with the same national health id, the same
-- TRN (both compared without spaces / hyphens, case-insensitive), or the same mobile number (digits, ≥ 7, one may be
-- the tail of the other, e.g. with / without country code). Empty result = no duplicate.
CREATE OR ALTER PROCEDURE opd.usp_CheckDuplicatePatient
    @HospitalId       UNIQUEIDENTIFIER,
    @PatientId        UNIQUEIDENTIFIER = NULL,   -- the patient being edited; NULL on registration
    @NationalHealthId NVARCHAR(50) = NULL,
    @Trn              NVARCHAR(20) = NULL,
    @Mobile           NVARCHAR(20) = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Hit TABLE (Field NVARCHAR(20), [Value] NVARCHAR(50), PatientId UNIQUEIDENTIFIER, Mrn NVARCHAR(20), FullName NVARCHAR(200));
    DECLARE @Nhi NVARCHAR(50) = REPLACE(REPLACE(ISNULL(@NationalHealthId, N''), N' ', N''), N'-', N''),
            @TrnN NVARCHAR(20) = REPLACE(REPLACE(ISNULL(@Trn, N''), N' ', N''), N'-', N''),
            @Mob NVARCHAR(50) = cfg.fn_DigitsOnly(@Mobile);

    IF LEN(@Nhi) > 0
        INSERT INTO @Hit
        SELECT TOP 1 N'NationalHealthId', @NationalHealthId, p.PatientId, p.Mrn, p.FullName
        FROM opd.Patient p
        WHERE p.HospitalId = @HospitalId AND (@PatientId IS NULL OR p.PatientId <> @PatientId)
          AND REPLACE(REPLACE(p.NationalHealthId, N' ', N''), N'-', N'') = @Nhi
        ORDER BY p.CreatedAt;

    IF NOT EXISTS (SELECT 1 FROM @Hit) AND LEN(@TrnN) > 0
        INSERT INTO @Hit
        SELECT TOP 1 N'Trn', @Trn, p.PatientId, p.Mrn, p.FullName
        FROM opd.Patient p
        WHERE p.HospitalId = @HospitalId AND (@PatientId IS NULL OR p.PatientId <> @PatientId)
          AND REPLACE(REPLACE(p.Trn, N' ', N''), N'-', N'') = @TrnN
        ORDER BY p.CreatedAt;

    IF NOT EXISTS (SELECT 1 FROM @Hit) AND LEN(@Mob) >= 7
        INSERT INTO @Hit
        SELECT TOP 1 N'Mobile', @Mobile, x.PatientId, x.Mrn, x.FullName
        FROM (SELECT p.PatientId, p.Mrn, p.FullName, p.CreatedAt,
                     cfg.fn_DigitsOnly(COALESCE(NULLIF(p.Mobile, N''), p.Phone)) AS d
              FROM opd.Patient p
              WHERE p.HospitalId = @HospitalId AND (@PatientId IS NULL OR p.PatientId <> @PatientId)) x
        WHERE LEN(x.d) >= 7 AND (x.d = @Mob OR x.d LIKE N'%' + @Mob OR @Mob LIKE N'%' + x.d)
        ORDER BY x.CreatedAt;

    SELECT Field, [Value], PatientId, Mrn, FullName FROM @Hit;
END;
GO

-- ---- 22.2 Patients — medical events and family history: get / update / delete (G-2) ----

CREATE OR ALTER PROCEDURE opd.usp_GetPatientMedicalEvent
    @HospitalId        UNIQUEIDENTIFIER,
    @PatientMedEventId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT PatientMedEventId, PatientId, EventId, EventName, EventMonth, EventYear, Notes, RecordedAt, RecordedByUserId
    FROM opd.PatientMedicalEvent
    WHERE PatientMedEventId = @PatientMedEventId AND HospitalId = @HospitalId AND IsActive = 1;
END;
GO

-- PatientService.SaveMedicalEventRecord (existing record). EventId is re-resolved from the name against the hospital's
-- cfg.MedicalEventType list, as in memory; a name not on the list is kept as free text (EventId NULL).
CREATE OR ALTER PROCEDURE opd.usp_UpdatePatientMedicalEvent
    @HospitalId        UNIQUEIDENTIFIER,
    @PatientMedEventId UNIQUEIDENTIFIER,
    @EventName         NVARCHAR(100),
    @EventMonth        INT = NULL,
    @EventYear         INT = NULL,
    @Notes             NVARCHAR(500) = NULL,
    @ModifiedBy        UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SET @EventName = LTRIM(RTRIM(@EventName));
    IF ISNULL(@EventName, N'') = N'' THROW 50200, N'Enter the surgery or event name.', 1;

    DECLARE @EventId UNIQUEIDENTIFIER = (SELECT TOP 1 EventId FROM cfg.MedicalEventType
                                         WHERE HospitalId = @HospitalId AND EventName = @EventName AND IsActive = 1);
    UPDATE opd.PatientMedicalEvent
    SET EventId = @EventId, EventName = @EventName, EventMonth = @EventMonth, EventYear = @EventYear, Notes = @Notes,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE PatientMedEventId = @PatientMedEventId AND HospitalId = @HospitalId AND IsActive = 1;

    -- empty result = not found (the service raises NotFound)
    EXEC opd.usp_GetPatientMedicalEvent @HospitalId = @HospitalId, @PatientMedEventId = @PatientMedEventId;
END;
GO

-- v3.5: soft delete (IsActive = 0) — memory removes the row, the table keeps history.
CREATE OR ALTER PROCEDURE opd.usp_DeletePatientMedicalEvent
    @HospitalId        UNIQUEIDENTIFIER,
    @PatientMedEventId UNIQUEIDENTIFIER,
    @ModifiedBy        UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    UPDATE opd.PatientMedicalEvent
    SET IsActive = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE PatientMedEventId = @PatientMedEventId AND HospitalId = @HospitalId AND IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_GetPatientFamilyHistory
    @HospitalId      UNIQUEIDENTIFIER,
    @FamilyHistoryId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT FamilyHistoryId, PatientId, RelationshipType, ConditionName, Notes, RecordedAt
    FROM opd.PatientFamilyHistory
    WHERE FamilyHistoryId = @FamilyHistoryId AND HospitalId = @HospitalId AND IsActive = 1;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_UpdatePatientFamilyHistory
    @HospitalId       UNIQUEIDENTIFIER,
    @FamilyHistoryId  UNIQUEIDENTIFIER,
    @RelationshipType NVARCHAR(30),
    @ConditionName    NVARCHAR(100),
    @Notes            NVARCHAR(300) = NULL,
    @ModifiedBy       UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    IF ISNULL(LTRIM(RTRIM(@RelationshipType)), N'') = N'' OR ISNULL(LTRIM(RTRIM(@ConditionName)), N'') = N''
        THROW 50201, N'Choose the relative and enter the condition.', 1;

    UPDATE opd.PatientFamilyHistory
    SET RelationshipType = @RelationshipType, ConditionName = LTRIM(RTRIM(@ConditionName)), Notes = @Notes,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE FamilyHistoryId = @FamilyHistoryId AND HospitalId = @HospitalId AND IsActive = 1;

    EXEC opd.usp_GetPatientFamilyHistory @HospitalId = @HospitalId, @FamilyHistoryId = @FamilyHistoryId;
END;
GO

-- v3.5: soft delete, as opd.usp_DeletePatientMedicalEvent.
CREATE OR ALTER PROCEDURE opd.usp_DeletePatientFamilyHistory
    @HospitalId      UNIQUEIDENTIFIER,
    @FamilyHistoryId UNIQUEIDENTIFIER,
    @ModifiedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    UPDATE opd.PatientFamilyHistory
    SET IsActive = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE FamilyHistoryId = @FamilyHistoryId AND HospitalId = @HospitalId AND IsActive = 1;
END;
GO

-- ---- 22.3 Patients — social history (G-3) ----

-- PatientService.UpdateSocialHistory: replaces all six values (NULL clears one), as the Family & Social tab saves them together.
CREATE OR ALTER PROCEDURE opd.usp_UpdatePatientSocialHistory
    @HospitalId       UNIQUEIDENTIFIER,
    @PatientId        UNIQUEIDENTIFIER,
    @SmokingStatus    NVARCHAR(10) = NULL,    -- NEVER | FORMER | CURRENT
    @SmokingPackYears INT = NULL,
    @AlcoholUse       NVARCHAR(12) = NULL,    -- NONE | OCCASIONAL | REGULAR
    @Occupation       NVARCHAR(80) = NULL,
    @PhysicalActivity NVARCHAR(12) = NULL,    -- SEDENTARY | LIGHT | MODERATE | ACTIVE
    @Diet             NVARCHAR(16) = NULL,    -- VEGETARIAN | EGGETARIAN | NON_VEGETARIAN | VEGAN
    @ModifiedBy       UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    UPDATE opd.Patient
    SET SmokingStatus = NULLIF(@SmokingStatus, N''), SmokingPackYears = @SmokingPackYears,
        AlcoholUse = NULLIF(@AlcoholUse, N''), Occupation = NULLIF(LTRIM(RTRIM(@Occupation)), N''),
        PhysicalActivity = NULLIF(@PhysicalActivity, N''), Diet = NULLIF(@Diet, N''),
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE PatientId = @PatientId AND HospitalId = @HospitalId;

    SELECT PatientId, SmokingStatus, SmokingPackYears, AlcoholUse, Occupation, PhysicalActivity, Diet
    FROM opd.Patient WHERE PatientId = @PatientId AND HospitalId = @HospitalId;
END;
GO

-- ---- 22.4 Patients — consent documents (G-4, metadata; the file itself is in the service's file store) ----

CREATE OR ALTER PROCEDURE opd.usp_ListPatientConsentDocuments
    @HospitalId UNIQUEIDENTIFIER,
    @PatientId  UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT ConsentDocumentId, PatientId, FormVersion, FileName, ContentType, SizeBytes, StoredFileName,
           UploadedByUserId, UploadedAt
    FROM opd.PatientConsentDocument
    WHERE PatientId = @PatientId AND HospitalId = @HospitalId AND IsActive = 1
    ORDER BY UploadedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_GetPatientConsentDocument
    @HospitalId        UNIQUEIDENTIFIER,
    @ConsentDocumentId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT ConsentDocumentId, PatientId, FormVersion, FileName, ContentType, SizeBytes, StoredFileName,
           UploadedByUserId, UploadedAt
    FROM opd.PatientConsentDocument
    WHERE ConsentDocumentId = @ConsentDocumentId AND HospitalId = @HospitalId AND IsActive = 1;
END;
GO

-- PatientService.SaveConsentDocument. The service writes the file first (StoredFileName = new GUID "N" + extension)
-- and records it here; if this fails it deletes the file.
CREATE OR ALTER PROCEDURE opd.usp_CreatePatientConsentDocument
    @HospitalId       UNIQUEIDENTIFIER,
    @PatientId        UNIQUEIDENTIFIER,
    @FormVersion      NVARCHAR(20) = NULL,
    @FileName         NVARCHAR(255),
    @ContentType      NVARCHAR(100),
    @SizeBytes        BIGINT,
    @StoredFileName   NVARCHAR(100),
    @UploadedByUserId UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM opd.Patient WHERE PatientId = @PatientId AND HospitalId = @HospitalId)
        THROW 50202, N'Patient not found.', 1;

    DECLARE @Out TABLE (ConsentDocumentId UNIQUEIDENTIFIER, PatientId UNIQUEIDENTIFIER, FormVersion NVARCHAR(20), FileName NVARCHAR(255),
                        ContentType NVARCHAR(100), SizeBytes BIGINT, StoredFileName NVARCHAR(100), UploadedByUserId UNIQUEIDENTIFIER,
                        UploadedAt DATETIMEOFFSET(3));
    INSERT INTO opd.PatientConsentDocument (HospitalId, PatientId, FormVersion, FileName, ContentType, SizeBytes, StoredFileName,
                                            UploadedByUserId, CreatedBy)
    OUTPUT inserted.ConsentDocumentId, inserted.PatientId, inserted.FormVersion, inserted.FileName, inserted.ContentType,
           inserted.SizeBytes, inserted.StoredFileName, inserted.UploadedByUserId, inserted.UploadedAt
    INTO @Out
    VALUES (@HospitalId, @PatientId, @FormVersion, @FileName, @ContentType, @SizeBytes, @StoredFileName,
            @UploadedByUserId, ISNULL(@UploadedByUserId, '00000000-0000-0000-0000-000000000000'));
    SELECT ConsentDocumentId, PatientId, FormVersion, FileName, ContentType, SizeBytes, StoredFileName, UploadedByUserId, UploadedAt FROM @Out;
END;
GO

-- ---- 22.5 Patients — CSV import (G-15) ----

-- ExportService.ImportPatients: creates every row through opd.usp_CreatePatient (same MRN rule, RegisteredVia
-- CSV_IMPORT), all or nothing.
-- @PatientsJson: [{"FullName","Gender","Dob":"yyyy-MM-dd","Phone","Email","Address","NationalHealthId"}, …].
-- v3.5: rows are validated here as well as by the page (ExportService.PatientRequiredColumns); the first bad row
-- (1-based) is named in error 50204.
CREATE OR ALTER PROCEDURE opd.usp_CreatePatientsBatch
    @HospitalId   UNIQUEIDENTIFIER,
    @PatientsJson NVARCHAR(MAX),
    @CreatedBy    UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF ISJSON(@PatientsJson) <> 1 THROW 50203, N'The import file could not be read.', 1;

    DECLARE @Rows TABLE (RowNo INT PRIMARY KEY, FullName NVARCHAR(200), Gender NVARCHAR(10), DobText NVARCHAR(30), Dob DATE,
                         Phone NVARCHAR(20), Email NVARCHAR(100), [Address] NVARCHAR(500), NationalHealthId NVARCHAR(50));
    INSERT INTO @Rows
    SELECT CAST(j.[key] AS INT) + 1,
           LTRIM(RTRIM(JSON_VALUE(j.value, '$.FullName'))), UPPER(LTRIM(RTRIM(JSON_VALUE(j.value, '$.Gender')))),
           JSON_VALUE(j.value, '$.Dob'), TRY_CONVERT(DATE, JSON_VALUE(j.value, '$.Dob'), 23),
           LTRIM(RTRIM(JSON_VALUE(j.value, '$.Phone'))), NULLIF(LTRIM(RTRIM(JSON_VALUE(j.value, '$.Email'))), N''),
           NULLIF(LTRIM(RTRIM(JSON_VALUE(j.value, '$.Address'))), N''), NULLIF(LTRIM(RTRIM(JSON_VALUE(j.value, '$.NationalHealthId'))), N'')
    FROM OPENJSON(@PatientsJson) j;

    DECLARE @Bad INT = (SELECT MIN(RowNo) FROM @Rows
                        WHERE ISNULL(FullName, N'') = N'' OR Gender NOT IN (N'MALE', N'FEMALE', N'OTHER') OR Gender IS NULL
                           OR Dob IS NULL OR ISNULL(Phone, N'') = N'');
    IF @Bad IS NOT NULL
    BEGIN
        DECLARE @Msg NVARCHAR(200) = N'Row ' + CAST(@Bad AS NVARCHAR(10)) + N': FullName, Gender (MALE/FEMALE/OTHER), Dob (yyyy-MM-dd) and Phone are required.';
        THROW 50204, @Msg, 1;
    END;

    DECLARE @Created TABLE (PatientId UNIQUEIDENTIFIER, Mrn NVARCHAR(20), FullName NVARCHAR(200), CreatedAt DATETIMEOFFSET(3));
    DECLARE @RowNo INT, @FullName NVARCHAR(200), @Gender NVARCHAR(10), @Dob DATE, @Phone NVARCHAR(20), @Email NVARCHAR(100),
            @Address NVARCHAR(500), @Nhi NVARCHAR(50);

    BEGIN TRAN;
    DECLARE r CURSOR LOCAL FAST_FORWARD FOR
        SELECT RowNo, FullName, Gender, Dob, Phone, Email, [Address], NationalHealthId FROM @Rows ORDER BY RowNo;
    OPEN r; FETCH NEXT FROM r INTO @RowNo, @FullName, @Gender, @Dob, @Phone, @Email, @Address, @Nhi;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        INSERT INTO @Created
        EXEC opd.usp_CreatePatient @HospitalId = @HospitalId, @FullName = @FullName, @Gender = @Gender, @Dob = @Dob,
             @Phone = @Phone, @Email = @Email, @Address = @Address, @NationalHealthId = @Nhi, @CreatedBy = @CreatedBy,
             @RegisteredVia = N'CSV_IMPORT';
        FETCH NEXT FROM r INTO @RowNo, @FullName, @Gender, @Dob, @Phone, @Email, @Address, @Nhi;
    END;
    CLOSE r; DEALLOCATE r;

    COMMIT;

    -- AUDIT: the service records one IMPORT entry (EntityType = 'Patient', Details = row count)
    SELECT PatientId, Mrn, FullName, CreatedAt FROM @Created;
END;
GO

-- ---- 22.6 Payers (insurers / government schemes) admin (G-5) ----
-- cfg.Payer is per country, not per hospital: every hospital in a country shares its payer list. The operations are
-- ADMIN-only; a hospital admin's change is visible to other hospitals in the same country (as in memory).

CREATE OR ALTER PROCEDURE cfg.usp_ListAllPayers
    @Country  NVARCHAR(5),
    @Category NVARCHAR(15) = NULL      -- PRIVATE | GOVT_SCHEME | NULL = both
AS BEGIN
    SET NOCOUNT ON;
    -- Unlike cfg.usp_ListPayers, includes inactive payers (Settings → Payers shows and re-enables them)
    SELECT PayerId, Name, Category, Country, IsActive
    FROM cfg.Payer
    WHERE Country = @Country AND (@Category IS NULL OR Category = @Category)
    ORDER BY Category, Name;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_GetPayer
    @PayerId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT PayerId, Name, Category, Country, IsActive FROM cfg.Payer WHERE PayerId = @PayerId;
END;
GO

-- PatientService.AddPayer: whitespace collapsed; an inactive payer with the same name is re-enabled; an active one is
-- a duplicate (50210 — memory returned null). v3.5: UQ_cfg_Payer_Country_Name is per country regardless of category,
-- so the same name under the other category is rejected (50211) instead of failing on the constraint.
CREATE OR ALTER PROCEDURE cfg.usp_SavePayer
    @Name      NVARCHAR(150),
    @Category  NVARCHAR(15),
    @Country   NVARCHAR(5),
    @CreatedBy UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SET @Name = LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(ISNULL(@Name, N''), NCHAR(9), N' '), NCHAR(13), N' '), NCHAR(10), N' ')));
    WHILE CHARINDEX(N'  ', @Name) > 0 SET @Name = REPLACE(@Name, N'  ', N' ');
    IF @Name = N'' OR @Category NOT IN (N'PRIVATE', N'GOVT_SCHEME') OR ISNULL(@Country, N'') = N''
        THROW 50212, N'Enter the payer name and choose private insurance or government scheme.', 1;

    DECLARE @PayerId UNIQUEIDENTIFIER, @ExistingCategory NVARCHAR(15), @IsActive BIT;
    SELECT @PayerId = PayerId, @ExistingCategory = Category, @IsActive = IsActive
    FROM cfg.Payer WHERE Country = @Country AND Name = @Name;

    IF @PayerId IS NOT NULL AND @ExistingCategory <> @Category
        THROW 50211, N'A payer with this name already exists under the other category.', 1;
    IF @PayerId IS NOT NULL AND @IsActive = 1
        THROW 50210, N'This payer is already on the list.', 1;

    IF @PayerId IS NOT NULL
        UPDATE cfg.Payer SET IsActive = 1, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @CreatedBy WHERE PayerId = @PayerId;
    ELSE
    BEGIN
        DECLARE @Out TABLE (PayerId UNIQUEIDENTIFIER);
        INSERT INTO cfg.Payer (Name, Category, Country, CreatedBy)
        OUTPUT inserted.PayerId INTO @Out
        VALUES (@Name, @Category, @Country, ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));
        SELECT @PayerId = PayerId FROM @Out;
    END;

    EXEC cfg.usp_GetPayer @PayerId = @PayerId;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_UpdatePayerActive
    @PayerId    UNIQUEIDENTIFIER,
    @IsActive   BIT = NULL,                 -- NULL toggles (PatientService.TogglePayer)
    @ModifiedBy UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    UPDATE cfg.Payer
    SET IsActive = COALESCE(@IsActive, CASE WHEN IsActive = 1 THEN 0 ELSE 1 END),
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE PayerId = @PayerId;
    EXEC cfg.usp_GetPayer @PayerId = @PayerId;
END;
GO

-- ---- 22.7 Scheduling — single appointment, patient history, range lists, dashboard (G-7, G-14) ----

CREATE OR ALTER PROCEDURE opd.usp_GetAppointment
    @HospitalId    UNIQUEIDENTIFIER,
    @AppointmentId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT a.AppointmentId, a.PatientId, p.FullName AS PatientName, p.Mrn, a.DoctorId, d.FullName AS DoctorName, d.Room AS DoctorRoom,
           a.SlotStart, a.SlotEnd, a.TimeZoneId, a.AppointmentDate,
           a.SlotStart AT TIME ZONE tz.WindowsTimeZoneId AS SlotStartLocal,
           a.SlotEnd AT TIME ZONE tz.WindowsTimeZoneId AS SlotEndLocal,
           a.[Type], a.Status, a.Notes, a.CheckedInAt, a.ConsultStartedAt, a.CompletedAt, a.CreatedAt
    FROM opd.Appointment a
    INNER JOIN opd.Patient p ON p.PatientId = a.PatientId
    INNER JOIN sec.ApplicationUser d ON d.UserId = a.DoctorId
    INNER JOIN cfg.TimeZone tz ON tz.TimeZoneId = a.TimeZoneId
    WHERE a.AppointmentId = @AppointmentId AND a.HospitalId = @HospitalId AND a.IsActive = 1;
END;
GO

-- SchedulingService.ListAppointmentsForPatient: every appointment of the patient, newest first (all statuses).
CREATE OR ALTER PROCEDURE opd.usp_ListAppointmentsForPatient
    @HospitalId UNIQUEIDENTIFIER,
    @PatientId  UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT a.AppointmentId, a.PatientId, a.DoctorId, d.FullName AS DoctorName,
           a.SlotStart, a.SlotEnd, a.TimeZoneId, a.AppointmentDate,
           a.SlotStart AT TIME ZONE tz.WindowsTimeZoneId AS SlotStartLocal,
           a.SlotEnd AT TIME ZONE tz.WindowsTimeZoneId AS SlotEndLocal,
           a.[Type], a.Status, a.Notes
    FROM opd.Appointment a
    INNER JOIN sec.ApplicationUser d ON d.UserId = a.DoctorId
    INNER JOIN cfg.TimeZone tz ON tz.TimeZoneId = a.TimeZoneId
    WHERE a.PatientId = @PatientId AND a.HospitalId = @HospitalId AND a.IsActive = 1
    ORDER BY a.SlotStart DESC;
END;
GO

-- ReportService.AppointmentList / DashboardList: appointments with AppointmentDate in [@FromDate, @ToDate] (hospital-local
-- dates, inclusive). @Kind as ReportService.MatchesKind: 'consultations' = seen (CHECKED_IN / IN_CONSULT / COMPLETED),
-- 'appointments' = not cancelled, 'cancelled' = cancelled, NULL = all. @Status narrows further (dashboard drill-down).
CREATE OR ALTER PROCEDURE opd.usp_ListAppointmentsForRange
    @HospitalId UNIQUEIDENTIFIER,
    @FromDate   DATE,
    @ToDate     DATE,
    @DoctorId   UNIQUEIDENTIFIER = NULL,    -- NULL = every doctor (front desk); a doctor-only user passes their own id
    @Kind       NVARCHAR(15) = NULL,
    @Status     NVARCHAR(15) = NULL
AS BEGIN
    SET NOCOUNT ON;
    SELECT a.AppointmentId, a.PatientId, p.FullName AS PatientName, p.Mrn, a.DoctorId, d.FullName AS DoctorName, d.Room AS DoctorRoom,
           a.SlotStart, a.SlotEnd, a.TimeZoneId, a.AppointmentDate,
           a.SlotStart AT TIME ZONE tz.WindowsTimeZoneId AS SlotStartLocal,
           a.SlotEnd AT TIME ZONE tz.WindowsTimeZoneId AS SlotEndLocal,
           a.[Type], a.Status, a.Notes
    FROM opd.Appointment a
    INNER JOIN opd.Patient p ON p.PatientId = a.PatientId
    INNER JOIN sec.ApplicationUser d ON d.UserId = a.DoctorId
    INNER JOIN cfg.TimeZone tz ON tz.TimeZoneId = a.TimeZoneId
    WHERE a.HospitalId = @HospitalId AND a.IsActive = 1
      AND a.AppointmentDate BETWEEN @FromDate AND @ToDate
      AND (@DoctorId IS NULL OR a.DoctorId = @DoctorId)
      AND (@Kind IS NULL
           OR (@Kind = N'consultations' AND a.Status IN (N'CHECKED_IN', N'IN_CONSULT', N'COMPLETED'))
           OR (@Kind = N'appointments'  AND a.Status <> N'CANCELLED')
           OR (@Kind = N'cancelled'     AND a.Status = N'CANCELLED'))
      AND (@Status IS NULL OR a.Status = @Status)
    ORDER BY a.SlotStart;
END;
GO

-- ReportService.Dashboard: counts for today / this week (Monday–Sunday) / this calendar month per kind, plus today's
-- non-cancelled appointments by status. Result set 1: Kind, TodayCount, WeekCount, MonthCount (3 rows).
-- Result set 2: Status, [Count], most first.
CREATE OR ALTER PROCEDURE opd.usp_GetAppointmentDashboard
    @HospitalId UNIQUEIDENTIFIER,
    @DoctorId   UNIQUEIDENTIFIER = NULL,
    @Today      DATE = NULL                 -- NULL = the hospital's today
AS BEGIN
    SET NOCOUNT ON;
    SET @Today = ISNULL(@Today, cfg.fn_HospitalToday(@HospitalId));
    DECLARE @WeekFrom DATE = DATEADD(DAY, -((DATEPART(WEEKDAY, @Today) + @@DATEFIRST - 2) % 7), @Today);
    DECLARE @WeekTo DATE = DATEADD(DAY, 6, @WeekFrom);
    DECLARE @MonthFrom DATE = DATEFROMPARTS(YEAR(@Today), MONTH(@Today), 1);
    DECLARE @MonthTo DATE = EOMONTH(@Today);
    DECLARE @From DATE = CASE WHEN @WeekFrom < @MonthFrom THEN @WeekFrom ELSE @MonthFrom END;
    DECLARE @To DATE = CASE WHEN @WeekTo > @MonthTo THEN @WeekTo ELSE @MonthTo END;

    ;WITH A AS (
        SELECT a.AppointmentDate, a.Status
        FROM opd.Appointment a
        WHERE a.HospitalId = @HospitalId AND a.IsActive = 1 AND a.AppointmentDate BETWEEN @From AND @To
          AND (@DoctorId IS NULL OR a.DoctorId = @DoctorId)
    ), K AS (
        SELECT k.Kind, A.AppointmentDate
        FROM A
        CROSS APPLY (VALUES
            (CASE WHEN A.Status IN (N'CHECKED_IN', N'IN_CONSULT', N'COMPLETED') THEN N'consultations' END),
            (CASE WHEN A.Status <> N'CANCELLED' THEN N'appointments' END),
            (CASE WHEN A.Status = N'CANCELLED' THEN N'cancelled' END)) k(Kind)
        WHERE k.Kind IS NOT NULL
    )
    SELECT kinds.Kind,
           COUNT(CASE WHEN K.AppointmentDate = @Today THEN 1 END) AS TodayCount,
           COUNT(CASE WHEN K.AppointmentDate BETWEEN @WeekFrom AND @WeekTo THEN 1 END) AS WeekCount,
           COUNT(CASE WHEN K.AppointmentDate BETWEEN @MonthFrom AND @MonthTo THEN 1 END) AS MonthCount
    FROM (VALUES (N'consultations'), (N'appointments'), (N'cancelled')) kinds(Kind)
    LEFT JOIN K ON K.Kind = kinds.Kind
    GROUP BY kinds.Kind;

    SELECT a.Status, COUNT(*) AS [Count]
    FROM opd.Appointment a
    WHERE a.HospitalId = @HospitalId AND a.IsActive = 1 AND a.AppointmentDate = @Today AND a.Status <> N'CANCELLED'
      AND (@DoctorId IS NULL OR a.DoctorId = @DoctorId)
    GROUP BY a.Status
    ORDER BY COUNT(*) DESC;
END;
GO

-- ---- 22.8 Scheduling — reschedule, recurring and patient-portal booking (G-8, G-12) ----

-- INTERNAL: is this exact slot one of opd.usp_ListAvailableSlots' free slots for the doctor, and still in the future on
-- the clinic's clock? Called (EXEC, not INSERT … EXEC) by the booking procedures below, inside their transaction after
-- they take the doctor-day application lock, so two bookings cannot both see the slot free.
-- v3.6: opd.usp_CreateAppointment (front desk) uses it too, with @AllowPast = 1.
CREATE OR ALTER PROCEDURE opd.usp_CheckSlotAvailable
    @HospitalId     UNIQUEIDENTIFIER,
    @DoctorId       UNIQUEIDENTIFIER,
    @SlotStartLocal DATETIME2(0),
    @SlotEndLocal   DATETIME2(0),
    @IsAvailable    BIT OUTPUT,
    @AllowPast      BIT = 0                    -- v3.6: 1 for front-desk bookings (opd.usp_CreateAppointment)
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Slots TABLE (SlotStart NVARCHAR(19), SlotEnd NVARCHAR(19), IsBooked BIT, TimeZoneId NVARCHAR(50));
    DECLARE @Date DATE = CAST(@SlotStartLocal AS DATE),
            @Minutes INT = DATEDIFF(MINUTE, @SlotStartLocal, @SlotEndLocal),
            @NowLocal DATETIME2(0) = CAST(SYSDATETIMEOFFSET() AT TIME ZONE cfg.fn_HospitalWindowsTz(@HospitalId) AS DATETIME2(0));
    -- A slot must be 5 minutes to 12 hours long and end on the day it starts. Checked before calling
    -- opd.usp_ListAvailableSlots, whose slot loop never ends for a duration ≤ 0.
    IF @Minutes < 5 OR @Minutes > 720 OR CAST(@SlotEndLocal AS DATE) <> @Date OR (@AllowPast = 0 AND @SlotStartLocal <= @NowLocal)
    BEGIN
        SET @IsAvailable = 0;
        RETURN;
    END;
    INSERT INTO @Slots
    EXEC opd.usp_ListAvailableSlots @DoctorId = @DoctorId, @HospitalId = @HospitalId, @AppointmentDate = @Date, @DurationMinutes = @Minutes;
    SET @IsAvailable = CASE WHEN EXISTS (SELECT 1 FROM @Slots WHERE IsBooked = 0 AND SlotStart = CONVERT(NVARCHAR(19), @SlotStartLocal, 126))
                            THEN 1 ELSE 0 END;
END;
GO

-- SchedulingService.Reschedule: a BOOKED appointment moves to another free slot of the same doctor and the same length.
-- As in memory, the original is CANCELLED and a new appointment is created (Notes 'Rescheduled'), in one transaction.
-- Returns the new appointment (opd.usp_CreateAppointment's shape).
CREATE OR ALTER PROCEDURE opd.usp_UpdateAppointmentSlot
    @HospitalId        UNIQUEIDENTIFIER,
    @AppointmentId     UNIQUEIDENTIFIER,
    @NewSlotStartLocal DATETIME2(0),       -- a SlotStart from opd.usp_ListAvailableSlots, unchanged
    @ModifiedBy        UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @PatientId UNIQUEIDENTIFIER, @DoctorId UNIQUEIDENTIFIER, @Type NVARCHAR(20), @Status NVARCHAR(15), @Minutes INT;
    SELECT @PatientId = PatientId, @DoctorId = DoctorId, @Type = [Type], @Status = Status,
           @Minutes = DATEDIFF(MINUTE, SlotStart, SlotEnd)
    FROM opd.Appointment WHERE AppointmentId = @AppointmentId AND HospitalId = @HospitalId AND IsActive = 1;
    IF @DoctorId IS NULL RETURN;                                     -- not found: empty result
    IF @Status <> N'BOOKED' THROW 50213, N'Only a booked appointment that has not been checked in can be rescheduled.', 1;

    DECLARE @NewEnd DATETIME2(0) = DATEADD(MINUTE, @Minutes, @NewSlotStartLocal), @Free BIT,
            @Lock NVARCHAR(255) = N'opd.Appointment:' + CONVERT(NVARCHAR(36), @DoctorId) + N':' + CONVERT(NVARCHAR(10), CAST(@NewSlotStartLocal AS DATE), 23);
    DECLARE @LockResult INT;
    BEGIN TRAN;
    EXEC @LockResult = sp_getapplock @Resource = @Lock, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
    IF @LockResult < 0 THROW 50220, N'The doctor''s schedule is being changed by someone else. Try again.', 1;
    EXEC opd.usp_CheckSlotAvailable @HospitalId = @HospitalId, @DoctorId = @DoctorId,
         @SlotStartLocal = @NewSlotStartLocal, @SlotEndLocal = @NewEnd, @IsAvailable = @Free OUTPUT;
    IF @Free = 0
    BEGIN
        ROLLBACK;
        THROW 50214, N'That time is no longer free — choose another slot.', 1;
    END;

    UPDATE opd.Appointment
    SET Status = N'CANCELLED', Notes = LEFT(ISNULL(Notes + N' · ', N'') + N'Rescheduled', 300),
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE AppointmentId = @AppointmentId AND Status = N'BOOKED';

    EXEC opd.usp_CreateAppointment @HospitalId = @HospitalId, @PatientId = @PatientId, @DoctorId = @DoctorId,
         @SlotStartLocal = @NewSlotStartLocal, @SlotEndLocal = @NewEnd, @Type = @Type, @Notes = N'Rescheduled', @CreatedBy = @ModifiedBy, @SkipSlotCheck = 1;
    COMMIT;
    -- AUDIT: the service records UPDATE on the original (EntityType = 'Appointment') with the new appointment id
END;
GO

-- SchedulingService.BookRecurring: @Count visits, @EveryDays apart from @StartDate; on each day the first free slot of the
-- appointment type's length (or the doctor's slot length). A day with no free slot is skipped, as in memory.
-- Returns the appointments created (0 rows = none could be booked). v3.5: only future slots (clinic clock) are used.
CREATE OR ALTER PROCEDURE opd.usp_CreateRecurringAppointments
    @HospitalId UNIQUEIDENTIFIER,
    @PatientId  UNIQUEIDENTIFIER,
    @DoctorId   UNIQUEIDENTIFIER,
    @StartDate  DATE,
    @EveryDays  INT,                       -- 7 = weekly, 14 = fortnightly, …
    @Count      INT,
    @Type       NVARCHAR(20) = N'FOLLOWUP',
    @CreatedBy  UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @EveryDays NOT BETWEEN 1 AND 365 OR @Count NOT BETWEEN 1 AND 52
        THROW 50215, N'Choose up to 52 visits, between 1 and 365 days apart.', 1;
    IF NOT EXISTS (SELECT 1 FROM opd.Patient WHERE PatientId = @PatientId AND HospitalId = @HospitalId)
        THROW 50202, N'Patient not found.', 1;

    DECLARE @Duration INT = (SELECT DurationMinutes FROM cfg.AppointmentType WHERE HospitalId = @HospitalId AND Code = @Type);
    DECLARE @NowLocal DATETIME2(0) = CAST(SYSDATETIMEOFFSET() AT TIME ZONE cfg.fn_HospitalWindowsTz(@HospitalId) AS DATETIME2(0));
    DECLARE @Slots TABLE (SlotStart NVARCHAR(19), SlotEnd NVARCHAR(19), IsBooked BIT, TimeZoneId NVARCHAR(50));
    DECLARE @Created TABLE (AppointmentId UNIQUEIDENTIFIER, AppointmentDate DATE, SlotStart DATETIMEOFFSET(0), SlotEnd DATETIMEOFFSET(0),
                            TimeZoneId NVARCHAR(50), SlotStartLocal DATETIMEOFFSET(0), SlotEndLocal DATETIMEOFFSET(0), Status NVARCHAR(15));
    DECLARE @i INT = 0, @Date DATE, @S DATETIME2(0), @E DATETIME2(0), @Lock NVARCHAR(255), @Note NVARCHAR(300);

    DECLARE @LockResult INT;
    BEGIN TRAN;
    WHILE @i < @Count
    BEGIN
        SET @Date = DATEADD(DAY, @i * @EveryDays, @StartDate);
        SET @Lock = N'opd.Appointment:' + CONVERT(NVARCHAR(36), @DoctorId) + N':' + CONVERT(NVARCHAR(10), @Date, 23);
        EXEC @LockResult = sp_getapplock @Resource = @Lock, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @LockResult < 0 THROW 50220, N'The doctor''s schedule is being changed by someone else. Try again.', 1;
        DELETE FROM @Slots;
        INSERT INTO @Slots
        EXEC opd.usp_ListAvailableSlots @DoctorId = @DoctorId, @HospitalId = @HospitalId, @AppointmentDate = @Date, @DurationMinutes = @Duration;
        SELECT @S = NULL, @E = NULL;
        SELECT TOP 1 @S = CONVERT(DATETIME2(0), SlotStart, 126), @E = CONVERT(DATETIME2(0), SlotEnd, 126)
        FROM @Slots WHERE IsBooked = 0 AND CONVERT(DATETIME2(0), SlotStart, 126) > @NowLocal
        ORDER BY SlotStart;
        IF @S IS NOT NULL
        BEGIN
            SET @Note = N'Recurring ' + CAST(@i + 1 AS NVARCHAR(5)) + N'/' + CAST(@Count AS NVARCHAR(5));
            INSERT INTO @Created
            EXEC opd.usp_CreateAppointment @HospitalId = @HospitalId, @PatientId = @PatientId, @DoctorId = @DoctorId,
                 @SlotStartLocal = @S, @SlotEndLocal = @E, @Type = @Type, @Notes = @Note, @CreatedBy = @CreatedBy, @SkipSlotCheck = 1;
        END;
        SET @i += 1;
    END;
    COMMIT;

    SELECT AppointmentId, AppointmentDate, SlotStart, SlotEnd, TimeZoneId, SlotStartLocal, SlotEndLocal, Status
    FROM @Created ORDER BY SlotStart;
END;
GO

-- Patient portal (self-book.aspx FindPatient): find the patient by MRN, or by phone number (same digits). Anonymous
-- caller, so v3.5 returns only what the page needs — never the full name, phone or date of birth.
-- TODO (privacy): an MRN or phone number alone identifies the patient, as it does in memory. Requiring a second factor
-- (date of birth, or an SMS code) is a product decision; add @Dob here when it is made.
CREATE OR ALTER PROCEDURE opd.usp_GetPortalPatient
    @HospitalId UNIQUEIDENTIFIER,
    @Mrn        NVARCHAR(20) = NULL,
    @Phone      NVARCHAR(20) = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Digits NVARCHAR(50) = cfg.fn_DigitsOnly(@Phone);
    SET @Mrn = NULLIF(LTRIM(RTRIM(@Mrn)), N'');
    IF @Mrn IS NULL AND LEN(@Digits) < 7 RETURN;

    SELECT TOP 1 p.PatientId, p.Mrn,
           -- "Ravi K." — first name and last initial, for the confirmation screen
           LEFT(COALESCE(NULLIF(p.FirstName, N''), LEFT(p.FullName, CHARINDEX(N' ', p.FullName + N' ') - 1)), 60)
             + CASE WHEN COALESCE(NULLIF(p.LastName, N''), NULLIF(LTRIM(SUBSTRING(p.FullName, CHARINDEX(N' ', p.FullName + N' ') + 1, 200)), N'')) IS NULL
                    THEN N'' ELSE N' ' + LEFT(COALESCE(NULLIF(p.LastName, N''), LTRIM(SUBSTRING(p.FullName, CHARINDEX(N' ', p.FullName + N' ') + 1, 200))), 1) + N'.' END
             AS DisplayName
    FROM opd.Patient p
    WHERE p.HospitalId = @HospitalId AND p.IsActive = 1
      AND ((@Mrn IS NOT NULL AND p.Mrn = @Mrn)
           OR (@Mrn IS NULL AND cfg.fn_DigitsOnly(p.Phone) = @Digits))
    ORDER BY p.CreatedAt;
END;
GO

-- Patient portal (self-book.aspx BookButton_Click): re-checks the slot and books it atomically (Notes 'Booked online').
-- The doctor must be an active DOCTOR of the hospital. Returns opd.usp_CreateAppointment's shape.
CREATE OR ALTER PROCEDURE opd.usp_CreatePortalAppointment
    @HospitalId     UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER,
    @DoctorId       UNIQUEIDENTIFIER,
    @SlotStartLocal DATETIME2(0),
    @SlotEndLocal   DATETIME2(0),
    @Type           NVARCHAR(20) = N'NEW'
AS BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM opd.Patient WHERE PatientId = @PatientId AND HospitalId = @HospitalId AND IsActive = 1)
        THROW 50202, N'Patient not found.', 1;
    IF NOT EXISTS (SELECT 1 FROM sec.ApplicationUser u INNER JOIN sec.UserRole r ON r.UserId = u.UserId AND r.[Role] = N'DOCTOR'
                   WHERE u.UserId = @DoctorId AND u.HospitalId = @HospitalId AND u.IsActive = 1)
        THROW 50216, N'Choose a doctor from the list.', 1;
    IF NOT EXISTS (SELECT 1 FROM cfg.AppointmentType WHERE HospitalId = @HospitalId AND Code = @Type AND IsActive = 1)
        THROW 50216, N'Choose a visit type from the list.', 1;

    DECLARE @Free BIT,
            @Lock NVARCHAR(255) = N'opd.Appointment:' + CONVERT(NVARCHAR(36), @DoctorId) + N':' + CONVERT(NVARCHAR(10), CAST(@SlotStartLocal AS DATE), 23);
    DECLARE @LockResult INT;
    BEGIN TRAN;
    EXEC @LockResult = sp_getapplock @Resource = @Lock, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
    IF @LockResult < 0 THROW 50220, N'The doctor''s schedule is being changed by someone else. Try again.', 1;
    EXEC opd.usp_CheckSlotAvailable @HospitalId = @HospitalId, @DoctorId = @DoctorId,
         @SlotStartLocal = @SlotStartLocal, @SlotEndLocal = @SlotEndLocal, @IsAvailable = @Free OUTPUT;
    IF @Free = 0
    BEGIN
        ROLLBACK;
        THROW 50214, N'That time was just taken — please choose another slot.', 1;
    END;
    EXEC opd.usp_CreateAppointment @HospitalId = @HospitalId, @PatientId = @PatientId, @DoctorId = @DoctorId,
         @SlotStartLocal = @SlotStartLocal, @SlotEndLocal = @SlotEndLocal, @Type = @Type, @Notes = N'Booked online', @CreatedBy = NULL, @SkipSlotCheck = 1;
    COMMIT;
    -- AUDIT: the service records CREATE (EntityType = 'Appointment', Details 'Booked online by patient {Mrn}')
END;
GO

-- ---- 22.9 Consultation — in-progress lookup, medication history, outside medicines, removing an order (G-9 … G-11) ----

-- ConsultationService.FindInProgress: the doctor's open consultation for the patient, without creating one.
CREATE OR ALTER PROCEDURE cln.usp_GetInProgressConsultation
    @HospitalId UNIQUEIDENTIFIER,
    @PatientId  UNIQUEIDENTIFIER,
    @DoctorId   UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 ConsultationId, PatientId, DoctorId, AppointmentId, VisitDate, TimeZoneId, VisitLocalDate, Status, IsLocked
    FROM cln.Consultation
    WHERE HospitalId = @HospitalId AND PatientId = @PatientId AND DoctorId = @DoctorId
      AND Status = N'IN_PROGRESS' AND IsActive = 1
    ORDER BY VisitDate DESC;
END;
GO

-- ConsultationService.ListMedicationHistory: every prescription of the patient — this clinic's and outside medicines,
-- all statuses — newest first by period start (else prescribed date).
CREATE OR ALTER PROCEDURE cln.usp_ListPatientMedicationHistory
    @HospitalId UNIQUEIDENTIFIER,
    @PatientId  UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT rx.PrescriptionId, rx.ConsultationId, rx.PatientId, rx.Source, rx.SourceDetail, rx.Category,
           rx.MedicineName, rx.Strength, rx.GenericClass, rx.TherapeuticClass, rx.DrugForm, rx.DeliveryRoute,
           rx.TakeInQty, rx.TakeInUnit, rx.TimesPerDay, rx.DurationValue, rx.DurationUnit, rx.PeriodStart, rx.PeriodEnd,
           rx.FrequencyMode, rx.FrequencyPeriod, rx.TimingBf, rx.TimingAn, rx.TimingNi, rx.TimingEm, rx.Prn,
           rx.Instructions, rx.DynamicInstruction, rx.ValidUntil, rx.PrescribedDate, rx.Status,
           c.DoctorId, d.FullName AS DoctorName
    FROM cln.Prescription rx
    LEFT JOIN cln.Consultation c ON c.ConsultationId = rx.ConsultationId
    LEFT JOIN sec.ApplicationUser d ON d.UserId = c.DoctorId
    WHERE rx.PatientId = @PatientId AND rx.HospitalId = @HospitalId AND rx.IsActive = 1
    ORDER BY COALESCE(CAST(rx.PeriodStart AS DATETIMEOFFSET(0)), rx.PrescribedDate) DESC;
END;
GO

-- ConsultationService.SaveOutsideMedication: a medicine the patient takes that another prescriber started
-- (Source = 'OUTSIDE', no consultation). New: generic / therapeutic class copied from the formulary when the name
-- matches a brand. Existing (@PrescriptionId): only the fields the Medications tab edits are replaced.
CREATE OR ALTER PROCEDURE cln.usp_SaveOutsideMedication
    @HospitalId         UNIQUEIDENTIFIER,
    @PatientId          UNIQUEIDENTIFIER,
    @PrescriptionId     UNIQUEIDENTIFIER = NULL,   -- NULL = new
    @MedicineName       NVARCHAR(200),
    @Strength           NVARCHAR(50) = NULL,
    @DrugForm           NVARCHAR(30) = NULL,
    @TakeInQty          DECIMAL(6,2) = 1,
    @TakeInUnit         NVARCHAR(20) = NULL,
    @TimesPerDay        INT = 1,
    @TimingNi           BIT = 0,
    @Prn                BIT = 0,
    @FrequencyMode      NVARCHAR(10) = NULL,
    @DynamicInstruction NVARCHAR(500) = NULL,
    @Instructions       NVARCHAR(500) = NULL,
    @PeriodStart        DATE = NULL,
    @SourceDetail       NVARCHAR(120) = NULL,      -- who prescribed it
    @ModifiedBy         UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SET @MedicineName = LTRIM(RTRIM(@MedicineName));
    IF ISNULL(@MedicineName, N'') = N'' THROW 50217, N'Enter the medicine name.', 1;

    IF @PrescriptionId IS NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM opd.Patient WHERE PatientId = @PatientId AND HospitalId = @HospitalId)
            THROW 50202, N'Patient not found.', 1;
        DECLARE @Out TABLE (PrescriptionId UNIQUEIDENTIFIER);
        INSERT INTO cln.Prescription (HospitalId, ConsultationId, PatientId, Source, SourceDetail, MedicineName, Strength,
                                      GenericClass, TherapeuticClass, DrugForm, TakeInQty, TakeInUnit, TimesPerDay, TimingNi, Prn,
                                      FrequencyMode, DynamicInstruction, Instructions, PeriodStart, Status, CreatedBy)
        OUTPUT inserted.PrescriptionId INTO @Out
        SELECT @HospitalId, NULL, @PatientId, N'OUTSIDE', @SourceDetail, @MedicineName, @Strength,
               f.GenericClass, f.TherapeuticClass, @DrugForm, ISNULL(@TakeInQty, 1), @TakeInUnit, ISNULL(@TimesPerDay, 1),
               ISNULL(@TimingNi, 0), ISNULL(@Prn, 0), @FrequencyMode, @DynamicInstruction, @Instructions, @PeriodStart, N'ACTIVE',
               ISNULL(@ModifiedBy, '00000000-0000-0000-0000-000000000000')
        FROM (SELECT 1 AS One) x
        OUTER APPLY (SELECT TOP 1 GenericClass, TherapeuticClass FROM cfg.DrugFormulary
                     WHERE HospitalId = @HospitalId AND BrandName = @MedicineName AND IsActive = 1) f;
        SELECT @PrescriptionId = PrescriptionId FROM @Out;
    END
    ELSE
        UPDATE cln.Prescription
        SET MedicineName = @MedicineName, Strength = @Strength, DrugForm = @DrugForm, TakeInQty = ISNULL(@TakeInQty, 1),
            TakeInUnit = @TakeInUnit, TimesPerDay = ISNULL(@TimesPerDay, 1), TimingNi = ISNULL(@TimingNi, 0), Prn = ISNULL(@Prn, 0),
            FrequencyMode = @FrequencyMode, DynamicInstruction = @DynamicInstruction, Instructions = @Instructions,
            PeriodStart = @PeriodStart, SourceDetail = @SourceDetail,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
        WHERE PrescriptionId = @PrescriptionId AND HospitalId = @HospitalId AND PatientId = @PatientId
          AND Source = N'OUTSIDE' AND IsActive = 1;

    SELECT PrescriptionId, PatientId, Source, SourceDetail, MedicineName, Strength, GenericClass, TherapeuticClass, DrugForm,
           TakeInQty, TakeInUnit, TimesPerDay, TimingNi, Prn, FrequencyMode, DynamicInstruction, Instructions, PeriodStart,
           PrescribedDate, Status
    FROM cln.Prescription
    WHERE PrescriptionId = @PrescriptionId AND HospitalId = @HospitalId AND Source = N'OUTSIDE' AND IsActive = 1;
END;
GO

-- ConsultationService.RemoveOrder: an order can be removed only while ORDERED (not yet sent to or claimed by a lab).
-- v3.5: soft delete (IsActive = 0), and refused once the consultation is signed (memory did not check the lock).
CREATE OR ALTER PROCEDURE cln.usp_DeleteInvestigationOrder
    @HospitalId UNIQUEIDENTIFIER,
    @OrderId    UNIQUEIDENTIFIER,
    @ModifiedBy UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Status NVARCHAR(15), @Locked BIT;
    SELECT @Status = o.Status, @Locked = c.IsLocked
    FROM cln.InvestigationOrder o INNER JOIN cln.Consultation c ON c.ConsultationId = o.ConsultationId
    WHERE o.OrderId = @OrderId AND o.HospitalId = @HospitalId AND o.IsActive = 1;
    IF @Status IS NULL RETURN;                                       -- not found
    IF @Status <> N'ORDERED' OR @Locked = 1
        THROW 50218, N'This order has already been sent to a lab or the consultation is signed; it cannot be removed.', 1;

    UPDATE cln.InvestigationOrder
    SET IsActive = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE OrderId = @OrderId AND Status = N'ORDERED' AND IsActive = 1;
    SELECT @OrderId AS OrderId;
END;
GO

-- ---- 22.10 Catalog and billing — edit a drug, service charge on/off (G-13) ----

-- CatalogService.SaveDrug (existing drug): replaces the editable fields; DefaultRoute is kept, as in memory.
CREATE OR ALTER PROCEDURE cfg.usp_UpdateDrug
    @HospitalId            UNIQUEIDENTIFIER,
    @DrugId                UNIQUEIDENTIFIER,
    @BrandName             NVARCHAR(200),
    @GenericName           NVARCHAR(200) = NULL,
    @Composition           NVARCHAR(500) = NULL,
    @TherapeuticClass      NVARCHAR(300) = NULL,
    @GenericClass          NVARCHAR(200) = NULL,
    @DrugForm              NVARCHAR(30) = NULL,
    @DefaultDosage         NVARCHAR(20) = NULL,
    @DefaultFrequency      NVARCHAR(20) = NULL,
    @DefaultDurationDays   INT = NULL,
    @DefaultInstructions   NVARCHAR(300) = NULL,
    @IsControlledSubstance BIT = 0,
    @ModifiedBy            UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SET @BrandName = LTRIM(RTRIM(@BrandName));
    IF ISNULL(@BrandName, N'') = N'' THROW 50219, N'Enter the brand name.', 1;

    UPDATE cfg.DrugFormulary
    SET BrandName = @BrandName, GenericName = @GenericName, Composition = @Composition, TherapeuticClass = @TherapeuticClass,
        GenericClass = @GenericClass, DrugForm = @DrugForm, DefaultDosage = @DefaultDosage, DefaultFrequency = @DefaultFrequency,
        DefaultDurationDays = @DefaultDurationDays, DefaultInstructions = @DefaultInstructions,
        IsControlledSubstance = ISNULL(@IsControlledSubstance, 0),
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE DrugId = @DrugId AND HospitalId = @HospitalId;

    SELECT DrugId, HospitalId, BrandName, GenericName, Composition, TherapeuticClass, GenericClass, DrugForm, DefaultRoute,
           DefaultDosage, DefaultFrequency, DefaultDurationDays, DefaultInstructions, IsControlledSubstance, IsActive
    FROM cfg.DrugFormulary WHERE DrugId = @DrugId AND HospitalId = @HospitalId;
END;
GO

-- BillingService.ListAllServiceCharges: the full master list for Settings → Services, inactive included.
CREATE OR ALTER PROCEDURE bil.usp_ListAllServiceCharges
    @HospitalId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT sc.ServiceChargeId, sc.ServiceCode, sc.ServiceName, sc.Category, sc.DefaultAmount, sc.TaxPercent, sc.IsActive, h.Currency
    FROM bil.ServiceChargeMaster sc
    INNER JOIN cfg.Hospital h ON h.HospitalId = sc.HospitalId
    WHERE sc.HospitalId = @HospitalId
    ORDER BY sc.Category, sc.ServiceName;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_UpdateServiceChargeActive
    @HospitalId      UNIQUEIDENTIFIER,
    @ServiceChargeId UNIQUEIDENTIFIER,
    @IsActive        BIT = NULL,              -- NULL toggles (BillingService.ToggleServiceCharge)
    @ModifiedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    UPDATE bil.ServiceChargeMaster
    SET IsActive = COALESCE(@IsActive, CASE WHEN IsActive = 1 THEN 0 ELSE 1 END),
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE ServiceChargeId = @ServiceChargeId AND HospitalId = @HospitalId;
    SELECT ServiceChargeId, ServiceName, IsActive
    FROM bil.ServiceChargeMaster WHERE ServiceChargeId = @ServiceChargeId AND HospitalId = @HospitalId;
END;
GO

-- ---- 22.11 Lab partners (G-5) ----

CREATE OR ALTER PROCEDURE cfg.usp_ListLabPartners
    @HospitalId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT LabPartnerId, Name, IsActive FROM cfg.LabPartner
    WHERE HospitalId = @HospitalId AND IsActive = 1
    ORDER BY Name;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_GetLabPartner
    @HospitalId   UNIQUEIDENTIFIER,
    @LabPartnerId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT LabPartnerId, Name, IsActive FROM cfg.LabPartner
    WHERE LabPartnerId = @LabPartnerId AND HospitalId = @HospitalId;
END;
GO

-- LabService.SaveLabPartner: by name (case-insensitive) — an existing partner is re-enabled and returned, else created.
CREATE OR ALTER PROCEDURE cfg.usp_SaveLabPartner
    @HospitalId UNIQUEIDENTIFIER,
    @Name       NVARCHAR(100),
    @CreatedBy  UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SET @Name = LTRIM(RTRIM(@Name));
    IF ISNULL(@Name, N'') = N'' THROW 50232, N'Enter the lab name.', 1;

    DECLARE @Id UNIQUEIDENTIFIER = (SELECT LabPartnerId FROM cfg.LabPartner WHERE HospitalId = @HospitalId AND Name = @Name);
    IF @Id IS NOT NULL
        UPDATE cfg.LabPartner SET IsActive = 1, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @CreatedBy
        WHERE LabPartnerId = @Id AND IsActive = 0;
    ELSE
    BEGIN
        DECLARE @Out TABLE (LabPartnerId UNIQUEIDENTIFIER);
        INSERT INTO cfg.LabPartner (HospitalId, Name, CreatedBy)
        OUTPUT inserted.LabPartnerId INTO @Out
        VALUES (@HospitalId, @Name, ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));
        SELECT @Id = LabPartnerId FROM @Out;
    END;
    EXEC cfg.usp_GetLabPartner @HospitalId = @HospitalId, @LabPartnerId = @Id;
END;
GO

-- ---- 22.12 Lab — claim a requisition by the patient's mobile number (G-6) ----

-- LabService.FindOrdersByMobile: every open order (ORDERED / SENT, not yet taken by a lab) of every patient in the
-- hospital registered with this mobile number (last 10 digits of Phone). Each lookup is logged in sec.LabClaimAttempt;
-- 5 lookups that found nothing within 15 minutes (since the user's last successful one) lock the user out for the
-- rest of that window (50231) — replacing the in-process LabService.ClaimFailures counter.
-- The service only calls this for a LAB_PARTNER caller and passes the caller's own LabPartnerId.
CREATE OR ALTER PROCEDURE cln.usp_SearchLabOrdersByMobile
    @HospitalId   UNIQUEIDENTIFIER,
    @UserId       UNIQUEIDENTIFIER,
    @LabPartnerId UNIQUEIDENTIFIER,
    @Mobile       NVARCHAR(20)
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Key NVARCHAR(10) = cfg.fn_MobileKey(@Mobile);
    IF LEN(@Key) < 10 THROW 50230, N'Enter the full mobile number.', 1;
    IF @LabPartnerId IS NULL THROW 50230, N'This account is not linked to a lab.', 1;

    DECLARE @WindowStart DATETIMEOFFSET(3) = DATEADD(MINUTE, -15, SYSUTCDATETIME());
    DECLARE @LastSuccess DATETIMEOFFSET(3) = (SELECT MAX(CreatedAt) FROM sec.LabClaimAttempt WHERE UserId = @UserId AND IsSuccess = 1);
    IF (SELECT COUNT(*) FROM sec.LabClaimAttempt
        WHERE UserId = @UserId AND IsSuccess = 0 AND CreatedAt > @WindowStart
          AND (@LastSuccess IS NULL OR CreatedAt > @LastSuccess)) >= 5
        THROW 50231, N'Too many numbers with no match. Try again in 15 minutes.', 1;

    DECLARE @Found TABLE (OrderId UNIQUEIDENTIFIER, ConsultationId UNIQUEIDENTIFIER, PatientId UNIQUEIDENTIFIER, PatientName NVARCHAR(200),
                          PatientMrn NVARCHAR(20), TestId UNIQUEIDENTIFIER, TestName NVARCHAR(100), TestType NVARCHAR(20),
                          Status NVARCHAR(15), OrderedAt DATETIMEOFFSET(3));
    INSERT INTO @Found
    SELECT o.OrderId, o.ConsultationId, o.PatientId, p.FullName, p.Mrn, o.TestId, o.TestName, o.TestType, o.Status, o.OrderedAt
    FROM cln.InvestigationOrder o
    INNER JOIN opd.Patient p ON p.PatientId = o.PatientId
    WHERE o.HospitalId = @HospitalId AND o.IsActive = 1 AND o.LabPartnerId IS NULL
      AND o.Status IN (N'ORDERED', N'SENT')
      AND cfg.fn_MobileKey(p.Phone) = @Key;

    INSERT INTO sec.LabClaimAttempt (HospitalId, UserId, IsSuccess, CreatedBy)
    VALUES (@HospitalId, @UserId, CASE WHEN EXISTS (SELECT 1 FROM @Found) THEN 1 ELSE 0 END, @UserId);

    SELECT OrderId, ConsultationId, PatientId, PatientName, PatientMrn, TestId, TestName, TestType, Status, OrderedAt
    FROM @Found ORDER BY OrderedAt DESC;
END;
GO

-- LabService.ClaimOrder: the lab takes the order (LabPartnerId set, Status SENT) if it is still open and the mobile
-- number matches its patient. The LabPartnerId IS NULL condition makes the first of two competing claims win.
-- Empty result = not claimable (NotFound).
CREATE OR ALTER PROCEDURE cln.usp_ConfirmLabOrderClaim
    @HospitalId   UNIQUEIDENTIFIER,
    @OrderId      UNIQUEIDENTIFIER,
    @LabPartnerId UNIQUEIDENTIFIER,
    @Mobile       NVARCHAR(20),
    @ModifiedBy   UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Key NVARCHAR(10) = cfg.fn_MobileKey(@Mobile);
    IF LEN(@Key) < 10 THROW 50230, N'Enter the full mobile number.', 1;

    DECLARE @Out TABLE (OrderId UNIQUEIDENTIFIER, PatientId UNIQUEIDENTIFIER, TestName NVARCHAR(100), Status NVARCHAR(15), LabPartnerId UNIQUEIDENTIFIER);
    UPDATE o
    SET LabPartnerId = @LabPartnerId, Status = N'SENT', ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    OUTPUT inserted.OrderId, inserted.PatientId, inserted.TestName, inserted.Status, inserted.LabPartnerId INTO @Out
    FROM cln.InvestigationOrder o
    INNER JOIN opd.Patient p ON p.PatientId = o.PatientId
    WHERE o.OrderId = @OrderId AND o.HospitalId = @HospitalId AND o.IsActive = 1 AND o.LabPartnerId IS NULL
      AND o.Status IN (N'ORDERED', N'SENT') AND cfg.fn_MobileKey(p.Phone) = @Key;
    SELECT OrderId, PatientId, TestName, Status, LabPartnerId FROM @Out;
    -- AUDIT: the service records UPDATE (EntityType = 'InvestigationOrder', Details 'Claimed by lab')
END;
GO

-- ---- 22.13 Lab result attachments (G-4, metadata; the file itself is in the service's file store) ----

-- LabService.SaveAttachment. OrderId and LabPartnerId are taken from the result, not the caller.
CREATE OR ALTER PROCEDURE cln.usp_CreateLabResultAttachment
    @HospitalId       UNIQUEIDENTIFIER,
    @ResultId         UNIQUEIDENTIFIER,
    @FileName         NVARCHAR(255),
    @ContentType      NVARCHAR(100),
    @SizeBytes        BIGINT,
    @StoredFileName   NVARCHAR(100),
    @UploadedByUserId UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @OrderId UNIQUEIDENTIFIER, @LabPartnerId UNIQUEIDENTIFIER;
    SELECT @OrderId = OrderId, @LabPartnerId = LabPartnerId
    FROM cln.LabResult WHERE ResultId = @ResultId AND HospitalId = @HospitalId AND IsActive = 1;
    IF @OrderId IS NULL THROW 50235, N'Lab result not found.', 1;

    DECLARE @Out TABLE (AttachmentId UNIQUEIDENTIFIER, ResultId UNIQUEIDENTIFIER, OrderId UNIQUEIDENTIFIER, LabPartnerId UNIQUEIDENTIFIER,
                        FileName NVARCHAR(255), ContentType NVARCHAR(100), SizeBytes BIGINT, StoredFileName NVARCHAR(100),
                        UploadedByUserId UNIQUEIDENTIFIER, UploadedAt DATETIMEOFFSET(3));
    INSERT INTO cln.LabResultAttachment (ResultId, OrderId, HospitalId, LabPartnerId, FileName, ContentType, SizeBytes, StoredFileName,
                                         UploadedByUserId, CreatedBy)
    OUTPUT inserted.AttachmentId, inserted.ResultId, inserted.OrderId, inserted.LabPartnerId, inserted.FileName, inserted.ContentType,
           inserted.SizeBytes, inserted.StoredFileName, inserted.UploadedByUserId, inserted.UploadedAt
    INTO @Out
    VALUES (@ResultId, @OrderId, @HospitalId, @LabPartnerId, @FileName, @ContentType, @SizeBytes, @StoredFileName,
            @UploadedByUserId, ISNULL(@UploadedByUserId, '00000000-0000-0000-0000-000000000000'));
    SELECT AttachmentId, ResultId, OrderId, LabPartnerId, FileName, ContentType, SizeBytes, StoredFileName, UploadedByUserId, UploadedAt FROM @Out;
END;
GO

-- LabService.ListAttachments (@ResultId) and CountAttachmentsForOrder (@OrderId — the caller counts the rows).
CREATE OR ALTER PROCEDURE cln.usp_ListLabResultAttachments
    @HospitalId UNIQUEIDENTIFIER,
    @ResultId   UNIQUEIDENTIFIER = NULL,
    @OrderId    UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    IF @ResultId IS NULL AND @OrderId IS NULL THROW 50236, N'A result or an order is required.', 1;
    SELECT AttachmentId, ResultId, OrderId, LabPartnerId, FileName, ContentType, SizeBytes, StoredFileName, UploadedByUserId, UploadedAt
    FROM cln.LabResultAttachment
    WHERE HospitalId = @HospitalId AND IsActive = 1
      AND (@ResultId IS NULL OR ResultId = @ResultId)
      AND (@OrderId IS NULL OR OrderId = @OrderId)
    ORDER BY UploadedAt;
END;
GO

-- LabService.GetAttachment. The service applies LabService.CanViewAttachment (a lab partner sees only its own files).
CREATE OR ALTER PROCEDURE cln.usp_GetLabResultAttachment
    @HospitalId   UNIQUEIDENTIFIER,
    @AttachmentId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT AttachmentId, ResultId, OrderId, LabPartnerId, FileName, ContentType, SizeBytes, StoredFileName, UploadedByUserId, UploadedAt
    FROM cln.LabResultAttachment
    WHERE AttachmentId = @AttachmentId AND HospitalId = @HospitalId AND IsActive = 1;
END;
GO

-- ---- 22.14 In-app user notifications (G-17) ----

CREATE OR ALTER PROCEDURE sec.usp_CreateUserNotification
    @HospitalId UNIQUEIDENTIFIER,
    @UserId     UNIQUEIDENTIFIER,
    @Kind       NVARCHAR(30),
    @Title      NVARCHAR(200),
    @Message    NVARCHAR(500) = NULL,
    @Url        NVARCHAR(500) = NULL,
    @IsCritical BIT = 0,
    @EntityId   NVARCHAR(50) = NULL,
    @CreatedBy  UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- the recipient must be a user of the same hospital
    IF NOT EXISTS (SELECT 1 FROM sec.ApplicationUser WHERE UserId = @UserId AND HospitalId = @HospitalId) RETURN;
    DECLARE @Out TABLE (NotificationId UNIQUEIDENTIFIER, CreatedAt DATETIMEOFFSET(3));
    INSERT INTO sec.UserNotification (HospitalId, UserId, Kind, Title, [Message], Url, IsCritical, EntityId, CreatedBy)
    OUTPUT inserted.NotificationId, inserted.CreatedAt INTO @Out
    VALUES (@HospitalId, @UserId, @Kind, @Title, @Message, @Url, ISNULL(@IsCritical, 0), @EntityId,
            ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));
    SELECT NotificationId, CreatedAt FROM @Out;
END;
GO

-- UserNotificationService.ListRecent: newest first, except that unread critical ones always come first.
CREATE OR ALTER PROCEDURE sec.usp_ListUserNotifications
    @HospitalId UNIQUEIDENTIFIER,
    @UserId     UNIQUEIDENTIFIER,
    @Take       INT = 15
AS BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Take) NotificationId, Kind, Title, [Message], Url, IsCritical, EntityId, ReadAt, CreatedAt
    FROM sec.UserNotification
    WHERE UserId = @UserId AND HospitalId = @HospitalId
    ORDER BY CASE WHEN IsCritical = 1 AND ReadAt IS NULL THEN 0 ELSE 1 END, CreatedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_GetUserNotificationUnreadCount
    @HospitalId UNIQUEIDENTIFIER,
    @UserId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT COUNT(*) AS UnreadCount
    FROM sec.UserNotification
    WHERE UserId = @UserId AND HospitalId = @HospitalId AND ReadAt IS NULL;
END;
GO

-- UserNotificationService.MarkRead: the caller's own notification only. Empty result = not found / someone else's.
CREATE OR ALTER PROCEDURE sec.usp_UpdateUserNotificationRead
    @HospitalId     UNIQUEIDENTIFIER,
    @UserId         UNIQUEIDENTIFIER,
    @NotificationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    UPDATE sec.UserNotification
    SET ReadAt = SYSUTCDATETIME(), ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE NotificationId = @NotificationId AND UserId = @UserId AND HospitalId = @HospitalId AND ReadAt IS NULL;
    SELECT NotificationId, Kind, Title, [Message], Url, IsCritical, EntityId, ReadAt, CreatedAt
    FROM sec.UserNotification
    WHERE NotificationId = @NotificationId AND UserId = @UserId AND HospitalId = @HospitalId;
END;
GO

-- UserNotificationService.MarkAllRead
CREATE OR ALTER PROCEDURE sec.usp_UpdateUserNotificationsReadAll
    @HospitalId UNIQUEIDENTIFIER,
    @UserId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    UPDATE sec.UserNotification
    SET ReadAt = SYSUTCDATETIME(), ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE UserId = @UserId AND HospitalId = @HospitalId AND ReadAt IS NULL;
    SELECT @@ROWCOUNT AS UpdatedCount;
END;
GO

-- UserNotificationService.MarkReadForEntity: clears everyone's notifications about something that has been dealt with
-- (e.g. a lab result marked reviewed). Called by the service inside the operation that deals with it.
CREATE OR ALTER PROCEDURE sec.usp_UpdateUserNotificationReadForEntity
    @HospitalId UNIQUEIDENTIFIER,
    @EntityId   NVARCHAR(50),
    @ModifiedBy UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    UPDATE sec.UserNotification
    SET ReadAt = SYSUTCDATETIME(), ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE EntityId = @EntityId AND HospitalId = @HospitalId AND ReadAt IS NULL;
END;
GO

-- ---- 22.15 Consultation form designer (G-16) ----
-- The designer's editing rules (toggle / move / add sections, fields, options, units, presets, problems, diff,
-- restore defaults) stay in C# — they work on the whole ConsultFormConfig document. The database stores that
-- document as JSON: one DRAFT and one PUBLISHED row per hospital (cfg.ConsultationFormConfig). A hospital with no
-- PUBLISHED row uses the built-in default form (ConsultationConfigService.Catalog()).

CREATE OR ALTER PROCEDURE cfg.usp_GetConsultationFormConfig
    @HospitalId  UNIQUEIDENTIFIER,
    @ConfigState NVARCHAR(10) = N'PUBLISHED'     -- PUBLISHED | DRAFT
AS BEGIN
    SET NOCOUNT ON;
    SELECT HospitalId, ConfigState, Version, PublishedAt, PublishedBy, Sections, VitalUnits, ModifiedAt
    FROM cfg.ConsultationFormConfig
    WHERE HospitalId = @HospitalId AND ConfigState = @ConfigState;
END;
GO

-- Saves the whole draft after an edit. @ExpectedModifiedAt (the draft's ModifiedAt as last read; NULL for the first
-- save) stops two administrators overwriting each other's draft (50241 → the service reports a Conflict).
CREATE OR ALTER PROCEDURE cfg.usp_SaveConsultationFormDraft
    @HospitalId         UNIQUEIDENTIFIER,
    @Sections           NVARCHAR(MAX),           -- JSON: List<ConsultFormSection>
    @VitalUnits         NVARCHAR(MAX) = NULL,    -- JSON: List<VitalUnitSetting>
    @ExpectedModifiedAt DATETIMEOFFSET(3) = NULL,
    @ModifiedBy         UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF ISJSON(@Sections) <> 1 OR (@VitalUnits IS NOT NULL AND ISJSON(@VitalUnits) <> 1)
        THROW 50240, N'The consultation form could not be saved (invalid form data).', 1;

    BEGIN TRAN;
    DECLARE @Current DATETIMEOFFSET(3), @Exists BIT = 0;
    SELECT @Exists = 1, @Current = COALESCE(ModifiedAt, CreatedAt)
    FROM cfg.ConsultationFormConfig WITH (UPDLOCK, HOLDLOCK)
    WHERE HospitalId = @HospitalId AND ConfigState = N'DRAFT';

    IF @Exists = 1 AND @ExpectedModifiedAt IS NOT NULL AND @Current <> @ExpectedModifiedAt
    BEGIN
        ROLLBACK;
        THROW 50241, N'Someone else changed the draft. Reload Consultation Setup and try again.', 1;
    END;

    IF @Exists = 1
        UPDATE cfg.ConsultationFormConfig
        SET Sections = @Sections, VitalUnits = @VitalUnits, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
        WHERE HospitalId = @HospitalId AND ConfigState = N'DRAFT';
    ELSE
        -- a new draft starts at the published version (the draft is a copy of the live form being edited)
        INSERT INTO cfg.ConsultationFormConfig (HospitalId, ConfigState, Version, Sections, VitalUnits, CreatedBy, ModifiedAt, ModifiedBy)
        SELECT @HospitalId, N'DRAFT',
               ISNULL((SELECT Version FROM cfg.ConsultationFormConfig WHERE HospitalId = @HospitalId AND ConfigState = N'PUBLISHED'), 0),
               @Sections, @VitalUnits, ISNULL(@ModifiedBy, '00000000-0000-0000-0000-000000000000'), SYSUTCDATETIME(), @ModifiedBy;
    COMMIT;

    EXEC cfg.usp_GetConsultationFormConfig @HospitalId = @HospitalId, @ConfigState = N'DRAFT';
END;
GO

-- ConsultationConfigService.Publish, after the service has checked ListPublishProblems / ListDraftChanges and created
-- any pending custom vitals (cfg.usp_CreateVitalConfig) and saved the draft without the pending flags. Copies the draft
-- to PUBLISHED with Version + 1 and turns each vital on/off in cfg.VitalDefinition from the draft's VitalUnits
-- (VitalCode / Enabled). Returns the new version.
CREATE OR ALTER PROCEDURE cfg.usp_ConfirmConsultationFormPublish
    @HospitalId  UNIQUEIDENTIFIER,
    @PublishedBy UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;
    DECLARE @Sections NVARCHAR(MAX), @VitalUnits NVARCHAR(MAX);
    SELECT @Sections = Sections, @VitalUnits = VitalUnits
    FROM cfg.ConsultationFormConfig WITH (UPDLOCK, HOLDLOCK)
    WHERE HospitalId = @HospitalId AND ConfigState = N'DRAFT';
    IF @Sections IS NULL
    BEGIN
        ROLLBACK;
        THROW 50242, N'There is no draft to publish.', 1;
    END;

    DECLARE @Version INT = 1 + ISNULL((SELECT Version FROM cfg.ConsultationFormConfig WITH (UPDLOCK, HOLDLOCK)
                                       WHERE HospitalId = @HospitalId AND ConfigState = N'PUBLISHED'), 0);
    DECLARE @Now DATETIMEOFFSET(3) = SYSUTCDATETIME();

    IF EXISTS (SELECT 1 FROM cfg.ConsultationFormConfig WHERE HospitalId = @HospitalId AND ConfigState = N'PUBLISHED')
        UPDATE cfg.ConsultationFormConfig
        SET Version = @Version, PublishedAt = @Now, PublishedBy = @PublishedBy, Sections = @Sections, VitalUnits = @VitalUnits,
            ModifiedAt = @Now, ModifiedBy = @PublishedBy
        WHERE HospitalId = @HospitalId AND ConfigState = N'PUBLISHED';
    ELSE
        INSERT INTO cfg.ConsultationFormConfig (HospitalId, ConfigState, Version, PublishedAt, PublishedBy, Sections, VitalUnits, CreatedBy)
        VALUES (@HospitalId, N'PUBLISHED', @Version, @Now, @PublishedBy, @Sections, @VitalUnits, @PublishedBy);

    UPDATE cfg.ConsultationFormConfig
    SET Version = @Version, PublishedAt = @Now, PublishedBy = @PublishedBy, ModifiedAt = @Now, ModifiedBy = @PublishedBy
    WHERE HospitalId = @HospitalId AND ConfigState = N'DRAFT';

    IF @VitalUnits IS NOT NULL
        UPDATE vd
        SET IsEnabled = j.Enabled, ModifiedAt = @Now, ModifiedBy = @PublishedBy
        FROM cfg.VitalDefinition vd
        INNER JOIN OPENJSON(@VitalUnits) WITH (VitalCode NVARCHAR(40) '$.VitalCode', Enabled BIT '$.Enabled') j
            ON j.VitalCode = vd.VitalCode
        WHERE vd.HospitalId = @HospitalId AND j.Enabled IS NOT NULL AND vd.IsEnabled <> j.Enabled;
    COMMIT;
    -- AUDIT: the service records UPDATE (EntityType = 'ConsultationForm', Details 'Published version {n}')
    SELECT @Version AS Version, @Now AS PublishedAt;
END;
GO

-- ConsultationConfigService.DiscardDraft: the draft goes back to the published form (or is removed when nothing has
-- been published, so the built-in default applies again).
CREATE OR ALTER PROCEDURE cfg.usp_CancelConsultationFormDraft
    @HospitalId UNIQUEIDENTIFIER,
    @ModifiedBy UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM cfg.ConsultationFormConfig WHERE HospitalId = @HospitalId AND ConfigState = N'PUBLISHED')
    BEGIN
        UPDATE d
        SET Sections = p.Sections, VitalUnits = p.VitalUnits, Version = p.Version, PublishedAt = p.PublishedAt,
            PublishedBy = p.PublishedBy, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
        FROM cfg.ConsultationFormConfig d
        INNER JOIN cfg.ConsultationFormConfig p ON p.HospitalId = d.HospitalId AND p.ConfigState = N'PUBLISHED'
        WHERE d.HospitalId = @HospitalId AND d.ConfigState = N'DRAFT';
    END
    ELSE
        -- Hard delete: nothing was published, so the draft row is the only copy and the default form replaces it.
        DELETE FROM cfg.ConsultationFormConfig WHERE HospitalId = @HospitalId AND ConfigState = N'DRAFT';
END;
GO

-- ConsultationConfigService.SaveCustomValues: one section's custom field values for a consultation.
-- @ValuesJson: [{"FieldKey","Value","SectionLabel","FieldLabel","InputType","DisplayValue"}, …] — labels, input type and
-- display value are worked out by the service from the published form. An empty Value removes that field's row.
-- v3.5: refused once the consultation is signed (memory did not check the lock).
CREATE OR ALTER PROCEDURE cln.usp_SaveConsultationCustomValues
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER,
    @SectionKey     NVARCHAR(60),
    @ValuesJson     NVARCHAR(MAX),
    @UpdatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM cln.Consultation WHERE ConsultationId = @ConsultationId AND HospitalId = @HospitalId AND IsLocked = 0)
        THROW 50245, N'The consultation was not found or is already signed.', 1;
    IF ISJSON(@ValuesJson) <> 1 THROW 50246, N'The form values could not be read.', 1;

    DECLARE @V TABLE (FieldKey NVARCHAR(60) PRIMARY KEY, [Value] NVARCHAR(MAX), SectionLabel NVARCHAR(100), FieldLabel NVARCHAR(100),
                      InputType NVARCHAR(10), DisplayValue NVARCHAR(MAX));
    INSERT INTO @V
    SELECT FieldKey, NULLIF([Value], N''), SectionLabel, FieldLabel, ISNULL(NULLIF(InputType, N''), N'TEXT'), DisplayValue
    FROM OPENJSON(@ValuesJson) WITH (FieldKey NVARCHAR(60) '$.FieldKey', [Value] NVARCHAR(MAX) '$.Value',
                                     SectionLabel NVARCHAR(100) '$.SectionLabel', FieldLabel NVARCHAR(100) '$.FieldLabel',
                                     InputType NVARCHAR(10) '$.InputType', DisplayValue NVARCHAR(MAX) '$.DisplayValue')
    WHERE ISNULL(FieldKey, N'') <> N'';

    BEGIN TRAN;
    -- Hard delete: a cleared field has no value to keep (the table has no IsActive column).
    DELETE cv FROM cln.ConsultationCustomValue cv
    INNER JOIN @V v ON v.FieldKey = cv.FieldKey AND v.[Value] IS NULL
    WHERE cv.ConsultationId = @ConsultationId AND cv.SectionKey = @SectionKey;

    UPDATE cv
    SET [Value] = v.[Value], SectionLabel = ISNULL(v.SectionLabel, cv.SectionLabel), FieldLabel = ISNULL(v.FieldLabel, cv.FieldLabel),
        InputType = v.InputType, DisplayValue = ISNULL(v.DisplayValue, v.[Value]),
        UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UpdatedBy
    FROM cln.ConsultationCustomValue cv
    INNER JOIN @V v ON v.FieldKey = cv.FieldKey AND v.[Value] IS NOT NULL
    WHERE cv.ConsultationId = @ConsultationId AND cv.SectionKey = @SectionKey;

    INSERT INTO cln.ConsultationCustomValue (HospitalId, ConsultationId, SectionKey, FieldKey, [Value], SectionLabel, FieldLabel,
                                             InputType, DisplayValue, UpdatedBy, CreatedBy)
    SELECT @HospitalId, @ConsultationId, @SectionKey, v.FieldKey, v.[Value], v.SectionLabel, v.FieldLabel, v.InputType,
           ISNULL(v.DisplayValue, v.[Value]), @UpdatedBy, ISNULL(@UpdatedBy, '00000000-0000-0000-0000-000000000000')
    FROM @V v
    WHERE v.[Value] IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM cln.ConsultationCustomValue cv
                      WHERE cv.ConsultationId = @ConsultationId AND cv.SectionKey = @SectionKey AND cv.FieldKey = v.FieldKey);
    COMMIT;
END;
GO

-- ConsultationConfigService.GetCustomValues / ListCustomValues: the consultation's non-empty custom values. The service
-- orders them by the published form's section and field order (and fills missing labels from it).
CREATE OR ALTER PROCEDURE cln.usp_ListConsultationCustomValues
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT ConsultationId, SectionKey, FieldKey, [Value], SectionLabel, FieldLabel, InputType, DisplayValue, UpdatedAt, UpdatedBy
    FROM cln.ConsultationCustomValue
    WHERE ConsultationId = @ConsultationId AND HospitalId = @HospitalId AND [Value] IS NOT NULL AND [Value] <> N''
    ORDER BY CreatedAt;
END;
GO

-- ============================================================
-- SUMMARY
-- ============================================================
-- 157 stored procedures + 3 reporting views, covering all 150
-- api.js methods (some methods share one underlying procedure —
-- e.g. checkEffectiveFee/getDoctorFee both call bil.usp_CheckEffectiveFee;
-- listPhrases/searchPhrases/deletePhrase and their duplicate api.js
-- entries collapse to the single canonical (last-defined) signature
-- per the task's dedupe rule; listUsers/listInvoices/listVitalsConfig/
-- listSpecialtyConfig duplicates likewise collapse to one procedure;
-- exportTableCsv shares sec.usp_ListExportData_* with listExportData).
--
-- v2.1 addendum (5 new procedures, 7 methods): cfg.usp_ToggleSpecialty,
-- cfg.usp_CreateVitalConfig, cfg.usp_CreateDrug, cfg.usp_UpdateDrugStatus,
-- cfg.usp_UpdateMessageTemplate were added to close out api.js methods
-- (toggleSpecialty, createVitalConfig, createDrug, updateDrugStatus,
-- updateMessageTemplate, exportTableCsv) that were added/fixed during a
-- Phase-1 JSDOM functional-test bug-fix pass, plus wiring the endpoint
-- for sec.usp_UpdatePermission (proc already existed in v2.0; api.js
-- didn't call it yet at the time).
-- See docs/API-SPECIFICATION.md for the full method → SP → endpoint map,
-- and the TODO comments above for schema/data.js field mismatches that
-- should be reconciled before Phase 4 cutover.
--
-- v3.0 (schema v4.0): all procedures renamed to the model-aligned column
-- names; the v2.x TODOs about SymptomCatalog, DrugFormulary and
-- LabTestCatalog are resolved by the v4.0 schema (per-hospital catalogs,
-- cfg.LabTestSpecialty). No procedure covers yet: the tables new in v4.0
-- (cfg.ConsultationFormConfig, sec.UserNotification,
-- opd.PatientConsentDocument,
-- cln.ConsultationCustomValue) and cln.LabResultAttachment.
--
-- v3.6: opd.usp_CreatePatient / UpdatePatient widened and MRN from settings (cfg.fn_FormatMrn);
-- sec.usp_GetUserCredential, sec.usp_RecordLoginAttempt; front-desk slot re-check in
-- opd.usp_CreateAppointment (@SkipSlotCheck for the booking procedures that already checked);
-- slot-length guard in opd.usp_ListAvailableSlots.
--
-- v3.5: section 22 — 52 procedures + cfg.fn_DigitsOnly / cfg.fn_MobileKey for the
-- in-memory features that had no procedure (API-SPECIFICATION.md v3.2 Known Gaps #1,
-- API-INTEGRATION-GUIDE.md §8). The v3.0 note above about tables with no procedure
-- (cfg.ConsultationFormConfig, sec.UserNotification, opd.PatientConsentDocument,
-- cln.ConsultationCustomValue, cln.LabResultAttachment) is resolved.
-- opd.usp_CheckSlotAvailable is an internal helper (OUTPUT parameter) for the
-- booking procedures.
--
-- v3.4: procedures added in v3.1 renamed to the DATA-LAYER-ARCHITECTURE verbs:
-- inv.usp_UpdateInventoryItemActive (was ToggleInventoryItem; takes @IsActive),
-- inv.usp_RecordStockReceipt / RecordStockAdjustment / RecordStockConsumption
-- (were ReceiveStock / AdjustStock / ConsumeStockForConsultation),
-- inv.usp_SaveItemServices (was SetItemServices), cln.usp_CreateConsultationServiceItem /
-- DeleteConsultationServiceItem (were Add / RemoveConsultationServiceItem).
--
-- v3.3: sec.usp_SaveDoctorProfile and cfg.usp_UpdateHospital (the two endpoints
-- the API spec had no procedure for); cfg.fn_RenderDoctorName renders doctor
-- display names, and cfg.usp_SaveHospitalSettings now re-renders them itself
-- when DOCTOR_NAME_TEMPLATE changes.
--
-- v3.2: UTC everywhere — every point in time is DATETIMEOFFSET (+00:00); section 0
-- adds cfg.fn_HospitalWindowsTz / cfg.fn_HospitalToday; "today", report ranges and
-- visit / appointment dates use the hospital's timezone; opd.usp_CreateAppointment
-- takes hospital-local wall-clock times (@SlotStartLocal / @SlotEndLocal) and stores
-- UTC + TimeZoneId; appointment lists also return SlotStartLocal / SlotEndLocal.
--
-- v3.1: section 21 — clinic inventory (inv.*) and Services & Procedures
-- items (cln.ConsultationServiceItem: a service, optionally with a stocked
-- item linked to it via inv.InventoryItemService and its own ItemQty); sign-off
-- (cln.usp_ConfirmSignOff) now deducts stock earliest-expiry first, all or
-- nothing, in the same transaction; invoice lines carry InventoryItemId.
