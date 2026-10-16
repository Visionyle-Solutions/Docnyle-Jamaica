/* ============================================================
   Consult360 — Row-level data-change audit (SQL Server 2019)

   Run after schema.sql. Re-runnable: tables are created only if missing,
   the configuration seed is a MERGE, procedures are CREATE OR ALTER.

   What it records
     Every INSERT / UPDATE / DELETE on an audited table writes one row per
     affected record to sec.DataAuditLog:
       INSERT       full new row (JSON)
       UPDATE       changed columns only, as {"Col":{"o":old,"n":new}}
       SOFT_DELETE  IsActive 1 → 0 (the application's delete; IsDeleted no longer exists)
       REACTIVATE   IsActive 0 → 1
       DELETE       full old row, flagged ActorIsInferred
     Updates that change nothing write nothing.
     The log itself is insert-only: UPDATE and DELETE on it are refused (section 2).

   How it differs from sec.AuditLog
     sec.AuditLog is written by the application (AuditService.Record) for
     user-level events: LOGIN, PRINT, EXPORT, SIGNOFF, OVERRIDE. That stays as
     it is. sec.DataAuditLog is written by triggers and answers "what did this
     row look like before, and who changed it".

   How it is built
     The triggers are not written by hand. sec.AuditConfig classifies every
     table; sec.usp_GenerateAuditTriggers reads it and emits one trigger per
     table (schema.trg_Audit_{Table}). To change behavior, edit the config
     and regenerate — never hand-edit a generated trigger.

   Adapted to this schema
     - Tables live in cfg/sec/opd/cln/bil/inv/msg, not dbo; config is keyed on
       (SchemaName, TableName) and triggers are created in the table's schema.
     - RecordId is the external {Model}Id UNIQUEIDENTIFIER (the NEWSEQUENTIALID
       column the API exposes), not the internal {Table}PkId. Tables without one
       fall back to their primary key; composite keys (sec.DoctorSpecialty,
       cfg.LabTestSpecialty) are rendered as "Col1=...;Col2=...".
     - HospitalId / ActorId are UNIQUEIDENTIFIER. Tables without a HospitalId
       column derive it through HospitalIdSource.
     - CreatedBy is NOT NULL and defaults to the System user
       00000000-0000-0000-0000-000000000000. The trigger turns that back into
       NULL in ActorId: "no user" is recorded as unknown, never as a person.
     - ModifiedBy is only trusted when the UPDATE statement itself set it.
       A procedure that updates a row without setting ModifiedBy leaves the
       previous editor's id in place; attributing the change to them would be
       wrong, so the actor is recorded as NULL instead.
     - Domain actor columns the procedures already populate (RecordedBy,
       UploadedByUserId, VerifiedBy, VoidedBy, OverriddenBy, ...) are mapped in
       config and take precedence over CreatedBy/ModifiedBy.
     - MaskedColumns: PHI and secrets (the columns marked "ENCRYPTION:" in
       schema.sql, PasswordHash, ApiKey, ConfidentialNote.NoteText) are recorded
       as changed, but their values are never copied into the log.
     - Columns already under Always Encrypted are skipped automatically: the
       server holds only ciphertext and cannot compare them.
   ============================================================ */

-- Stored with the procedure and triggers; FOR XML .value() and ISJSON checks require them.
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO


/* ------------------------------------------------------------
   1. Configuration table
   ------------------------------------------------------------ */
IF OBJECT_ID(N'sec.AuditConfig', N'U') IS NULL
CREATE TABLE sec.AuditConfig (
    AuditConfigPkId INT IDENTITY(1,1) NOT NULL,
    SchemaName SYSNAME NOT NULL,
    TableName SYSNAME NOT NULL,
    Tier TINYINT NOT NULL CONSTRAINT CK_sec_AC_Tier CHECK (Tier BETWEEN 0 AND 3),
        -- 0 = not audited (trigger dropped), 1 = configuration / reference,
        -- 2 = operational, financial, security, 3 = clinical / PHI
    Domain NVARCHAR(40) NOT NULL,                       -- 'Unclassified' blocks @Execute = 1
    IsEnabled BIT NOT NULL CONSTRAINT DF_sec_AC_Enabled DEFAULT 1,
    ExcludedColumns NVARCHAR(2000) NULL,                -- comma list: never diffed, never copied (blobs, snapshots)
    MaskedColumns NVARCHAR(2000) NULL,                  -- comma list: change recorded, value never copied (PHI, secrets)
    RecordIdColumn SYSNAME NULL,                        -- NULL = the NEWSEQUENTIALID column, else the primary key
    HospitalIdSource NVARCHAR(400) NULL,                -- scalar subquery; {r} stands for the audited row, e.g.
                                                        -- (SELECT x.HospitalId FROM bil.Invoice x WHERE x.InvoiceId = {r}.InvoiceId)
    ActorColumnInsert SYSNAME NULL,                     -- checked before CreatedBy
    ActorColumnUpdate SYSNAME NULL,                     -- used when this UPDATE changed it, before ModifiedBy
    Rationale NVARCHAR(400) NULL,
    ReviewedBy NVARCHAR(100) NULL,                      -- sign-off; NULL blocks @Execute = 1
    ReviewedOn DATETIMEOFFSET(3) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_sec_AC_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_sec_AC_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_AC_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_sec_AC PRIMARY KEY CLUSTERED (AuditConfigPkId),
    CONSTRAINT UQ_sec_AC_Table UNIQUE NONCLUSTERED (SchemaName, TableName)
);
GO


/* ------------------------------------------------------------
   2. Audit log table
   No foreign keys on purpose: an audit row must never be rejected because
   the hospital or user it names was changed or removed.
   ------------------------------------------------------------ */
