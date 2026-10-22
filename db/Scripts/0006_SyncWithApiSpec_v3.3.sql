/* ============================================================================================
   0006_SyncWithApiSpec_v3.3.sql
   Brings Jamaica_DocnyleDB in line with API-SPECIFICATION.md v3.3, for the items in
   SPEC-VS-DATABASE-DIFFERENCES.md that need a database change.

   Base: "Jamaica sql.sql" (scripted 19-10-2026 10:48:47).

   Backward compatible with the API as built:
     - every new @HospitalId parameter is OPTIONAL and added LAST. While the API does not pass it,
       the procedure behaves as before, except that an unknown id now raises NOT FOUND
       instead of silently "succeeding".
     - no table, column or result-set shape changes. The new procedures (section 7, 8) are additions.

   NOT covered (no database change needed, or a decision is needed first):
     - Naming / missing-field rows: update the spec to match the API instead.
     - Consultation-form JSON (ConsultFormSection / VitalUnitSetting): needs a written definition.
     - Vitals / follow-up after sign-off (spec is silent): see the note at the end.
     - Row-level security policy: a separate, larger change.

   New error numbers (50250-50260; no existing THROW uses them):
     50250 Diagnosis not found.           50256 Drug not found.
     50251 Prescription not found.        50257 Phrase not found.
     50252 Allergy not found.             50258 Invoice not found.
     50253 Coverage not found.            50259 Invoice is already cancelled.
     50254 Coverage line not found.       50260 Lab order not found.
     50255 Stock batch not found.         50261 Enter a fee of 0 or more.
   Reused: 50121 Invoice is cancelled. 50030 Consultation not found or already signed off.
           50065 Doctor not found in this hospital.

   Sections
     1  Audit actions IMPORT / CANCEL / VOID                                (summary #1)
     2  Consultation: DeleteDiagnosis, CancelPrescription, ConfirmSignOff   (#2, #13)
     3  Patients: UpdateAllergy, DeleteAllergy, DeactivatePatientCoverage   (#5)
     4  Catalog: UpdateDrugStatus, DeletePhrase                             (#6)
     5  Lab: SubmitLabResult                                                (#7)
     6  Billing / inventory: coverage lines, CancelInvoice, CreatePayment,
        CreateInvoice, CreateReceipt, RecordStockAdjustment                 (#3, #4, #12, numbering, currency)
     7  New bil.usp_SaveDoctorFee                                           (#8)
     8  New report procedures                                               (#9)
     9  msg.usp_SaveNotificationConfig partial update                       (#10)
   ============================================================================================ */

USE [Jamaica_DocnyleDB]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ============================================================================================
   1. Audit actions: the spec audits CreatePatientsBatch as IMPORT, CancelInvoice as CANCEL,
      UpdatePaymentVoid as VOID. CK_sec_Aud_Action rejected them, so no audit row was written.
   ============================================================================================ */
ALTER TABLE [sec].[AuditLog] DROP CONSTRAINT [CK_sec_Aud_Action];
GO
ALTER TABLE [sec].[AuditLog] WITH CHECK ADD CONSTRAINT [CK_sec_Aud_Action] CHECK ([Action] IN
    ('CREATE','READ','UPDATE','DELETE','LOGIN','LOGIN_FAIL','LOGOUT','EXPORT','PRINT',
     'OVERRIDE','SIGNOFF','SUBMIT','APPROVE','REJECT',
     'IMPORT','CANCEL','VOID'));
GO
ALTER TABLE [sec].[AuditLog] CHECK CONSTRAINT [CK_sec_Aud_Action];
GO

/* ============================================================================================
   2. Consultation
   ============================================================================================ */

-- Was: any hospital's diagnosis, unknown id "succeeded".
CREATE OR ALTER PROCEDURE [cln].[usp_DeleteDiagnosis]
    @DiagnosisId    UNIQUEIDENTIFIER,
    @HospitalId     UNIQUEIDENTIFIER = NULL,     -- v0006: caller's hospital
    @ModifiedBy     UNIQUEIDENTIFIER = NULL      -- v0006
