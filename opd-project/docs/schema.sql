/* ============================================================
   Consult360 — SQL Server 2019 Schema
   Version: 3.0 (Dual-key, temporal-ready, billing-complete)
   
   Key pattern:
     {Table}PkId   BIGINT IDENTITY(1,1)              — clustered PK, internal
     {Table}Id     UNIQUEIDENTIFIER NEWSEQUENTIALID()  — external, API-facing
   
   State: IsActive (business), IsDeleted (soft delete)
   Audit: CreatedAt, CreatedBy, ModifiedAt, ModifiedBy
   Schemas: cfg(16), sec(5), opd(7), cln(14), bil(7), msg(2) = 51 tables
   ============================================================ */

CREATE SCHEMA cfg AUTHORIZATION dbo;
GO
CREATE SCHEMA sec AUTHORIZATION dbo;
GO
CREATE SCHEMA opd AUTHORIZATION dbo;
GO
CREATE SCHEMA cln AUTHORIZATION dbo;
GO
CREATE SCHEMA bil AUTHORIZATION dbo;
GO
CREATE SCHEMA msg AUTHORIZATION dbo;
GO

-- ============================================================
-- A. cfg — CONFIGURATION & REFERENCE DATA (21 tables)
-- ============================================================

CREATE TABLE cfg.Hospital (
    HospitalPkId            BIGINT IDENTITY(1,1) NOT NULL,
    HospitalId              UNIQUEIDENTIFIER    NOT NULL    CONSTRAINT DF_cfg_Hospital_Id DEFAULT NEWSEQUENTIALID(),
    Name                    NVARCHAR(200)       NOT NULL,
    [Address]               NVARCHAR(500)       NULL,
    Phone                   NVARCHAR(20)        NULL,       -- ENCRYPTION: DETERMINISTIC
    Email                   NVARCHAR(100)       NULL,       -- ENCRYPTION: DETERMINISTIC
    Country                 NVARCHAR(5)         NOT NULL    CONSTRAINT DF_cfg_Hospital_Country DEFAULT 'XX',
    Currency                NVARCHAR(5)         NOT NULL    CONSTRAINT DF_cfg_Hospital_Currency DEFAULT 'USD',
    Timezone                NVARCHAR(50)        NULL,
    LicenseNumber           NVARCHAR(50)        NULL,
    LogoBase64              NVARCHAR(MAX)       NULL,
    PatientIdLabel          NVARCHAR(30)        NOT NULL    CONSTRAINT DF_cfg_Hospital_PidLbl DEFAULT 'MRN',
    NationalHealthIdLabel   NVARCHAR(50)        NOT NULL    CONSTRAINT DF_cfg_Hospital_NhidLbl DEFAULT 'National Health ID',
    IsNationalHealthIdMandatory BIT             NOT NULL    CONSTRAINT DF_cfg_Hospital_NhidMand DEFAULT 0,
    DefaultTempUnit         NVARCHAR(1)         NOT NULL    CONSTRAINT DF_cfg_Hospital_TempUnit DEFAULT 'F'
                            CONSTRAINT CK_cfg_Hospital_TempUnit CHECK (DefaultTempUnit IN ('F','C')),
    DefaultRxValidityDays   INT                 NOT NULL    CONSTRAINT DF_cfg_Hospital_RxVal DEFAULT 30,
    IsActive                BIT                 NOT NULL    CONSTRAINT DF_cfg_Hospital_Active DEFAULT 1,
    IsDeleted               BIT                 NOT NULL    CONSTRAINT DF_cfg_Hospital_Deleted DEFAULT 0,
    CreatedAt               DATETIME2(3)        NOT NULL    CONSTRAINT DF_cfg_Hospital_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy               UNIQUEIDENTIFIER    NULL,
    ModifiedAt              DATETIME2(3)        NULL,
    ModifiedBy              UNIQUEIDENTIFIER    NULL,
    CONSTRAINT PK_cfg_Hospital PRIMARY KEY CLUSTERED (HospitalPkId),
    CONSTRAINT UQ_cfg_Hospital_Id UNIQUE NONCLUSTERED (HospitalId)
);

-- Key/value hospital configuration (Schedule Settings + Settings → Hospital & Practice).
-- Keys: APPT_SLOT_MINS*, APPT_BUFFER_MINS, FREE_FOLLOWUP_ALLOWED*, FREE_FOLLOWUP_DAYS*,
-- DATE_FORMAT, TIME_FORMAT, MRN_PREFIX, MRN_FORMAT, MRN_SEQ (system-managed),
-- DOCTOR_NAME_TEMPLATE.  (* = can be overridden per doctor in opd.DoctorSetting)
-- data.js: hospitalSettings
CREATE TABLE cfg.HospitalSetting (
    HospitalSettingPkId BIGINT IDENTITY(1,1) NOT NULL,
    HospitalId      UNIQUEIDENTIFIER    NOT NULL,
    SettingKey      NVARCHAR(40)        NOT NULL,
    [Value]         NVARCHAR(200)       NULL,
    ModifiedAt      DATETIME2(3)        NULL,
    ModifiedBy      UNIQUEIDENTIFIER    NULL,
    CONSTRAINT PK_cfg_HSet PRIMARY KEY CLUSTERED (HospitalSettingPkId),
    CONSTRAINT UQ_cfg_HSet_Key UNIQUE NONCLUSTERED (HospitalId, SettingKey),
    CONSTRAINT FK_cfg_HSet_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId)
);

-- Configurable appointment types. opd.Appointment.[Type] stores Code.
-- DurationMinutes NULL = the doctor's slot length; FeeCategory picks the
-- bil.DoctorFeeSchedule row; IsChargeable = 0 → no consultation fee.
-- data.js: appointmentTypes
CREATE TABLE cfg.AppointmentType (
    AppointmentTypePkId BIGINT IDENTITY(1,1) NOT NULL,
    AppointmentTypeId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_AT_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    Code NVARCHAR(20) NOT NULL, Name NVARCHAR(100) NOT NULL,
    DurationMinutes INT NULL CONSTRAINT CK_cfg_AT_Dur CHECK (DurationMinutes IS NULL OR DurationMinutes >= 5),
    Color NVARCHAR(7) NOT NULL CONSTRAINT DF_cfg_AT_Color DEFAULT '#0f766e',
    IsChargeable BIT NOT NULL CONSTRAINT DF_cfg_AT_Charge DEFAULT 1,
    FeeCategory NVARCHAR(10) NOT NULL CONSTRAINT DF_cfg_AT_FeeCat DEFAULT 'NEW'
        CONSTRAINT CK_cfg_AT_FeeCat CHECK (FeeCategory IN ('NEW','FOLLOWUP')),
    SortOrder INT NOT NULL CONSTRAINT DF_cfg_AT_Sort DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_AT_Active DEFAULT 1,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cfg_AT_CreatedAt DEFAULT SYSUTCDATETIME(),
    ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_AT PRIMARY KEY CLUSTERED (AppointmentTypePkId),
    CONSTRAINT UQ_cfg_AT_Id UNIQUE NONCLUSTERED (AppointmentTypeId),
    CONSTRAINT UQ_cfg_AT_Code UNIQUE NONCLUSTERED (HospitalId, Code),
    CONSTRAINT FK_cfg_AT_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId)
);

-- Insurers (PRIVATE) and government schemes (GOVT_SCHEME: CGHS / Aarogyasri, NHF, Medicare …), per country.
-- Used by the coverage / N-way bill split (Charge = Σ coverage liabilities + Patient).
CREATE TABLE cfg.Payer (
    PayerPkId BIGINT IDENTITY(1,1) NOT NULL,
    PayerId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_Payer_Id DEFAULT NEWSEQUENTIALID(),
    Name NVARCHAR(150) NOT NULL,
    Category NVARCHAR(15) NOT NULL CONSTRAINT CK_cfg_Payer_Cat CHECK (Category IN ('PRIVATE','GOVT_SCHEME')),
    Country NVARCHAR(5) NOT NULL,                 -- IN, JM, US … (cfg.Hospital.Country)
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_Payer_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cfg_Payer_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cfg_Payer_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_cfg_Payer PRIMARY KEY CLUSTERED (PayerPkId),
    CONSTRAINT UQ_cfg_Payer_Id UNIQUE NONCLUSTERED (PayerId),
    CONSTRAINT UQ_cfg_Payer_Country_Name UNIQUE (Country, Name),
    INDEX IX_cfg_Payer_Country NONCLUSTERED (Country, Category)
);

-- Pre-visit intake form questions (answered on online booking). data.js: intakeQuestions
CREATE TABLE cfg.IntakeQuestion (
    IntakeQuestionPkId BIGINT IDENTITY(1,1) NOT NULL,
    QuestionId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_IQ_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    Label NVARCHAR(200) NOT NULL,
    QuestionType NVARCHAR(10) NOT NULL CONSTRAINT CK_cfg_IQ_Type CHECK (QuestionType IN ('TEXT','NUMBER','CHOICE')),
    Options NVARCHAR(500) NULL,            -- comma-separated, CHOICE only
    SortOrder INT NOT NULL CONSTRAINT DF_cfg_IQ_Sort DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_IQ_Active DEFAULT 1,   -- soft delete keeps old answers readable
    CONSTRAINT PK_cfg_IQ PRIMARY KEY CLUSTERED (IntakeQuestionPkId),
    CONSTRAINT UQ_cfg_IQ_Id UNIQUE NONCLUSTERED (QuestionId),
    CONSTRAINT FK_cfg_IQ_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId)
);

-- Consent text signed at the visit; Version bumps on every edit. data.js: consentForms
CREATE TABLE cfg.ConsentForm (
    ConsentFormPkId BIGINT IDENTITY(1,1) NOT NULL,
    ConsentFormId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_CF_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    Title NVARCHAR(200) NOT NULL, BodyText NVARCHAR(MAX) NOT NULL,
    Version INT NOT NULL CONSTRAINT DF_cfg_CF_Ver DEFAULT 1,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_CF_Active DEFAULT 1,
    ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_CF PRIMARY KEY CLUSTERED (ConsentFormPkId),
    CONSTRAINT UQ_cfg_CF_Id UNIQUE NONCLUSTERED (ConsentFormId),
    CONSTRAINT FK_cfg_CF_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId)
);


CREATE TABLE cfg.Compliance (
    CompliancePkId          BIGINT IDENTITY(1,1) NOT NULL,
    ComplianceId            UNIQUEIDENTIFIER    NOT NULL    CONSTRAINT DF_cfg_Comp_Id DEFAULT NEWSEQUENTIALID(),
    ComplianceName          NVARCHAR(50)        NOT NULL,
    Country                 NVARCHAR(5)         NOT NULL    CONSTRAINT DF_cfg_Comp_Country DEFAULT 'ALL',
    IsMandatory             BIT                 NOT NULL    CONSTRAINT DF_cfg_Comp_Mand DEFAULT 0,
    DisplayLabel            NVARCHAR(100)       NOT NULL,
    PrintLabel              NVARCHAR(30)        NOT NULL,
    ValidationRegex         NVARCHAR(200)       NULL,
    SortOrder               INT                 NOT NULL    CONSTRAINT DF_cfg_Comp_Sort DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_Comp_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cfg_Comp_Deleted DEFAULT 0,
    CreatedAt               DATETIME2(3)        NOT NULL    CONSTRAINT DF_cfg_Comp_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL, ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_Comp PRIMARY KEY CLUSTERED (CompliancePkId),
    CONSTRAINT UQ_cfg_Comp_Id UNIQUE NONCLUSTERED (ComplianceId)
);