IF OBJECT_ID(N'sec.DataAuditLog', N'U') IS NULL
CREATE TABLE sec.DataAuditLog (
    DataAuditLogPkId BIGINT IDENTITY(1,1) NOT NULL,
    DataAuditLogId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_DAL_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NULL,                   -- NULL for global tables (cfg.Payer, cfg.Compliance, ...)
    ActorId UNIQUEIDENTIFIER NULL,                      -- NULL = not known; never the System user
    [Action] NVARCHAR(20) NOT NULL CONSTRAINT CK_sec_DAL_Action
        CHECK ([Action] IN ('INSERT','UPDATE','SOFT_DELETE','REACTIVATE','DELETE','AUDIT_ERROR')),
    SchemaName SYSNAME NOT NULL,
    TableName SYSNAME NOT NULL,
    RecordId NVARCHAR(200) NULL,
    Details NVARCHAR(MAX) NULL CONSTRAINT CK_sec_DAL_Details CHECK (Details IS NULL OR ISJSON(Details) = 1),
    IsActive BIT NOT NULL CONSTRAINT DF_sec_DAL_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_sec_DAL_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_DAL_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_sec_DAL PRIMARY KEY CLUSTERED (DataAuditLogPkId),
    CONSTRAINT UQ_sec_DAL_Id UNIQUE NONCLUSTERED (DataAuditLogId)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_sec_DAL_Hospital_Date' AND object_id = OBJECT_ID(N'sec.DataAuditLog'))
    CREATE NONCLUSTERED INDEX IX_sec_DAL_Hospital_Date ON sec.DataAuditLog (HospitalId, CreatedAt DESC)
    WITH (DATA_COMPRESSION = PAGE);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_sec_DAL_Actor_Date' AND object_id = OBJECT_ID(N'sec.DataAuditLog'))
    CREATE NONCLUSTERED INDEX IX_sec_DAL_Actor_Date ON sec.DataAuditLog (ActorId, CreatedAt DESC)
    INCLUDE ([Action], SchemaName, TableName, RecordId) WITH (DATA_COMPRESSION = PAGE);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_sec_DAL_Record_Date' AND object_id = OBJECT_ID(N'sec.DataAuditLog'))
    CREATE NONCLUSTERED INDEX IX_sec_DAL_Record_Date ON sec.DataAuditLog (SchemaName, TableName, RecordId, CreatedAt DESC)
    INCLUDE ([Action], ActorId, HospitalId) WITH (DATA_COMPRESSION = PAGE);
GO

/* Insert-only. Audit rows are evidence: once written they cannot be changed or removed.
   Two layers, because either alone can be got around:
     - DENY stops every application login (they all inherit public). The audit
       triggers still insert: they are owned by dbo like this table, so ownership
       chaining skips the permission check.
     - The INSTEAD OF trigger also stops users who hold UPDATE/DELETE through a role
       grant or db_owner, and makes the refusal explicit instead of a permission error.
   Not covered: sysadmin / dbo can disable the trigger, and TRUNCATE or DROP bypass
   triggers (both need ALTER/CONTROL, denied below to everyone but owners). Tamper-
   evidence against administrators needs SQL Server 2022 ledger tables or shipping
   the log off the server. ModifiedAt/ModifiedBy stay NULL and IsActive stays 1. */
DENY UPDATE, DELETE, ALTER ON sec.DataAuditLog TO public;
GO
CREATE OR ALTER TRIGGER sec.trg_DataAuditLog_Immutable ON sec.DataAuditLog
INSTEAD OF UPDATE, DELETE
AS BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM deleted) RETURN;   -- statements that touch no rows pass
    THROW 51000, N'sec.DataAuditLog is insert-only: audit rows cannot be updated or deleted.', 1;
END;
GO


/* ------------------------------------------------------------
   3. Classification of every table in schema.sql
   ReviewedBy is left NULL on purpose: the classification below is a
   proposal. Someone accountable for compliance signs it off (section 5)
   before triggers can be applied.
   ------------------------------------------------------------ */