AS BEGIN
    SET NOCOUNT ON;
    UPDATE cln.ConsultationDiagnosis
    SET IsActive = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = ISNULL(@ModifiedBy, ModifiedBy)
    WHERE DiagnosisId = @DiagnosisId
      AND (@HospitalId IS NULL OR HospitalId = @HospitalId);
    IF @@ROWCOUNT = 0 THROW 50250, N'Diagnosis not found.', 1;
END;
GO

-- Was: any hospital's prescription, unknown id "succeeded".
CREATE OR ALTER PROCEDURE [cln].[usp_CancelPrescription]
    @PrescriptionId UNIQUEIDENTIFIER,
    @HospitalId     UNIQUEIDENTIFIER = NULL,     -- v0006
    @ModifiedBy     UNIQUEIDENTIFIER = NULL      -- v0006
AS BEGIN
    SET NOCOUNT ON;
    UPDATE cln.Prescription
    SET Status = 'DISCONTINUED', ModifiedAt = SYSUTCDATETIME(), ModifiedBy = ISNULL(@ModifiedBy, ModifiedBy)
    WHERE PrescriptionId = @PrescriptionId
      AND (@HospitalId IS NULL OR HospitalId = @HospitalId);
    IF @@ROWCOUNT = 0 THROW 50251, N'Prescription not found.', 1;
END;
GO

-- Was: a second call on a locked consultation added another attestation (and re-ran stock consumption).
-- Now: the consultation is locked for update and must be unsigned, before anything else runs.
CREATE OR ALTER PROCEDURE [cln].[usp_ConfirmSignOff]
    @ConsultationId UNIQUEIDENTIFIER,
    @DoctorId       UNIQUEIDENTIFIER,
    @SignatureHash  NVARCHAR(128),             -- computed by the API layer over the signed content
    @HospitalId     UNIQUEIDENTIFIER = NULL    -- v0006