CREATE TABLE cfg.HospitalCompliance (
    HospitalCompliancePkId  BIGINT IDENTITY(1,1) NOT NULL,
    HospitalComplianceId    UNIQUEIDENTIFIER    NOT NULL    CONSTRAINT DF_cfg_HC_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId              UNIQUEIDENTIFIER    NOT NULL,
    ComplianceId            UNIQUEIDENTIFIER    NOT NULL,
    RegistrationNumber      NVARCHAR(100)       NOT NULL,
    IssuedOn                DATE                NULL,
    ExpiresOn               DATE                NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_HC_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cfg_HC_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cfg_HC_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL, ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_HC PRIMARY KEY CLUSTERED (HospitalCompliancePkId),
    CONSTRAINT UQ_cfg_HC_Id UNIQUE NONCLUSTERED (HospitalComplianceId),
    CONSTRAINT FK_cfg_HC_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cfg_HC_ComplianceId FOREIGN KEY (ComplianceId) REFERENCES cfg.Compliance(ComplianceId),
    CONSTRAINT UQ_cfg_HC_Hosp_Comp UNIQUE (HospitalId, ComplianceId),
    INDEX IX_cfg_HC_HospitalId NONCLUSTERED (HospitalId)
);

CREATE TABLE cfg.Specialty (
    SpecialtyPkId BIGINT IDENTITY(1,1) NOT NULL,
    SpecialtyId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_Spec_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    SpecialtyName NVARCHAR(100) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_Spec_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cfg_Spec_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cfg_Spec_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL, ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_Spec PRIMARY KEY CLUSTERED (SpecialtyPkId),
    CONSTRAINT UQ_cfg_Spec_Id UNIQUE NONCLUSTERED (SpecialtyId),
    CONSTRAINT FK_cfg_Spec_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    INDEX IX_cfg_Spec_HospitalId NONCLUSTERED (HospitalId)
);

CREATE TABLE cfg.VitalDefinition (
    VitalDefinitionPkId BIGINT IDENTITY(1,1) NOT NULL,
    VitalDefinitionId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_VD_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    VitalCode NVARCHAR(40) NOT NULL,
    DisplayLabel NVARCHAR(50) NOT NULL,
    AllowedUnits NVARCHAR(20) NULL,
    MinValue DECIMAL(8,2) NULL, MaxValue DECIMAL(8,2) NULL,
    IsCustom BIT NOT NULL CONSTRAINT DF_cfg_VD_Custom DEFAULT 0,   -- added by the hospital in Settings → Clinical
    SortOrder INT NOT NULL CONSTRAINT DF_cfg_VD_Sort DEFAULT 0,
    IsEnabled BIT NOT NULL CONSTRAINT DF_cfg_VD_Enabled DEFAULT 1,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_VD_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cfg_VD_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cfg_VD_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL, ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_VD PRIMARY KEY CLUSTERED (VitalDefinitionPkId),
    CONSTRAINT UQ_cfg_VD_Id UNIQUE NONCLUSTERED (VitalDefinitionId),
    CONSTRAINT FK_cfg_VD_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    INDEX IX_cfg_VD_HospitalId NONCLUSTERED (HospitalId)
);

CREATE TABLE cfg.MedicalHistoryItem (
    MedicalHistoryItemPkId BIGINT IDENTITY(1,1) NOT NULL,
    MedicalHistoryItemId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_MHI_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ItemName NVARCHAR(100) NOT NULL,
    SortOrder INT NOT NULL CONSTRAINT DF_cfg_MHI_Sort DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_MHI_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cfg_MHI_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cfg_MHI_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL, ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_MHI PRIMARY KEY CLUSTERED (MedicalHistoryItemPkId),
    CONSTRAINT UQ_cfg_MHI_Id UNIQUE NONCLUSTERED (MedicalHistoryItemId),
    CONSTRAINT FK_cfg_MHI_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    INDEX IX_cfg_MHI_HospitalId NONCLUSTERED (HospitalId)
);

CREATE TABLE cfg.MedicalEventType (
    MedicalEventTypePkId BIGINT IDENTITY(1,1) NOT NULL,
    MedicalEventTypeId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_MET_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, EventTypeName NVARCHAR(100) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_MET_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cfg_MET_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cfg_MET_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL, ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_MET PRIMARY KEY CLUSTERED (MedicalEventTypePkId),
    CONSTRAINT UQ_cfg_MET_Id UNIQUE NONCLUSTERED (MedicalEventTypeId),
    CONSTRAINT FK_cfg_MET_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    INDEX IX_cfg_MET_HospitalId NONCLUSTERED (HospitalId)
);

CREATE TABLE cfg.SymptomCatalog (
    SymptomCatalogPkId BIGINT IDENTITY(1,1) NOT NULL,
    SymptomCatalogId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_Sym_Id DEFAULT NEWSEQUENTIALID(),
    SymptomName NVARCHAR(100) NOT NULL,
    Category NVARCHAR(20) NOT NULL CONSTRAINT CK_cfg_Sym_Cat CHECK (Category IN ('QUICK_PICK','OTHER')),
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_Sym_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cfg_Sym_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cfg_Sym_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_cfg_Sym PRIMARY KEY CLUSTERED (SymptomCatalogPkId),
    CONSTRAINT UQ_cfg_Sym_Id UNIQUE NONCLUSTERED (SymptomCatalogId)
);

CREATE TABLE cfg.ICD10Catalog (
    ICD10CatalogPkId BIGINT IDENTITY(1,1) NOT NULL,
    ICD10CatalogId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_ICD_Id DEFAULT NEWSEQUENTIALID(),
    Code NVARCHAR(10) NOT NULL, [Description] NVARCHAR(300) NOT NULL, Synonyms NVARCHAR(500) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_ICD_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cfg_ICD_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cfg_ICD_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_cfg_ICD PRIMARY KEY CLUSTERED (ICD10CatalogPkId),
    CONSTRAINT UQ_cfg_ICD_Id UNIQUE NONCLUSTERED (ICD10CatalogId),
    CONSTRAINT UQ_cfg_ICD_Code UNIQUE (Code)
);

CREATE TABLE cfg.DrugFormulary (
    DrugFormularyPkId BIGINT IDENTITY(1,1) NOT NULL,
    DrugFormularyId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_Drug_Id DEFAULT NEWSEQUENTIALID(),
    BrandName NVARCHAR(200) NOT NULL, GenericName NVARCHAR(200) NULL,
    TherapeuticClass NVARCHAR(300) NULL, Composition NVARCHAR(500) NULL,
    Strength NVARCHAR(50) NULL, DrugForm NVARCHAR(30) NULL, DeliveryRoute NVARCHAR(30) NULL,
    DefaultDosage NVARCHAR(20) NULL, DefaultFrequency NVARCHAR(20) NULL,
    DefaultDuration INT NULL, DefaultInstructions NVARCHAR(300) NULL,
    IsControlled BIT NOT NULL CONSTRAINT DF_cfg_Drug_Ctrl DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_Drug_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cfg_Drug_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cfg_Drug_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL, ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_Drug PRIMARY KEY CLUSTERED (DrugFormularyPkId),
    CONSTRAINT UQ_cfg_Drug_Id UNIQUE NONCLUSTERED (DrugFormularyId),
    INDEX IX_cfg_Drug_BrandName NONCLUSTERED (BrandName)
);

-- External lab / imaging centres. sec.ApplicationUser.LabPartnerId links LAB_PARTNER accounts
-- (several staff per partner) to one row here. data.js: labPartners
CREATE TABLE cfg.LabPartner (
    LabPartnerPkId BIGINT IDENTITY(1,1) NOT NULL,
    LabPartnerId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_LP_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_LP_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cfg_LP_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cfg_LP_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_LP PRIMARY KEY CLUSTERED (LabPartnerPkId),
    CONSTRAINT UQ_cfg_LP_Id UNIQUE NONCLUSTERED (LabPartnerId),
    CONSTRAINT FK_cfg_LP_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT UQ_cfg_LP_Hosp_Name UNIQUE (HospitalId, Name)
);

CREATE TABLE cfg.LabTestCatalog (
    LabTestCatalogPkId BIGINT IDENTITY(1,1) NOT NULL,
    LabTestCatalogId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_LT_Id DEFAULT NEWSEQUENTIALID(),
    TestName NVARCHAR(100) NOT NULL,
    TestType NVARCHAR(20) NOT NULL CONSTRAINT CK_cfg_LT_Type CHECK (TestType IN ('LAB','IMAGING','PATHOLOGY')),
    DefaultSampleType NVARCHAR(50) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_LT_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cfg_LT_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cfg_LT_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_cfg_LT PRIMARY KEY CLUSTERED (LabTestCatalogPkId),
    CONSTRAINT UQ_cfg_LT_Id UNIQUE NONCLUSTERED (LabTestCatalogId)
);

-- Groups phrases ("/" macros) by condition. Matches a consultation when one of its
-- diagnoses' ICD-10 codes starts with a prefix in Icd10Prefixes. data.js: clinicalConditions
CREATE TABLE cfg.ClinicalCondition (
    ClinicalConditionPkId BIGINT IDENTITY(1,1) NOT NULL,
    ConditionId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_CC_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    Icd10Prefixes NVARCHAR(200) NULL,      -- comma-separated, e.g. 'I10,I11'
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_CC_Active DEFAULT 1,
    CONSTRAINT PK_cfg_CC PRIMARY KEY CLUSTERED (ClinicalConditionPkId),
    CONSTRAINT UQ_cfg_CC_Id UNIQUE NONCLUSTERED (ConditionId),
    CONSTRAINT FK_cfg_CC_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId)
);

CREATE TABLE cfg.PhraseBank (
    PhraseBankPkId BIGINT IDENTITY(1,1) NOT NULL,
    PhraseBankId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_PB_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, UserId UNIQUEIDENTIFIER NULL,
    Category NVARCHAR(20) NOT NULL CONSTRAINT CK_cfg_PB_Cat CHECK (Category IN ('SUBJECTIVE','OBJECTIVE','PLAN','INSTRUCTION','CUSTOM')),
    TriggerCode NVARCHAR(30) NULL, PhraseText NVARCHAR(500) NOT NULL,
    IsGlobal BIT NOT NULL CONSTRAINT DF_cfg_PB_Global DEFAULT 0,
    ConditionId UNIQUEIDENTIFIER NULL,     -- NULL = any condition (data.js: ConditionId)
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_PB_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cfg_PB_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cfg_PB_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL, ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_PB PRIMARY KEY CLUSTERED (PhraseBankPkId),
    CONSTRAINT UQ_cfg_PB_Id UNIQUE NONCLUSTERED (PhraseBankId),
    CONSTRAINT FK_cfg_PB_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cfg_PB_ConditionId FOREIGN KEY (ConditionId) REFERENCES cfg.ClinicalCondition(ConditionId),
    INDEX IX_cfg_PB_HospitalId NONCLUSTERED (HospitalId)
);

CREATE TABLE cfg.PaymentMethod (
    PaymentMethodPkId BIGINT IDENTITY(1,1) NOT NULL,
    PaymentMethodId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_PM_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, MethodName NVARCHAR(30) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_PM_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cfg_PM_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cfg_PM_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_cfg_PM PRIMARY KEY CLUSTERED (PaymentMethodPkId),
    CONSTRAINT UQ_cfg_PM_Id UNIQUE NONCLUSTERED (PaymentMethodId),
    CONSTRAINT FK_cfg_PM_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    INDEX IX_cfg_PM_HospitalId NONCLUSTERED (HospitalId)
);

CREATE TABLE cfg.ContradictionRule (
    ContradictionRulePkId BIGINT IDENTITY(1,1) NOT NULL,
    ContradictionRuleId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_CR_Id DEFAULT NEWSEQUENTIALID(),
    RuleType NVARCHAR(30) NOT NULL, ConditionJson NVARCHAR(MAX) NOT NULL,
    MessageTemplate NVARCHAR(300) NOT NULL,
    Severity NVARCHAR(10) NOT NULL CONSTRAINT CK_cfg_CR_Sev CHECK (Severity IN ('WARNING','CRITICAL')),
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_CR_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cfg_CR_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cfg_CR_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_cfg_CR PRIMARY KEY CLUSTERED (ContradictionRulePkId),
    CONSTRAINT UQ_cfg_CR_Id UNIQUE NONCLUSTERED (ContradictionRuleId)
);