MERGE sec.AuditConfig AS tgt
USING (VALUES
-- SchemaName, TableName, Tier, Domain, ExcludedColumns, MaskedColumns, HospitalIdSource, ActorColumnInsert, ActorColumnUpdate, Rationale
 ('cfg','TimeZone',               1,'Configuration', NULL, NULL, NULL, NULL, NULL, 'IANA → Windows timezone map used for every local date'),
 ('cfg','Hospital',               1,'Configuration', 'LogoBase64', 'ContactNumber,PrimaryEmail', NULL, NULL, NULL, 'Tenant identity, prefixes, label rules; logo image excluded'),
 ('cfg','HospitalSetting',        1,'Configuration', NULL, NULL, NULL, NULL, NULL, 'Slot length, follow-up and MRN rules change billing and scheduling outcomes'),
 ('cfg','AppointmentType',        1,'Configuration', NULL, NULL, NULL, NULL, NULL, 'Drives duration and chargeability'),
 ('cfg','Payer',                  1,'Configuration', NULL, NULL, NULL, NULL, NULL, 'Global payer master'),
 ('cfg','IntakeQuestion',         1,'Configuration', NULL, NULL, NULL, NULL, NULL, 'Wording of questions patients answered'),
 ('cfg','ConsentForm',            1,'Configuration', NULL, NULL, NULL, NULL, NULL, 'Legal text versions patients consented to'),
 ('cfg','Compliance',             1,'Configuration', NULL, NULL, NULL, NULL, NULL, 'Country registration rules'),
 ('cfg','HospitalCompliance',     1,'Configuration', NULL, NULL, NULL, NULL, NULL, 'Registration numbers printed on documents'),
 ('cfg','Specialty',              1,'Configuration', NULL, NULL, NULL, NULL, NULL, NULL),
 ('cfg','VitalDefinition',        1,'Configuration', NULL, NULL, NULL, NULL, NULL, 'Ranges drive abnormal-vital flags'),
 ('cfg','MedicalHistoryItem',     1,'Configuration', NULL, NULL, NULL, NULL, NULL, NULL),
 ('cfg','MedicalEventType',       1,'Configuration', NULL, NULL, NULL, NULL, NULL, NULL),
 ('cfg','SymptomCatalog',         1,'Configuration', NULL, NULL, NULL, NULL, NULL, NULL),
 ('cfg','ICD10Catalog',           0,'Reference',     NULL, NULL, NULL, NULL, NULL, 'Static code set, bulk-loaded; changes come from releases, not users'),
 ('cfg','DrugFormulary',          1,'Configuration', NULL, NULL, NULL, NULL, NULL, 'Controlled-substance and default-dose flags affect prescribing'),
 ('cfg','LabPartner',             1,'Configuration', NULL, NULL, NULL, NULL, NULL, NULL),
 ('cfg','LabTestCatalog',         1,'Configuration', NULL, NULL, NULL, NULL, NULL, NULL),
 ('cfg','LabTestSpecialty',       1,'Configuration', NULL, NULL, '(SELECT x.HospitalId FROM cfg.LabTestCatalog x WHERE x.TestId = {r}.TestId)', NULL, NULL, 'Composite key junction'),
 ('cfg','ClinicalCondition',      1,'Configuration', NULL, NULL, NULL, NULL, NULL, NULL),
 ('cfg','PhraseBank',             1,'Configuration', NULL, NULL, NULL, NULL, NULL, NULL),
 ('cfg','PaymentMethod',          1,'Configuration', NULL, NULL, NULL, NULL, NULL, NULL),
 ('cfg','MessageTemplate',        1,'Configuration', NULL, NULL, NULL, NULL, NULL, 'Text sent to patients'),
 ('cfg','ConsultationFormConfig', 1,'Configuration', NULL, NULL, NULL, NULL, 'PublishedBy', 'Which consultation sections a doctor saw'),
 ('cfg','ContradictionRule',      1,'Configuration', NULL, NULL, NULL, NULL, NULL, 'Clinical safety rules'),
 ('cfg','BranchFlowRule',         1,'Configuration', NULL, NULL, NULL, NULL, NULL, 'Clinical flow rules'),
 ('sec','ApplicationUser',        2,'Security',      NULL, 'PasswordHash,FullName,Email,Phone', NULL, NULL, NULL, 'Account changes; password change recorded, hash never copied'),
 ('sec','UserRole',               2,'Security',      NULL, NULL, '(SELECT x.HospitalId FROM sec.ApplicationUser x WHERE x.UserId = {r}.UserId)', NULL, NULL, 'Privilege grants'),
 ('sec','DoctorSpecialty',        2,'Security',      NULL, NULL, '(SELECT x.HospitalId FROM sec.ApplicationUser x WHERE x.UserId = {r}.DoctorId)', NULL, NULL, 'Composite key junction'),
 ('sec','PermissionMatrix',       2,'Security',      NULL, NULL, NULL, NULL, NULL, 'Access control changes'),
 ('sec','AuditLog',               0,'System',        NULL, NULL, NULL, NULL, NULL, 'Already an append-only audit trail'),
 ('sec','UserNotification',       0,'System',        NULL, NULL, NULL, NULL, NULL, 'Read flags churn; no clinical or financial effect'),
 ('sec','UserSession',            0,'System',        NULL, NULL, NULL, NULL, NULL, 'Holds session tokens; logins are in sec.AuditLog'),
 ('sec','LoginAttempt',           0,'System',        NULL, NULL, NULL, NULL, NULL, 'Itself an append-only log'),
 ('opd','Patient',                3,'Patient',       NULL, 'FullName,Phone,Email,NationalHealthId,InsuranceProvider,InsurancePolicyNo,EmergencyContactName,EmergencyContactPhone,Trn,CountryCode', NULL, NULL, NULL, 'Demographics; identifiers masked'),
 ('opd','PatientAllergy',         3,'Patient',       NULL, NULL, NULL, NULL, NULL, 'Allergy list drives prescribing alerts'),
 ('opd','PatientMedicalHistory',  3,'Patient',       NULL, NULL, NULL, 'RecordedByUserId', NULL, NULL),
 ('opd','PatientMedicalEvent',    3,'Patient',       NULL, NULL, NULL, 'RecordedByUserId', NULL, NULL),
 ('opd','PatientFamilyHistory',   3,'Patient',       NULL, NULL, NULL, NULL, NULL, NULL),
 ('opd','PatientConsentDocument', 3,'Patient',       NULL, NULL, NULL, 'UploadedByUserId', NULL, 'Signed consent uploads'),
 ('opd','Appointment',            2,'Scheduling',    NULL, NULL, NULL, NULL, 'CheckedInBy', 'Status transitions, no-shows, cancellations'),
 ('opd','PatientCoverage',        2,'Billing',       NULL, 'MemberId,PolicyNumber,GovtCardNumber', NULL, NULL, NULL, 'Insurance identifiers masked'),
 ('opd','DoctorSlotConfig',       1,'Scheduling',    NULL, NULL, NULL, NULL, NULL, NULL),
 ('opd','DoctorSetting',          1,'Scheduling',    NULL, NULL, NULL, NULL, NULL, NULL),
 ('opd','DoctorLeave',            2,'Scheduling',    NULL, NULL, NULL, NULL, NULL, NULL),
 ('opd','IntakeResponse',         3,'Patient',       NULL, NULL, NULL, NULL, NULL, NULL),
 ('opd','ConsentRecord',          3,'Patient',       'BodySnapshot', NULL, NULL, NULL, NULL, 'Snapshot text is immutable and large; the consent event is what matters'),
 ('cln','Consultation',           3,'Clinical',      NULL, NULL, NULL, NULL, NULL, 'SOAP notes and status; edits after sign-off must be traceable'),
 ('cln','ConsultationVital',      3,'Clinical',      NULL, NULL, NULL, 'RecordedBy', NULL, NULL),
 ('cln','ConsultationCustomVital',3,'Clinical',      NULL, NULL, NULL, NULL, NULL, NULL),
 ('cln','ConsultationSymptom',    3,'Clinical',      NULL, NULL, NULL, NULL, NULL, NULL),
 ('cln','ConsultationDiagnosis',  3,'Clinical',      NULL, NULL, NULL, NULL, NULL, NULL),
 ('cln','Prescription',           3,'Clinical',      NULL, NULL, NULL, NULL, NULL, NULL),
 ('cln','InvestigationOrder',     3,'Clinical',      NULL, NULL, NULL, NULL, NULL, NULL),
 ('cln','LabResult',              3,'Clinical',      NULL, NULL, NULL, 'UploadedByUserId', NULL, NULL),
 ('cln','LabResultAttachment',    3,'Clinical',      NULL, NULL, NULL, 'UploadedByUserId', NULL, NULL),
 ('cln','Referral',               3,'Clinical',      NULL, NULL, NULL, NULL, NULL, NULL),
 ('cln','ConsultationServiceItem',3,'Clinical',      NULL, NULL, NULL, 'AddedByUserId', NULL, 'Procedures performed; also billed'),
 ('cln','ConsultationAttestation',3,'Clinical',      NULL, NULL, NULL, NULL, NULL, 'Sign-off record'),
 ('cln','ConsultationAddendum',   3,'Clinical',      NULL, NULL, NULL, NULL, NULL, NULL),
 ('cln','ConfidentialNote',       3,'Clinical',      NULL, 'NoteText', NULL, NULL, NULL, 'Edit trail kept; note text never copied'),
 ('cln','ConsultationSketch',     3,'Clinical',      'ImageData', NULL, NULL, NULL, NULL, 'Image payload excluded'),
 ('cln','DoctorLabFavorite',      0,'Configuration', NULL, NULL, NULL, NULL, NULL, 'A doctor''s personal shortlist; no clinical or financial effect'),
 ('cln','ConsultationCustomValue',3,'Clinical',      NULL, NULL, NULL, 'UpdatedBy', 'UpdatedBy', NULL),
 ('cln','PrescriptionSafetyAlert',3,'Clinical',      NULL, NULL, NULL, NULL, 'OverriddenBy', 'Alert overrides'),
 ('bil','DoctorFeeSchedule',      2,'Billing',       NULL, NULL, NULL, NULL, NULL, NULL),
 ('bil','ServiceChargeMaster',    2,'Billing',       NULL, NULL, NULL, NULL, NULL, NULL),
 ('bil','Invoice',                2,'Billing',       NULL, NULL, NULL, NULL, NULL, NULL),
 ('bil','InvoiceLineItem',        2,'Billing',       NULL, NULL, '(SELECT x.HospitalId FROM bil.Invoice x WHERE x.InvoiceId = {r}.InvoiceId)', NULL, NULL, 'Hard-deleted by usp_DeleteInvoiceLineItem; DELETE keeps the full row'),
 ('bil','EligibilityVerification',2,'Billing',       NULL, NULL, '(SELECT x.HospitalId FROM opd.PatientCoverage x WHERE x.CoverageId = {r}.CoverageId)', 'VerifiedBy', NULL, NULL),
 ('bil','InvoiceCoverageLine',    2,'Billing',       NULL, NULL, '(SELECT x.HospitalId FROM bil.Invoice x WHERE x.InvoiceId = {r}.InvoiceId)', NULL, NULL, NULL),
 ('bil','Payment',                2,'Billing',       NULL, NULL, NULL, 'ReceivedBy', 'VoidedBy', 'Voids must be attributable'),
 ('bil','Receipt',                2,'Billing',       NULL, NULL, NULL, NULL, NULL, NULL),
 ('bil','CreditNote',             2,'Billing',       NULL, NULL, NULL, 'IssuedBy', NULL, NULL),
 ('inv','InventoryItem',          2,'Inventory',     NULL, NULL, NULL, NULL, NULL, 'Selling price and tax billed for stocked items'),
 ('inv','InventoryItemService',   2,'Inventory',     NULL, NULL, '(SELECT x.HospitalId FROM inv.InventoryItem x WHERE x.InventoryItemId = {r}.InventoryItemId)', NULL, NULL, 'Which items are offered for which service; composite key'),
 ('inv','StockBatch',             2,'Inventory',     NULL, NULL, NULL, 'ReceivedByUserId', NULL, 'On-hand quantities; every change also has a ledger row'),
 ('inv','StockMovement',          0,'Inventory',     NULL, NULL, NULL, NULL, NULL, 'Itself an append-only stock ledger'),
 ('msg','NotificationConfig',     1,'Messaging',     NULL, 'ApiKey', NULL, NULL, NULL, 'Provider settings; API key masked'),
 ('msg','MessageQueue',           0,'Messaging',     NULL, NULL, NULL, NULL, NULL, 'Delivery-status churn; holds copies of patient contact details')
) AS src (SchemaName, TableName, Tier, Domain, ExcludedColumns, MaskedColumns, HospitalIdSource, ActorColumnInsert, ActorColumnUpdate, Rationale)
ON tgt.SchemaName = src.SchemaName AND tgt.TableName = src.TableName
WHEN NOT MATCHED BY TARGET THEN
    INSERT (SchemaName, TableName, Tier, Domain, ExcludedColumns, MaskedColumns, HospitalIdSource, ActorColumnInsert, ActorColumnUpdate, Rationale)
    VALUES (src.SchemaName, src.TableName, src.Tier, src.Domain, src.ExcludedColumns, src.MaskedColumns, src.HospitalIdSource, src.ActorColumnInsert, src.ActorColumnUpdate, src.Rationale);