AS BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Out TABLE (AttestationId UNIQUEIDENTIFIER, SignedAt DATETIMEOFFSET(3));
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='SIGNOFF', EntityType='Consultation')
    BEGIN TRAN;
    -- v0006: refuse an already-signed (or unknown / other hospital's) consultation. UPDLOCK makes two
    -- concurrent sign-offs queue here; the second one then sees IsLocked = 1.
    IF NOT EXISTS (SELECT 1 FROM cln.Consultation WITH (UPDLOCK, HOLDLOCK)
                   WHERE ConsultationId = @ConsultationId AND IsLocked = 0
                     AND (@HospitalId IS NULL OR HospitalId = @HospitalId))
        THROW 50030, N'Consultation not found or already signed off.', 1;

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

/* ============================================================================================
   3. Patients: were protected only when the API caller sent the optional PatientId.
   ============================================================================================ */

CREATE OR ALTER PROCEDURE [opd].[usp_UpdateAllergy]
    @AllergyId      UNIQUEIDENTIFIER,
    @ReactionType   NVARCHAR(30) = NULL,
    @ReactionDescription NVARCHAR(200) = NULL,
    @Severity       NVARCHAR(10) = NULL,
    @Status         NVARCHAR(10) = NULL,
    @IsTreated      BIT = NULL,
    @SinceWhen      DATE = NULL,
    @Notes          NVARCHAR(500) = NULL,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL,
    @HospitalId     UNIQUEIDENTIFIER = NULL     -- v0006
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
    WHERE AllergyId = @AllergyId AND IsActive = 1
      AND (@HospitalId IS NULL OR HospitalId = @HospitalId);
    IF @@ROWCOUNT = 0 THROW 50252, N'Allergy not found.', 1;
END;
GO

CREATE OR ALTER PROCEDURE [opd].[usp_DeleteAllergy]
    @AllergyId      UNIQUEIDENTIFIER,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL,
    @HospitalId     UNIQUEIDENTIFIER = NULL     -- v0006
AS BEGIN
    SET NOCOUNT ON;
    -- Soft delete: Status = 'INACTIVE' (allowed by CK_opd_PA_Status since schema v4.0)
    UPDATE opd.PatientAllergy
    SET Status = 'INACTIVE', ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE AllergyId = @AllergyId
      AND (@HospitalId IS NULL OR HospitalId = @HospitalId);
    IF @@ROWCOUNT = 0 THROW 50252, N'Allergy not found.', 1;
END;
GO

CREATE OR ALTER PROCEDURE [opd].[usp_DeactivatePatientCoverage]
    @CoverageId     UNIQUEIDENTIFIER,
    @UserId         UNIQUEIDENTIFIER = NULL,
    @HospitalId     UNIQUEIDENTIFIER = NULL     -- v0006
AS BEGIN
    SET NOCOUNT ON;
    UPDATE opd.PatientCoverage SET IsActive = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE CoverageId = @CoverageId
      AND (@HospitalId IS NULL OR HospitalId = @HospitalId);
    IF @@ROWCOUNT = 0 THROW 50253, N'Coverage not found.', 1;
END;
GO

/* ============================================================================================
   4. Catalog: the API had to toggle another hospital's drug back (overwriting its ModifiedAt/By)
      and find phrases by repeated list calls.
   ============================================================================================ */

CREATE OR ALTER PROCEDURE [cfg].[usp_UpdateDrugStatus]
    @DrugId             UNIQUEIDENTIFIER,
    @ModifiedBy         UNIQUEIDENTIFIER = NULL,
    @HospitalId         UNIQUEIDENTIFIER = NULL     -- v0006
AS BEGIN
    SET NOCOUNT ON;
    -- data.js toggles the current value (no explicit flag passed by api.js)
    UPDATE cfg.DrugFormulary
    SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE DrugId = @DrugId
      AND (@HospitalId IS NULL OR HospitalId = @HospitalId);
    IF @@ROWCOUNT = 0 THROW 50256, N'Drug not found.', 1;

    SELECT DrugId, BrandName, IsActive
    FROM cfg.DrugFormulary WHERE DrugId = @DrugId;
END;
GO

CREATE OR ALTER PROCEDURE [cfg].[usp_DeletePhrase]
    @PhraseId       UNIQUEIDENTIFIER,
    @ModifiedBy     UNIQUEIDENTIFIER = NULL,
    @HospitalId     UNIQUEIDENTIFIER = NULL     -- v0006
AS BEGIN
    SET NOCOUNT ON;
    -- data.js performs a hard splice(); schema has IsActive, so soft delete (IsActive = 0) is
    -- used (preferred per architecture §4.8) rather than a physical DELETE.
    UPDATE cfg.PhraseBank SET IsActive = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHERE PhraseId = @PhraseId AND IsActive = 1
      AND (@HospitalId IS NULL OR HospitalId = @HospitalId);
    IF @@ROWCOUNT = 0 THROW 50257, N'Phrase not found.', 1;
END;
GO

/* ============================================================================================
   5. Lab: the order UPDATE had no hospital filter, so another hospital's order became RESULTS_READY.
   ============================================================================================ */

CREATE OR ALTER PROCEDURE [cln].[usp_SubmitLabResult]
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
    IF @@ROWCOUNT = 0 THROW 50260, N'Lab order not found.', 1;   -- v0006
    SELECT ResultId, Status, UploadedAt FROM @Out;

    UPDATE cln.InvestigationOrder SET Status = 'RESULTS_READY'
    WHERE OrderId = @OrderId AND HospitalId = @HospitalId;   -- v0006: hospital filter
    COMMIT;
END;
GO

/* ============================================================================================
   6. Billing and inventory
      Document numbers: MAX(Id)+1 is now read WITH (UPDLOCK, HOLDLOCK) inside the same transaction
      as the INSERT, so two concurrent creates queue instead of colliding on the unique number.
      The number format is unchanged.
   ============================================================================================ */

-- Coverage lines carry no HospitalId; the hospital comes from the invoice.
CREATE OR ALTER PROCEDURE [bil].[usp_EditInvoiceCoverageLine]
    @CoverageLineId UNIQUEIDENTIFIER,
    @Liability      DECIMAL(10,2),
    @UserId         UNIQUEIDENTIFIER = NULL,
    @HospitalId     UNIQUEIDENTIFIER = NULL     -- v0006
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @InvoiceId UNIQUEIDENTIFIER = (
        SELECT l.InvoiceId FROM bil.InvoiceCoverageLine l
        INNER JOIN bil.Invoice i ON i.InvoiceId = l.InvoiceId
        WHERE l.CoverageLineId = @CoverageLineId
          AND (@HospitalId IS NULL OR i.HospitalId = @HospitalId));
    IF @InvoiceId IS NULL THROW 50254, N'Coverage line not found.', 1;

    UPDATE bil.InvoiceCoverageLine
    SET Liability = CASE WHEN @Liability < 0 THEN 0 ELSE @Liability END, IsManualAmount = 1,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE CoverageLineId = @CoverageLineId;
    EXEC bil.usp_RecalculateInvoiceSplit @InvoiceId = @InvoiceId;
    EXEC bil.usp_GetInvoiceBillingSplit @InvoiceId = @InvoiceId;
END;
GO

CREATE OR ALTER PROCEDURE [bil].[usp_RemoveInvoiceCoverageLine]
    @CoverageLineId UNIQUEIDENTIFIER,
    @HospitalId     UNIQUEIDENTIFIER = NULL     -- v0006
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @InvoiceId UNIQUEIDENTIFIER = (
        SELECT l.InvoiceId FROM bil.InvoiceCoverageLine l
        INNER JOIN bil.Invoice i ON i.InvoiceId = l.InvoiceId
        WHERE l.CoverageLineId = @CoverageLineId
          AND (@HospitalId IS NULL OR i.HospitalId = @HospitalId));
    IF @InvoiceId IS NULL THROW 50254, N'Coverage line not found.', 1;

    IF EXISTS (SELECT 1 FROM bil.InvoiceCoverageLine WHERE CoverageLineId = @CoverageLineId AND SettledAmount > 0)
        THROW 50122, 'The payer has already settled against this coverage.', 1;
    DELETE FROM bil.InvoiceCoverageLine WHERE CoverageLineId = @CoverageLineId;
    EXEC bil.usp_RecalculateInvoiceSplit @InvoiceId = @InvoiceId;
    EXEC bil.usp_GetInvoiceBillingSplit @InvoiceId = @InvoiceId;
END;
GO

-- Was: no state check (a second call issued a second credit note), ModifiedBy not set,
-- credit-note number could collide.
CREATE OR ALTER PROCEDURE [bil].[usp_CancelInvoice]
    @InvoiceId      UNIQUEIDENTIFIER,
    @Reason         NVARCHAR(300),
    @IssuedBy       UNIQUEIDENTIFIER,
    @HospitalId     UNIQUEIDENTIFIER = NULL     -- v0006
AS BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Out TABLE (CreditNoteId UNIQUEIDENTIFIER, CreditNoteNumber NVARCHAR(30), Amount DECIMAL(10,2));
    DECLARE @InvHospitalId UNIQUEIDENTIFIER, @Amount DECIMAL(10,2), @Status NVARCHAR(10);
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='CANCEL', EntityType='Invoice')
    BEGIN TRAN;
    SELECT @InvHospitalId = HospitalId, @Amount = TotalAmount, @Status = Status
    FROM bil.Invoice WITH (UPDLOCK, HOLDLOCK)
    WHERE InvoiceId = @InvoiceId AND IsActive = 1
      AND (@HospitalId IS NULL OR HospitalId = @HospitalId);
    IF @InvHospitalId IS NULL THROW 50258, N'Invoice not found.', 1;
    IF @Status = 'CANCELLED' THROW 50259, N'Invoice is already cancelled.', 1;

    UPDATE bil.Invoice SET Status = 'CANCELLED', ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @IssuedBy
    WHERE InvoiceId = @InvoiceId;

    DECLARE @CnNumber NVARCHAR(30);
    SELECT @CnNumber = 'CN-' + RIGHT('000000' + CAST(ISNULL(MAX(Id), 0) + 1 AS NVARCHAR), 6)
    FROM bil.CreditNote WITH (UPDLOCK, HOLDLOCK) WHERE HospitalId = @InvHospitalId;

    INSERT INTO bil.CreditNote (HospitalId, CreditNoteNumber, InvoiceId, Amount, Reason, IssuedBy)
    OUTPUT inserted.CreditNoteId, inserted.CreditNoteNumber, inserted.Amount
    INTO @Out
    VALUES (@InvHospitalId, @CnNumber, @InvoiceId, @Amount, @Reason, @IssuedBy);
    COMMIT;
    SELECT CreditNoteId, CreditNoteNumber, Amount FROM @Out;