CREATE TABLE cfg.BranchFlowRule (
    BranchFlowRulePkId BIGINT IDENTITY(1,1) NOT NULL,
    BranchFlowRuleId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_BF_Id DEFAULT NEWSEQUENTIALID(),
    RuleType NVARCHAR(30) NOT NULL, ConditionJson NVARCHAR(MAX) NOT NULL, ActionJson NVARCHAR(MAX) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_BF_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cfg_BF_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cfg_BF_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_cfg_BF PRIMARY KEY CLUSTERED (BranchFlowRulePkId),
    CONSTRAINT UQ_cfg_BF_Id UNIQUE NONCLUSTERED (BranchFlowRuleId)
);

CREATE TABLE cfg.MessageTemplate (
    MessageTemplatePkId BIGINT IDENTITY(1,1) NOT NULL,
    MessageTemplateId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_MT_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, Name NVARCHAR(100) NOT NULL,
    TriggerEvent NVARCHAR(30) NOT NULL,
    Channel NVARCHAR(10) NOT NULL CONSTRAINT CK_cfg_MT_Ch CHECK (Channel IN ('WHATSAPP','EMAIL','BOTH')),
    TemplateText NVARCHAR(1000) NOT NULL,
    IsEnabled BIT NOT NULL CONSTRAINT DF_cfg_MT_Enabled DEFAULT 1,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_MT_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cfg_MT_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cfg_MT_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL, ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_MT PRIMARY KEY CLUSTERED (MessageTemplatePkId),
    CONSTRAINT UQ_cfg_MT_Id UNIQUE NONCLUSTERED (MessageTemplateId),
    CONSTRAINT FK_cfg_MT_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    INDEX IX_cfg_MT_HospitalId NONCLUSTERED (HospitalId)
);

-- ============================================================
-- B. sec — SECURITY (6 tables)
-- ============================================================

CREATE TABLE sec.ApplicationUser (
    UserPkId BIGINT IDENTITY(1,1) NOT NULL,
    UserId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_Usr_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    FullName NVARCHAR(200) NOT NULL,                        -- ENCRYPTION: DETERMINISTIC
    Email NVARCHAR(100) NOT NULL,                           -- ENCRYPTION: DETERMINISTIC
    PasswordHash NVARCHAR(200) NOT NULL,                    -- ENCRYPTION: RANDOMIZED
    Phone NVARCHAR(20) NULL,                                -- ENCRYPTION: DETERMINISTIC
    [Role] NVARCHAR(20) NOT NULL CONSTRAINT CK_sec_Usr_Role CHECK ([Role] IN ('ADMIN','DOCTOR','RECEPTIONIST','BILLING','NURSE','LAB_PARTNER')),
    SpecialtyId UNIQUEIDENTIFIER NULL, LicenseNumber NVARCHAR(50) NULL, LabPartnerId UNIQUEIDENTIFIER NULL,
    -- Doctor profile (Schedule Settings → Doctors). FullName is rendered from these via
    -- the DOCTOR_NAME_TEMPLATE hospital setting unless DisplayNameOverride is set.
    Prefix NVARCHAR(20) NULL, FirstName NVARCHAR(100) NULL, MiddleName NVARCHAR(100) NULL, LastName NVARCHAR(100) NULL,
    Qualifications NVARCHAR(200) NULL, DisplayNameOverride NVARCHAR(200) NULL,
    Room NVARCHAR(50) NULL, Languages NVARCHAR(200) NULL, Bio NVARCHAR(1000) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_sec_Usr_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_sec_Usr_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_sec_Usr_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL, ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_sec_Usr PRIMARY KEY CLUSTERED (UserPkId),
    CONSTRAINT UQ_sec_Usr_Id UNIQUE NONCLUSTERED (UserId),
    CONSTRAINT UQ_sec_Usr_Email UNIQUE (Email),
    CONSTRAINT FK_sec_Usr_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_sec_Usr_SpecialtyId FOREIGN KEY (SpecialtyId) REFERENCES cfg.Specialty(SpecialtyId),
    INDEX IX_sec_Usr_HospitalId NONCLUSTERED (HospitalId),
    INDEX IX_sec_Usr_Email NONCLUSTERED (Email)
);

-- A doctor may have several specialties; ApplicationUser.SpecialtyId stays the primary.
-- data.js: doctorSpecialties
CREATE TABLE sec.DoctorSpecialty (
    DoctorId UNIQUEIDENTIFIER NOT NULL, SpecialtyId UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT PK_sec_DS PRIMARY KEY CLUSTERED (DoctorId, SpecialtyId),
    CONSTRAINT FK_sec_DS_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId),
    CONSTRAINT FK_sec_DS_SpecialtyId FOREIGN KEY (SpecialtyId) REFERENCES cfg.Specialty(SpecialtyId)
);

CREATE TABLE sec.PermissionMatrix (
    PermissionMatrixPkId BIGINT IDENTITY(1,1) NOT NULL,
    PermissionMatrixId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_Perm_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, [Role] NVARCHAR(20) NOT NULL, Feature NVARCHAR(30) NOT NULL,
    IsGranted BIT NOT NULL CONSTRAINT DF_sec_Perm_Granted DEFAULT 1,
    CONSTRAINT PK_sec_Perm PRIMARY KEY CLUSTERED (PermissionMatrixPkId),
    CONSTRAINT UQ_sec_Perm_Id UNIQUE NONCLUSTERED (PermissionMatrixId),
    CONSTRAINT FK_sec_Perm_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT UQ_sec_Perm_Role_Feature UNIQUE (HospitalId, [Role], Feature),
    INDEX IX_sec_Perm_HospitalId NONCLUSTERED (HospitalId)
);

CREATE TABLE sec.AuditLog (  -- append-only: no ModifiedAt, no IsActive, no IsDeleted
    AuditLogPkId BIGINT IDENTITY(1,1) NOT NULL,
    AuditLogId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_Aud_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, UserId UNIQUEIDENTIFIER NOT NULL,
    [Action] NVARCHAR(20) NOT NULL CONSTRAINT CK_sec_Aud_Action CHECK ([Action] IN ('CREATE','READ','UPDATE','DELETE','LOGIN','LOGIN_FAIL','LOGOUT','EXPORT','PRINT','OVERRIDE','SIGNOFF','SUBMIT','APPROVE','REJECT')),
    EntityType NVARCHAR(50) NOT NULL, EntityId NVARCHAR(50) NULL, Details NVARCHAR(500) NULL,
    SessionToken NVARCHAR(100) NULL, Browser NVARCHAR(100) NULL,
    IpAddress NVARCHAR(45) NULL, UserAgent NVARCHAR(200) NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_sec_Aud_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_sec_Aud PRIMARY KEY CLUSTERED (AuditLogPkId),
    CONSTRAINT UQ_sec_Aud_Id UNIQUE NONCLUSTERED (AuditLogId),
    INDEX IX_sec_Aud_HospitalId NONCLUSTERED (HospitalId),
    INDEX IX_sec_Aud_UserId NONCLUSTERED (UserId),
    INDEX IX_sec_Aud_CreatedAt NONCLUSTERED (CreatedAt DESC),
    INDEX IX_sec_Aud_Entity NONCLUSTERED (EntityType, EntityId)
);

CREATE TABLE sec.UserSession (
    UserSessionPkId BIGINT IDENTITY(1,1) NOT NULL,
    UserSessionId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_Ses_Id DEFAULT NEWSEQUENTIALID(),
    UserId UNIQUEIDENTIFIER NOT NULL, SessionToken NVARCHAR(100) NOT NULL,
    Browser NVARCHAR(100) NULL, IpAddress NVARCHAR(45) NULL, UserAgent NVARCHAR(200) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_sec_Ses_Active DEFAULT 1,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_sec_Ses_CreatedAt DEFAULT SYSUTCDATETIME(),
    ExpiresAt DATETIME2(3) NOT NULL,
    InvalidatedAt DATETIME2(3) NULL,
    InvalidationReason NVARCHAR(30) NULL CONSTRAINT CK_sec_Ses_Reason CHECK (InvalidationReason IN ('NEW_LOGIN','LOGOUT','TIMEOUT','ADMIN_REVOKE',NULL)),
    CONSTRAINT PK_sec_Ses PRIMARY KEY CLUSTERED (UserSessionPkId),
    CONSTRAINT UQ_sec_Ses_Id UNIQUE NONCLUSTERED (UserSessionId),
    CONSTRAINT UQ_sec_Ses_Token UNIQUE (SessionToken),
    CONSTRAINT FK_sec_Ses_UserId FOREIGN KEY (UserId) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_sec_Ses_UserId NONCLUSTERED (UserId)
);

CREATE TABLE sec.LoginAttempt (  -- append-only
    LoginAttemptPkId BIGINT IDENTITY(1,1) NOT NULL,
    LoginAttemptId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_LA_Id DEFAULT NEWSEQUENTIALID(),
    EmailAttempted NVARCHAR(100) NOT NULL, IsSuccess BIT NOT NULL,
    IpAddress NVARCHAR(45) NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_sec_LA_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_sec_LA PRIMARY KEY CLUSTERED (LoginAttemptPkId),
    CONSTRAINT UQ_sec_LA_Id UNIQUE NONCLUSTERED (LoginAttemptId),
    INDEX IX_sec_LA_Email NONCLUSTERED (EmailAttempted),
    INDEX IX_sec_LA_CreatedAt NONCLUSTERED (CreatedAt DESC)
);

-- ============================================================
-- C. opd — OPD CORE (11 tables)
-- ============================================================

CREATE TABLE opd.Patient (  -- TEMPORAL: Phase 4
    PatientPkId BIGINT IDENTITY(1,1) NOT NULL,
    PatientId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_Pat_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, Mrn NVARCHAR(20) NOT NULL,
    FullName NVARCHAR(200) NOT NULL,                        -- ENCRYPTION: DETERMINISTIC
    Gender NVARCHAR(10) NOT NULL CONSTRAINT CK_opd_Pat_Gender CHECK (Gender IN ('MALE','FEMALE','OTHER')),
    Dob DATE NOT NULL,
    Phone NVARCHAR(20) NOT NULL,                            -- ENCRYPTION: DETERMINISTIC
    Email NVARCHAR(100) NULL,                               -- ENCRYPTION: DETERMINISTIC
    [Address] NVARCHAR(500) NULL,                           -- ENCRYPTION: RANDOMIZED
    NationalHealthId NVARCHAR(50) NULL,                     -- ENCRYPTION: DETERMINISTIC
    InsuranceProvider NVARCHAR(100) NULL,                   -- ENCRYPTION: RANDOMIZED
    InsurancePolicyNo NVARCHAR(50) NULL,                    -- ENCRYPTION: RANDOMIZED
    PhotoBase64 NVARCHAR(MAX) NULL,                         -- ENCRYPTION: RANDOMIZED
    EmergencyContactName NVARCHAR(200) NULL,                -- ENCRYPTION: RANDOMIZED
    EmergencyContactPhone NVARCHAR(20) NULL,                -- ENCRYPTION: RANDOMIZED
    -- Social history (Health Record → Family & Social)
    SmokingStatus NVARCHAR(10) NULL CONSTRAINT CK_opd_Pat_Smoking CHECK (SmokingStatus IN ('NEVER','FORMER','CURRENT')),
    SmokingPackYears INT NULL, AlcoholUse NVARCHAR(12) NULL CONSTRAINT CK_opd_Pat_Alcohol CHECK (AlcoholUse IN ('NONE','OCCASIONAL','REGULAR')),
    Occupation NVARCHAR(80) NULL,
    PhysicalActivity NVARCHAR(12) NULL CONSTRAINT CK_opd_Pat_Activity CHECK (PhysicalActivity IN ('SEDENTARY','LIGHT','MODERATE','ACTIVE')),
    Diet NVARCHAR(16) NULL CONSTRAINT CK_opd_Pat_Diet CHECK (Diet IN ('VEGETARIAN','EGGETARIAN','NON_VEGETARIAN','VEGAN')),
    IsActive BIT NOT NULL CONSTRAINT DF_opd_Pat_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_opd_Pat_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_opd_Pat_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL, ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_Pat PRIMARY KEY CLUSTERED (PatientPkId),
    CONSTRAINT UQ_opd_Pat_Id UNIQUE NONCLUSTERED (PatientId),
    CONSTRAINT FK_opd_Pat_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT UQ_opd_Pat_Hosp_Mrn UNIQUE (HospitalId, Mrn),
    INDEX IX_opd_Pat_HospitalId NONCLUSTERED (HospitalId),
    INDEX IX_opd_Pat_Hosp_Phone NONCLUSTERED (HospitalId, Phone)
);