-- Existing rows are left alone: once a row is reviewed, re-running this file must not overwrite the sign-off.
GO


/* ------------------------------------------------------------
   4. Trigger generator
   ------------------------------------------------------------ */
CREATE OR ALTER PROCEDURE sec.usp_GenerateAuditTriggers
    @SchemaName       SYSNAME = NULL,   -- NULL = all schemas
    @TableName        SYSNAME = NULL,   -- NULL = all configured tables
    @Execute          BIT     = 0,      -- 0 = return the DDL only, 1 = apply it
    @IncludeDrops     BIT     = 1,      -- drop existing triggers on Tier 0 / disabled tables
    @AllowUnreviewed  BIT     = 0,      -- override the sign-off gate
    @FloatScale       TINYINT = 6       -- decimal places for float/real values
AS BEGIN
    SET NOCOUNT ON;

    DECLARE @System NVARCHAR(60) = N'''00000000-0000-0000-0000-000000000000''';
    DECLARE @NL NCHAR(2) = NCHAR(13) + NCHAR(10);
    -- FOR JSON renders float/real in scientific notation; TRY_CAST to decimal keeps
    -- them readable and yields NULL rather than failing the audit on overflow.
    DECLARE @floatType NVARCHAR(30) = N'decimal(38,' + CAST(@FloatScale AS NVARCHAR(2)) + N')';

    -- Gate: emitting DDL is always allowed; applying it needs a signed-off classification.
    IF @Execute = 1 AND @AllowUnreviewed = 0
       AND EXISTS (SELECT 1 FROM sec.AuditConfig
                   WHERE IsActive = 1
                     AND (@SchemaName IS NULL OR SchemaName = @SchemaName)
                     AND (@TableName IS NULL OR TableName = @TableName)
                     AND (Domain = N'Unclassified' OR ReviewedBy IS NULL))
    BEGIN
        RAISERROR(N'Refusing to execute: sec.AuditConfig has unclassified or unsigned-off rows in scope.
Review the classification, set ReviewedBy/ReviewedOn (see audit-log.sql section 5),
or re-run with @AllowUnreviewed = 1 if you accept that.', 16, 1);
        RETURN;
    END;

    -- Housekeeping columns: never part of the UPDATE diff, otherwise every update
    -- registers as a change. The actor is captured in ActorId instead.
    DECLARE @GlobalDeny TABLE (ColumnName SYSNAME PRIMARY KEY);
    INSERT INTO @GlobalDeny VALUES (N'CreatedAt'), (N'CreatedBy'), (N'ModifiedAt'), (N'ModifiedBy');

    DECLARE @Results TABLE (
        Seq INT IDENTITY(1,1) PRIMARY KEY,
        SchemaName SYSNAME, TableName SYSNAME, Tier TINYINT,
        DropDdl NVARCHAR(MAX), CreateDdl NVARCHAR(MAX), Note NVARCHAR(400)
    );

    DECLARE @sch SYSNAME, @tbl SYSNAME, @tier TINYINT, @enabled BIT,
            @excluded NVARCHAR(2000), @masked NVARCHAR(2000), @ridCol SYSNAME, @hospSrc NVARCHAR(400),
            @actIns SYSNAME, @actUpd SYSNAME,
            @obj NVARCHAR(300), @objId INT, @trg NVARCHAR(300), @tblLit NVARCHAR(300), @schLit NVARCHAR(300),
            @pk1 SYSNAME, @pkCount INT, @join NVARCHAR(MAX),
            @ridI NVARCHAR(MAX), @ridD NVARCHAR(MAX), @hospExpr NVARCHAR(MAX), @actorExpr NVARCHAR(MAX),
            @actionExpr NVARCHAR(MAX), @diffList NVARCHAR(MAX), @rowList NVARCHAR(MAX), @dropSql NVARCHAR(MAX);

    DECLARE cfg CURSOR LOCAL FAST_FORWARD FOR
        SELECT SchemaName, TableName, Tier, IsEnabled, ExcludedColumns, MaskedColumns,
               RecordIdColumn, HospitalIdSource, ActorColumnInsert, ActorColumnUpdate
        FROM sec.AuditConfig
        WHERE IsActive = 1
          AND (@SchemaName IS NULL OR SchemaName = @SchemaName)
          AND (@TableName IS NULL OR TableName = @TableName)
          AND NOT (SchemaName = N'sec' AND TableName IN (N'AuditConfig', N'DataAuditLog'))
        ORDER BY SchemaName, TableName;

    OPEN cfg;
    FETCH NEXT FROM cfg INTO @sch, @tbl, @tier, @enabled, @excluded, @masked, @ridCol, @hospSrc, @actIns, @actUpd;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @obj    = QUOTENAME(@sch) + N'.' + QUOTENAME(@tbl);
        SET @objId  = OBJECT_ID(@obj, N'U');
        SET @trg    = QUOTENAME(@sch) + N'.' + QUOTENAME(N'trg_Audit_' + @tbl);
        SET @schLit = N'N' + QUOTENAME(@sch, N'''');
        SET @tblLit = N'N' + QUOTENAME(@tbl, N'''');
        SET @excluded = N',' + REPLACE(ISNULL(@excluded, N''), N' ', N'') + N',';
        SET @masked   = N',' + REPLACE(ISNULL(@masked, N''), N' ', N'') + N',';
        SET @dropSql  = N'IF OBJECT_ID(N' + QUOTENAME(@trg, N'''') + N', N''TR'') IS NOT NULL' + @NL
                      + N'    DROP TRIGGER ' + @trg + N';';

        IF @objId IS NULL
        BEGIN
            INSERT INTO @Results (SchemaName, TableName, Tier, Note)
            VALUES (@sch, @tbl, @tier, N'SKIPPED: table does not exist in this database.');
            GOTO NextTable;
        END;

        -- Tier 0 or disabled: no audit. Drop a trigger left from an earlier classification.
        IF @tier = 0 OR @enabled = 0
        BEGIN
            IF @IncludeDrops = 1 AND OBJECT_ID(@trg, N'TR') IS NOT NULL
                INSERT INTO @Results (SchemaName, TableName, Tier, DropDdl, Note)
                VALUES (@sch, @tbl, @tier, @dropSql, N'Not audited: trigger removed.');
            GOTO NextTable;
        END;

        -- Primary key: joins inserted to deleted. PkId is an IDENTITY and never changes.
        SELECT @pkCount = COUNT(*), @pk1 = MAX(CASE WHEN ic.key_ordinal = 1 THEN c.name ELSE N'' END),
               @join = STUFF((SELECT N' AND i.' + QUOTENAME(c2.name) + N' = d.' + QUOTENAME(c2.name)
                              FROM sys.index_columns ic2
                              JOIN sys.columns c2 ON c2.object_id = ic2.object_id AND c2.column_id = ic2.column_id
                              WHERE ic2.object_id = i.object_id AND ic2.index_id = i.index_id
                              ORDER BY ic2.key_ordinal
                              FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 5, N'')
        FROM sys.indexes i
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = @objId AND i.is_primary_key = 1
        GROUP BY i.object_id, i.index_id;

        IF @pk1 IS NULL
        BEGIN
            INSERT INTO @Results (SchemaName, TableName, Tier, Note)
            VALUES (@sch, @tbl, @tier, N'SKIPPED: no primary key. Classify as Tier 0 or add one.');
            GOTO NextTable;
        END;

        -- RecordId: the external {Model}Id, else the primary key ("A=..;B=.." when composite).
        IF @ridCol IS NULL OR COL_LENGTH(@obj, @ridCol) IS NULL
            SELECT @ridCol = c.name
            FROM sys.columns c
            JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE c.object_id = @objId AND dc.definition LIKE N'%newsequentialid%';

        IF @ridCol IS NOT NULL
            SET @ridD = N'CAST(d.' + QUOTENAME(@ridCol) + N' AS nvarchar(200))';
        ELSE IF @pkCount = 1
            SET @ridD = N'CAST(d.' + QUOTENAME(@pk1) + N' AS nvarchar(200))';
        ELSE
            SET @ridD = N'CONCAT(' + STUFF((
                SELECT N', N''' + CASE WHEN ic.key_ordinal = 1 THEN N'' ELSE N';' END + c.name + N'='', d.' + QUOTENAME(c.name)
                FROM sys.indexes i
                JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE i.object_id = @objId AND i.is_primary_key = 1
                ORDER BY ic.key_ordinal
                FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N'') + N')';
        SET @ridI = REPLACE(@ridD, N'd.[', N'i.[');

        -- HospitalId: own column, else the configured lookup, else NULL (global table).
        SET @hospExpr = CASE
            WHEN COL_LENGTH(@obj, N'HospitalId') IS NOT NULL THEN N'ISNULL(i.[HospitalId], d.[HospitalId])'
            WHEN @hospSrc IS NOT NULL THEN N'CASE WHEN i.' + QUOTENAME(@pk1) + N' IS NOT NULL THEN '
                 + REPLACE(@hospSrc, N'{r}', N'i') + N' ELSE ' + REPLACE(@hospSrc, N'{r}', N'd') + N' END'
            ELSE N'CAST(NULL AS uniqueidentifier)' END;

        -- Actor. NULLIF(..., System) keeps "no user" honest: unknown is NULL, never a person.
        IF @actIns IS NOT NULL AND COL_LENGTH(@obj, @actIns) IS NULL SET @actIns = NULL;
        IF @actUpd IS NOT NULL AND COL_LENGTH(@obj, @actUpd) IS NULL SET @actUpd = NULL;
        SET @actorExpr = N'CASE' + @NL
            + N'                    WHEN d.' + QUOTENAME(@pk1) + N' IS NULL THEN COALESCE('
            + ISNULL(N'NULLIF(TRY_CAST(i.' + QUOTENAME(@actIns) + N' AS uniqueidentifier), ' + @System + N'), ', N'')
            + CASE WHEN COL_LENGTH(@obj, N'CreatedBy') IS NOT NULL
                   THEN N'NULLIF(i.[CreatedBy], ' + @System + N'), ' ELSE N'' END
            + N'NULL)' + @NL
            + N'                    WHEN i.' + QUOTENAME(@pk1) + N' IS NOT NULL THEN COALESCE('
            -- UPDATE(col): the statement's SET list wrote the column. A column it did not
            -- write still holds the previous editor, who must not be blamed for this change.
            + ISNULL(N'CASE WHEN UPDATE(' + QUOTENAME(@actUpd) + N')'
                   + N' THEN NULLIF(TRY_CAST(i.' + QUOTENAME(@actUpd) + N' AS uniqueidentifier), ' + @System + N') END, ', N'')
            + CASE WHEN COL_LENGTH(@obj, N'ModifiedBy') IS NOT NULL
                   THEN N'CASE WHEN UPDATE([ModifiedBy]) THEN NULLIF(i.[ModifiedBy], ' + @System + N') END, '
                   ELSE N'' END
            + N'NULL)' + @NL
            -- Hard DELETE: the last recorded editor, not necessarily the deleter (flagged in the payload).
            + N'                    ELSE '
            + CASE WHEN COL_LENGTH(@obj, N'ModifiedBy') IS NOT NULL
                   THEN N'NULLIF(d.[ModifiedBy], ' + @System + N')' ELSE N'CAST(NULL AS uniqueidentifier)' END + @NL
            + N'                END';

        SET @actionExpr = N'CASE WHEN i.' + QUOTENAME(@pk1) + N' IS NOT NULL AND d.' + QUOTENAME(@pk1) + N' IS NOT NULL THEN '
            + CASE WHEN COL_LENGTH(@obj, N'IsActive') IS NOT NULL
                   THEN N'CASE WHEN d.[IsActive] = 1 AND i.[IsActive] = 0 THEN ''SOFT_DELETE'''
                      + N' WHEN d.[IsActive] = 0 AND i.[IsActive] = 1 THEN ''REACTIVATE'' ELSE ''UPDATE'' END'
                   ELSE N'''UPDATE''' END
            + N' WHEN i.' + QUOTENAME(@pk1) + N' IS NOT NULL THEN ''INSERT'' ELSE ''DELETE'' END';

        -- Columns that can be audited at all: not computed, not rowversion, not Always Encrypted, not excluded.
        DECLARE @cols TABLE (ColId INT, ColName SYSNAME, TypeName SYSNAME, IsMasked BIT, InDiff BIT);
        DELETE FROM @cols;
        INSERT INTO @cols
        SELECT c.column_id, c.name, ty.name,
               CASE WHEN @masked LIKE N'%,' + c.name + N',%' THEN 1 ELSE 0 END,
               CASE WHEN ty.name IN (N'xml', N'geography', N'geometry', N'hierarchyid', N'image', N'text', N'ntext')
                      OR c.name IN (SELECT ColumnName FROM @GlobalDeny) THEN 0 ELSE 1 END
        FROM sys.columns c
        JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objId
          AND c.is_computed = 0
          AND ty.name NOT IN (N'timestamp', N'rowversion')
          AND c.encryption_type IS NULL
          AND @excluded NOT LIKE N'%,' + c.name + N',%';

        -- UPDATE diff. EXCEPT is NULL-safe: NULL vs NULL is equal, NULL vs value is a change.
        -- Masked columns report that they changed, never the values.
        SET @diffList = (
            SELECT N'                  + ISNULL(CASE WHEN EXISTS (SELECT i.' + QUOTENAME(ColName) + N' EXCEPT SELECT d.' + QUOTENAME(ColName) + N')' + @NL
                 + N'                         THEN N'',"' + STRING_ESCAPE(ColName, 'json') + N'":'' + '
                 + CASE WHEN IsMasked = 1 THEN N'N''{"changed":true}'''
                        ELSE N'(SELECT ' + CASE WHEN TypeName IN (N'float', N'real')
                                                THEN N'TRY_CAST(d.' + QUOTENAME(ColName) + N' AS ' + @floatType + N') AS o, TRY_CAST(i.' + QUOTENAME(ColName) + N' AS ' + @floatType + N') AS n'
                                                ELSE N'd.' + QUOTENAME(ColName) + N' AS o, i.' + QUOTENAME(ColName) + N' AS n' END
                           + N' FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)' END
                 + N' END, N'''')' + @NL
            FROM @cols WHERE InDiff = 1
            ORDER BY ColId
            FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)');

        -- Full row for INSERT / DELETE payloads. {a} is replaced by i or d.
        SET @rowList = STUFF((
            SELECT N', ' + CASE
                    WHEN IsMasked = 1 THEN N'CASE WHEN {a}.' + QUOTENAME(ColName) + N' IS NULL THEN NULL ELSE N''[masked]'' END AS ' + QUOTENAME(ColName)
                    WHEN TypeName IN (N'float', N'real') THEN N'TRY_CAST({a}.' + QUOTENAME(ColName) + N' AS ' + @floatType + N') AS ' + QUOTENAME(ColName)
                    ELSE N'{a}.' + QUOTENAME(ColName) END
            FROM @cols
            ORDER BY ColId
            FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N'');

        IF @diffList IS NULL OR @rowList IS NULL
        BEGIN
            INSERT INTO @Results (SchemaName, TableName, Tier, Note)
            VALUES (@sch, @tbl, @tier, N'SKIPPED: every column is excluded. Check ExcludedColumns.');
            GOTO NextTable;
        END;

        INSERT INTO @Results (SchemaName, TableName, Tier, DropDdl, CreateDdl)
        VALUES (@sch, @tbl, @tier, @dropSql,
N'-- GENERATED by sec.usp_GenerateAuditTriggers. Do not hand-edit:' + @NL +
N'-- change sec.AuditConfig and regenerate.  Table: ' + @obj + N'  Tier: ' + CAST(@tier AS NVARCHAR(1)) + @NL +
N'CREATE TRIGGER ' + @trg + N' ON ' + @obj + @NL +
N'AFTER INSERT, UPDATE, DELETE' + @NL +
N'AS BEGIN' + @NL +
N'    SET NOCOUNT ON;' + @NL +
N'    -- Triggers run with XACT_ABORT ON, under which any error dooms the caller''s' + @NL +
N'    -- transaction. OFF lets the CATCH below keep the clinical write alive.' + @NL +
N'    SET XACT_ABORT OFF;' + @NL +
N'    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;' + @NL +
N'' + @NL +
N'    BEGIN TRY' + @NL +
N'        INSERT INTO sec.DataAuditLog (HospitalId, ActorId, [Action], SchemaName, TableName, RecordId, Details, CreatedBy)' + @NL +
N'        SELECT ' + @hospExpr + N',' + @NL +
N'               x.ActorId, x.[Action], ' + @schLit + N', ' + @tblLit + N',' + @NL +
N'               CASE WHEN i.' + QUOTENAME(@pk1) + N' IS NOT NULL THEN ' + @ridI + N' ELSE ' + @ridD + N' END,' + @NL +
N'               x.Details,' + @NL +
N'               ISNULL(x.ActorId, ' + @System + N')' + @NL +
N'        FROM inserted i' + @NL +
N'        FULL OUTER JOIN deleted d ON ' + @join + @NL +
N'        CROSS APPLY (SELECT Diff = CAST(N'''' AS nvarchar(max))' + @NL +
@diffList +
N'        ) AS df' + @NL +
N'        CROSS APPLY (' + @NL +
N'            SELECT' + @NL +
N'                [Action] = ' + @actionExpr + N',' + @NL +
N'                ActorId = ' + @actorExpr + N',' + @NL +
N'                Details = CASE' + @NL +
N'                    -- UPDATE: changed columns only; NULL when nothing changed (no audit row).' + @NL +
N'                    WHEN i.' + QUOTENAME(@pk1) + N' IS NOT NULL AND d.' + QUOTENAME(@pk1) + N' IS NOT NULL THEN' + @NL +
N'                        CASE WHEN df.Diff = N'''' THEN NULL ELSE N''{'' + STUFF(df.Diff, 1, 1, N'''') + N''}'' END' + @NL +
N'                    -- INSERT: full new row.' + @NL +
N'                    WHEN i.' + QUOTENAME(@pk1) + N' IS NOT NULL THEN' + @NL +
N'                        (SELECT ' + REPLACE(@rowList, N'{a}.', N'i.') + N' FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)' + @NL +
N'                    -- DELETE: full old row, never reduced to a diff.' + @NL +
N'                    ELSE' + @NL +
N'                        (SELECT ' + REPLACE(@rowList, N'{a}.', N'd.') + N', CAST(1 AS bit) AS ActorIsInferred FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)' + @NL +
N'                END' + @NL +
N'        ) AS x' + @NL +
N'        WHERE x.Details IS NOT NULL;' + @NL +
N'    END TRY' + @NL +
N'    BEGIN CATCH' + @NL +
N'        -- Fail-open: auditing must never block clinical care. If the transaction is' + @NL +
N'        -- already doomed nothing can be written and the caller''s statement rolls back.' + @NL +
N'        -- Replace this block with THROW if your compliance posture is fail-closed.' + @NL +
N'        IF XACT_STATE() = 1' + @NL +
N'        BEGIN' + @NL +
N'        BEGIN TRY' + @NL +
N'            INSERT INTO sec.DataAuditLog (HospitalId, ActorId, [Action], SchemaName, TableName, RecordId, Details)' + @NL +
N'            SELECT NULL, NULL, ''AUDIT_ERROR'', ' + @schLit + N', ' + @tblLit + N',' + @NL +
N'                   CASE WHEN i.' + QUOTENAME(@pk1) + N' IS NOT NULL THEN ' + @ridI + N' ELSE ' + @ridD + N' END,' + @NL +
N'                   (SELECT ERROR_NUMBER() AS ErrorNumber, ERROR_MESSAGE() AS ErrorMessage FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)' + @NL +
N'            FROM inserted i FULL OUTER JOIN deleted d ON ' + @join + N';' + @NL +
N'        END TRY' + @NL +
N'        BEGIN CATCH' + @NL +
N'            SET NOCOUNT ON;' + @NL +
N'        END CATCH' + @NL +
N'        END' + @NL +
N'    END CATCH' + @NL +
N'END;');

        NextTable:
        SET @ridCol = NULL; SET @pk1 = NULL; SET @join = NULL;
        FETCH NEXT FROM cfg INTO @sch, @tbl, @tier, @enabled, @excluded, @masked, @ridCol, @hospSrc, @actIns, @actUpd;
    END;
    CLOSE cfg; DEALLOCATE cfg;

    IF @Execute = 0
    BEGIN
        SELECT Seq, SchemaName, TableName, Tier, Note,
               Ddl = ISNULL(DropDdl + @NL + N'GO' + @NL, N'') + ISNULL(CreateDdl + @NL + N'GO', N'')
        FROM @Results ORDER BY Seq;

        -- One script, ready to save as a migration file. .value() decodes the
        -- &lt; / &amp; entities FOR XML would otherwise leave in the DDL.
        SELECT FullScript = (
            SELECT ISNULL(DropDdl + @NL + N'GO' + @NL, N'') + ISNULL(CreateDdl + @NL + N'GO' + @NL, N'') + @NL
            FROM @Results WHERE DropDdl IS NOT NULL
            ORDER BY Seq
            FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)');

        DECLARE @GenCount INT = (SELECT COUNT(*) FROM @Results WHERE DropDdl IS NOT NULL);
        PRINT 'Generated DDL for ' + CAST(@GenCount AS VARCHAR(10)) + ' table(s). @Execute = 0, so nothing was applied.';
        RETURN;
    END;

    -- Apply: drop + create per table in its own transaction, so one failure does not leave the batch half-applied.
    DECLARE @s SYSNAME, @t SYSNAME, @d NVARCHAR(MAX), @c NVARCHAR(MAX), @ok INT = 0, @fail INT = 0;
    DECLARE @errors TABLE (SchemaName SYSNAME, TableName SYSNAME, ErrorMessage NVARCHAR(4000));
    DECLARE run CURSOR LOCAL FAST_FORWARD FOR
        SELECT SchemaName, TableName, DropDdl, CreateDdl FROM @Results WHERE DropDdl IS NOT NULL ORDER BY Seq;
    OPEN run;
    FETCH NEXT FROM run INTO @s, @t, @d, @c;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        BEGIN TRY
            BEGIN TRAN;
                EXEC sys.sp_executesql @d;
                IF @c IS NOT NULL EXEC sys.sp_executesql @c;
            COMMIT;
            SET @ok += 1;
        END TRY
        BEGIN CATCH
            IF @@TRANCOUNT > 0 ROLLBACK;
            SET @fail += 1;
            INSERT INTO @errors VALUES (@s, @t, ERROR_MESSAGE());
        END CATCH;
        FETCH NEXT FROM run INTO @s, @t, @d, @c;
    END;
    CLOSE run; DEALLOCATE run;

    PRINT 'Applied: ' + CAST(@ok AS VARCHAR(10)) + '   Failed: ' + CAST(@fail AS VARCHAR(10));
    SELECT Seq, SchemaName, TableName, Tier, Note FROM @Results WHERE Note IS NOT NULL ORDER BY Seq;
    IF @fail > 0 SELECT Failure = 'ApplyError', SchemaName, TableName, ErrorMessage FROM @errors;
END;
GO


/* ------------------------------------------------------------
   5. Usage
   ------------------------------------------------------------

   -- Tables in the database that have no classification yet (add them to section 3):
   SELECT s.name AS SchemaName, t.name AS TableName
   FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
   WHERE s.name IN ('cfg','sec','opd','cln','bil','inv','msg')
     AND NOT EXISTS (SELECT 1 FROM sec.AuditConfig c WHERE c.SchemaName = s.name AND c.TableName = t.name)
     AND NOT (s.name = 'sec' AND t.name IN ('AuditConfig','DataAuditLog'));

   -- Sign off the classification once it has been reviewed:
   UPDATE sec.AuditConfig SET ReviewedBy = N'<name>', ReviewedOn = SYSUTCDATETIME(),
          ModifiedAt = SYSUTCDATETIME()
   WHERE ReviewedBy IS NULL;

   -- Preview the DDL (nothing applied), then apply:
   EXEC sec.usp_GenerateAuditTriggers;
   EXEC sec.usp_GenerateAuditTriggers @Execute = 1;

   -- One table only:
   EXEC sec.usp_GenerateAuditTriggers @SchemaName = 'opd', @TableName = 'Patient', @Execute = 1;

   -- History of one record:
   SELECT CreatedAt, [Action], ActorId, Details
   FROM sec.DataAuditLog
   WHERE SchemaName = 'opd' AND TableName = 'Patient' AND RecordId = '<PatientId>'
   ORDER BY CreatedAt DESC;

   Regenerate after any schema.sql change that adds, renames or drops columns:
   the triggers list columns explicitly and fail (as AUDIT_ERROR rows) on a
   dropped one.
   ------------------------------------------------------------ */