END;
GO

-- Was: never checked the invoice (spec: 50121 when cancelled), transaction number could collide.
CREATE OR ALTER PROCEDURE [bil].[usp_CreatePayment]
    @HospitalId     UNIQUEIDENTIFIER,
    @InvoiceId      UNIQUEIDENTIFIER,
    @ComponentType  NVARCHAR(15) = 'PATIENT',      -- PATIENT | INSURANCE (payer settlement)
    @CoverageLineId UNIQUEIDENTIFIER = NULL,       -- required for INSURANCE: the claim being settled
    @PaymentMethod  NVARCHAR(30),
    @Amount         DECIMAL(10,2),
    @Reference      NVARCHAR(100) = NULL,
    @ReceivedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Out TABLE (TransactionId UNIQUEIDENTIFIER, TransactionNumber NVARCHAR(30), Amount DECIMAL(10,2), PaidAt DATETIMEOFFSET(3));
    DECLARE @Status NVARCHAR(10);

    BEGIN TRAN;
    -- v0006: the invoice must belong to the hospital and not be cancelled
    SELECT @Status = Status FROM bil.Invoice WITH (UPDLOCK, HOLDLOCK)
    WHERE InvoiceId = @InvoiceId AND HospitalId = @HospitalId AND IsActive = 1;
    IF @Status IS NULL THROW 50258, N'Invoice not found.', 1;
    IF @Status = 'CANCELLED' THROW 50121, 'Invoice is cancelled.', 1;

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
    FROM bil.Payment WITH (UPDLOCK, HOLDLOCK) WHERE HospitalId = @HospitalId;

    EXEC bil.usp_RecalculateInvoiceSplit @InvoiceId = @InvoiceId;
    COMMIT;
    SELECT TransactionId, TransactionNumber, Amount, PaidAt FROM @Out;
