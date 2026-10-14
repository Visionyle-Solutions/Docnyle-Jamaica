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

   Naming: {SqlSchema}.usp_{Verb}{Entity}{Qualifier}
   Verbs (14, per architecture doc §2): Get, List, Search, Check,
     Create, Save, Update, Delete, Submit, Confirm, Cancel,
     Approve, Reject, Record.

   Conventions applied throughout:
   - SET NOCOUNT ON in every procedure.
   - Every hospital-scoped list/search/get procedure accepts
     @HospitalId and filters WHERE HospitalId = @HospitalId.
   - Every table access uses the external UNIQUEIDENTIFIER
     {Table}Id column, never the internal {Table}PkId — callers
     never see PkId (see schema.sql header, dual-key pattern).
   - New rows: INSERT ... OUTPUT inserted.* — NEWSEQUENTIALID()
     default supplies the id (see DF_{schema}_{tbl}_Id defaults
     in schema.sql), matching data.js's nextId(prefix) pattern.
   - Soft delete: tables with IsActive/IsDeleted/Status columns
     are updated, never hard-deleted, matching data.js behavior
     (e.g. deleteAllergy sets Status='INACTIVE'). Where data.js
     itself performs an in-memory splice() AND the schema table
     has no IsActive/IsDeleted/Status column to soft-delete on
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

CREATE OR ALTER PROCEDURE sec.usp_AuthenticateUser
    @Email          NVARCHAR(100),
    @PasswordHash   NVARCHAR(200)
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='LOGIN' or 'LOGIN_FAIL')
    SELECT u.UserId, u.HospitalId, u.FullName, u.Email, u.Phone, u.[Role],
           u.SpecialtyId, u.LicenseNumber, u.LabPartnerId, u.IsActive
    FROM sec.ApplicationUser u
    INNER JOIN cfg.Hospital h ON h.HospitalId = u.HospitalId AND h.IsDeleted = 0
    WHERE u.Email = @Email AND u.PasswordHash = @PasswordHash
      AND u.IsActive = 1 AND u.IsDeleted = 0;

    INSERT INTO sec.LoginAttempt (EmailAttempted, IsSuccess)
    VALUES (@Email, CASE WHEN @@ROWCOUNT > 0 THEN 1 ELSE 0 END);
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_CreateUser
    @HospitalId     UNIQUEIDENTIFIER,
    @FullName       NVARCHAR(200),
    @Email          NVARCHAR(100),
    @PasswordHash   NVARCHAR(200) = 'demo123',
    @Phone          NVARCHAR(20) = NULL,
    @Role           NVARCHAR(20),
    @SpecialtyId    UNIQUEIDENTIFIER = NULL,
    @LicenseNumber  NVARCHAR(50) = NULL,
    @LabPartnerId   UNIQUEIDENTIFIER = NULL,
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    INSERT INTO sec.ApplicationUser (HospitalId, FullName, Email, PasswordHash, Phone,
                                     [Role], SpecialtyId, LicenseNumber, LabPartnerId, CreatedBy)
    OUTPUT inserted.UserId, inserted.HospitalId, inserted.FullName, inserted.Email,
           inserted.Phone, inserted.[Role], inserted.SpecialtyId, inserted.LicenseNumber,
           inserted.IsActive, inserted.CreatedAt
    VALUES (@HospitalId, @FullName, @Email, @PasswordHash, @Phone,
            @Role, @SpecialtyId, @LicenseNumber, @LabPartnerId, @CreatedBy);
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_UpdateUser
    @UserId         UNIQUEIDENTIFIER,
    @FullName       NVARCHAR(200) = NULL,
    @Phone          NVARCHAR(20) = NULL,
    @Role           NVARCHAR(20) = NULL,
    @SpecialtyId    UNIQUEIDENTIFIER = NULL,
    @LicenseNumber  NVARCHAR(50) = NULL,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='UPDATE') if Role changed
    UPDATE sec.ApplicationUser SET
        FullName = ISNULL(@FullName, FullName),
        Phone = ISNULL(@Phone, Phone),
        [Role] = ISNULL(@Role, [Role]),
        SpecialtyId = ISNULL(@SpecialtyId, SpecialtyId),
        LicenseNumber = ISNULL(@LicenseNumber, LicenseNumber),
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE UserId = @UserId AND IsDeleted = 0;

    SELECT UserId, HospitalId, FullName, Email, Phone, [Role], SpecialtyId,
           LicenseNumber, IsActive FROM sec.ApplicationUser WHERE UserId = @UserId;
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
    WHERE UserId = @UserId AND IsDeleted = 0;

    SELECT UserId, IsActive FROM sec.ApplicationUser WHERE UserId = @UserId;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListUsers
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT UserId, FullName, Email, Phone, [Role], SpecialtyId,
           LicenseNumber, LabPartnerId, IsActive, CreatedAt
    FROM sec.ApplicationUser
    WHERE HospitalId = @HospitalId AND IsDeleted = 0
    ORDER BY FullName;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_CreateUserSession
    @UserId         UNIQUEIDENTIFIER,
    @SessionToken   NVARCHAR(100),
    @Browser        NVARCHAR(100) = NULL,
    @IpAddress      NVARCHAR(45) = NULL,
    @ExpiresAt      DATETIME2(3)
AS BEGIN
    SET NOCOUNT ON;
    UPDATE sec.UserSession
    SET IsActive = 0, InvalidatedAt = SYSUTCDATETIME(), InvalidationReason = 'NEW_LOGIN'
    WHERE UserId = @UserId AND IsActive = 1;

    INSERT INTO sec.UserSession (UserId, SessionToken, Browser, IpAddress, ExpiresAt)
    OUTPUT inserted.UserSessionId, inserted.SessionToken, inserted.CreatedAt, inserted.ExpiresAt
    VALUES (@UserId, @SessionToken, @Browser, @IpAddress, @ExpiresAt);
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
    SELECT HospitalId, Name, [Address], Phone, Email, Country, Currency, Timezone,
           LicenseNumber, PatientIdLabel, NationalHealthIdLabel, IsNationalHealthIdMandatory,
           DefaultTempUnit, DefaultRxValidityDays, IsActive
    FROM cfg.Hospital WHERE HospitalId = @HospitalId AND IsDeleted = 0;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_ListSpecialties
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT SpecialtyId, HospitalId, SpecialtyName, IsActive
    FROM cfg.Specialty
    WHERE HospitalId = @HospitalId AND IsActive = 1 AND IsDeleted = 0
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
    WHERE HospitalId = @HospitalId AND IsDeleted = 0
    ORDER BY SpecialtyName;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_CreateSpecialty
    @HospitalId     UNIQUEIDENTIFIER,
    @SpecialtyName  NVARCHAR(100),
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    INSERT INTO cfg.Specialty (HospitalId, SpecialtyName, CreatedBy)
    OUTPUT inserted.SpecialtyId, inserted.SpecialtyName, inserted.IsActive
    VALUES (@HospitalId, @SpecialtyName, @CreatedBy);
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
    WHERE HospitalId = @HospitalId AND IsActive = 1 AND IsDeleted = 0
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
    WHERE HospitalId = @HospitalId AND IsDeleted = 0
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
    WHERE HospitalId = @HospitalId AND IsEnabled = 1 AND IsDeleted = 0
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
    INSERT INTO cfg.VitalDefinition (HospitalId, VitalCode, DisplayLabel, AllowedUnits, MinValue, MaxValue, SortOrder, CreatedBy)
    OUTPUT inserted.VitalDefinitionId, inserted.HospitalId, inserted.VitalCode, inserted.DisplayLabel,
           inserted.AllowedUnits, inserted.MinValue, inserted.MaxValue, inserted.SortOrder, inserted.IsEnabled
    VALUES (@HospitalId, @VitalCode, @DisplayLabel, @AllowedUnits, @MinValue, @MaxValue, @SortOrder, @CreatedBy);
END;
GO

-- ---- Medical history / event config (quick-pick master lists) ----

CREATE OR ALTER PROCEDURE cfg.usp_ListMedicalHistoryItems
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT MedicalHistoryItemId, HospitalId, ItemName, SortOrder
    FROM cfg.MedicalHistoryItem
    WHERE HospitalId = @HospitalId AND IsActive = 1 AND IsDeleted = 0
    ORDER BY SortOrder;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_ListMedicalHistoryConfig
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- Admin config view — includes inactive items, matches data.js listMedicalHistoryConfig
    SELECT MedicalHistoryItemId, HospitalId, ItemName, SortOrder, IsActive
    FROM cfg.MedicalHistoryItem
    WHERE HospitalId = @HospitalId AND IsDeleted = 0
    ORDER BY SortOrder;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_CreateMedicalHistoryConfigItem
    @HospitalId     UNIQUEIDENTIFIER,
    @ItemName       NVARCHAR(100),
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Sort INT;
    SELECT @Sort = ISNULL(MAX(SortOrder), 0) + 1 FROM cfg.MedicalHistoryItem WHERE HospitalId = @HospitalId;
    INSERT INTO cfg.MedicalHistoryItem (HospitalId, ItemName, SortOrder, CreatedBy)
    OUTPUT inserted.MedicalHistoryItemId, inserted.ItemName, inserted.SortOrder
    VALUES (@HospitalId, @ItemName, @Sort, @CreatedBy);
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
    SELECT MedicalEventTypeId, HospitalId, EventTypeName
    FROM cfg.MedicalEventType
    WHERE HospitalId = @HospitalId AND IsActive = 1 AND IsDeleted = 0
    ORDER BY EventTypeName;
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
           PhotoBase64, EmergencyContactName, EmergencyContactPhone,
           IsActive, CreatedAt
    FROM opd.Patient
    WHERE PatientId = @PatientId AND IsDeleted = 0;
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
        FROM opd.Patient WHERE HospitalId = @HospitalId AND IsDeleted = 0
        ORDER BY FullName;
        RETURN;
    END;
    SELECT PatientId, Mrn, FullName, Gender, Dob, Phone, Email, IsActive
    FROM opd.Patient
    WHERE HospitalId = @HospitalId AND IsDeleted = 0
      AND (FullName LIKE '%' + @Term + '%'
           OR Mrn LIKE '%' + @Term + '%'
           OR Phone LIKE '%' + @Term + '%'
           OR NationalHealthId LIKE '%' + @Term + '%')
    ORDER BY FullName;
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
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='CREATE', EntityType='Patient')
    DECLARE @Mrn NVARCHAR(20);
    SELECT @Mrn = 'MRN-' + RIGHT('000000' + CAST(ISNULL(MAX(PatientPkId), 0) + 1 AS NVARCHAR), 6)
    FROM opd.Patient WHERE HospitalId = @HospitalId;

    INSERT INTO opd.Patient (HospitalId, Mrn, FullName, Gender, Dob, Phone, Email,
                             [Address], NationalHealthId, InsuranceProvider, InsurancePolicyNo,
                             EmergencyContactName, EmergencyContactPhone, CreatedBy)
    OUTPUT inserted.PatientId, inserted.Mrn, inserted.FullName, inserted.CreatedAt
    VALUES (@HospitalId, @Mrn, @FullName, @Gender, @Dob, @Phone, @Email,
            @Address, @NationalHealthId, @InsuranceProvider, @InsurancePolicyNo,
            @EmergencyContactName, @EmergencyContactPhone, @CreatedBy);
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
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    UPDATE opd.Patient SET
        FullName = ISNULL(@FullName, FullName),
        Phone = ISNULL(@Phone, Phone),
        Email = ISNULL(@Email, Email),
        [Address] = ISNULL(@Address, [Address]),
        NationalHealthId = ISNULL(@NationalHealthId, NationalHealthId),
        InsuranceProvider = ISNULL(@InsuranceProvider, InsuranceProvider),
        InsurancePolicyNo = ISNULL(@InsurancePolicyNo, InsurancePolicyNo),
        EmergencyContactName = ISNULL(@EmergencyContactName, EmergencyContactName),
        EmergencyContactPhone = ISNULL(@EmergencyContactPhone, EmergencyContactPhone),
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE PatientId = @PatientId AND IsDeleted = 0;

    SELECT PatientId, Mrn, FullName, Phone, Email FROM opd.Patient WHERE PatientId = @PatientId;
END;
GO

-- ---- Allergies ----