CREATE TABLE opd.PatientAllergy (
    PatientAllergyPkId BIGINT IDENTITY(1,1) NOT NULL,
    PatientAllergyId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_PA_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL,
    AllergyType NVARCHAR(20) NOT NULL CONSTRAINT CK_opd_PA_Type CHECK (AllergyType IN ('DRUG','FOOD','ENVIRONMENT','OTHER')),
    AllergyName NVARCHAR(100) NOT NULL, Reaction NVARCHAR(200) NULL,
    Severity NVARCHAR(10) NOT NULL CONSTRAINT CK_opd_PA_Sev CHECK (Severity IN ('MILD','MODERATE','SEVERE')),
    Status NVARCHAR(10) NOT NULL CONSTRAINT CK_opd_PA_Status CHECK (Status IN ('ACTIVE','RESOLVED')) CONSTRAINT DF_opd_PA_Status DEFAULT 'ACTIVE',
    OnsetDate DATE NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_opd_PA_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_opd_PA_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_opd_PA_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL, ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_PA PRIMARY KEY CLUSTERED (PatientAllergyPkId),
    CONSTRAINT UQ_opd_PA_Id UNIQUE NONCLUSTERED (PatientAllergyId),
    CONSTRAINT FK_opd_PA_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_PA_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    INDEX IX_opd_PA_PatientId NONCLUSTERED (PatientId)
);

CREATE TABLE opd.PatientMedicalHistory (
    PatientMedicalHistoryPkId BIGINT IDENTITY(1,1) NOT NULL,
    PatientMedicalHistoryId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_PMH_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL,
    ConditionName NVARCHAR(100) NOT NULL, SinceMonth INT NULL, SinceYear INT NULL, Notes NVARCHAR(500) NULL,
    ConditionStatus NVARCHAR(12) NOT NULL CONSTRAINT CK_opd_PMH_Status CHECK (ConditionStatus IN ('ACTIVE','CONTROLLED','RESOLVED')) CONSTRAINT DF_opd_PMH_Status DEFAULT 'ACTIVE',
    IsActive BIT NOT NULL CONSTRAINT DF_opd_PMH_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_opd_PMH_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_opd_PMH_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_PMH PRIMARY KEY CLUSTERED (PatientMedicalHistoryPkId),
    CONSTRAINT UQ_opd_PMH_Id UNIQUE NONCLUSTERED (PatientMedicalHistoryId),
    CONSTRAINT FK_opd_PMH_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_PMH_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    INDEX IX_opd_PMH_PatientId NONCLUSTERED (PatientId)
);

CREATE TABLE opd.PatientMedicalEvent (
    PatientMedicalEventPkId BIGINT IDENTITY(1,1) NOT NULL,
    PatientMedicalEventId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_PME_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL,
    EventType NVARCHAR(50) NOT NULL, EventDate DATE NULL, Notes NVARCHAR(500) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_opd_PME_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_opd_PME_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_opd_PME_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_PME PRIMARY KEY CLUSTERED (PatientMedicalEventPkId),
    CONSTRAINT UQ_opd_PME_Id UNIQUE NONCLUSTERED (PatientMedicalEventId),
    CONSTRAINT FK_opd_PME_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_PME_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    INDEX IX_opd_PME_PatientId NONCLUSTERED (PatientId)
);

CREATE TABLE opd.PatientFamilyHistory (
    PatientFamilyHistoryPkId BIGINT IDENTITY(1,1) NOT NULL,
    PatientFamilyHistoryId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_PFH_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL,
    Relation NVARCHAR(30) NOT NULL, [Condition] NVARCHAR(100) NOT NULL, Notes NVARCHAR(300) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_opd_PFH_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_opd_PFH_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_opd_PFH_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_PFH PRIMARY KEY CLUSTERED (PatientFamilyHistoryPkId),
    CONSTRAINT UQ_opd_PFH_Id UNIQUE NONCLUSTERED (PatientFamilyHistoryId),
    CONSTRAINT FK_opd_PFH_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_PFH_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    INDEX IX_opd_PFH_PatientId NONCLUSTERED (PatientId)
);

CREATE TABLE opd.Appointment (  -- TEMPORAL: Phase 4
    AppointmentPkId BIGINT IDENTITY(1,1) NOT NULL,
    AppointmentId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_Appt_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL, DoctorId UNIQUEIDENTIFIER NOT NULL,
    SlotStart NVARCHAR(10) NOT NULL, SlotEnd NVARCHAR(10) NULL, AppointmentDate DATE NOT NULL,
    [Type] NVARCHAR(20) NOT NULL,          -- cfg.AppointmentType.Code (NEW, FOLLOWUP, PROC, …)
    -- Lifecycle: BOOKED → CHECKED_IN (front desk) → IN_CONSULT (doctor opens consultation)
    --            → COMPLETED (consultation signed off, cln.usp_ConfirmSignOff).
    -- CANCELLED / NO_SHOW only from BOOKED or CHECKED_IN. Enforced by opd.usp_UpdateAppointmentStatus.
    Status NVARCHAR(15) NOT NULL CONSTRAINT CK_opd_Appt_Status CHECK (Status IN ('BOOKED','CHECKED_IN','IN_CONSULT','COMPLETED','CANCELLED','NO_SHOW')) CONSTRAINT DF_opd_Appt_Status DEFAULT 'BOOKED',
    CheckedInAt DATETIME2(3) NULL,         -- set on → CHECKED_IN
    CheckedInBy UNIQUEIDENTIFIER NULL,     -- front-desk user who checked the patient in
    ConsultStartedAt DATETIME2(3) NULL,    -- set on → IN_CONSULT
    CompletedAt DATETIME2(3) NULL,         -- set on → COMPLETED
    Notes NVARCHAR(300) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_opd_Appt_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_opd_Appt_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_opd_Appt_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL, ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_Appt PRIMARY KEY CLUSTERED (AppointmentPkId),
    CONSTRAINT UQ_opd_Appt_Id UNIQUE NONCLUSTERED (AppointmentId),
    CONSTRAINT FK_opd_Appt_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_Appt_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT FK_opd_Appt_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId),
    CONSTRAINT FK_opd_Appt_CheckedInBy FOREIGN KEY (CheckedInBy) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_opd_Appt_DoctorDate NONCLUSTERED (DoctorId, AppointmentDate),
    INDEX IX_opd_Appt_HospitalDate NONCLUSTERED (HospitalId, AppointmentDate),  -- front-desk day list, all doctors
    INDEX IX_opd_Appt_PatientId NONCLUSTERED (PatientId),
    INDEX IX_opd_Appt_Status NONCLUSTERED (Status)
);

-- A patient's coverages. No active row = Self-Pay. A patient may hold Private Insurance and a
-- Govt scheme at the same time; Priority orders how liabilities are allocated at billing.
CREATE TABLE opd.PatientCoverage (
    CoveragePkId BIGINT IDENTITY(1,1) NOT NULL,
    CoverageId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_Cov_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL, PayerId UNIQUEIDENTIFIER NOT NULL,
    CoverageType NVARCHAR(20) NOT NULL CONSTRAINT CK_opd_Cov_Type CHECK (CoverageType IN ('PRIVATE_INSURANCE','GOVT_SCHEME')),
    PlanName NVARCHAR(100) NULL,
    MemberId NVARCHAR(50) NULL,                    -- ENCRYPTION: DETERMINISTIC
    PolicyNumber NVARCHAR(50) NULL,                -- ENCRYPTION: DETERMINISTIC
    GroupNumber NVARCHAR(50) NULL,
    PolicyHolderName NVARCHAR(200) NULL,
    Relationship NVARCHAR(20) NULL,                -- Self, Spouse, Child, Parent, Dependent
    ClaimMode NVARCHAR(15) NULL CONSTRAINT CK_opd_Cov_Claim CHECK (ClaimMode IN ('CASHLESS','REIMBURSEMENT') OR ClaimMode IS NULL),
    GovtCardNumber NVARCHAR(50) NULL,              -- ENCRYPTION: DETERMINISTIC (scheme / NHF card)
    CardImageUrl NVARCHAR(500) NULL,               -- blob storage URL of the uploaded card
    EffectiveDate DATE NULL, ExpiryDate DATE NULL,
    Status NVARCHAR(10) NOT NULL CONSTRAINT CK_opd_Cov_Status CHECK (Status IN ('ACTIVE','PENDING','INACTIVE','EXPIRED')) CONSTRAINT DF_opd_Cov_Status DEFAULT 'ACTIVE',
    Priority INT NOT NULL CONSTRAINT DF_opd_Cov_Priority DEFAULT 1,
    IsActive BIT NOT NULL CONSTRAINT DF_opd_Cov_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_opd_Cov_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_opd_Cov_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL, ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_Cov PRIMARY KEY CLUSTERED (CoveragePkId),
    CONSTRAINT UQ_opd_Cov_Id UNIQUE NONCLUSTERED (CoverageId),
    CONSTRAINT FK_opd_Cov_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_Cov_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT FK_opd_Cov_PayerId FOREIGN KEY (PayerId) REFERENCES cfg.Payer(PayerId),
    INDEX IX_opd_Cov_PatientId NONCLUSTERED (PatientId, IsActive)
);

-- Weekly availability (Schedule Settings → Doctors): one row per time window, several
-- windows per weekday allowed, no row for a weekday = closed. The slot length is NOT
-- stored here — it comes from APPT_SLOT_MINS (opd.DoctorSetting → cfg.HospitalSetting)
-- or the appointment type's duration. data.js: doctorSlotConfig
CREATE TABLE opd.DoctorSlotConfig (
    SlotConfigPkId BIGINT IDENTITY(1,1) NOT NULL,
    SlotConfigId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_SC_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, DoctorId UNIQUEIDENTIFIER NOT NULL,
    DayOfWeek NVARCHAR(3) NOT NULL CONSTRAINT CK_opd_SC_Day CHECK (DayOfWeek IN ('MON','TUE','WED','THU','FRI','SAT','SUN')),
    StartTime NVARCHAR(5) NOT NULL, EndTime NVARCHAR(5) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_opd_SC_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_opd_SC_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_opd_SC_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_opd_SC PRIMARY KEY CLUSTERED (SlotConfigPkId),
    CONSTRAINT UQ_opd_SC_Id UNIQUE NONCLUSTERED (SlotConfigId),
    CONSTRAINT CK_opd_SC_Window CHECK (EndTime > StartTime),
    CONSTRAINT FK_opd_SC_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_SC_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_opd_SC_DoctorDay NONCLUSTERED (DoctorId, DayOfWeek)
);