END;
GO

-- Was: @Currency defaulted to 'USD'; now NULL = the hospital's currency. Invoice number could collide.
CREATE OR ALTER PROCEDURE [bil].[usp_CreateInvoice]
    @HospitalId     UNIQUEIDENTIFIER,
    @ConsultationId UNIQUEIDENTIFIER = NULL,
    @PatientId      UNIQUEIDENTIFIER,
    @DoctorId       UNIQUEIDENTIFIER = NULL,
    @ConsultationType NVARCHAR(10) = NULL,
    @IsFreeFollowup BIT = 0,
    @ChargeNote     NVARCHAR(300) = NULL,
    @Currency       NVARCHAR(5) = NULL,          -- v0006: NULL = cfg.Hospital.Currency
    @CreatedBy      UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Out TABLE (InvoiceId UNIQUEIDENTIFIER, InvoiceNumber NVARCHAR(30), Status NVARCHAR(10), CreatedAt DATETIMEOFFSET(3));
    SET @Currency = COALESCE(@Currency, (SELECT Currency FROM cfg.Hospital WHERE HospitalId = @HospitalId), 'USD');

    BEGIN TRAN;
    DECLARE @InvoiceNumber NVARCHAR(30);
    SELECT @InvoiceNumber = 'INV-' + RIGHT('000000' + CAST(ISNULL(MAX(Id), 0) + 1 AS NVARCHAR), 6)
    FROM bil.Invoice WITH (UPDLOCK, HOLDLOCK) WHERE HospitalId = @HospitalId;

    INSERT INTO bil.Invoice (HospitalId, InvoiceNumber, ConsultationId, PatientId,
                              DoctorId, ConsultationType, IsFreeFollowup, ChargeNote, Currency, TotalAmount, CreatedBy)
    OUTPUT inserted.InvoiceId, inserted.InvoiceNumber, inserted.Status, inserted.CreatedAt
    INTO @Out
    VALUES (@HospitalId, @InvoiceNumber, @ConsultationId, @PatientId,
            @DoctorId, @ConsultationType, @IsFreeFollowup, @ChargeNote, @Currency, 0, ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000'));
    COMMIT;
    SELECT InvoiceId, InvoiceNumber, Status, CreatedAt FROM @Out;
END;
GO

-- Was: @Currency defaulted to 'USD'; now NULL = the invoice's currency. Receipt number could collide.
CREATE OR ALTER PROCEDURE [bil].[usp_CreateReceipt]
    @HospitalId     UNIQUEIDENTIFIER,
    @InvoiceId      UNIQUEIDENTIFIER,
    @PatientId      UNIQUEIDENTIFIER = NULL,
    @TotalAmount    DECIMAL(10,2),
    @Currency       NVARCHAR(5) = NULL           -- v0006: NULL = bil.Invoice.Currency
AS BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Out TABLE (ReceiptId UNIQUEIDENTIFIER, ReceiptNumber NVARCHAR(30), GeneratedAt DATETIMEOFFSET(3));
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='PRINT', EntityType='Receipt') on print
    -- v4.0: Receipt.PaymentId dropped — a receipt belongs to the invoice (Receipt model).
    SET @Currency = COALESCE(@Currency,
                             (SELECT Currency FROM bil.Invoice WHERE InvoiceId = @InvoiceId),
                             (SELECT Currency FROM cfg.Hospital WHERE HospitalId = @HospitalId), 'USD');

    BEGIN TRAN;
    DECLARE @ReceiptNumber NVARCHAR(30);
    SELECT @ReceiptNumber = 'RCP-' + RIGHT('000000' + CAST(ISNULL(MAX(Id), 0) + 1 AS NVARCHAR), 6)
    FROM bil.Receipt WITH (UPDLOCK, HOLDLOCK) WHERE HospitalId = @HospitalId;

    INSERT INTO bil.Receipt (HospitalId, ReceiptNumber, InvoiceId, PatientId, TotalAmount, Currency)
    OUTPUT inserted.ReceiptId, inserted.ReceiptNumber, inserted.GeneratedAt
    INTO @Out
    VALUES (@HospitalId, @ReceiptNumber, @InvoiceId, @PatientId, @TotalAmount, @Currency);
    COMMIT;
    SELECT ReceiptId, ReceiptNumber, GeneratedAt FROM @Out;