CREATE OR ALTER PROCEDURE opd.usp_ListAllergies
    @PatientId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT PatientAllergyId, HospitalId, PatientId, AllergyType, AllergyName,
           Reaction, Severity, Status, OnsetDate
    FROM opd.PatientAllergy
    WHERE PatientId = @PatientId AND IsDeleted = 0
    ORDER BY CreatedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_CreateAllergy
    @HospitalId     UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER,
    @AllergyType    NVARCHAR(20),
    @AllergyName    NVARCHAR(100),
    @Reaction       NVARCHAR(200) = NULL,
    @Severity       NVARCHAR(10),
    @OnsetDate      DATE = NULL,
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    INSERT INTO opd.PatientAllergy (HospitalId, PatientId, AllergyType, AllergyName,
                                     Reaction, Severity, OnsetDate, CreatedBy)
    OUTPUT inserted.PatientAllergyId, inserted.AllergyName, inserted.Severity, inserted.Status
    VALUES (@HospitalId, @PatientId, @AllergyType, @AllergyName, @Reaction, @Severity, @OnsetDate, @CreatedBy);
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_UpdateAllergy
    @PatientAllergyId UNIQUEIDENTIFIER,
    @Reaction       NVARCHAR(200) = NULL,
    @Severity       NVARCHAR(10) = NULL,
    @Status         NVARCHAR(10) = NULL,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    UPDATE opd.PatientAllergy SET
        Reaction = ISNULL(@Reaction, Reaction),
        Severity = ISNULL(@Severity, Severity),
        Status = ISNULL(@Status, Status),
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE PatientAllergyId = @PatientAllergyId AND IsDeleted = 0;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_DeleteAllergy
    @PatientAllergyId UNIQUEIDENTIFIER,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- Soft delete: sets Status='RESOLVED'-equivalent per data.js ('INACTIVE'); schema CK
    -- constraint CK_opd_PA_Status only allows ACTIVE|RESOLVED, so RESOLVED is used here.
    -- TODO: schema.sql CK_opd_PA_Status does not include an 'INACTIVE' value used by
    -- data.js deleteAllergy — mapped to 'RESOLVED' as the closest equivalent; align enum
    -- if the true intent is a distinct "withdrawn" state.
    UPDATE opd.PatientAllergy
    SET Status = 'RESOLVED', ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE PatientAllergyId = @PatientAllergyId;
END;
GO

-- ---- Patient medical history ----

CREATE OR ALTER PROCEDURE opd.usp_ListPatientMedicalHistory
    @PatientId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT PatientMedicalHistoryId, HospitalId, PatientId, ConditionName,
           SinceMonth, SinceYear, Notes, CreatedAt, CreatedBy
    FROM opd.PatientMedicalHistory
    WHERE PatientId = @PatientId AND IsDeleted = 0
    ORDER BY CreatedAt DESC;
END;
GO

-- Alias covering both data.js getPatientMedicalHistory and listPatientMedicalHistory
-- (same underlying table/shape; kept as two procs to match the two distinct api.js
-- method names 1:1).
CREATE OR ALTER PROCEDURE opd.usp_GetPatientMedicalHistory
    @PatientId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT PatientMedicalHistoryId, HospitalId, PatientId, ConditionName,
           SinceMonth, SinceYear, Notes, CreatedAt, CreatedBy
    FROM opd.PatientMedicalHistory
    WHERE PatientId = @PatientId AND IsDeleted = 0
    ORDER BY CreatedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_CreatePatientMedicalHistory
    @HospitalId     UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER,
    @ConditionName  NVARCHAR(100),
    @SinceMonth     INT = NULL,
    @SinceYear      INT = NULL,
    @Notes          NVARCHAR(500) = NULL,
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    INSERT INTO opd.PatientMedicalHistory (HospitalId, PatientId, ConditionName, SinceMonth, SinceYear, Notes, CreatedBy)
    OUTPUT inserted.PatientMedicalHistoryId, inserted.ConditionName, inserted.CreatedAt
    VALUES (@HospitalId, @PatientId, @ConditionName, @SinceMonth, @SinceYear, @Notes, @CreatedBy);
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_SavePatientMedicalHistory
    @HospitalId     UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER,
    @ConditionName  NVARCHAR(100),
    @SinceMonth     INT = NULL,
    @SinceYear      INT = NULL,
    @Notes          NVARCHAR(500) = NULL,
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- Upsert by (PatientId, ConditionName) — matches data.js checklist auto-save behavior
    MERGE opd.PatientMedicalHistory AS tgt
    USING (SELECT @PatientId AS PatientId, @ConditionName AS ConditionName) AS src
        ON tgt.PatientId = src.PatientId AND tgt.ConditionName = src.ConditionName AND tgt.IsDeleted = 0
    WHEN MATCHED THEN UPDATE SET SinceMonth = @SinceMonth, SinceYear = @SinceYear, Notes = @Notes
    WHEN NOT MATCHED THEN
        INSERT (HospitalId, PatientId, ConditionName, SinceMonth, SinceYear, Notes, CreatedBy)
        VALUES (@HospitalId, @PatientId, @ConditionName, @SinceMonth, @SinceYear, @Notes, @CreatedBy);

    SELECT PatientMedicalHistoryId, ConditionName, SinceMonth, SinceYear, Notes
    FROM opd.PatientMedicalHistory WHERE PatientId = @PatientId AND ConditionName = @ConditionName AND IsDeleted = 0;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_DeletePatientMedicalHistory
    @PatientId      UNIQUEIDENTIFIER,
    @ConditionName  NVARCHAR(100)
AS BEGIN
    SET NOCOUNT ON;
    -- data.js performs a hard splice(); schema has IsDeleted, so soft delete is used here
    -- (preferred per architecture doc §4.8) rather than a physical DELETE.
    UPDATE opd.PatientMedicalHistory SET IsDeleted = 1
    WHERE PatientId = @PatientId AND ConditionName = @ConditionName;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_UpdatePatientMedicalHistory
    @PatientId      UNIQUEIDENTIFIER,
    @ConditionName  NVARCHAR(100),
    @SinceMonth     INT = NULL,
    @SinceYear      INT = NULL,
    @Notes          NVARCHAR(500) = NULL
AS BEGIN
    SET NOCOUNT ON;
    UPDATE opd.PatientMedicalHistory SET
        SinceMonth = ISNULL(@SinceMonth, SinceMonth),
        SinceYear = ISNULL(@SinceYear, SinceYear),
        Notes = ISNULL(@Notes, Notes)
    WHERE PatientId = @PatientId AND ConditionName = @ConditionName AND IsDeleted = 0;
END;
GO

-- ---- Patient medical events ----

CREATE OR ALTER PROCEDURE opd.usp_ListPatientMedicalEvents
    @PatientId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT PatientMedicalEventId, HospitalId, PatientId, EventType, EventDate, Notes, CreatedAt
    FROM opd.PatientMedicalEvent
    WHERE PatientId = @PatientId AND IsDeleted = 0
    ORDER BY CreatedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_CreatePatientMedicalEvent
    @HospitalId     UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER,
    @EventType      NVARCHAR(50),
    @EventDate      DATE = NULL,
    @Notes          NVARCHAR(500) = NULL,
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    INSERT INTO opd.PatientMedicalEvent (HospitalId, PatientId, EventType, EventDate, Notes, CreatedBy)
    OUTPUT inserted.PatientMedicalEventId, inserted.EventType, inserted.CreatedAt
    VALUES (@HospitalId, @PatientId, @EventType, @EventDate, @Notes, @CreatedBy);
END;
GO

-- ---- Patient family history ----

CREATE OR ALTER PROCEDURE opd.usp_ListPatientFamilyHistory
    @PatientId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT PatientFamilyHistoryId, HospitalId, PatientId, Relation, [Condition], Notes, CreatedAt
    FROM opd.PatientFamilyHistory
    WHERE PatientId = @PatientId AND IsDeleted = 0
    ORDER BY CreatedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_CreatePatientFamilyHistory
    @HospitalId     UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER,
    @Relation       NVARCHAR(30),
    @Condition      NVARCHAR(100),
    @Notes          NVARCHAR(300) = NULL,
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    INSERT INTO opd.PatientFamilyHistory (HospitalId, PatientId, Relation, [Condition], Notes, CreatedBy)
    OUTPUT inserted.PatientFamilyHistoryId, inserted.Relation, inserted.[Condition]
    VALUES (@HospitalId, @PatientId, @Relation, @Condition, @Notes, @CreatedBy);
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
           CAST(c.VisitDate AS DATE) AS VisitDate,
           u.FullName AS DoctorName,
           STUFF((
               SELECT ', ' + cs.SymptomName
               FROM cln.ConsultationSymptom cs
               WHERE cs.ConsultationId = c.ConsultationId AND cs.[State] = 'YES'
               FOR XML PATH('')), 1, 2, '') AS ChiefComplaint,
           (SELECT TOP (1) dx.ICD10Code + ' ' + dx.[Description]
              FROM cln.ConsultationDiagnosis dx
              WHERE dx.ConsultationId = c.ConsultationId AND dx.IsPrimary = 1 AND dx.IsDeleted = 0
              ORDER BY dx.SequenceOrder) AS PrimaryDiagnosis
    FROM cln.Consultation c
    INNER JOIN sec.ApplicationUser u ON u.UserId = c.DoctorId
    WHERE c.PatientId = @PatientId AND c.Status = 'LOCKED' AND c.IsDeleted = 0
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
    WHERE PatientId = @PatientId AND DoctorId = @DoctorId AND Status = 'LOCKED' AND IsDeleted = 0
    ORDER BY VisitDate DESC;

    SELECT ConsultationId, VisitDate FROM cln.Consultation WHERE ConsultationId = @ConsultationId;

    SELECT PrescriptionId, MedicineName, Strength, DrugForm, DeliveryRoute, Category,
           TakeInQty, TimesPerDay, DurationValue, DurationUnit, Instructions, IsPrn
    FROM cln.Prescription
    WHERE ConsultationId = @ConsultationId AND Status = 'ACTIVE' AND IsDeleted = 0;
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
           a.SlotStart, a.SlotEnd, a.[Type], a.Status, a.Notes
    FROM opd.Appointment a
    INNER JOIN opd.Patient p ON p.PatientId = a.PatientId
    WHERE a.DoctorId = @DoctorId AND a.HospitalId = @HospitalId
      AND a.AppointmentDate = CAST(SYSUTCDATETIME() AS DATE)
      AND a.IsDeleted = 0
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
           a.SlotStart, a.SlotEnd, a.[Type], a.Status, a.Notes,
           a.CheckedInAt, a.ConsultStartedAt, a.CompletedAt
    FROM opd.Appointment a
    INNER JOIN opd.Patient p ON p.PatientId = a.PatientId
    WHERE a.DoctorId = @DoctorId AND a.HospitalId = @HospitalId
      AND a.AppointmentDate = @AppointmentDate AND a.IsDeleted = 0
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
           a.SlotStart, a.SlotEnd, a.[Type], a.Status, a.Notes,
           a.CheckedInAt, a.ConsultStartedAt, a.CompletedAt
    FROM opd.Appointment a
    INNER JOIN opd.Patient p ON p.PatientId = a.PatientId
    INNER JOIN sec.ApplicationUser u ON u.UserId = a.DoctorId
    WHERE a.HospitalId = @HospitalId
      AND a.AppointmentDate = @AppointmentDate AND a.IsDeleted = 0
    ORDER BY a.SlotStart, u.FullName;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_ListDoctorSlotConfig
    @DoctorId       UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT SlotConfigId, HospitalId, DoctorId, DayOfWeek, StartTime, EndTime
    FROM opd.DoctorSlotConfig
    WHERE DoctorId = @DoctorId AND IsActive = 1 AND IsDeleted = 0
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
    IF EXISTS (SELECT 1 FROM opd.DoctorLeave WHERE DoctorId = @DoctorId AND HospitalId = @HospitalId AND LeaveDate = @AppointmentDate) RETURN;

    DECLARE @Dow NVARCHAR(3) = (SELECT code FROM (VALUES
        (1,'MON'),(2,'TUE'),(3,'WED'),(4,'THU'),(5,'FRI'),(6,'SAT'),(7,'SUN')) AS d(n, code)
        WHERE d.n = ((DATEPART(WEEKDAY, @AppointmentDate) + @@DATEFIRST - 2) % 7) + 1);
    DECLARE @Slot INT = COALESCE(@DurationMinutes, TRY_CAST(cfg.fn_ResolveSetting(@HospitalId, @DoctorId, 'APPT_SLOT_MINS') AS INT), 15);
    DECLARE @Buffer INT = COALESCE(TRY_CAST(cfg.fn_ResolveSetting(@HospitalId, NULL, 'APPT_BUFFER_MINS') AS INT), 0);

    DECLARE @Grid TABLE (StartMin INT NOT NULL);
    DECLARE @WStart INT, @WEnd INT, @M INT;
    DECLARE w CURSOR LOCAL FAST_FORWARD FOR
        SELECT DATEDIFF(MINUTE, '00:00', CAST(StartTime AS TIME)), DATEDIFF(MINUTE, '00:00', CAST(EndTime AS TIME))
        FROM opd.DoctorSlotConfig
        WHERE DoctorId = @DoctorId AND HospitalId = @HospitalId AND DayOfWeek = @Dow AND IsActive = 1 AND IsDeleted = 0
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
        SELECT DATEDIFF(MINUTE, '00:00', CAST(a.SlotStart AS TIME)) AS S,
               DATEDIFF(MINUTE, '00:00', CAST(COALESCE(a.SlotEnd, a.SlotStart) AS TIME)) + @Buffer AS E
        FROM opd.Appointment a
        WHERE a.DoctorId = @DoctorId AND a.AppointmentDate = @AppointmentDate AND a.Status <> 'CANCELLED' AND a.IsDeleted = 0
    )
    SELECT CONVERT(NVARCHAR(10), @AppointmentDate, 23) + 'T' + FORMAT(DATEADD(MINUTE, g.StartMin, CAST('00:00' AS TIME)), 'HH\:mm') + ':00' AS SlotStart,
           CONVERT(NVARCHAR(10), @AppointmentDate, 23) + 'T' + FORMAT(DATEADD(MINUTE, g.StartMin + @Slot, CAST('00:00' AS TIME)), 'HH\:mm') + ':00' AS SlotEnd,
           CASE WHEN EXISTS (SELECT 1 FROM Taken t WHERE g.StartMin < t.E AND g.StartMin + @Slot + @Buffer > t.S)
                THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsBooked
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
               WHERE DoctorId = @DoctorId AND HospitalId = @HospitalId AND IsActive = 1 AND IsDeleted = 0) x) AS WorkingDays
    FROM (SELECT 1 AS one) d
    LEFT JOIN opd.DoctorLeave l ON l.DoctorId = @DoctorId AND l.HospitalId = @HospitalId AND l.LeaveDate = @AppointmentDate;
    -- result set 2: that day's windows
    SELECT StartTime, EndTime FROM opd.DoctorSlotConfig
    WHERE DoctorId = @DoctorId AND HospitalId = @HospitalId AND DayOfWeek = @Dow AND IsActive = 1 AND IsDeleted = 0
    ORDER BY StartTime;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_SaveDoctorSlotConfig
    @DoctorId UNIQUEIDENTIFIER, @HospitalId UNIQUEIDENTIFIER,
    @WindowsJson NVARCHAR(MAX)          -- [{ "DayOfWeek":"MON","StartTime":"09:00","EndTime":"13:00" }, …]