-- Per-doctor overrides of APPT_SLOT_MINS / FREE_FOLLOWUP_ALLOWED / FREE_FOLLOWUP_DAYS and
-- the doctor's charge model (CHARGE_MODEL FLAT|UNIT, CHARGE_UNIT_MINS, CHARGE_UNIT_RATE).
-- No row = inherit cfg.HospitalSetting. data.js: doctorSettings
CREATE TABLE opd.DoctorSetting (
    DoctorSettingPkId BIGINT IDENTITY(1,1) NOT NULL,
    HospitalId UNIQUEIDENTIFIER NOT NULL, DoctorId UNIQUEIDENTIFIER NOT NULL,
    SettingKey NVARCHAR(40) NOT NULL CONSTRAINT CK_opd_DSet_Key CHECK (SettingKey IN
        ('APPT_SLOT_MINS','FREE_FOLLOWUP_ALLOWED','FREE_FOLLOWUP_DAYS','CHARGE_MODEL','CHARGE_UNIT_MINS','CHARGE_UNIT_RATE')),
    [Value] NVARCHAR(200) NOT NULL,
    ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_DSet PRIMARY KEY CLUSTERED (DoctorSettingPkId),
    CONSTRAINT UQ_opd_DSet_Key UNIQUE NONCLUSTERED (HospitalId, DoctorId, SettingKey),
    CONSTRAINT FK_opd_DSet_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_DSet_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId)
);

-- Full-day leave / holidays: no slots are offered on these dates. data.js: doctorLeave
CREATE TABLE opd.DoctorLeave (
    DoctorLeavePkId BIGINT IDENTITY(1,1) NOT NULL,
    LeaveId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_DL_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, DoctorId UNIQUEIDENTIFIER NOT NULL,
    LeaveDate DATE NOT NULL, Reason NVARCHAR(200) NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_opd_DL_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_DL PRIMARY KEY CLUSTERED (DoctorLeavePkId),
    CONSTRAINT UQ_opd_DL_Id UNIQUE NONCLUSTERED (LeaveId),
    CONSTRAINT UQ_opd_DL_Day UNIQUE NONCLUSTERED (HospitalId, DoctorId, LeaveDate),
    CONSTRAINT FK_opd_DL_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_DL_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId)
);

-- Patient answers to the intake form, one per appointment. AnswersJson =
-- [{ QuestionId, Label, Answer }] — labels snapshotted. data.js: intakeResponses
CREATE TABLE opd.IntakeResponse (
    IntakeResponsePkId BIGINT IDENTITY(1,1) NOT NULL,
    ResponseId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_IR_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, AppointmentId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL,
    AnswersJson NVARCHAR(MAX) NOT NULL CONSTRAINT CK_opd_IR_Json CHECK (ISJSON(AnswersJson) = 1),
    SubmittedAt DATETIME2(3) NOT NULL CONSTRAINT DF_opd_IR_At DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_opd_IR PRIMARY KEY CLUSTERED (IntakeResponsePkId),
    CONSTRAINT UQ_opd_IR_Id UNIQUE NONCLUSTERED (ResponseId),
    CONSTRAINT UQ_opd_IR_Appt UNIQUE NONCLUSTERED (AppointmentId),
    CONSTRAINT FK_opd_IR_AppointmentId FOREIGN KEY (AppointmentId) REFERENCES opd.Appointment(AppointmentId),
    CONSTRAINT FK_opd_IR_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId)
);

-- Signed consents — append-only (no update/delete); title/text snapshotted at signing.
-- data.js: consentRecords
CREATE TABLE opd.ConsentRecord (
    ConsentRecordPkId BIGINT IDENTITY(1,1) NOT NULL,
    ConsentRecordId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_CR_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsentFormId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL,
    AppointmentId UNIQUEIDENTIFIER NULL, ConsultationId UNIQUEIDENTIFIER NULL,
    SignedByName NVARCHAR(200) NOT NULL,
    Method NVARCHAR(10) NOT NULL CONSTRAINT CK_opd_CR_Method CHECK (Method IN ('IN_CLINIC','PORTAL')),
    FormVersion INT NOT NULL, TitleSnapshot NVARCHAR(200) NOT NULL, BodySnapshot NVARCHAR(MAX) NOT NULL,
    RecordedBy UNIQUEIDENTIFIER NULL,
    SignedAt DATETIME2(3) NOT NULL CONSTRAINT DF_opd_CR_At DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_opd_CR PRIMARY KEY CLUSTERED (ConsentRecordPkId),
    CONSTRAINT UQ_opd_CR_Id UNIQUE NONCLUSTERED (ConsentRecordId),
    CONSTRAINT FK_opd_CR_FormId FOREIGN KEY (ConsentFormId) REFERENCES cfg.ConsentForm(ConsentFormId),
    CONSTRAINT FK_opd_CR_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    INDEX IX_opd_CR_Appt NONCLUSTERED (AppointmentId)
);

-- ============================================================
-- D. cln — CLINICAL (14 tables)
-- ============================================================

CREATE TABLE cln.Consultation (  -- TEMPORAL: Phase 4
    ConsultationPkId BIGINT IDENTITY(1,1) NOT NULL,
    ConsultationId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Con_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL, DoctorId UNIQUEIDENTIFIER NOT NULL,
    AppointmentId UNIQUEIDENTIFIER NULL, VisitDate DATE NOT NULL,
    ConsultationType NVARCHAR(10) NOT NULL CONSTRAINT CK_cln_Con_Type CHECK (ConsultationType IN ('NEW','FOLLOWUP')) CONSTRAINT DF_cln_Con_Type DEFAULT 'NEW',
    Status NVARCHAR(15) NOT NULL CONSTRAINT CK_cln_Con_Status CHECK (Status IN ('IN_PROGRESS','COMPLETED','LOCKED')) CONSTRAINT DF_cln_Con_Status DEFAULT 'IN_PROGRESS',
    ChiefComplaint NVARCHAR(500) NULL,
    SubjectiveText NVARCHAR(MAX) NULL, ObjectiveText NVARCHAR(MAX) NULL,
    AssessmentText NVARCHAR(MAX) NULL, PlanText NVARCHAR(MAX) NULL,
    FollowUpDate DATE NULL, FollowUpNotes NVARCHAR(500) NULL,
    IsLocked BIT NOT NULL CONSTRAINT DF_cln_Con_Locked DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_cln_Con_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cln_Con_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cln_Con_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL, ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_Con PRIMARY KEY CLUSTERED (ConsultationPkId),
    CONSTRAINT UQ_cln_Con_Id UNIQUE NONCLUSTERED (ConsultationId),
    CONSTRAINT FK_cln_Con_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_Con_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT FK_cln_Con_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId),
    CONSTRAINT FK_cln_Con_ApptId FOREIGN KEY (AppointmentId) REFERENCES opd.Appointment(AppointmentId),
    INDEX IX_cln_Con_HospitalId NONCLUSTERED (HospitalId),
    INDEX IX_cln_Con_PatientId NONCLUSTERED (PatientId),
    INDEX IX_cln_Con_DoctorDate NONCLUSTERED (DoctorId, VisitDate),
    INDEX IX_cln_Con_Status NONCLUSTERED (Status)
);

CREATE TABLE cln.ConsultationVital (
    ConsultationVitalPkId BIGINT IDENTITY(1,1) NOT NULL,
    ConsultationVitalId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_CV_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL,
    TempValue DECIMAL(5,1) NULL, BpSystolic INT NULL, BpDiastolic INT NULL,
    PulseBpm INT NULL, SpO2 INT NULL, RespRate INT NULL,
    HeightCm DECIMAL(5,1) NULL, WeightKg DECIMAL(5,1) NULL, Bmi DECIMAL(4,1) NULL,
    PainScale INT NULL, WaistCircCm DECIMAL(5,1) NULL, HeadCircCm DECIMAL(5,1) NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cln_CV_CreatedAt DEFAULT SYSUTCDATETIME(),
    ModifiedAt DATETIME2(3) NULL,
    CONSTRAINT PK_cln_CV PRIMARY KEY CLUSTERED (ConsultationVitalPkId),
    CONSTRAINT UQ_cln_CV_Id UNIQUE NONCLUSTERED (ConsultationVitalId),
    CONSTRAINT FK_cln_CV_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_CV_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT UQ_cln_CV_ConsultId UNIQUE (ConsultationId)
);

/* Values of hospital-defined (custom) vitals: one row per consultation per custom vital code */
CREATE TABLE cln.ConsultationCustomVital (
    ConsultationCustomVitalPkId BIGINT IDENTITY(1,1) NOT NULL,
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL,
    VitalCode NVARCHAR(40) NOT NULL,
    VitalValue DECIMAL(10,2) NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cln_CCV_CreatedAt DEFAULT SYSUTCDATETIME(),
    ModifiedAt DATETIME2(3) NULL,
    CONSTRAINT PK_cln_CCV PRIMARY KEY CLUSTERED (ConsultationCustomVitalPkId),
    CONSTRAINT FK_cln_CCV_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_CCV_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT UQ_cln_CCV_Consult_Code UNIQUE (ConsultationId, VitalCode)
);

CREATE TABLE cln.ConsultationSymptom (
    ConsultationSymptomPkId BIGINT IDENTITY(1,1) NOT NULL,
    ConsultationSymptomId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_CS_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL,
    SymptomName NVARCHAR(100) NOT NULL,
    [State] NVARCHAR(10) NOT NULL CONSTRAINT CK_cln_CS_State CHECK ([State] IN ('YES','NO','NOT_ASKED')) CONSTRAINT DF_cln_CS_State DEFAULT 'NOT_ASKED',
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cln_CS_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_cln_CS PRIMARY KEY CLUSTERED (ConsultationSymptomPkId),
    CONSTRAINT UQ_cln_CS_Id UNIQUE NONCLUSTERED (ConsultationSymptomId),
    CONSTRAINT FK_cln_CS_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_CS_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    INDEX IX_cln_CS_ConsultId NONCLUSTERED (ConsultationId)
);

CREATE TABLE cln.ConsultationDiagnosis (
    ConsultationDiagnosisPkId BIGINT IDENTITY(1,1) NOT NULL,
    ConsultationDiagnosisId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Dx_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL,
    ICD10Code NVARCHAR(10) NOT NULL, [Description] NVARCHAR(300) NOT NULL,
    IsPrimary BIT NOT NULL CONSTRAINT DF_cln_Dx_Primary DEFAULT 0,
    Laterality NVARCHAR(10) NULL,
    Chronicity NVARCHAR(10) NULL CONSTRAINT CK_cln_Dx_Chron CHECK (Chronicity IN ('ACUTE','CHRONIC',NULL)),
    SequenceOrder INT NOT NULL CONSTRAINT DF_cln_Dx_Seq DEFAULT 1,
    IsActive BIT NOT NULL CONSTRAINT DF_cln_Dx_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cln_Dx_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cln_Dx_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_cln_Dx PRIMARY KEY CLUSTERED (ConsultationDiagnosisPkId),
    CONSTRAINT UQ_cln_Dx_Id UNIQUE NONCLUSTERED (ConsultationDiagnosisId),
    CONSTRAINT FK_cln_Dx_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_Dx_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    INDEX IX_cln_Dx_ConsultId NONCLUSTERED (ConsultationId)
);