END;
GO

-- Was: any hospital's batch; unknown batch surfaced as 50024 "below zero".
CREATE OR ALTER PROCEDURE [inv].[usp_RecordStockAdjustment]
    @StockBatchId UNIQUEIDENTIFIER,
    @Delta        INT,
    @Reason       NVARCHAR(200),
    @UserId       UNIQUEIDENTIFIER = NULL,
    @HospitalId   UNIQUEIDENTIFIER = NULL       -- v0006
AS BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF @Delta = 0 THROW 50023, N'Enter a non-zero quantity.', 1;
    BEGIN TRAN;
        IF NOT EXISTS (SELECT 1 FROM inv.StockBatch WITH (UPDLOCK, HOLDLOCK)
                       WHERE StockBatchId = @StockBatchId
                         AND (@HospitalId IS NULL OR HospitalId = @HospitalId))
            THROW 50255, N'Stock batch not found.', 1;

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

/* ============================================================================================
   7. New: bil.usp_SaveDoctorFee
      Spec (SaveDoctorCharge): "a changed flat fee closes the current fee row and starts a new one
      today". Same amount: nothing changes. A row that already started today is corrected in place
      (closing it at yesterday would break CK_bil_FS_Range).
      Returns the fee row in force today (same columns as bil.usp_CheckEffectiveFee).
      To save charge settings and fees in one transaction, the API calls opd.usp_SaveDoctorSettings and
      this procedure inside one TransactionScope / SqlTransaction.
   ============================================================================================ */