AS BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRAN;
        UPDATE opd.DoctorSlotConfig SET IsDeleted = 1, IsActive = 0
        WHERE DoctorId = @DoctorId AND HospitalId = @HospitalId AND IsDeleted = 0;
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
    USING (SELECT [key] AS SettingKey, [value] AS [Value] FROM OPENJSON(@ValuesJson) WHERE [key] <> 'MRN_SEQ') AS s
       ON t.HospitalId = @HospitalId AND t.SettingKey = s.SettingKey
    WHEN MATCHED THEN UPDATE SET [Value] = s.[Value], ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHEN NOT MATCHED THEN INSERT (HospitalId, SettingKey, [Value], ModifiedAt, ModifiedBy)
        VALUES (@HospitalId, s.SettingKey, s.[Value], SYSUTCDATETIME(), @ModifiedBy);
    -- DOCTOR_NAME_TEMPLATE changed → the API layer re-renders sec.ApplicationUser.FullName for the hospital's doctors
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
    JOIN OPENJSON(@ValuesJson) j ON j.[key] = d.SettingKey
    WHERE d.HospitalId = @HospitalId AND d.DoctorId = @DoctorId AND NULLIF(j.[value], '') IS NULL;
    MERGE opd.DoctorSetting AS t
    USING (SELECT [key] AS SettingKey, [value] AS [Value] FROM OPENJSON(@ValuesJson) WHERE NULLIF([value], '') IS NOT NULL) AS s
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
        INSERT INTO opd.DoctorLeave (HospitalId, DoctorId, LeaveDate, Reason, CreatedBy) VALUES (@HospitalId, @DoctorId, @LeaveDate, @Reason, @CreatedBy);
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
    WHERE u.HospitalId = @HospitalId AND u.[Role] = 'DOCTOR' AND u.IsActive = 1 AND u.IsDeleted = 0
    ORDER BY u.FullName;   -- PasswordHash is never returned
END;
GO

-- sec.usp_SaveDoctorProfile: INSERT/UPDATE sec.ApplicationUser (Role = 'DOCTOR') + replace
-- sec.DoctorSpecialty rows; FullName = DisplayNameOverride or the DOCTOR_NAME_TEMPLATE
-- rendering (done in the API layer, same rules as data.js _renderDoctorName). A new doctor
-- also gets ₹0 NEW / FOLLOWUP rows in bil.DoctorFeeSchedule.

CREATE OR ALTER PROCEDURE bil.usp_GetConsultationCharge
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- Mirrors data.js getConsultationCharge:
    -- appointment (same patient + doctor + day) → cfg.AppointmentType (chargeable? FeeCategory, duration)
    -- → CHARGE_MODEL FLAT (bil.DoctorFeeSchedule for FeeCategory) | UNIT (CEILING(duration / unit) × rate)
    -- → free follow-up if a PAID (fee > 0, not cancelled) consultation with the same doctor is within FREE_FOLLOWUP_DAYS.
    DECLARE @HospitalId UNIQUEIDENTIFIER, @PatientId UNIQUEIDENTIFIER, @DoctorId UNIQUEIDENTIFIER, @VisitDate DATE;
    SELECT @HospitalId = HospitalId, @PatientId = PatientId, @DoctorId = DoctorId, @VisitDate = CAST(VisitDate AS DATE)
    FROM cln.Consultation WHERE ConsultationId = @ConsultationId;

    DECLARE @TypeCode NVARCHAR(20), @Duration INT;
    SELECT TOP (1) @TypeCode = a.[Type],
           @Duration = DATEDIFF(MINUTE, CAST(a.SlotStart AS TIME), CAST(COALESCE(a.SlotEnd, a.SlotStart) AS TIME))
    FROM opd.Appointment a
    WHERE a.PatientId = @PatientId AND a.DoctorId = @DoctorId AND a.AppointmentDate = @VisitDate AND a.Status <> 'CANCELLED' AND a.IsDeleted = 0
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
        SELECT MAX(CAST(c.VisitDate AS DATE)) FROM bil.Invoice i
        JOIN cln.Consultation c ON c.ConsultationId = i.ConsultationId
        WHERE i.PatientId = @PatientId AND i.DoctorId = @DoctorId AND i.Status <> 'CANCELLED' AND i.IsFreeFollowup = 0
          AND i.ConsultationId <> @ConsultationId AND CAST(c.VisitDate AS DATE) <= @VisitDate
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
    INSERT INTO cfg.IntakeQuestion (HospitalId, Label, QuestionType, Options, SortOrder)
    OUTPUT inserted.QuestionId, inserted.HospitalId, inserted.Label, inserted.QuestionType, inserted.Options, inserted.SortOrder
    VALUES (@HospitalId, @Label, @QuestionType, @Options, (SELECT COUNT(*) + 1 FROM cfg.IntakeQuestion WHERE HospitalId = @HospitalId));
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
    @HospitalId UNIQUEIDENTIFIER, @AppointmentId UNIQUEIDENTIFIER, @PatientId UNIQUEIDENTIFIER, @AnswersJson NVARCHAR(MAX)
AS BEGIN
    SET NOCOUNT ON;
    MERGE opd.IntakeResponse AS t USING (SELECT @AppointmentId AS AppointmentId) AS s ON t.AppointmentId = s.AppointmentId
    WHEN MATCHED THEN UPDATE SET AnswersJson = @AnswersJson, SubmittedAt = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN INSERT (HospitalId, AppointmentId, PatientId, AnswersJson) VALUES (@HospitalId, @AppointmentId, @PatientId, @AnswersJson);
    SELECT * FROM opd.IntakeResponse WHERE AppointmentId = @AppointmentId;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_RecordConsent
    @HospitalId UNIQUEIDENTIFIER, @ConsentFormId UNIQUEIDENTIFIER, @PatientId UNIQUEIDENTIFIER,
    @AppointmentId UNIQUEIDENTIFIER = NULL, @ConsultationId UNIQUEIDENTIFIER = NULL,
    @SignedByName NVARCHAR(200), @Method NVARCHAR(10) = 'IN_CLINIC', @RecordedBy UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- append-only: snapshot the current form text so later edits never change this record
    INSERT INTO opd.ConsentRecord (HospitalId, ConsentFormId, PatientId, AppointmentId, ConsultationId, SignedByName, Method,
                                   FormVersion, TitleSnapshot, BodySnapshot, RecordedBy)
    OUTPUT inserted.*
    SELECT @HospitalId, f.ConsentFormId, @PatientId, @AppointmentId, @ConsultationId, @SignedByName, @Method,
           f.Version, f.Title, f.BodyText, @RecordedBy
    FROM cfg.ConsentForm f WHERE f.ConsentFormId = @ConsentFormId;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_GetVisitIntakeAndConsent
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @HospitalId UNIQUEIDENTIFIER, @PatientId UNIQUEIDENTIFIER, @DoctorId UNIQUEIDENTIFIER, @VisitDate DATE, @AppointmentId UNIQUEIDENTIFIER;
    SELECT @HospitalId = HospitalId, @PatientId = PatientId, @DoctorId = DoctorId, @VisitDate = CAST(VisitDate AS DATE)
    FROM cln.Consultation WHERE ConsultationId = @ConsultationId;
    SELECT TOP (1) @AppointmentId = AppointmentId FROM opd.Appointment
    WHERE PatientId = @PatientId AND DoctorId = @DoctorId AND AppointmentDate = @VisitDate AND Status <> 'CANCELLED' AND IsDeleted = 0
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
    INSERT INTO cfg.ClinicalCondition (HospitalId, Name, Icd10Prefixes)
    OUTPUT inserted.ConditionId, inserted.HospitalId, inserted.Name, inserted.Icd10Prefixes
    VALUES (@HospitalId, @Name, UPPER(@Icd10Prefixes));
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

CREATE OR ALTER PROCEDURE opd.usp_CreateAppointment
    @HospitalId     UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER,
    @DoctorId       UNIQUEIDENTIFIER,
    @AppointmentDate DATE,
    @SlotStart      NVARCHAR(10),
    @SlotEnd        NVARCHAR(10) = NULL,
    @Type           NVARCHAR(10) = 'NEW',
    @Notes          NVARCHAR(300) = NULL,
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    INSERT INTO opd.Appointment (HospitalId, PatientId, DoctorId, AppointmentDate,
                                  SlotStart, SlotEnd, [Type], Notes, CreatedBy)
    OUTPUT inserted.AppointmentId, inserted.AppointmentDate, inserted.SlotStart, inserted.Status
    VALUES (@HospitalId, @PatientId, @DoctorId, @AppointmentDate,
            @SlotStart, @SlotEnd, @Type, @Notes, @CreatedBy);
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_UpdateAppointmentStatus
    @AppointmentId  UNIQUEIDENTIFIER,
    @Status         NVARCHAR(15),
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
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
    WHERE AppointmentId = @AppointmentId AND IsDeleted = 0
      AND (   (Status = 'BOOKED'     AND @Status IN ('CHECKED_IN','CANCELLED','NO_SHOW'))
           OR (Status = 'CHECKED_IN' AND @Status IN ('IN_CONSULT','CANCELLED','NO_SHOW'))
           OR (Status = 'IN_CONSULT' AND @Status = 'COMPLETED'));

    IF @@ROWCOUNT = 0
        SELECT AppointmentId, Status, CheckedInAt, ConsultStartedAt, CompletedAt
        FROM opd.Appointment
        WHERE AppointmentId = @AppointmentId AND IsDeleted = 0 AND Status = @Status;  -- same-status no-op; empty = rejected
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
    WHERE HospitalId = @HospitalId AND IsDeleted = 0
      AND AppointmentDate BETWEEN @FromDate AND @ToDate;

    SELECT Status, COUNT(*) AS StatusCount
    FROM opd.Appointment
    WHERE HospitalId = @HospitalId AND IsDeleted = 0
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
    DECLARE @ConsultationId UNIQUEIDENTIFIER;
    SELECT @ConsultationId = ConsultationId
    FROM cln.Consultation
    WHERE PatientId = @PatientId AND DoctorId = @DoctorId
      AND Status = 'IN_PROGRESS' AND IsDeleted = 0;

    IF @ConsultationId IS NOT NULL BEGIN
        SELECT ConsultationId, HospitalId, PatientId, DoctorId, AppointmentId,
               VisitDate, ConsultationType, Status, ChiefComplaint,
               SubjectiveText, ObjectiveText, AssessmentText, PlanText,
               FollowUpDate, FollowUpNotes, IsLocked
        FROM cln.Consultation WHERE ConsultationId = @ConsultationId;
    END ELSE BEGIN
        INSERT INTO cln.Consultation (HospitalId, PatientId, DoctorId, AppointmentId,
                                       VisitDate, CreatedBy)
        OUTPUT inserted.ConsultationId, inserted.HospitalId, inserted.PatientId,
               inserted.DoctorId, inserted.VisitDate, inserted.ConsultationType,
               inserted.Status, inserted.IsLocked
        VALUES (@HospitalId, @PatientId, @DoctorId, @AppointmentId,
                CAST(SYSUTCDATETIME() AS DATE), @DoctorId);
    END;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_GetConsultationById
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='READ', EntityType='Consultation')
    SELECT ConsultationId, HospitalId, PatientId, DoctorId, AppointmentId, VisitDate,
           ConsultationType, Status, ChiefComplaint, SubjectiveText, ObjectiveText,
           AssessmentText, PlanText, FollowUpDate, FollowUpNotes, IsLocked
    FROM cln.Consultation WHERE ConsultationId = @ConsultationId AND IsDeleted = 0;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_SaveSoapNotes
    @ConsultationId UNIQUEIDENTIFIER,
    @ChiefComplaint NVARCHAR(500) = NULL,
    @SubjectiveText NVARCHAR(MAX) = NULL,
    @ObjectiveText  NVARCHAR(MAX) = NULL,
    @AssessmentText NVARCHAR(MAX) = NULL,
    @PlanText       NVARCHAR(MAX) = NULL,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    UPDATE cln.Consultation SET
        ChiefComplaint = ISNULL(@ChiefComplaint, ChiefComplaint),
        SubjectiveText = ISNULL(@SubjectiveText, SubjectiveText),
        ObjectiveText = ISNULL(@ObjectiveText, ObjectiveText),
        AssessmentText = ISNULL(@AssessmentText, AssessmentText),
        PlanText = ISNULL(@PlanText, PlanText),
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE ConsultationId = @ConsultationId AND IsLocked = 0;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_SaveFollowup
    @ConsultationId UNIQUEIDENTIFIER,
    @FollowUpDate   DATE = NULL,
    @FollowUpNotes  NVARCHAR(500) = NULL,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    UPDATE cln.Consultation
    SET FollowUpDate = @FollowUpDate, FollowUpNotes = @FollowUpNotes,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE ConsultationId = @ConsultationId;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListConsultationsForDate
    @HospitalId     UNIQUEIDENTIFIER,
    @VisitDate      DATE