CREATE TABLE cln.Prescription (  -- TEMPORAL: Phase 4
    PrescriptionPkId BIGINT IDENTITY(1,1) NOT NULL,
    PrescriptionId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Rx_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    ConsultationId UNIQUEIDENTIFIER NULL,                   -- NULL for outside medicines recorded on the Health Record
    PatientId UNIQUEIDENTIFIER NOT NULL,
    Source NVARCHAR(10) NULL CONSTRAINT CK_cln_Rx_Source CHECK (Source IN ('OUTSIDE')),  -- NULL = prescribed in a consultation
    SourceDetail NVARCHAR(120) NULL,                       -- who prescribed an outside medicine
    MedicineName NVARCHAR(200) NOT NULL, Strength NVARCHAR(50) NULL,
    DrugForm NVARCHAR(30) NULL, DeliveryRoute NVARCHAR(30) NULL,
    Category NVARCHAR(20) NULL, TargetArea NVARCHAR(50) NULL,
    TakeInUnit NVARCHAR(20) NULL, TakeInQty DECIMAL(5,1) NOT NULL CONSTRAINT DF_cln_Rx_Qty DEFAULT 1,
    TimesPerDay INT NOT NULL CONSTRAINT DF_cln_Rx_Times DEFAULT 1,
    DurationValue INT NULL, DurationUnit NVARCHAR(10) NULL,
    PeriodStartDate DATE NULL, PeriodEndDate DATE NULL, FrequencyMode NVARCHAR(10) NULL,
    IsTimingBf BIT NOT NULL CONSTRAINT DF_cln_Rx_Bf DEFAULT 0,
    IsTimingAn BIT NOT NULL CONSTRAINT DF_cln_Rx_An DEFAULT 0,
    IsTimingNi BIT NOT NULL CONSTRAINT DF_cln_Rx_Ni DEFAULT 0,
    IsTimingEm BIT NOT NULL CONSTRAINT DF_cln_Rx_Em DEFAULT 0,
    IsPrn BIT NOT NULL CONSTRAINT DF_cln_Rx_Prn DEFAULT 0,
    Instructions NVARCHAR(500) NULL, DynamicInstruction NVARCHAR(500) NULL,
    Status NVARCHAR(15) NOT NULL CONSTRAINT CK_cln_Rx_Status CHECK (Status IN ('ACTIVE','DISCONTINUED','COMPLETED')) CONSTRAINT DF_cln_Rx_Status DEFAULT 'ACTIVE',
    IsActive BIT NOT NULL CONSTRAINT DF_cln_Rx_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cln_Rx_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cln_Rx_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL, ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_Rx PRIMARY KEY CLUSTERED (PrescriptionPkId),
    CONSTRAINT UQ_cln_Rx_Id UNIQUE NONCLUSTERED (PrescriptionId),
    CONSTRAINT FK_cln_Rx_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_Rx_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT FK_cln_Rx_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    INDEX IX_cln_Rx_ConsultId NONCLUSTERED (ConsultationId),
    CONSTRAINT CK_cln_Rx_Origin CHECK (ConsultationId IS NOT NULL OR Source = 'OUTSIDE'),
    INDEX IX_cln_Rx_PatientId NONCLUSTERED (PatientId)
);

CREATE TABLE cln.PrescriptionSafetyAlert (
    SafetyAlertPkId BIGINT IDENTITY(1,1) NOT NULL,
    SafetyAlertId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_SA_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, PrescriptionId UNIQUEIDENTIFIER NOT NULL,
    AlertType NVARCHAR(30) NOT NULL, AlertMessage NVARCHAR(500) NOT NULL,
    IsOverridden BIT NOT NULL CONSTRAINT DF_cln_SA_Over DEFAULT 0,
    OverrideReason NVARCHAR(300) NULL, OverriddenBy UNIQUEIDENTIFIER NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cln_SA_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_cln_SA PRIMARY KEY CLUSTERED (SafetyAlertPkId),
    CONSTRAINT UQ_cln_SA_Id UNIQUE NONCLUSTERED (SafetyAlertId),
    CONSTRAINT FK_cln_SA_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_SA_RxId FOREIGN KEY (PrescriptionId) REFERENCES cln.Prescription(PrescriptionId),
    INDEX IX_cln_SA_RxId NONCLUSTERED (PrescriptionId)
);

CREATE TABLE cln.InvestigationOrder (
    InvestigationOrderPkId BIGINT IDENTITY(1,1) NOT NULL,
    InvestigationOrderId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_IO_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL,
    TestName NVARCHAR(100) NOT NULL, TestType NVARCHAR(20) NOT NULL,
    Priority NVARCHAR(10) NOT NULL CONSTRAINT CK_cln_IO_Pri CHECK (Priority IN ('ROUTINE','URGENT','STAT')) CONSTRAINT DF_cln_IO_Pri DEFAULT 'ROUTINE',
    ClinicalNotes NVARCHAR(300) NULL, IsFasting BIT NOT NULL CONSTRAINT DF_cln_IO_Fast DEFAULT 0,
    LabPartnerId UNIQUEIDENTIFIER NULL,  -- set when a lab partner claims the order from the patient's requisition
    Status NVARCHAR(15) NOT NULL CONSTRAINT CK_cln_IO_Status CHECK (Status IN ('ORDERED','IN_PROGRESS','COMPLETED','CANCELLED')) CONSTRAINT DF_cln_IO_Status DEFAULT 'ORDERED',
    IsActive BIT NOT NULL CONSTRAINT DF_cln_IO_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cln_IO_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cln_IO_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_IO PRIMARY KEY CLUSTERED (InvestigationOrderPkId),
    CONSTRAINT UQ_cln_IO_Id UNIQUE NONCLUSTERED (InvestigationOrderId),
    CONSTRAINT FK_cln_IO_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_IO_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT FK_cln_IO_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    INDEX IX_cln_IO_ConsultId NONCLUSTERED (ConsultationId)
);

CREATE TABLE cln.LabResult (
    LabResultPkId BIGINT IDENTITY(1,1) NOT NULL,
    LabResultId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_LR_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, InvestigationOrderId UNIQUEIDENTIFIER NOT NULL,
    LabPartnerId UNIQUEIDENTIFIER NULL, ResultSummary NVARCHAR(500) NULL,
    ResultValue NVARCHAR(100) NULL, ReferenceRange NVARCHAR(100) NULL,
    Interpretation NVARCHAR(10) NULL CONSTRAINT CK_cln_LR_Interp CHECK (Interpretation IN ('NORMAL','ABNORMAL','CRITICAL',NULL)),
    TechnicianName NVARCHAR(100) NULL, ReviewedBy UNIQUEIDENTIFIER NULL,
    ReviewedAt DATETIME2(3) NULL, ReviewNotes NVARCHAR(300) NULL,
    Status NVARCHAR(10) NOT NULL CONSTRAINT CK_cln_LR_Status CHECK (Status IN ('PENDING','UPLOADED','REVIEWED')) CONSTRAINT DF_cln_LR_Status DEFAULT 'PENDING',
    IsActive BIT NOT NULL CONSTRAINT DF_cln_LR_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cln_LR_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cln_LR_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_cln_LR PRIMARY KEY CLUSTERED (LabResultPkId),
    CONSTRAINT UQ_cln_LR_Id UNIQUE NONCLUSTERED (LabResultId),
    CONSTRAINT FK_cln_LR_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_LR_OrderId FOREIGN KEY (InvestigationOrderId) REFERENCES cln.InvestigationOrder(InvestigationOrderId),
    INDEX IX_cln_LR_OrderId NONCLUSTERED (InvestigationOrderId)
);

-- Report files (PDF / JPG / PNG / DICOM) uploaded with a lab result. The bytes are stored outside the
-- database (App_Data/lab-reports/{HospitalId}/{StoredFileName}); this row holds the metadata. data.js: labResultAttachments
CREATE TABLE cln.LabResultAttachment (
    LabResultAttachmentPkId BIGINT IDENTITY(1,1) NOT NULL,
    LabResultAttachmentId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_LRA_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, LabResultId UNIQUEIDENTIFIER NOT NULL, InvestigationOrderId UNIQUEIDENTIFIER NOT NULL,
    LabPartnerId UNIQUEIDENTIFIER NULL,
    FileName NVARCHAR(255) NOT NULL, ContentType NVARCHAR(50) NOT NULL, SizeBytes BIGINT NOT NULL,
    StoredFileName NVARCHAR(100) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_cln_LRA_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cln_LRA_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cln_LRA_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_LRA PRIMARY KEY CLUSTERED (LabResultAttachmentPkId),
    CONSTRAINT UQ_cln_LRA_Id UNIQUE NONCLUSTERED (LabResultAttachmentId),
    CONSTRAINT FK_cln_LRA_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_LRA_ResultId FOREIGN KEY (LabResultId) REFERENCES cln.LabResult(LabResultId),
    CONSTRAINT FK_cln_LRA_OrderId FOREIGN KEY (InvestigationOrderId) REFERENCES cln.InvestigationOrder(InvestigationOrderId),
    INDEX IX_cln_LRA_ResultId NONCLUSTERED (LabResultId)
);

CREATE TABLE cln.Referral (
    ReferralPkId BIGINT IDENTITY(1,1) NOT NULL,
    ReferralId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Ref_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL,
    ReferToSpecialty NVARCHAR(100) NOT NULL, ReferToDoctorName NVARCHAR(200) NULL,
    Urgency NVARCHAR(10) NOT NULL CONSTRAINT CK_cln_Ref_Urg CHECK (Urgency IN ('ROUTINE','URGENT','EMERGENCY')) CONSTRAINT DF_cln_Ref_Urg DEFAULT 'ROUTINE',
    Reason NVARCHAR(500) NULL,
    Status NVARCHAR(15) NOT NULL CONSTRAINT CK_cln_Ref_Status CHECK (Status IN ('PENDING','ACCEPTED','COMPLETED','CANCELLED')) CONSTRAINT DF_cln_Ref_Status DEFAULT 'PENDING',
    IsActive BIT NOT NULL CONSTRAINT DF_cln_Ref_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_cln_Ref_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cln_Ref_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_Ref PRIMARY KEY CLUSTERED (ReferralPkId),
    CONSTRAINT UQ_cln_Ref_Id UNIQUE NONCLUSTERED (ReferralId),
    CONSTRAINT FK_cln_Ref_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_Ref_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT FK_cln_Ref_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    INDEX IX_cln_Ref_ConsultId NONCLUSTERED (ConsultationId)
);

CREATE TABLE cln.ConsultationAttestation (
    AttestationPkId BIGINT IDENTITY(1,1) NOT NULL,
    AttestationId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Att_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL,
    IsSubjectiveChecked BIT NOT NULL CONSTRAINT DF_cln_Att_S DEFAULT 0,
    IsObjectiveChecked BIT NOT NULL CONSTRAINT DF_cln_Att_O DEFAULT 0,
    IsAssessmentChecked BIT NOT NULL CONSTRAINT DF_cln_Att_A DEFAULT 0,
    IsPlanChecked BIT NOT NULL CONSTRAINT DF_cln_Att_P DEFAULT 0,
    SignedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cln_Att_SignedAt DEFAULT SYSUTCDATETIME(),
    SignedBy UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT PK_cln_Att PRIMARY KEY CLUSTERED (AttestationPkId),
    CONSTRAINT UQ_cln_Att_Id UNIQUE NONCLUSTERED (AttestationId),
    CONSTRAINT FK_cln_Att_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_Att_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT UQ_cln_Att_ConsultId UNIQUE (ConsultationId)
);

CREATE TABLE cln.ConsultationAddendum (  -- append-only
    AddendumPkId BIGINT IDENTITY(1,1) NOT NULL,
    AddendumId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Add_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL,
    AddendumText NVARCHAR(MAX) NOT NULL, AddedBy UNIQUEIDENTIFIER NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cln_Add_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_cln_Add PRIMARY KEY CLUSTERED (AddendumPkId),
    CONSTRAINT UQ_cln_Add_Id UNIQUE NONCLUSTERED (AddendumId),
    CONSTRAINT FK_cln_Add_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_Add_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    INDEX IX_cln_Add_ConsultId NONCLUSTERED (ConsultationId)
);

CREATE TABLE cln.ConfidentialNote (
    ConfidentialNotePkId BIGINT IDENTITY(1,1) NOT NULL,
    ConfidentialNoteId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_CN_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL,
    NoteText NVARCHAR(MAX) NOT NULL, CreatedBy UNIQUEIDENTIFIER NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cln_CN_CreatedAt DEFAULT SYSUTCDATETIME(),
    ModifiedAt DATETIME2(3) NULL,
    CONSTRAINT PK_cln_CN PRIMARY KEY CLUSTERED (ConfidentialNotePkId),
    CONSTRAINT UQ_cln_CN_Id UNIQUE NONCLUSTERED (ConfidentialNoteId),
    CONSTRAINT FK_cln_CN_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_CN_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT UQ_cln_CN_ConsultId UNIQUE (ConsultationId)
);