CREATE OR ALTER PROCEDURE [bil].[usp_SaveDoctorFee]
    @HospitalId       UNIQUEIDENTIFIER,
    @DoctorId         UNIQUEIDENTIFIER,
    @ConsultationType NVARCHAR(10),            -- NEW | FOLLOWUP
    @FeeAmount        DECIMAL(10,2),
    @UserId           UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='UPDATE', EntityType='DoctorFee')
    IF @FeeAmount IS NULL OR @FeeAmount < 0 THROW 50261, N'Enter a fee of 0 or more.', 1;
    IF NOT EXISTS (SELECT 1 FROM sec.ApplicationUser WHERE UserId = @DoctorId AND HospitalId = @HospitalId)
        THROW 50065, N'Doctor not found in this hospital.', 1;

    DECLARE @Today DATE = cfg.fn_HospitalToday(@HospitalId);
    DECLARE @Actor UNIQUEIDENTIFIER = ISNULL(@UserId, '00000000-0000-0000-0000-000000000000');
    DECLARE @CurId UNIQUEIDENTIFIER, @CurAmount DECIMAL(10,2), @CurFrom DATE;

    BEGIN TRAN;
    SELECT TOP (1) @CurId = FeeScheduleId, @CurAmount = FeeAmount, @CurFrom = EffectiveFrom
    FROM bil.DoctorFeeSchedule WITH (UPDLOCK, HOLDLOCK)
    WHERE HospitalId = @HospitalId AND DoctorId = @DoctorId AND ConsultationType = @ConsultationType
      AND EffectiveFrom <= @Today AND (EffectiveTo IS NULL OR EffectiveTo >= @Today) AND IsActive = 1
    ORDER BY EffectiveFrom DESC;

    IF @CurId IS NOT NULL AND @CurFrom = @Today AND @CurAmount <> @FeeAmount
        UPDATE bil.DoctorFeeSchedule
        SET FeeAmount = @FeeAmount, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE FeeScheduleId = @CurId;
    ELSE IF @CurId IS NULL OR @CurAmount <> @FeeAmount
    BEGIN
        IF @CurId IS NOT NULL
            UPDATE bil.DoctorFeeSchedule
            SET EffectiveTo = DATEADD(DAY, -1, @Today), ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
            WHERE FeeScheduleId = @CurId;

        INSERT INTO bil.DoctorFeeSchedule (HospitalId, DoctorId, ConsultationType, FeeAmount, Currency, EffectiveFrom, CreatedBy)
        SELECT @HospitalId, @DoctorId, @ConsultationType, @FeeAmount, h.Currency, @Today, @Actor
        FROM cfg.Hospital h WHERE h.HospitalId = @HospitalId;
    END
    COMMIT;

    SELECT TOP (1) FeeScheduleId, DoctorId, ConsultationType, FeeAmount, Currency, EffectiveFrom, EffectiveTo
    FROM bil.DoctorFeeSchedule
    WHERE HospitalId = @HospitalId AND DoctorId = @DoctorId AND ConsultationType = @ConsultationType
      AND EffectiveFrom <= @Today AND (EffectiveTo IS NULL OR EffectiveTo >= @Today) AND IsActive = 1
    ORDER BY EffectiveFrom DESC;
END;
GO

/* ============================================================================================
   8. New report procedures over the existing views (the API reads them with fixed SELECTs today).
      Results keep the views' meaning; Currency is included so the caller never adds JMD to USD.
   ============================================================================================ */
CREATE OR ALTER PROCEDURE [bil].[usp_ListRevenueByServiceReport]
    @HospitalId     UNIQUEIDENTIFIER,
    @FromDate       DATE,
    @ToDate         DATE