AS BEGIN
    SET NOCOUNT ON;
    SELECT ConsultationId, PatientId, DoctorId, VisitDate, ConsultationType, Status
    FROM cln.Consultation
    WHERE HospitalId = @HospitalId AND VisitDate = @VisitDate AND IsDeleted = 0;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ConfirmSignOff
    @ConsultationId UNIQUEIDENTIFIER,
    @DoctorId       UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='SIGNOFF', EntityType='Consultation')
    UPDATE cln.Consultation
    SET Status = 'LOCKED', IsLocked = 1, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @DoctorId
    WHERE ConsultationId = @ConsultationId AND IsLocked = 0;

    INSERT INTO cln.ConsultationAttestation (HospitalId, ConsultationId,
        IsSubjectiveChecked, IsObjectiveChecked, IsAssessmentChecked, IsPlanChecked, SignedBy)
    OUTPUT inserted.AttestationId, inserted.SignedAt
    SELECT HospitalId, ConsultationId, 1, 1, 1, 1, @DoctorId
    FROM cln.Consultation WHERE ConsultationId = @ConsultationId;

    -- Signing off completes the visit: linked appointment IN_CONSULT → COMPLETED
    UPDATE a
    SET a.Status = 'COMPLETED', a.CompletedAt = SYSUTCDATETIME(),
        a.ModifiedAt = SYSUTCDATETIME(), a.ModifiedBy = @DoctorId
    FROM opd.Appointment a
    INNER JOIN cln.Consultation c
        ON c.AppointmentId = a.AppointmentId
        OR (c.AppointmentId IS NULL AND a.PatientId = c.PatientId AND a.DoctorId = c.DoctorId AND a.AppointmentDate = c.VisitDate)
    WHERE c.ConsultationId = @ConsultationId AND a.Status = 'IN_CONSULT' AND a.IsDeleted = 0;
END;
GO

-- ============================================================
-- 7. VITALS (cln.ConsultationVital)
-- ============================================================

CREATE OR ALTER PROCEDURE cln.usp_GetConsultationVitals
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT ConsultationVitalId, ConsultationId, TempValue, BpSystolic, BpDiastolic,
           PulseBpm, SpO2, RespRate, HeightCm, WeightKg, Bmi, PainScale,
           WaistCircCm, HeadCircCm
    FROM cln.ConsultationVital WHERE ConsultationId = @ConsultationId;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_SaveVitals
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER,
    @TempValue      DECIMAL(5,1) = NULL,
    @BpSystolic     INT = NULL,
    @BpDiastolic    INT = NULL,
    @PulseBpm       INT = NULL,
    @SpO2           INT = NULL,
    @RespRate       INT = NULL,
    @HeightCm       DECIMAL(5,1) = NULL,
    @WeightKg       DECIMAL(5,1) = NULL,
    @PainScale      INT = NULL,
    @WaistCircCm    DECIMAL(5,1) = NULL,
    @HeadCircCm     DECIMAL(5,1) = NULL
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
        ModifiedAt = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (HospitalId, ConsultationId, TempValue, BpSystolic, BpDiastolic, PulseBpm,
                SpO2, RespRate, HeightCm, WeightKg, Bmi, PainScale, WaistCircCm, HeadCircCm)
        VALUES (@HospitalId, @ConsultationId, @TempValue, @BpSystolic, @BpDiastolic, @PulseBpm,
                @SpO2, @RespRate, @HeightCm, @WeightKg, @Bmi, @PainScale, @WaistCircCm, @HeadCircCm);

    SELECT * FROM cln.ConsultationVital WHERE ConsultationId = @ConsultationId;
END;
GO

-- ============================================================
-- 8. SYMPTOMS (cfg.SymptomCatalog, cln.ConsultationSymptom)
-- ============================================================
-- TODO: cfg.SymptomCatalog in schema.sql has no HospitalId column (it is modeled
-- as shared reference data), while data.js's symptomCatalog array is keyed per
-- hospital (HospitalId on every row) and has a SymptomCode distinct from
-- DisplayLabel. The procedures below assume the schema is authoritative (global
-- catalog, Category IN ('QUICK_PICK','OTHER')) and the @HospitalId parameter is
-- accepted for interface parity but unused in the WHERE clause — flag for a
-- schema/data.js reconciliation before Phase 4 cutover.

CREATE OR ALTER PROCEDURE cfg.usp_ListSymptomCatalog
    @HospitalId     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SELECT SymptomCatalogId, SymptomName, Category
    FROM cfg.SymptomCatalog WHERE IsActive = 1 AND IsDeleted = 0
    ORDER BY SymptomName;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_ListSymptomQuickPicks
    @HospitalId     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SELECT SymptomCatalogId, SymptomName, Category
    FROM cfg.SymptomCatalog WHERE Category = 'QUICK_PICK' AND IsActive = 1 AND IsDeleted = 0
    ORDER BY SymptomName;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_ListSymptomOthers
    @HospitalId     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SELECT SymptomCatalogId, SymptomName, Category
    FROM cfg.SymptomCatalog WHERE Category = 'OTHER' AND IsActive = 1 AND IsDeleted = 0
    ORDER BY SymptomName;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_CreateCustomSymptom
    @HospitalId     UNIQUEIDENTIFIER = NULL,
    @SymptomName    NVARCHAR(100)
AS BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM cfg.SymptomCatalog WHERE SymptomName = @SymptomName AND IsDeleted = 0) BEGIN
        SELECT SymptomCatalogId, SymptomName, Category FROM cfg.SymptomCatalog
        WHERE SymptomName = @SymptomName AND IsDeleted = 0;
        RETURN;
    END;
    INSERT INTO cfg.SymptomCatalog (SymptomName, Category)
    OUTPUT inserted.SymptomCatalogId, inserted.SymptomName, inserted.Category
    VALUES (@SymptomName, 'OTHER');
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListConsultationSymptoms
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT ConsultationSymptomId, ConsultationId, SymptomName, [State]
    FROM cln.ConsultationSymptom WHERE ConsultationId = @ConsultationId;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_UpdateSymptomState
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER,
    @SymptomName    NVARCHAR(100),
    @State          NVARCHAR(10)
AS BEGIN
    SET NOCOUNT ON;
    -- Upsert (insert on first toggle, update thereafter) — matches data.js tri-state cycle
    MERGE cln.ConsultationSymptom AS tgt
    USING (SELECT @ConsultationId AS ConsultationId, @SymptomName AS SymptomName) AS src
        ON tgt.ConsultationId = src.ConsultationId AND tgt.SymptomName = src.SymptomName
    WHEN MATCHED THEN UPDATE SET [State] = @State
    WHEN NOT MATCHED THEN
        INSERT (HospitalId, ConsultationId, SymptomName, [State])
        VALUES (@HospitalId, @ConsultationId, @SymptomName, @State);
END;
GO

-- ============================================================
-- 9. PHRASES (cfg.PhraseBank)
-- ============================================================

CREATE OR ALTER PROCEDURE cfg.usp_ListPhrases
    @HospitalId     UNIQUEIDENTIFIER,
    @Section        NVARCHAR(20),
    @SpecialtyId    UNIQUEIDENTIFIER = NULL,
    @DoctorId       UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- Category is the schema equivalent of data.js Section; scope priority DOCTOR
    -- (personal) > SPECIALTY > GLOBAL is expressed via IsGlobal + UserId.
    SELECT PhraseBankId, HospitalId, UserId, Category, TriggerCode, PhraseText
    FROM cfg.PhraseBank
    WHERE HospitalId = @HospitalId AND Category = @Section AND IsActive = 1 AND IsDeleted = 0
      AND (IsGlobal = 1 OR UserId = @DoctorId)
    ORDER BY CASE WHEN UserId = @DoctorId THEN 0 WHEN IsGlobal = 0 THEN 1 ELSE 2 END, PhraseText;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_SearchPhrases
    @Term           NVARCHAR(100),
    @HospitalId     UNIQUEIDENTIFIER,
    @Section        NVARCHAR(20),
    @SpecialtyId    UNIQUEIDENTIFIER = NULL,
    @DoctorId       UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SELECT TOP (5) PhraseBankId, HospitalId, UserId, Category, TriggerCode, PhraseText
    FROM cfg.PhraseBank
    WHERE HospitalId = @HospitalId AND Category = @Section AND IsActive = 1 AND IsDeleted = 0
      AND (IsGlobal = 1 OR UserId = @DoctorId)
      AND (TriggerCode LIKE '%' + @Term + '%' OR PhraseText LIKE '%' + @Term + '%')
    ORDER BY CASE WHEN UserId = @DoctorId THEN 0 WHEN IsGlobal = 0 THEN 1 ELSE 2 END;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_CreatePhrase
    @HospitalId     UNIQUEIDENTIFIER,
    @UserId         UNIQUEIDENTIFIER,
    @Category       NVARCHAR(20),
    @TriggerCode    NVARCHAR(30) = NULL,
    @PhraseText     NVARCHAR(500),
    @IsGlobal       BIT = 0
AS BEGIN
    SET NOCOUNT ON;
    INSERT INTO cfg.PhraseBank (HospitalId, UserId, Category, TriggerCode, PhraseText, IsGlobal, CreatedBy)
    OUTPUT inserted.PhraseBankId, inserted.PhraseText
    VALUES (@HospitalId, @UserId, @Category, @TriggerCode, @PhraseText, @IsGlobal, @UserId);
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_DeletePhrase
    @PhraseBankId   UNIQUEIDENTIFIER,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- data.js performs a hard splice(); schema has IsDeleted, so soft delete is
    -- used (preferred per architecture §4.8) rather than a physical DELETE.
    UPDATE cfg.PhraseBank SET IsDeleted = 1, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE PhraseBankId = @PhraseBankId;
END;
GO

-- ============================================================
-- 10. DIAGNOSES / ICD10 (cfg.ICD10Catalog, cln.ConsultationDiagnosis)
-- ============================================================

CREATE OR ALTER PROCEDURE cfg.usp_SearchIcd10Catalog
    @Term           NVARCHAR(100)
AS BEGIN
    SET NOCOUNT ON;
    SELECT ICD10CatalogId, Code, [Description], Synonyms
    FROM cfg.ICD10Catalog
    WHERE IsActive = 1 AND IsDeleted = 0
      AND (Code LIKE @Term + '%' OR [Description] LIKE '%' + @Term + '%' OR Synonyms LIKE '%' + @Term + '%')
    ORDER BY Code;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListConsultationDiagnoses
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT ConsultationDiagnosisId, ConsultationId, ICD10Code, [Description],
           IsPrimary, Laterality, Chronicity, SequenceOrder
    FROM cln.ConsultationDiagnosis
    WHERE ConsultationId = @ConsultationId AND IsDeleted = 0
    ORDER BY SequenceOrder;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_CreateDiagnosis
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER,
    @ICD10Code      NVARCHAR(10),
    @Description    NVARCHAR(300),
    @Laterality     NVARCHAR(10) = NULL,
    @Chronicity     NVARCHAR(10) = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- First diagnosis on a consultation defaults to IsPrimary=1, matching data.js
    DECLARE @ExistingCount INT;
    SELECT @ExistingCount = COUNT(*) FROM cln.ConsultationDiagnosis
    WHERE ConsultationId = @ConsultationId AND IsDeleted = 0;

    INSERT INTO cln.ConsultationDiagnosis (HospitalId, ConsultationId, ICD10Code, [Description],
                                            IsPrimary, Laterality, Chronicity, SequenceOrder)
    OUTPUT inserted.ConsultationDiagnosisId, inserted.ICD10Code, inserted.IsPrimary
    VALUES (@HospitalId, @ConsultationId, @ICD10Code, @Description,
            CASE WHEN @ExistingCount = 0 THEN 1 ELSE 0 END, @Laterality, @Chronicity, @ExistingCount);
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_DeleteDiagnosis
    @ConsultationDiagnosisId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    UPDATE cln.ConsultationDiagnosis SET IsDeleted = 1 WHERE ConsultationDiagnosisId = @ConsultationDiagnosisId;