CREATE TABLE cln.ConsultationSketch (
    SketchPkId BIGINT IDENTITY(1,1) NOT NULL,
    SketchId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Sk_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL,
    PatientId UNIQUEIDENTIFIER NOT NULL, DoctorId UNIQUEIDENTIFIER NOT NULL,
    Title NVARCHAR(100) NULL,
    SketchDataUrl NVARCHAR(MAX) NOT NULL,                   -- JPEG base64
    StrokesJson NVARCHAR(MAX) NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cln_Sk_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_cln_Sk PRIMARY KEY CLUSTERED (SketchPkId),
    CONSTRAINT UQ_cln_Sk_Id UNIQUE NONCLUSTERED (SketchId),
    CONSTRAINT FK_cln_Sk_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_Sk_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT FK_cln_Sk_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT FK_cln_Sk_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_cln_Sk_PatientId NONCLUSTERED (PatientId)
);

CREATE TABLE cln.DoctorLabFavorite (
    DoctorLabFavoritePkId BIGINT IDENTITY(1,1) NOT NULL,
    DoctorLabFavoriteId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_DLF_Id DEFAULT NEWSEQUENTIALID(),
    DoctorId UNIQUEIDENTIFIER NOT NULL, LabTestCatalogId UNIQUEIDENTIFIER NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_cln_DLF_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_cln_DLF PRIMARY KEY CLUSTERED (DoctorLabFavoritePkId),
    CONSTRAINT UQ_cln_DLF_Id UNIQUE NONCLUSTERED (DoctorLabFavoriteId),
    CONSTRAINT FK_cln_DLF_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId),
    CONSTRAINT FK_cln_DLF_TestId FOREIGN KEY (LabTestCatalogId) REFERENCES cfg.LabTestCatalog(LabTestCatalogId),
    CONSTRAINT UQ_cln_DLF_Doc_Test UNIQUE (DoctorId, LabTestCatalogId)
);

-- ============================================================
-- E. bil — BILLING (7 tables) — FULLY ALIGNED WITH data.js v2.0
-- ============================================================

CREATE TABLE bil.DoctorFeeSchedule (
    FeeSchedulePkId BIGINT IDENTITY(1,1) NOT NULL,
    FeeScheduleId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_FS_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, DoctorId UNIQUEIDENTIFIER NOT NULL,
    ConsultationType NVARCHAR(10) NOT NULL CONSTRAINT CK_bil_FS_Type CHECK (ConsultationType IN ('NEW','FOLLOWUP')),
    FeeAmount DECIMAL(10,2) NOT NULL, Currency NVARCHAR(5) NOT NULL CONSTRAINT DF_bil_FS_Curr DEFAULT 'USD',
    EffectiveFrom DATE NOT NULL, EffectiveTo DATE NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_bil_FS_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_bil_FS_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_bil_FS_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL, ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_bil_FS PRIMARY KEY CLUSTERED (FeeSchedulePkId),
    CONSTRAINT UQ_bil_FS_Id UNIQUE NONCLUSTERED (FeeScheduleId),
    CONSTRAINT FK_bil_FS_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_bil_FS_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_bil_FS_HospitalId NONCLUSTERED (HospitalId)
);

CREATE TABLE bil.ServiceChargeMaster (
    ServiceChargePkId BIGINT IDENTITY(1,1) NOT NULL,
    ServiceChargeId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_SC_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    ServiceCode NVARCHAR(20) NOT NULL, ServiceName NVARCHAR(200) NOT NULL,
    Category NVARCHAR(20) NOT NULL CONSTRAINT CK_bil_SC_Cat CHECK (Category IN ('CONSULTATION','LAB','IMAGING','PROCEDURE','SUPPLY','INJECTION','OTHER')),
    DefaultAmount DECIMAL(10,2) NOT NULL,
    TaxPercent DECIMAL(5,2) NOT NULL CONSTRAINT DF_bil_SC_Tax DEFAULT 0,
    Currency NVARCHAR(5) NOT NULL CONSTRAINT DF_bil_SC_Curr DEFAULT 'USD',
    IsActive BIT NOT NULL CONSTRAINT DF_bil_SC_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_bil_SC_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_bil_SC_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL, ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_bil_SC PRIMARY KEY CLUSTERED (ServiceChargePkId),
    CONSTRAINT UQ_bil_SC_Id UNIQUE NONCLUSTERED (ServiceChargeId),
    CONSTRAINT FK_bil_SC_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT UQ_bil_SC_Hosp_Code UNIQUE (HospitalId, ServiceCode),
    INDEX IX_bil_SC_HospitalId NONCLUSTERED (HospitalId)
);

CREATE TABLE bil.Invoice (  -- TEMPORAL: Phase 4
    InvoicePkId BIGINT IDENTITY(1,1) NOT NULL,
    InvoiceId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_Inv_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, InvoiceNumber NVARCHAR(30) NOT NULL,
    ConsultationId UNIQUEIDENTIFIER NULL, PatientId UNIQUEIDENTIFIER NOT NULL,
    DoctorId UNIQUEIDENTIFIER NULL,
    ConsultationType NVARCHAR(10) NULL,
    IsFreeFollowup BIT NOT NULL CONSTRAINT DF_bil_Inv_FreeFU DEFAULT 0,   -- billed ₹0 under the free follow-up policy
    ChargeNote NVARCHAR(300) NULL,                                        -- why the consultation fee is what it is
    Currency NVARCHAR(5) NOT NULL CONSTRAINT DF_bil_Inv_Curr DEFAULT 'USD',
    SubTotal DECIMAL(10,2) NOT NULL CONSTRAINT DF_bil_Inv_Sub DEFAULT 0,
    DiscountAmount DECIMAL(10,2) NOT NULL CONSTRAINT DF_bil_Inv_Disc DEFAULT 0,
    TaxAmount DECIMAL(10,2) NOT NULL CONSTRAINT DF_bil_Inv_Tax DEFAULT 0,
    TotalAmount DECIMAL(10,2) NOT NULL,
    PaidAmount DECIMAL(10,2) NOT NULL CONSTRAINT DF_bil_Inv_Paid DEFAULT 0,       -- all non-voided payments (patient + payer settlements)
    -- N-way split (bil.usp_RecalculateInvoiceSplit): TotalAmount = CoveredAmount + PatientLiability
    CoveredAmount DECIMAL(10,2) NOT NULL CONSTRAINT DF_bil_Inv_Covered DEFAULT 0,     -- Σ verified bil.InvoiceCoverageLine.Liability
    PatientLiability DECIMAL(10,2) NOT NULL CONSTRAINT DF_bil_Inv_PatLiab DEFAULT 0,  -- TotalAmount − CoveredAmount
    PatientPaidAmount DECIMAL(10,2) NOT NULL CONSTRAINT DF_bil_Inv_PatPaid DEFAULT 0, -- non-voided PATIENT payments
    -- PAID once the patient share is settled (covered amounts are payer receivables) or everything is paid
    Status NVARCHAR(10) NOT NULL CONSTRAINT CK_bil_Inv_Status CHECK (Status IN ('DRAFT','PENDING','PAID','PARTIAL','CANCELLED')) CONSTRAINT DF_bil_Inv_Status DEFAULT 'PENDING',
    IsActive BIT NOT NULL CONSTRAINT DF_bil_Inv_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_bil_Inv_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_bil_Inv_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL, ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_bil_Inv PRIMARY KEY CLUSTERED (InvoicePkId),
    CONSTRAINT UQ_bil_Inv_Id UNIQUE NONCLUSTERED (InvoiceId),
    CONSTRAINT FK_bil_Inv_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_bil_Inv_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT FK_bil_Inv_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT FK_bil_Inv_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId),
    CONSTRAINT UQ_bil_Inv_Hosp_Number UNIQUE (HospitalId, InvoiceNumber),
    INDEX IX_bil_Inv_HospitalId NONCLUSTERED (HospitalId),
    INDEX IX_bil_Inv_PatientId NONCLUSTERED (PatientId),
    INDEX IX_bil_Inv_Status NONCLUSTERED (Status),
    INDEX IX_bil_Inv_CreatedAt NONCLUSTERED (CreatedAt DESC)
);

CREATE TABLE bil.InvoiceLineItem (
    LineItemPkId BIGINT IDENTITY(1,1) NOT NULL,
    LineItemId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_LI_Id DEFAULT NEWSEQUENTIALID(),
    InvoiceId UNIQUEIDENTIFIER NOT NULL, ServiceChargeId UNIQUEIDENTIFIER NULL,
    [Description] NVARCHAR(200) NOT NULL,
    Category NVARCHAR(20) NOT NULL,
    Qty INT NOT NULL CONSTRAINT DF_bil_LI_Qty DEFAULT 1,
    UnitAmount DECIMAL(10,2) NOT NULL,
    DiscountPercent DECIMAL(5,2) NOT NULL CONSTRAINT DF_bil_LI_DiscPct DEFAULT 0,
    DiscountAmount DECIMAL(10,2) NOT NULL CONSTRAINT DF_bil_LI_DiscAmt DEFAULT 0,
    TaxPercent DECIMAL(5,2) NOT NULL CONSTRAINT DF_bil_LI_TaxPct DEFAULT 0,
    TaxAmount DECIMAL(10,2) NOT NULL CONSTRAINT DF_bil_LI_TaxAmt DEFAULT 0,
    LineTotal DECIMAL(10,2) NOT NULL,
    SortOrder INT NOT NULL CONSTRAINT DF_bil_LI_Sort DEFAULT 0,
    CONSTRAINT PK_bil_LI PRIMARY KEY CLUSTERED (LineItemPkId),
    CONSTRAINT UQ_bil_LI_Id UNIQUE NONCLUSTERED (LineItemId),
    CONSTRAINT FK_bil_LI_InvoiceId FOREIGN KEY (InvoiceId) REFERENCES bil.Invoice(InvoiceId),
    INDEX IX_bil_LI_InvoiceId NONCLUSTERED (InvoiceId)
);

-- Every insurer / scheme eligibility response recorded at billing (history kept, never updated).
CREATE TABLE bil.EligibilityVerification (
    VerificationPkId BIGINT IDENTITY(1,1) NOT NULL,
    VerificationId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_ELV_Id DEFAULT NEWSEQUENTIALID(),
    CoverageId UNIQUEIDENTIFIER NOT NULL, InvoiceId UNIQUEIDENTIFIER NULL,
    Status NVARCHAR(10) NOT NULL CONSTRAINT CK_bil_ELV_Status CHECK (Status IN ('VERIFIED','PENDING','REJECTED')),
    AuthorizationNumber NVARCHAR(50) NULL,
    ApprovedType NVARCHAR(10) NOT NULL CONSTRAINT CK_bil_ELV_Type CHECK (ApprovedType IN ('PERCENT','AMOUNT')) CONSTRAINT DF_bil_ELV_Type DEFAULT 'PERCENT',
    ApprovedValue DECIMAL(10,2) NOT NULL CONSTRAINT DF_bil_ELV_Value DEFAULT 0,   -- 70 (%) or 3500 (amount)
    Notes NVARCHAR(300) NULL,
    VerifiedBy UNIQUEIDENTIFIER NULL,
    VerifiedAt DATETIME2(3) NOT NULL CONSTRAINT DF_bil_ELV_At DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_bil_ELV PRIMARY KEY CLUSTERED (VerificationPkId),
    CONSTRAINT UQ_bil_ELV_Id UNIQUE NONCLUSTERED (VerificationId),
    CONSTRAINT FK_bil_ELV_CoverageId FOREIGN KEY (CoverageId) REFERENCES opd.PatientCoverage(CoverageId),
    CONSTRAINT FK_bil_ELV_InvoiceId FOREIGN KEY (InvoiceId) REFERENCES bil.Invoice(InvoiceId),
    CONSTRAINT FK_bil_ELV_VerifiedBy FOREIGN KEY (VerifiedBy) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_bil_ELV_CoverageId NONCLUSTERED (CoverageId, VerifiedAt)
);