AS BEGIN
    SET NOCOUNT ON;
    SELECT Category, TotalRevenue, InvoiceCount, InvoiceDate, Currency
    FROM bil.vw_RevenueByServiceCategory
    WHERE HospitalId = @HospitalId AND InvoiceDate BETWEEN @FromDate AND @ToDate
    ORDER BY InvoiceDate, Category;
END;
GO

-- Result set 1: the day's totals, one row per currency (vw_DayEndSummary).
-- Result set 2: ByComponent, payments received that day (vw_PaymentsByComponent).
CREATE OR ALTER PROCEDURE [bil].[usp_GetDayEndSummary]
    @HospitalId     UNIQUEIDENTIFIER,
    @Date           DATE
AS BEGIN
    SET NOCOUNT ON;
    SELECT Currency, InvoiceCount, TotalBilled, TotalCollected, TotalOutstanding
    FROM bil.vw_DayEndSummary
    WHERE HospitalId = @HospitalId AND BillingDate = @Date
    ORDER BY Currency;

    SELECT ComponentType, PaymentMethod, TotalAmount, PaymentCount
    FROM bil.vw_PaymentsByComponent
    WHERE HospitalId = @HospitalId AND PaymentDate = @Date
    ORDER BY ComponentType, PaymentMethod;
END;
GO

/* ============================================================================================
   9. msg.usp_SaveNotificationConfig: spec says every member is optional. Was: omitted members were
      overwritten with NULL and @Enabled defaulted to 0. Now NULL = keep the stored value
      (a new row still starts with Enabled = 0). To clear a text value, send ''.
   ============================================================================================ */
CREATE OR ALTER PROCEDURE [msg].[usp_SaveNotificationConfig]
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
    @Enabled        BIT = NULL,                -- v0006: NULL = keep (was default 0)
    @ModifiedBy     UNIQUEIDENTIFIER = NULL
AS BEGIN
    SET NOCOUNT ON;
    -- AUDIT: caller must also invoke sec.usp_RecordAuditEntry (Action='UPDATE', EntityType='NotificationConfig')
    MERGE msg.NotificationConfig AS tgt
    USING (SELECT @HospitalId AS HospitalId, @Channel AS Channel) AS src
        ON tgt.HospitalId = src.HospitalId AND tgt.Channel = src.Channel
    WHEN MATCHED THEN UPDATE SET
        Provider = ISNULL(@Provider, Provider), ApiKey = ISNULL(@ApiKey, ApiKey),
        PhoneNumberId = ISNULL(@PhoneNumberId, PhoneNumberId),
        BusinessAccountId = ISNULL(@BusinessAccountId, BusinessAccountId),
        SmtpHost = ISNULL(@SmtpHost, SmtpHost), SmtpPort = ISNULL(@SmtpPort, SmtpPort),
        FromEmail = ISNULL(@FromEmail, FromEmail), FromName = ISNULL(@FromName, FromName),
        Enabled = ISNULL(@Enabled, Enabled),
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
    WHEN NOT MATCHED THEN
        INSERT (HospitalId, Channel, Provider, ApiKey, PhoneNumberId, BusinessAccountId,
                SmtpHost, SmtpPort, FromEmail, FromName, Enabled, ModifiedAt, ModifiedBy)
        VALUES (@HospitalId, @Channel, @Provider, @ApiKey, @PhoneNumberId, @BusinessAccountId,
                @SmtpHost, @SmtpPort, @FromEmail, @FromName, ISNULL(@Enabled, 0), SYSUTCDATETIME(), @ModifiedBy);
END;
GO

/* ============================================================================================
   Not changed here (decide first):
     cln.usp_SaveVitals / cln.usp_SaveFollowup still save on a signed (locked) consultation. The spec
     is silent. To block it, add to each, before the MERGE / UPDATE:
         IF EXISTS (SELECT 1 FROM cln.Consultation WHERE ConsultationId = @ConsultationId AND IsLocked = 1)
             THROW 50245, N'The consultation was not found or is already signed.', 1;
   ============================================================================================ */