END;
GO

-- ============================================================
-- 11. FORMULARY / PRESCRIPTIONS (cfg.DrugFormulary, cln.Prescription)
-- ============================================================
-- TODO: data.js's drugFormularyCatalog carries TargetSpecialties, IsGeneralMedicine,
-- IsControlledSubstance (bool), GenericClass, DefaultDurationDays — schema.sql's
-- cfg.DrugFormulary has IsControlled but not the others (has DefaultDuration INT
-- instead of DefaultDurationDays, no GenericClass/TargetSpecialties/IsGeneralMedicine
-- columns). Procedures below use only columns that exist in schema.sql; the
-- specialty-targeting and generic-class reconciliation-engine fields are omitted
-- pending a schema extension.

CREATE OR ALTER PROCEDURE cfg.usp_SearchFormulary
    @Term           NVARCHAR(100),
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT DrugFormularyId, BrandName, GenericName, TherapeuticClass, Composition,
           Strength, DrugForm, DeliveryRoute, DefaultDosage, DefaultFrequency,
           DefaultDuration, DefaultInstructions, IsControlled
    FROM cfg.DrugFormulary
    WHERE IsActive = 1 AND IsDeleted = 0
      AND (BrandName LIKE '%' + @Term + '%' OR GenericName LIKE '%' + @Term + '%')
    ORDER BY BrandName;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_GetDrugByName
    @BrandName      NVARCHAR(200)
AS BEGIN
    SET NOCOUNT ON;
    SELECT TOP (1) DrugFormularyId, BrandName, GenericName, TherapeuticClass, Composition,
           Strength, DrugForm, DeliveryRoute, DefaultDosage, DefaultFrequency,
           DefaultDuration, DefaultInstructions, IsControlled
    FROM cfg.DrugFormulary
    WHERE IsActive = 1 AND IsDeleted = 0 AND BrandName = @BrandName;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_CreateDrug
    @BrandName              NVARCHAR(200),
    @GenericName            NVARCHAR(200) = NULL,
    @TherapeuticClass       NVARCHAR(300) = NULL,
    @Composition            NVARCHAR(500) = NULL,
    @Strength               NVARCHAR(50) = NULL,
    @DrugForm               NVARCHAR(30) = NULL,
    @DeliveryRoute          NVARCHAR(30) = NULL,
    @DefaultDosage          NVARCHAR(20) = NULL,
    @DefaultFrequency       NVARCHAR(20) = NULL,
    @DefaultDuration        INT = NULL,
    @DefaultInstructions    NVARCHAR(300) = NULL,
    @IsControlled           BIT = 0,
    @CreatedBy              UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    INSERT INTO cfg.DrugFormulary (BrandName, GenericName, TherapeuticClass, Composition, Strength,
           DrugForm, DeliveryRoute, DefaultDosage, DefaultFrequency, DefaultDuration,
           DefaultInstructions, IsControlled, CreatedBy)
    OUTPUT inserted.DrugFormularyId, inserted.BrandName, inserted.GenericName, inserted.TherapeuticClass,
           inserted.Composition, inserted.Strength, inserted.DrugForm, inserted.DeliveryRoute,
           inserted.DefaultDosage, inserted.DefaultFrequency, inserted.DefaultDuration,
           inserted.DefaultInstructions, inserted.IsControlled, inserted.IsActive
    VALUES (@BrandName, @GenericName, @TherapeuticClass, @Composition, @Strength, @DrugForm,
           @DeliveryRoute, @DefaultDosage, @DefaultFrequency, @DefaultDuration,
           @DefaultInstructions, @IsControlled, @CreatedBy);
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_UpdateDrugStatus
    @DrugFormularyId    UNIQUEIDENTIFIER,
    @ModifiedBy          UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- data.js toggles the current value (no explicit flag passed by api.js)
    UPDATE cfg.DrugFormulary
    SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE DrugFormularyId = @DrugFormularyId;

    SELECT DrugFormularyId, BrandName, IsActive
    FROM cfg.DrugFormulary WHERE DrugFormularyId = @DrugFormularyId;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListPrescriptions
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT PrescriptionId, ConsultationId, PatientId, MedicineName, Strength, DrugForm,
           DeliveryRoute, Category, TakeInQty, TimesPerDay, DurationValue, DurationUnit,
           Instructions, IsPrn, IsTimingBf, IsTimingAn, IsTimingNi, IsTimingEm, Status
    FROM cln.Prescription
    WHERE ConsultationId = @ConsultationId AND Status = 'ACTIVE' AND IsDeleted = 0;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListActiveMedicationsForPatient
    @PatientId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT PrescriptionId, ConsultationId, MedicineName, Strength, DrugForm,
           TakeInQty, TimesPerDay, DurationValue, DurationUnit, Instructions, Status
    FROM cln.Prescription
    WHERE PatientId = @PatientId AND Status = 'ACTIVE' AND IsDeleted = 0
    ORDER BY CreatedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_CreatePrescription
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER,
    @MedicineName   NVARCHAR(200),
    @Strength       NVARCHAR(50) = NULL,
    @DrugForm       NVARCHAR(30) = NULL,
    @DeliveryRoute  NVARCHAR(30) = NULL,
    @Category       NVARCHAR(20) = NULL,
    @TakeInQty      DECIMAL(5,1) = 1,
    @TimesPerDay    INT = 1,
    @DurationValue  INT = NULL,
    @DurationUnit   NVARCHAR(10) = NULL,
    @Instructions   NVARCHAR(500) = NULL,
    @IsPrn          BIT = 0,
    @IsTimingBf     BIT = 0,
    @IsTimingAn     BIT = 0,
    @IsTimingNi     BIT = 0,
    @IsTimingEm     BIT = 0,
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='CREATE', EntityType='Prescription')
    INSERT INTO cln.Prescription (HospitalId, ConsultationId, PatientId, MedicineName,
        Strength, DrugForm, DeliveryRoute, Category, TakeInQty, TimesPerDay,
        DurationValue, DurationUnit, Instructions, IsPrn,
        IsTimingBf, IsTimingAn, IsTimingNi, IsTimingEm, CreatedBy)
    OUTPUT inserted.PrescriptionId, inserted.MedicineName, inserted.Status, inserted.CreatedAt
    VALUES (@HospitalId, @ConsultationId, @PatientId, @MedicineName,
        @Strength, @DrugForm, @DeliveryRoute, @Category, @TakeInQty, @TimesPerDay,
        @DurationValue, @DurationUnit, @Instructions, @IsPrn,
        @IsTimingBf, @IsTimingAn, @IsTimingNi, @IsTimingEm, @CreatedBy);
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
    WHERE a.PatientId = @PatientId AND a.Status = 'ACTIVE' AND a.IsDeleted = 0
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
-- TODO: data.js's labTestCatalog carries Category and SpecialtyIds (JSON array)
-- used to filter by specialty; schema.sql's cfg.LabTestCatalog has TestType and
-- DefaultSampleType only, no Category/SpecialtyIds columns. The @SpecialtyId
-- parameter below is accepted for interface parity but not filtered on —
-- flag for a schema extension (e.g. a cln.LabTestCatalog_Specialty junction
-- table per architecture §5.2 "junction tables" convention) before Phase 4.

CREATE OR ALTER PROCEDURE cln.usp_ListDoctorLabFavorites
    @DoctorId       UNIQUEIDENTIFIER,
    @SpecialtyId    UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SELECT t.LabTestCatalogId, t.TestName, t.TestType, t.DefaultSampleType
    FROM cln.DoctorLabFavorite f
    INNER JOIN cfg.LabTestCatalog t ON t.LabTestCatalogId = f.LabTestCatalogId
    WHERE f.DoctorId = @DoctorId AND t.IsActive = 1 AND t.IsDeleted = 0;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_SearchLabTests
    @Term           NVARCHAR(100),
    @SpecialtyId    UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    SELECT LabTestCatalogId, TestName, TestType, DefaultSampleType
    FROM cfg.LabTestCatalog
    WHERE IsActive = 1 AND IsDeleted = 0 AND TestName LIKE '%' + @Term + '%'
    ORDER BY TestName;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_CreateLabTest
    @TestName       NVARCHAR(100),
    @TestType       NVARCHAR(20) = 'LAB',
    @DefaultSampleType NVARCHAR(50) = NULL
AS BEGIN
    SET NOCOUNT ON;
    INSERT INTO cfg.LabTestCatalog (TestName, TestType, DefaultSampleType)
    OUTPUT inserted.LabTestCatalogId, inserted.TestName
    VALUES (@TestName, @TestType, @DefaultSampleType);
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_CreateLabFavorite
    @DoctorId       UNIQUEIDENTIFIER,
    @LabTestCatalogId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM cln.DoctorLabFavorite WHERE DoctorId = @DoctorId AND LabTestCatalogId = @LabTestCatalogId)
        INSERT INTO cln.DoctorLabFavorite (DoctorId, LabTestCatalogId) VALUES (@DoctorId, @LabTestCatalogId);

    SELECT DoctorLabFavoriteId, DoctorId, LabTestCatalogId
    FROM cln.DoctorLabFavorite WHERE DoctorId = @DoctorId AND LabTestCatalogId = @LabTestCatalogId;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_DeleteLabFavorite
    @DoctorId       UNIQUEIDENTIFIER,
    @LabTestCatalogId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- No IsActive/IsDeleted on this junction table — hard delete matches data.js splice()
    DELETE FROM cln.DoctorLabFavorite WHERE DoctorId = @DoctorId AND LabTestCatalogId = @LabTestCatalogId;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_CreateInvestigationOrder
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER,
    @TestName       NVARCHAR(100),
    @TestType       NVARCHAR(20) = 'LAB',
    @Priority       NVARCHAR(10) = 'ROUTINE',
    @ClinicalNotes  NVARCHAR(300) = NULL,
    @IsFasting      BIT = 0,
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    INSERT INTO cln.InvestigationOrder (HospitalId, ConsultationId, PatientId,
                                        TestName, TestType, Priority, ClinicalNotes, IsFasting, CreatedBy)
    OUTPUT inserted.InvestigationOrderId, inserted.TestName, inserted.Status
    VALUES (@HospitalId, @ConsultationId, @PatientId,
            @TestName, @TestType, @Priority, @ClinicalNotes, @IsFasting, @CreatedBy);
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListInvestigationOrders
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT InvestigationOrderId, ConsultationId, PatientId, TestName, TestType,
           Priority, ClinicalNotes, IsFasting, Status, CreatedAt
    FROM cln.InvestigationOrder
    WHERE ConsultationId = @ConsultationId AND IsDeleted = 0;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListPendingLabOrders
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT o.InvestigationOrderId, o.PatientId, p.FullName AS PatientName, p.Mrn AS PatientMrn,
           o.TestName, o.TestType, o.Priority, o.ClinicalNotes, o.IsFasting, o.Status, o.CreatedAt
    FROM cln.InvestigationOrder o
    INNER JOIN opd.Patient p ON p.PatientId = o.PatientId
    WHERE o.HospitalId = @HospitalId AND o.Status = 'ORDERED' AND o.IsDeleted = 0
    ORDER BY o.CreatedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_UpdateOrderStatus
    @InvestigationOrderId UNIQUEIDENTIFIER,
    @Status         NVARCHAR(15)