-- A verified coverage applied to an invoice. Liability = ApprovedValue % of the invoice total
-- (re-derived when charges change) or the fixed amount, capped so Σ liabilities ≤ TotalAmount.
CREATE TABLE bil.InvoiceCoverageLine (
    CoverageLinePkId BIGINT IDENTITY(1,1) NOT NULL,
    CoverageLineId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_ICL_Id DEFAULT NEWSEQUENTIALID(),
    InvoiceId UNIQUEIDENTIFIER NOT NULL, CoverageId UNIQUEIDENTIFIER NOT NULL, PayerId UNIQUEIDENTIFIER NOT NULL,
    CoverageType NVARCHAR(20) NOT NULL CONSTRAINT CK_bil_ICL_Type CHECK (CoverageType IN ('PRIVATE_INSURANCE','GOVT_SCHEME')),
    Priority INT NOT NULL CONSTRAINT DF_bil_ICL_Priority DEFAULT 1,
    AuthorizationNumber NVARCHAR(50) NULL,
    ApprovedType NVARCHAR(10) NOT NULL CONSTRAINT CK_bil_ICL_AppType CHECK (ApprovedType IN ('PERCENT','AMOUNT')),
    ApprovedValue DECIMAL(10,2) NOT NULL,
    IsManualAmount BIT NOT NULL CONSTRAINT DF_bil_ICL_Manual DEFAULT 0,   -- liability overridden by staff
    Liability DECIMAL(10,2) NOT NULL CONSTRAINT DF_bil_ICL_Liab DEFAULT 0,
    SettledAmount DECIMAL(10,2) NOT NULL CONSTRAINT DF_bil_ICL_Settled DEFAULT 0,  -- Σ payer settlements (bil.Payment.CoverageLineId)
    Status NVARCHAR(10) NOT NULL CONSTRAINT CK_bil_ICL_Status CHECK (Status IN ('VERIFIED')) CONSTRAINT DF_bil_ICL_Status DEFAULT 'VERIFIED',
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_bil_ICL_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL, ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_bil_ICL PRIMARY KEY CLUSTERED (CoverageLinePkId),
    CONSTRAINT UQ_bil_ICL_Id UNIQUE NONCLUSTERED (CoverageLineId),
    CONSTRAINT UQ_bil_ICL_Inv_Cov UNIQUE (InvoiceId, CoverageId),
    CONSTRAINT FK_bil_ICL_InvoiceId FOREIGN KEY (InvoiceId) REFERENCES bil.Invoice(InvoiceId),
    CONSTRAINT FK_bil_ICL_CoverageId FOREIGN KEY (CoverageId) REFERENCES opd.PatientCoverage(CoverageId),
    CONSTRAINT FK_bil_ICL_PayerId FOREIGN KEY (PayerId) REFERENCES cfg.Payer(PayerId),
    INDEX IX_bil_ICL_InvoiceId NONCLUSTERED (InvoiceId)
);

CREATE TABLE bil.Payment (
    PaymentPkId BIGINT IDENTITY(1,1) NOT NULL,
    PaymentId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_Pay_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, InvoiceId UNIQUEIDENTIFIER NOT NULL,
    TransactionNumber NVARCHAR(30) NOT NULL,
    ComponentType NVARCHAR(15) NULL CONSTRAINT CK_bil_Pay_Comp CHECK (ComponentType IN ('PATIENT','INSURANCE',NULL)),
    CoverageLineId UNIQUEIDENTIFIER NULL,          -- INSURANCE settlements: the claim (coverage line) being settled
    PaymentMethod NVARCHAR(30) NOT NULL,
    Amount DECIMAL(10,2) NOT NULL,
    Reference NVARCHAR(100) NULL,
    PaidAt DATETIME2(3) NOT NULL CONSTRAINT DF_bil_Pay_PaidAt DEFAULT SYSUTCDATETIME(),
    ReceivedBy UNIQUEIDENTIFIER NULL,
    IsVoided BIT NOT NULL CONSTRAINT DF_bil_Pay_Voided DEFAULT 0,
    VoidedAt DATETIME2(3) NULL,
    VoidedBy UNIQUEIDENTIFIER NULL,
    VoidReason NVARCHAR(300) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_bil_Pay_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_bil_Pay_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_bil_Pay_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_bil_Pay PRIMARY KEY CLUSTERED (PaymentPkId),
    CONSTRAINT UQ_bil_Pay_Id UNIQUE NONCLUSTERED (PaymentId),
    CONSTRAINT FK_bil_Pay_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_bil_Pay_InvoiceId FOREIGN KEY (InvoiceId) REFERENCES bil.Invoice(InvoiceId),
    CONSTRAINT FK_bil_Pay_CoverageLineId FOREIGN KEY (CoverageLineId) REFERENCES bil.InvoiceCoverageLine(CoverageLineId),
    CONSTRAINT CK_bil_Pay_CovLine CHECK (CoverageLineId IS NULL OR ComponentType = 'INSURANCE'),
    INDEX IX_bil_Pay_InvoiceId NONCLUSTERED (InvoiceId),
    INDEX IX_bil_Pay_HospitalId NONCLUSTERED (HospitalId),
    INDEX IX_bil_Pay_PaidAt NONCLUSTERED (PaidAt DESC)
);

CREATE TABLE bil.Receipt (
    ReceiptPkId BIGINT IDENTITY(1,1) NOT NULL,
    ReceiptId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_Rcp_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ReceiptNumber NVARCHAR(30) NOT NULL,
    PaymentId UNIQUEIDENTIFIER NULL, InvoiceId UNIQUEIDENTIFIER NOT NULL,
    PatientId UNIQUEIDENTIFIER NULL,
    TotalAmount DECIMAL(10,2) NOT NULL,
    Currency NVARCHAR(5) NOT NULL CONSTRAINT DF_bil_Rcp_Curr DEFAULT 'USD',
    GeneratedAt DATETIME2(3) NOT NULL CONSTRAINT DF_bil_Rcp_GenAt DEFAULT SYSUTCDATETIME(),
    IsActive BIT NOT NULL CONSTRAINT DF_bil_Rcp_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_bil_Rcp_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_bil_Rcp_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_bil_Rcp PRIMARY KEY CLUSTERED (ReceiptPkId),
    CONSTRAINT UQ_bil_Rcp_Id UNIQUE NONCLUSTERED (ReceiptId),
    CONSTRAINT FK_bil_Rcp_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_bil_Rcp_InvoiceId FOREIGN KEY (InvoiceId) REFERENCES bil.Invoice(InvoiceId),
    CONSTRAINT FK_bil_Rcp_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT UQ_bil_Rcp_Hosp_Number UNIQUE (HospitalId, ReceiptNumber),
    INDEX IX_bil_Rcp_HospitalId NONCLUSTERED (HospitalId)
);

CREATE TABLE bil.CreditNote (
    CreditNotePkId BIGINT IDENTITY(1,1) NOT NULL,
    CreditNoteId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_CN_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, CreditNoteNumber NVARCHAR(30) NOT NULL,
    InvoiceId UNIQUEIDENTIFIER NOT NULL,
    Amount DECIMAL(10,2) NOT NULL, Reason NVARCHAR(300) NOT NULL,
    IssuedBy UNIQUEIDENTIFIER NOT NULL,
    IssuedAt DATETIME2(3) NOT NULL CONSTRAINT DF_bil_CN_IssuedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_bil_CN PRIMARY KEY CLUSTERED (CreditNotePkId),
    CONSTRAINT UQ_bil_CN_Id UNIQUE NONCLUSTERED (CreditNoteId),
    CONSTRAINT FK_bil_CN_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_bil_CN_InvoiceId FOREIGN KEY (InvoiceId) REFERENCES bil.Invoice(InvoiceId),
    CONSTRAINT UQ_bil_CN_Hosp_Number UNIQUE (HospitalId, CreditNoteNumber),
    INDEX IX_bil_CN_InvoiceId NONCLUSTERED (InvoiceId)
);

-- ============================================================
-- F. msg — MESSAGING (2 tables)
-- ============================================================

CREATE TABLE msg.NotificationConfig (
    NotificationConfigPkId BIGINT IDENTITY(1,1) NOT NULL,
    NotificationConfigId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_msg_NC_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    Channel NVARCHAR(10) NOT NULL CONSTRAINT CK_msg_NC_Ch CHECK (Channel IN ('WHATSAPP','EMAIL')),
    Provider NVARCHAR(20) NULL,
    ApiKey NVARCHAR(500) NULL,                              -- ENCRYPTION: RANDOMIZED
    ConfigJson NVARCHAR(MAX) NULL,
    IsEnabled BIT NOT NULL CONSTRAINT DF_msg_NC_Enabled DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_msg_NC_Active DEFAULT 1,
    IsDeleted BIT NOT NULL CONSTRAINT DF_msg_NC_Deleted DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_msg_NC_CreatedAt DEFAULT SYSUTCDATETIME(),
    ModifiedAt DATETIME2(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_msg_NC PRIMARY KEY CLUSTERED (NotificationConfigPkId),
    CONSTRAINT UQ_msg_NC_Id UNIQUE NONCLUSTERED (NotificationConfigId),
    CONSTRAINT FK_msg_NC_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT UQ_msg_NC_Hosp_Ch UNIQUE (HospitalId, Channel),
    INDEX IX_msg_NC_HospitalId NONCLUSTERED (HospitalId)
);

CREATE TABLE msg.MessageQueue (
    MessageQueuePkId BIGINT IDENTITY(1,1) NOT NULL,
    MessageQueueId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_msg_MQ_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NULL,
    Channel NVARCHAR(10) NOT NULL, TemplateName NVARCHAR(100) NULL,
    PatientName NVARCHAR(200) NULL,                         -- ENCRYPTION: RANDOMIZED
    PatientPhone NVARCHAR(20) NULL,                         -- ENCRYPTION: RANDOMIZED
    MessageText NVARCHAR(MAX) NULL,                         -- ENCRYPTION: RANDOMIZED
    Status NVARCHAR(15) NOT NULL CONSTRAINT CK_msg_MQ_Status CHECK (Status IN ('QUEUED','SENDING','SENT','FAILED','SIMULATED')) CONSTRAINT DF_msg_MQ_Status DEFAULT 'QUEUED',
    QueuedAt DATETIME2(3) NOT NULL CONSTRAINT DF_msg_MQ_QueuedAt DEFAULT SYSUTCDATETIME(),
    SentAt DATETIME2(3) NULL, ErrorMessage NVARCHAR(500) NULL,
    CONSTRAINT PK_msg_MQ PRIMARY KEY CLUSTERED (MessageQueuePkId),
    CONSTRAINT UQ_msg_MQ_Id UNIQUE NONCLUSTERED (MessageQueueId),
    CONSTRAINT FK_msg_MQ_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_msg_MQ_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    INDEX IX_msg_MQ_HospitalId NONCLUSTERED (HospitalId),
    INDEX IX_msg_MQ_Status NONCLUSTERED (Status),
    INDEX IX_msg_MQ_QueuedAt NONCLUSTERED (QueuedAt DESC)
);

-- ============================================================
-- SUMMARY
-- ============================================================
-- cfg: 17 | sec: 5 | opd: 8 | cln: 14 | bil: 9 | msg: 2 = 55 tables
-- Dual-key: BIGINT IDENTITY clustered + UNIQUEIDENTIFIER NEWSEQUENTIALID() external
-- All FKs reference the UNIQUEIDENTIFIER column
-- Temporal: Patient, Appointment, Consultation, Prescription, Invoice, ApplicationUser
-- 423+ named constraints, 73 FKs, 61+ indexes
-- Encryption: 19 PII columns annotated
-- AuditLog: SessionToken + Browser columns for forensic attribution
-- Billing: SubTotal, DiscountAmount, TaxAmount on Invoice; ComponentType, IsVoided on Payment
-- Coverage split: cfg.Payer, opd.PatientCoverage, bil.EligibilityVerification, bil.InvoiceCoverageLine;
--   Invoice.CoveredAmount / PatientLiability / PatientPaidAmount; Payment.CoverageLineId (payer settlement)