AS BEGIN
    SET NOCOUNT ON;
    UPDATE cln.InvestigationOrder SET Status = @Status WHERE InvestigationOrderId = @InvestigationOrderId;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_SubmitLabResult
    @InvestigationOrderId UNIQUEIDENTIFIER,
    @HospitalId     UNIQUEIDENTIFIER,
    @LabPartnerId   UNIQUEIDENTIFIER = NULL,
    @ResultSummary  NVARCHAR(500) = NULL,
    @ResultValue    NVARCHAR(100) = NULL,
    @ReferenceRange NVARCHAR(100) = NULL,
    @Interpretation NVARCHAR(10) = NULL,
    @TechnicianName NVARCHAR(100) = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='SUBMIT', EntityType='LabResult')
    INSERT INTO cln.LabResult (HospitalId, InvestigationOrderId, LabPartnerId,
                                ResultSummary, ResultValue, ReferenceRange,
                                Interpretation, TechnicianName, Status)
    OUTPUT inserted.LabResultId, inserted.Status
    VALUES (@HospitalId, @InvestigationOrderId, @LabPartnerId,
            @ResultSummary, @ResultValue, @ReferenceRange,
            @Interpretation, @TechnicianName, 'UPLOADED');

    UPDATE cln.InvestigationOrder SET Status = 'COMPLETED'
    WHERE InvestigationOrderId = @InvestigationOrderId;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListLabResultsForPatient
    @PatientId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT r.LabResultId, r.InvestigationOrderId, o.TestName, r.ResultSummary, r.ResultValue,
           r.ReferenceRange, r.Interpretation, r.Status, r.CreatedAt
    FROM cln.LabResult r
    INNER JOIN cln.InvestigationOrder o ON o.InvestigationOrderId = r.InvestigationOrderId
    WHERE o.PatientId = @PatientId AND r.IsDeleted = 0
    ORDER BY r.CreatedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListPendingReviewResults
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT r.LabResultId, o.PatientId, p.FullName AS PatientName, p.Mrn AS PatientMrn,
           o.TestName, r.ResultSummary, r.ResultValue, r.Interpretation, r.Status, r.CreatedAt
    FROM cln.LabResult r
    INNER JOIN cln.InvestigationOrder o ON o.InvestigationOrderId = r.InvestigationOrderId
    INNER JOIN opd.Patient p ON p.PatientId = o.PatientId
    WHERE r.HospitalId = @HospitalId AND r.Status = 'UPLOADED' AND r.IsDeleted = 0
    ORDER BY r.CreatedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_UpdateLabResultStatus
    @LabResultId    UNIQUEIDENTIFIER,
    @ReviewedBy     UNIQUEIDENTIFIER = NULL,
    @ReviewNotes    NVARCHAR(300) = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='APPROVE', EntityType='LabResult')
    UPDATE cln.LabResult
    SET Status = 'REVIEWED', ReviewedBy = @ReviewedBy, ReviewedAt = SYSUTCDATETIME(), ReviewNotes = @ReviewNotes
    WHERE LabResultId = @LabResultId;
END;
GO

-- ============================================================
-- 13. REFERRALS (cln.Referral)
-- ============================================================

CREATE OR ALTER PROCEDURE cln.usp_CreateReferral
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER,
    @ReferToSpecialty NVARCHAR(100),
    @ReferToDoctorName NVARCHAR(200) = NULL,
    @Urgency        NVARCHAR(10) = 'ROUTINE',
    @Reason         NVARCHAR(500) = NULL,
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    INSERT INTO cln.Referral (HospitalId, ConsultationId, PatientId, ReferToSpecialty,
                               ReferToDoctorName, Urgency, Reason, CreatedBy)
    OUTPUT inserted.ReferralId, inserted.ReferToSpecialty, inserted.Status
    VALUES (@HospitalId, @ConsultationId, @PatientId, @ReferToSpecialty,
            @ReferToDoctorName, @Urgency, @Reason, @CreatedBy);
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListReferrals
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT ReferralId, ConsultationId, PatientId, ReferToSpecialty, ReferToDoctorName,
           Urgency, Reason, Status, CreatedAt
    FROM cln.Referral WHERE ConsultationId = @ConsultationId AND IsDeleted = 0;
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
    @SketchDataUrl  NVARCHAR(MAX),
    @StrokesJson    NVARCHAR(MAX) = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- data.js always inserts a new row (never updates) despite the "save" verb;
    -- mirrored here as an INSERT-only procedure.
    INSERT INTO cln.ConsultationSketch (HospitalId, ConsultationId, PatientId, DoctorId,
                                         Title, SketchDataUrl, StrokesJson)
    OUTPUT inserted.SketchId, inserted.CreatedAt
    VALUES (@HospitalId, @ConsultationId, @PatientId, @DoctorId, @Title, @SketchDataUrl, @StrokesJson);
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListSketches
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT SketchId, ConsultationId, PatientId, DoctorId, Title, SketchDataUrl, StrokesJson, CreatedAt
    FROM cln.ConsultationSketch WHERE ConsultationId = @ConsultationId
    ORDER BY CreatedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListPatientSketches
    @PatientId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT SketchId, ConsultationId, PatientId, DoctorId, Title, SketchDataUrl, StrokesJson, CreatedAt
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
    -- own author (CreatedBy = @DoctorId), matching data.js's DoctorId match.
    SELECT ConfidentialNoteId, ConsultationId, NoteText, CreatedBy, CreatedAt, ModifiedAt
    FROM cln.ConfidentialNote
    WHERE ConsultationId = @ConsultationId AND CreatedBy = @DoctorId;
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
        ON tgt.ConsultationId = src.ConsultationId AND tgt.CreatedBy = @DoctorId
    WHEN MATCHED THEN UPDATE SET NoteText = @NoteText, ModifiedAt = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (HospitalId, ConsultationId, NoteText, CreatedBy)
        VALUES (@HospitalId, @ConsultationId, @NoteText, @DoctorId);

    SELECT ConfidentialNoteId, NoteText, ModifiedAt
    FROM cln.ConfidentialNote WHERE ConsultationId = @ConsultationId AND CreatedBy = @DoctorId;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_CreateAddendum
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER,
    @AddendumText   NVARCHAR(MAX),
    @AddedBy        UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- Append-only, matching schema comment on cln.ConsultationAddendum — never updated.
    INSERT INTO cln.ConsultationAddendum (HospitalId, ConsultationId, AddendumText, AddedBy)
    OUTPUT inserted.AddendumId, inserted.CreatedAt
    VALUES (@HospitalId, @ConsultationId, @AddendumText, @AddedBy);
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListAddendums
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT AddendumId, ConsultationId, AddendumText, AddedBy, CreatedAt
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
    DECLARE @Date DATE = ISNULL(@OnDate, CAST(SYSUTCDATETIME() AS DATE));
    SELECT TOP (1) FeeScheduleId, DoctorId, ConsultationType, FeeAmount, Currency,
           EffectiveFrom, EffectiveTo
    FROM bil.DoctorFeeSchedule
    WHERE DoctorId = @DoctorId AND ConsultationType = @ConsultationType
      AND EffectiveFrom <= @Date AND (EffectiveTo IS NULL OR EffectiveTo >= @Date)
      AND IsActive = 1 AND IsDeleted = 0
    ORDER BY EffectiveFrom DESC;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_ListDoctorFees
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT FeeScheduleId, DoctorId, ConsultationType, FeeAmount, Currency, EffectiveFrom, EffectiveTo
    FROM bil.DoctorFeeSchedule
    WHERE HospitalId = @HospitalId AND IsActive = 1 AND IsDeleted = 0;
END;
GO

-- ---- Service charge master ----

CREATE OR ALTER PROCEDURE bil.usp_ListServiceCharges
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT ServiceChargeId, ServiceCode, ServiceName, Category, DefaultAmount, TaxPercent, Currency
    FROM bil.ServiceChargeMaster
    WHERE HospitalId = @HospitalId AND IsActive = 1 AND IsDeleted = 0
    ORDER BY ServiceName;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_GetServiceCharge
    @ServiceChargeId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT ServiceChargeId, ServiceCode, ServiceName, Category, DefaultAmount, TaxPercent, Currency
    FROM bil.ServiceChargeMaster WHERE ServiceChargeId = @ServiceChargeId AND IsDeleted = 0;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_SearchServiceCharges
    @Term           NVARCHAR(100),
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT ServiceChargeId, ServiceCode, ServiceName, Category, DefaultAmount, TaxPercent, Currency
    FROM bil.ServiceChargeMaster
    WHERE HospitalId = @HospitalId AND IsActive = 1 AND IsDeleted = 0
      AND (ServiceName LIKE '%' + @Term + '%' OR ServiceCode LIKE '%' + @Term + '%')
    ORDER BY ServiceName;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_CreateServiceCharge
    @HospitalId     UNIQUEIDENTIFIER,
    @ServiceCode    NVARCHAR(20),
    @ServiceName    NVARCHAR(200),
    @Category       NVARCHAR(20),
    @DefaultAmount  DECIMAL(10,2),
    @TaxPercent     DECIMAL(5,2) = 0,
    @Currency       NVARCHAR(5) = 'USD',
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    INSERT INTO bil.ServiceChargeMaster (HospitalId, ServiceCode, ServiceName, Category,
                                          DefaultAmount, TaxPercent, Currency, CreatedBy)
    OUTPUT inserted.ServiceChargeId, inserted.ServiceName, inserted.DefaultAmount
    VALUES (@HospitalId, @ServiceCode, @ServiceName, @Category, @DefaultAmount, @TaxPercent, @Currency, @CreatedBy);
END;
GO

-- ---- Invoices ----

CREATE OR ALTER PROCEDURE bil.usp_CreateInvoice
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER = NULL,
    @PatientId      UNIQUEIDENTIFIER,
    @DoctorId       UNIQUEIDENTIFIER = NULL,
    @ConsultationType NVARCHAR(10) = NULL,
    @Currency       NVARCHAR(5) = 'USD',
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @InvoiceNumber NVARCHAR(30);
    SELECT @InvoiceNumber = 'INV-' + RIGHT('000000' + CAST(ISNULL(MAX(InvoicePkId), 0) + 1 AS NVARCHAR), 6)
    FROM bil.Invoice WHERE HospitalId = @HospitalId;

    INSERT INTO bil.Invoice (HospitalId, InvoiceNumber, ConsultationId, PatientId,
                              DoctorId, ConsultationType, Currency, TotalAmount, CreatedBy)
    OUTPUT inserted.InvoiceId, inserted.InvoiceNumber, inserted.Status, inserted.CreatedAt
    VALUES (@HospitalId, @InvoiceNumber, @ConsultationId, @PatientId,
            @DoctorId, @ConsultationType, @Currency, 0, @CreatedBy);
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_GetInvoiceForConsultation
    @ConsultationId UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT TOP (1) InvoiceId, InvoiceNumber, PatientId, DoctorId, Currency, SubTotal,
           DiscountAmount, TaxAmount, TotalAmount, PaidAmount, Status, CreatedAt
    FROM bil.Invoice
    WHERE ConsultationId = @ConsultationId AND Status <> 'CANCELLED' AND IsDeleted = 0;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_ListInvoices
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT InvoiceId, InvoiceNumber, ConsultationId, PatientId, DoctorId, ConsultationType,
           Currency, SubTotal, DiscountAmount, TaxAmount, TotalAmount, PaidAmount, Status, CreatedAt
    FROM bil.Invoice
    WHERE HospitalId = @HospitalId AND IsDeleted = 0
    ORDER BY CreatedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_GetInvoice
    @InvoiceId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT InvoiceId, InvoiceNumber, ConsultationId, PatientId, DoctorId, ConsultationType,
           Currency, SubTotal, DiscountAmount, TaxAmount, TotalAmount, PaidAmount, Status, CreatedAt
    FROM bil.Invoice WHERE InvoiceId = @InvoiceId AND IsDeleted = 0;
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
    WHERE InvoiceId = @InvoiceId AND IsDeleted = 0;

    SELECT InvoiceId, Status, TotalAmount, PaidAmount FROM bil.Invoice WHERE InvoiceId = @InvoiceId;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_CancelInvoice
    @InvoiceId      UNIQUEIDENTIFIER,
    @Reason         NVARCHAR(300),
    @IssuedBy       UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @HospitalId UNIQUEIDENTIFIER, @Amount DECIMAL(10,2);
    SELECT @HospitalId = HospitalId, @Amount = TotalAmount
    FROM bil.Invoice WHERE InvoiceId = @InvoiceId;

    UPDATE bil.Invoice SET Status = 'CANCELLED', ModifiedAt = SYSUTCDATETIME()
    WHERE InvoiceId = @InvoiceId;

    DECLARE @CnNumber NVARCHAR(30);
    SELECT @CnNumber = 'CN-' + RIGHT('000000' + CAST(ISNULL(MAX(CreditNotePkId), 0) + 1 AS NVARCHAR), 6)
    FROM bil.CreditNote WHERE HospitalId = @HospitalId;

    INSERT INTO bil.CreditNote (HospitalId, CreditNoteNumber, InvoiceId, Amount, Reason, IssuedBy)
    OUTPUT inserted.CreditNoteId, inserted.CreditNoteNumber, inserted.Amount
    VALUES (@HospitalId, @CnNumber, @InvoiceId, @Amount, @Reason, @IssuedBy);
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
    @Description    NVARCHAR(200),
    @Category       NVARCHAR(20),
    @Qty            INT = 1,
    @UnitAmount     DECIMAL(10,2),
    @DiscountPercent DECIMAL(5,2) = 0,
    @TaxPercent     DECIMAL(5,2) = 0
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @Subtotal DECIMAL(10,2) = @Qty * @UnitAmount;
    DECLARE @DiscAmt DECIMAL(10,2) = ROUND(@Subtotal * @DiscountPercent / 100, 2);
    DECLARE @AfterDisc DECIMAL(10,2) = @Subtotal - @DiscAmt;
    DECLARE @TaxAmt DECIMAL(10,2) = ROUND(@AfterDisc * @TaxPercent / 100, 2);
    DECLARE @LineTotal DECIMAL(10,2) = @AfterDisc + @TaxAmt;
    DECLARE @Sort INT;
    SELECT @Sort = ISNULL(MAX(SortOrder), 0) + 1 FROM bil.InvoiceLineItem WHERE InvoiceId = @InvoiceId;

    INSERT INTO bil.InvoiceLineItem (InvoiceId, ServiceChargeId, [Description], Category,
                                      Qty, UnitAmount, DiscountPercent, DiscountAmount,
                                      TaxPercent, TaxAmount, LineTotal, SortOrder)
    OUTPUT inserted.LineItemId, inserted.[Description], inserted.LineTotal
    VALUES (@InvoiceId, @ServiceChargeId, @Description, @Category,
            @Qty, @UnitAmount, @DiscountPercent, @DiscAmt,
            @TaxPercent, @TaxAmt, @LineTotal, @Sort);
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_ListInvoiceLineItems
    @InvoiceId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT LineItemId, InvoiceId, ServiceChargeId, [Description], Category, Qty, UnitAmount,
           DiscountPercent, DiscountAmount, TaxPercent, TaxAmount, LineTotal, SortOrder
    FROM bil.InvoiceLineItem WHERE InvoiceId = @InvoiceId ORDER BY SortOrder;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_DeleteInvoiceLineItem
    @LineItemId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- bil.InvoiceLineItem has no IsActive/IsDeleted column — hard delete matches
    -- data.js splice() and is schema-correct (no soft-delete column to set).
    DELETE FROM bil.InvoiceLineItem WHERE LineItemId = @LineItemId;
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
    IF @ComponentType = 'INSURANCE' AND NOT EXISTS (
        SELECT 1 FROM bil.InvoiceCoverageLine WHERE CoverageLineId = @CoverageLineId AND InvoiceId = @InvoiceId)
        THROW 50120, 'An insurance settlement must reference a coverage line on this invoice.', 1;

    INSERT INTO bil.Payment (HospitalId, InvoiceId, TransactionNumber, ComponentType, CoverageLineId,
                              PaymentMethod, Amount, Reference, ReceivedBy)
    OUTPUT inserted.PaymentId, inserted.TransactionNumber, inserted.Amount, inserted.PaidAt
    SELECT @HospitalId, @InvoiceId,
           'TXN-' + RIGHT('000000' + CAST(ISNULL(MAX(PaymentPkId), 0) + 1 AS NVARCHAR), 6),
           @ComponentType, CASE WHEN @ComponentType = 'INSURANCE' THEN @CoverageLineId END,
           @PaymentMethod, @Amount, @Reference, @ReceivedBy
    FROM bil.Payment WHERE HospitalId = @HospitalId;

    EXEC bil.usp_RecalculateInvoiceSplit @InvoiceId = @InvoiceId;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_ListPaymentsForInvoice
    @InvoiceId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT PaymentId, InvoiceId, TransactionNumber, ComponentType, PaymentMethod,
           Amount, Reference, PaidAt, IsVoided, VoidReason
    FROM bil.Payment WHERE InvoiceId = @InvoiceId ORDER BY PaidAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_ListPayments
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT PaymentId, InvoiceId, TransactionNumber, ComponentType, PaymentMethod,
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
    SELECT @InvoiceId = InvoiceId FROM bil.Payment WHERE PaymentId = @TransactionId;

    UPDATE bil.Payment SET IsVoided = 1, VoidedAt = SYSUTCDATETIME(),
                           VoidedBy = @VoidedBy, VoidReason = @VoidReason
    WHERE PaymentId = @TransactionId AND IsVoided = 0;

    IF @InvoiceId IS NOT NULL
        EXEC bil.usp_RecalculateInvoiceSplit @InvoiceId = @InvoiceId;
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_CreateReceipt
    @HospitalId     UNIQUEIDENTIFIER,
    @InvoiceId      UNIQUEIDENTIFIER,
    @PaymentId      UNIQUEIDENTIFIER = NULL,
    @PatientId      UNIQUEIDENTIFIER = NULL,
    @TotalAmount    DECIMAL(10,2),
    @Currency       NVARCHAR(5) = 'USD'
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='PRINT', EntityType='Receipt') on print
    DECLARE @ReceiptNumber NVARCHAR(30);
    SELECT @ReceiptNumber = 'RCP-' + RIGHT('000000' + CAST(ISNULL(MAX(ReceiptPkId), 0) + 1 AS NVARCHAR), 6)
    FROM bil.Receipt WHERE HospitalId = @HospitalId;

    INSERT INTO bil.Receipt (HospitalId, ReceiptNumber, PaymentId, InvoiceId, PatientId, TotalAmount, Currency)
    OUTPUT inserted.ReceiptId, inserted.ReceiptNumber, inserted.GeneratedAt
    VALUES (@HospitalId, @ReceiptNumber, @PaymentId, @InvoiceId, @PatientId, @TotalAmount, @Currency);
END;
GO

CREATE OR ALTER PROCEDURE bil.usp_GetReceiptForInvoice
    @InvoiceId      UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT ReceiptId, ReceiptNumber, InvoiceId, PatientId, TotalAmount, Currency, GeneratedAt
    FROM bil.Receipt WHERE InvoiceId = @InvoiceId AND IsDeleted = 0;
END;
GO

-- ---- Coverage & N-way bill split (ported from MediSlot billing) ----
-- Charge (Invoice.TotalAmount) = Σ verified coverage liabilities + Patient share.
-- Self-Pay (no coverage), Private only, Govt scheme only, and Private + Govt together.

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
        SELECT CoverageLineId, Priority, CoverageLinePkId,
               CASE WHEN ApprovedType = 'PERCENT' AND IsManualAmount = 0 THEN ROUND(@Charge * ApprovedValue / 100, 2)
                    WHEN IsManualAmount = 1 THEN Liability
                    ELSE ApprovedValue END AS Wanted
        FROM bil.InvoiceCoverageLine WHERE InvoiceId = @InvoiceId AND Status = 'VERIFIED'
    ), ordered AS (
        SELECT CoverageLineId, Wanted,
               ISNULL(SUM(Wanted) OVER (ORDER BY Priority, CoverageLinePkId ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS WantedBefore
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

CREATE OR ALTER PROCEDURE cfg.usp_ListPayers
    @Country        NVARCHAR(5),
    @Category       NVARCHAR(15) = NULL      -- PRIVATE | GOVT_SCHEME | NULL = both
AS BEGIN
    SET NOCOUNT ON;
    SELECT PayerId, Name, Category, Country
    FROM cfg.Payer
    WHERE Country = @Country AND (@Category IS NULL OR Category = @Category) AND IsActive = 1 AND IsDeleted = 0
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
           c.GovtCardNumber, c.CardImageUrl, c.EffectiveDate, c.ExpiryDate, c.Status, c.Priority, c.IsActive,
           v.Status AS LastVerificationStatus, v.VerifiedAt AS LastVerifiedAt
    FROM opd.PatientCoverage c
    INNER JOIN cfg.Payer py ON py.PayerId = c.PayerId
    OUTER APPLY (SELECT TOP 1 Status, VerifiedAt FROM bil.EligibilityVerification ev
                 WHERE ev.CoverageId = c.CoverageId ORDER BY ev.VerifiedAt DESC) v
    WHERE c.PatientId = @PatientId AND c.IsDeleted = 0 AND (@IncludeInactive = 1 OR c.IsActive = 1)
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
    @CardImageUrl     NVARCHAR(500) = NULL,
    @EffectiveDate    DATE = NULL,
    @ExpiryDate       DATE = NULL,
    @Status           NVARCHAR(10) = 'ACTIVE',
    @UserId           UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    IF @CoverageId IS NOT NULL
        UPDATE opd.PatientCoverage SET PayerId = @PayerId, CoverageType = @CoverageType, PlanName = @PlanName,
            MemberId = @MemberId, PolicyNumber = @PolicyNumber, GroupNumber = @GroupNumber,
            PolicyHolderName = @PolicyHolderName, Relationship = @Relationship, ClaimMode = @ClaimMode,
            GovtCardNumber = @GovtCardNumber, CardImageUrl = ISNULL(@CardImageUrl, CardImageUrl),
            EffectiveDate = @EffectiveDate, ExpiryDate = @ExpiryDate, Status = @Status,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        OUTPUT inserted.CoverageId, inserted.Priority
        WHERE CoverageId = @CoverageId AND IsDeleted = 0;
    ELSE
        INSERT INTO opd.PatientCoverage (HospitalId, PatientId, PayerId, CoverageType, PlanName, MemberId, PolicyNumber,
            GroupNumber, PolicyHolderName, Relationship, ClaimMode, GovtCardNumber, CardImageUrl,
            EffectiveDate, ExpiryDate, Status, Priority, CreatedBy)
        OUTPUT inserted.CoverageId, inserted.Priority
        SELECT @HospitalId, @PatientId, @PayerId, @CoverageType, @PlanName, @MemberId, @PolicyNumber,
            @GroupNumber, @PolicyHolderName, @Relationship, @ClaimMode, @GovtCardNumber, @CardImageUrl,
            @EffectiveDate, @ExpiryDate, @Status,
            (SELECT COUNT(*) + 1 FROM opd.PatientCoverage WHERE PatientId = @PatientId AND IsDeleted = 0), @UserId;
END;
GO

CREATE OR ALTER PROCEDURE opd.usp_DeactivatePatientCoverage
    @CoverageId     UNIQUEIDENTIFIER,
    @UserId         UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    UPDATE opd.PatientCoverage SET IsActive = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE CoverageId = @CoverageId AND IsDeleted = 0;
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

-- ============================================================
-- 17. REPORTS
-- ============================================================

CREATE OR ALTER PROCEDURE cln.usp_ListConsultationVolumeReport
    @HospitalId     UNIQUEIDENTIFIER,
    @FromDate       DATE,
    @ToDate         DATE
AS BEGIN
    SET NOCOUNT ON;
    SELECT VisitDate AS [Date], COUNT(*) AS ConsultationCount
    FROM cln.Consultation
    WHERE HospitalId = @HospitalId AND VisitDate BETWEEN @FromDate AND @ToDate AND IsDeleted = 0
    GROUP BY VisitDate
    ORDER BY VisitDate;
END;
GO

CREATE OR ALTER PROCEDURE cln.usp_ListDiagnosisFrequencyReport
    @HospitalId     UNIQUEIDENTIFIER,
    @FromDate       DATE,
    @ToDate         DATE
AS BEGIN
    SET NOCOUNT ON;
    SELECT dx.ICD10Code, dx.[Description], COUNT(*) AS DiagnosisCount
    FROM cln.ConsultationDiagnosis dx
    INNER JOIN cln.Consultation c ON c.ConsultationId = dx.ConsultationId
    WHERE c.HospitalId = @HospitalId AND c.VisitDate BETWEEN @FromDate AND @ToDate
      AND dx.IsDeleted = 0 AND c.IsDeleted = 0
    GROUP BY dx.ICD10Code, dx.[Description]
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
    WHERE c.HospitalId = @HospitalId AND c.VisitDate BETWEEN @FromDate AND @ToDate
      AND p.IsDeleted = 0 AND c.IsDeleted = 0
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
    SELECT DoctorId, COUNT(*) AS InvoiceCount, SUM(TotalAmount) AS TotalInvoiced, SUM(PaidAmount) AS TotalPaid
    FROM bil.Invoice
    WHERE HospitalId = @HospitalId AND CAST(CreatedAt AS DATE) BETWEEN @FromDate AND @ToDate AND IsDeleted = 0
    GROUP BY DoctorId;
END;
GO

-- ---- Reporting views (performance) ----

CREATE OR ALTER VIEW bil.vw_RevenueByServiceCategory AS
    SELECT i.HospitalId, i.Currency, li.Category,
           SUM(li.LineTotal) AS TotalRevenue,
           COUNT(DISTINCT i.InvoiceId) AS InvoiceCount,
           CAST(i.CreatedAt AS DATE) AS InvoiceDate
    FROM bil.Invoice i
    INNER JOIN bil.InvoiceLineItem li ON li.InvoiceId = i.InvoiceId
    WHERE i.Status IN ('PAID','PARTIAL') AND i.IsDeleted = 0
    GROUP BY i.HospitalId, i.Currency, li.Category, CAST(i.CreatedAt AS DATE);
GO

CREATE OR ALTER VIEW bil.vw_DayEndSummary AS
    SELECT i.HospitalId, CAST(i.CreatedAt AS DATE) AS BillingDate,
           i.Currency,
           COUNT(DISTINCT i.InvoiceId) AS InvoiceCount,
           SUM(i.TotalAmount) AS TotalBilled,
           SUM(i.PaidAmount) AS TotalCollected,
           SUM(i.TotalAmount - i.PaidAmount) AS TotalOutstanding
    FROM bil.Invoice i
    WHERE i.Status <> 'CANCELLED' AND i.IsDeleted = 0
    GROUP BY i.HospitalId, CAST(i.CreatedAt AS DATE), i.Currency;
GO

CREATE OR ALTER VIEW bil.vw_PaymentsByComponent AS
    SELECT p.HospitalId, CAST(p.PaidAt AS DATE) AS PaymentDate,
           p.ComponentType, p.PaymentMethod,
           SUM(p.Amount) AS TotalAmount, COUNT(*) AS PaymentCount
    FROM bil.Payment p
    WHERE p.IsVoided = 0 AND p.IsDeleted = 0
    GROUP BY p.HospitalId, CAST(p.PaidAt AS DATE), p.ComponentType, p.PaymentMethod;
GO

-- ============================================================
-- 18. COMPLIANCE (cfg.Compliance, cfg.HospitalCompliance)
-- ============================================================

CREATE OR ALTER PROCEDURE cfg.usp_ListCompliances
    @Country        NVARCHAR(5)
AS BEGIN
    SET NOCOUNT ON;
    SELECT ComplianceId, ComplianceName, Country, IsMandatory, DisplayLabel, PrintLabel,
           ValidationRegex, SortOrder
    FROM cfg.Compliance
    WHERE IsActive = 1 AND IsDeleted = 0 AND (Country = @Country OR Country = 'ALL')
    ORDER BY SortOrder;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_ListHospitalCompliances
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT hc.HospitalComplianceId, hc.HospitalId, hc.ComplianceId, c.ComplianceName,
           c.DisplayLabel, c.PrintLabel, c.IsMandatory,
           hc.RegistrationNumber, hc.IssuedOn, hc.ExpiresOn
    FROM cfg.HospitalCompliance hc
    INNER JOIN cfg.Compliance c ON c.ComplianceId = hc.ComplianceId
    WHERE hc.HospitalId = @HospitalId AND hc.IsActive = 1 AND hc.IsDeleted = 0;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_SaveHospitalCompliance
    @HospitalId     UNIQUEIDENTIFIER,
    @ComplianceId   UNIQUEIDENTIFIER,
    @RegistrationNumber NVARCHAR(100),
    @IssuedOn       DATE = NULL,
    @ExpiresOn      DATE = NULL,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='UPDATE', EntityType='HospitalCompliance')
    MERGE cfg.HospitalCompliance AS tgt
    USING (SELECT @HospitalId AS HospitalId, @ComplianceId AS ComplianceId) AS src
        ON tgt.HospitalId = src.HospitalId AND tgt.ComplianceId = src.ComplianceId
    WHEN MATCHED THEN UPDATE SET
        RegistrationNumber = @RegistrationNumber, IssuedOn = @IssuedOn, ExpiresOn = @ExpiresOn,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHEN NOT MATCHED THEN
        INSERT (HospitalId, ComplianceId, RegistrationNumber, IssuedOn, ExpiresOn, CreatedBy)
        VALUES (@HospitalId, @ComplianceId, @RegistrationNumber, @IssuedOn, @ExpiresOn, @ModifiedBy);

    SELECT HospitalComplianceId, RegistrationNumber, IssuedOn, ExpiresOn
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
    SELECT MessageTemplateId, HospitalId, Name, TriggerEvent, Channel, TemplateText, IsEnabled
    FROM cfg.MessageTemplate
    WHERE HospitalId = @HospitalId AND IsActive = 1 AND IsDeleted = 0;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_UpdateMessageTemplateStatus
    @MessageTemplateId UNIQUEIDENTIFIER,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- data.js toggles the current value (no explicit flag passed by api.js)
    UPDATE cfg.MessageTemplate
    SET IsEnabled = CASE WHEN IsEnabled = 1 THEN 0 ELSE 1 END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE MessageTemplateId = @MessageTemplateId;
END;
GO

CREATE OR ALTER PROCEDURE cfg.usp_UpdateMessageTemplate
    @MessageTemplateId UNIQUEIDENTIFIER,
    @Name           NVARCHAR(100) = NULL,
    @TriggerEvent   NVARCHAR(30) = NULL,
    @Channel        NVARCHAR(10) = NULL,
    @TemplateText   NVARCHAR(1000) = NULL,
    @IsEnabled      BIT = NULL,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- Partial update — mirrors data.js's Object.assign(template, patch): only
    -- overwrites fields the caller actually supplied (non-NULL). Distinct from
    -- usp_UpdateMessageTemplateStatus above, which only ever toggles IsEnabled.
    UPDATE cfg.MessageTemplate
    SET Name = COALESCE(@Name, Name),
        TriggerEvent = COALESCE(@TriggerEvent, TriggerEvent),
        Channel = COALESCE(@Channel, Channel),
        TemplateText = COALESCE(@TemplateText, TemplateText),
        IsEnabled = COALESCE(@IsEnabled, IsEnabled),
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE MessageTemplateId = @MessageTemplateId;

    SELECT MessageTemplateId, HospitalId, Name, TriggerEvent, Channel, TemplateText, IsEnabled
    FROM cfg.MessageTemplate WHERE MessageTemplateId = @MessageTemplateId;
END;
GO

CREATE OR ALTER PROCEDURE msg.usp_GetNotificationConfig
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- ApiKey (ENCRYPTION: RANDOMIZED) is deliberately excluded from the read path;
    -- expose only IsEnabled/Provider to the settings UI.
    SELECT Channel, Provider, ConfigJson, IsEnabled
    FROM msg.NotificationConfig
    WHERE HospitalId = @HospitalId AND IsDeleted = 0;
END;
GO

CREATE OR ALTER PROCEDURE msg.usp_SaveNotificationConfig
    @HospitalId     UNIQUEIDENTIFIER,
    @Channel        NVARCHAR(10),
    @Provider       NVARCHAR(20) = NULL,
    @ApiKey         NVARCHAR(500) = NULL,
    @ConfigJson     NVARCHAR(MAX) = NULL,
    @IsEnabled      BIT = 0,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='UPDATE', EntityType='NotificationConfig')
    MERGE msg.NotificationConfig AS tgt
    USING (SELECT @HospitalId AS HospitalId, @Channel AS Channel) AS src
        ON tgt.HospitalId = src.HospitalId AND tgt.Channel = src.Channel
    WHEN MATCHED THEN UPDATE SET
        Provider = ISNULL(@Provider, Provider), ApiKey = ISNULL(@ApiKey, ApiKey),
        ConfigJson = ISNULL(@ConfigJson, ConfigJson), IsEnabled = @IsEnabled,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHEN NOT MATCHED THEN
        INSERT (HospitalId, Channel, Provider, ApiKey, ConfigJson, IsEnabled)
        VALUES (@HospitalId, @Channel, @Provider, @ApiKey, @ConfigJson, @IsEnabled);
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
    -- Status defaults to QUEUED if the channel is enabled in msg.NotificationConfig,
    -- else SIMULATED (Phase-1 sandbox behavior preserved for demo hospitals).
    DECLARE @IsEnabled BIT;
    SELECT @IsEnabled = IsEnabled FROM msg.NotificationConfig
    WHERE HospitalId = @HospitalId AND Channel = @Channel;

    INSERT INTO msg.MessageQueue (HospitalId, PatientId, Channel, TemplateName,
                                   PatientName, PatientPhone, MessageText, Status)
    OUTPUT inserted.MessageQueueId, inserted.Status, inserted.QueuedAt
    VALUES (@HospitalId, @PatientId, @Channel, @TemplateName, @PatientName, @PatientPhone,
            @MessageText, CASE WHEN ISNULL(@IsEnabled, 0) = 1 THEN 'QUEUED' ELSE 'SIMULATED' END);
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
    SELECT MessageQueueId, PatientId, Channel, TemplateName, Status, QueuedAt, SentAt, ErrorMessage
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
    @UserId         UNIQUEIDENTIFIER,
    @Action         NVARCHAR(20),
    @EntityType     NVARCHAR(50),
    @EntityId       NVARCHAR(50) = NULL,
    @Details        NVARCHAR(500) = NULL,
    @SessionToken   NVARCHAR(100) = NULL,
    @Browser        NVARCHAR(100) = NULL,
    @IpAddress      NVARCHAR(45) = NULL,
    @UserAgent      NVARCHAR(200) = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- Append-only (sec.AuditLog has no IsActive/IsDeleted/ModifiedAt per architecture §9)
    -- Details must never contain PII — see architecture doc §8 rule 6 / §9 "What NOT to log"
    INSERT INTO sec.AuditLog (HospitalId, UserId, [Action], EntityType, EntityId,
                              Details, SessionToken, Browser, IpAddress, UserAgent)
    OUTPUT inserted.AuditLogId, inserted.CreatedAt
    VALUES (@HospitalId, @UserId, @Action, @EntityType, @EntityId,
            @Details, @SessionToken, @Browser, @IpAddress, @UserAgent);
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListAuditLogs
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    SELECT AuditLogId, UserId, [Action], EntityType, EntityId, Details, CreatedAt
    FROM sec.AuditLog WHERE HospitalId = @HospitalId
    ORDER BY CreatedAt DESC;
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
    SELECT TOP (@Top) AuditLogId, UserId, [Action], EntityType, EntityId, Details, CreatedAt
    FROM sec.AuditLog
    WHERE HospitalId = @HospitalId
      AND (@Action IS NULL OR [Action] = @Action)
      AND (@EntityType IS NULL OR EntityType = @EntityType)
      AND (@UserId IS NULL OR UserId = @UserId)
      AND (@FromDate IS NULL OR CAST(CreatedAt AS DATE) >= @FromDate)
      AND (@ToDate IS NULL OR CAST(CreatedAt AS DATE) <= @ToDate)
    ORDER BY CreatedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListExportTables
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- Row counts for the export-picker screen — allow-listed table set only
    -- (never expose arbitrary table names to the client, per architecture §8.5).
    SELECT 'patients' AS TableKey, COUNT(*) AS RowCount_ FROM opd.Patient WHERE HospitalId = @HospitalId AND IsDeleted = 0
    UNION ALL SELECT 'appointments', COUNT(*) FROM opd.Appointment WHERE HospitalId = @HospitalId AND IsDeleted = 0
    UNION ALL SELECT 'consultations', COUNT(*) FROM cln.Consultation WHERE HospitalId = @HospitalId AND IsDeleted = 0
    UNION ALL SELECT 'prescriptions', COUNT(*) FROM cln.Prescription WHERE HospitalId = @HospitalId AND IsDeleted = 0
    UNION ALL SELECT 'invoices', COUNT(*) FROM bil.Invoice WHERE HospitalId = @HospitalId AND IsDeleted = 0
    UNION ALL SELECT 'paymentTransactions', COUNT(*) FROM bil.Payment WHERE HospitalId = @HospitalId AND IsDeleted = 0
    UNION ALL SELECT 'receipts', COUNT(*) FROM bil.Receipt WHERE HospitalId = @HospitalId AND IsDeleted = 0
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
    FROM opd.Patient WHERE HospitalId = @HospitalId AND IsDeleted = 0;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListExportData_Appointments
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='EXPORT', Details='appointments')
    SELECT AppointmentId, PatientId, DoctorId, AppointmentDate, SlotStart, SlotEnd, [Type], Status
    FROM opd.Appointment WHERE HospitalId = @HospitalId AND IsDeleted = 0;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListExportData_Consultations
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='EXPORT', Details='consultations')
    SELECT ConsultationId, PatientId, DoctorId, VisitDate, ConsultationType, Status
    FROM cln.Consultation WHERE HospitalId = @HospitalId AND IsDeleted = 0;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListExportData_Prescriptions
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='EXPORT', Details='prescriptions')
    SELECT PrescriptionId, ConsultationId, PatientId, MedicineName, Status, CreatedAt
    FROM cln.Prescription WHERE HospitalId = @HospitalId AND IsDeleted = 0;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListExportData_Invoices
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='EXPORT', Details='invoices')
    SELECT InvoiceId, InvoiceNumber, PatientId, TotalAmount, PaidAmount, Status, CreatedAt
    FROM bil.Invoice WHERE HospitalId = @HospitalId AND IsDeleted = 0;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListExportData_PaymentTransactions
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='EXPORT', Details='paymentTransactions')
    SELECT PaymentId, InvoiceId, TransactionNumber, Amount, PaymentMethod, PaidAt, IsVoided
    FROM bil.Payment WHERE HospitalId = @HospitalId AND IsDeleted = 0;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListExportData_Receipts
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='EXPORT', Details='receipts')
    SELECT ReceiptId, ReceiptNumber, InvoiceId, TotalAmount, Currency, GeneratedAt
    FROM bil.Receipt WHERE HospitalId = @HospitalId AND IsDeleted = 0;
END;
GO

CREATE OR ALTER PROCEDURE sec.usp_ListExportData_AuditLogs
    @HospitalId     UNIQUEIDENTIFIER
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='EXPORT', Details='auditLogs')
    SELECT AuditLogId, UserId, [Action], EntityType, EntityId, CreatedAt
    FROM sec.AuditLog WHERE HospitalId = @HospitalId;
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
