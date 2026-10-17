/* ============================================================
   Consult360 — SQL Server 2019 Schema
   Version: 4.1 (2026-10-17: + sec.LabClaimAttempt; 4.0 in sync with the application models, 2026-10-15)

   Source of truth: the C# models in the Models folder, held today by the in-memory
   Services/DataStore.cs (one list per table). Column names follow the model
   property names, so a row maps onto its model without renaming.

   Key pattern:
     Id            BIGINT IDENTITY(1,1)              — clustered PK, internal (INT on small reference tables)
     {Model}Id     UNIQUEIDENTIFIER NEWSEQUENTIALID()  — external, API-facing (the model's Id property)

   State: IsActive (business state and soft delete; DEFAULT 1, soft delete sets 0)
   Time:  every point in time is DATETIMEOFFSET holding UTC (+00:00) — system events (CreatedAt, ModifiedAt, PaidAt,
          SignedAt …) are shown in the viewing user's own timezone. Business events tied to a clinic (opd.Appointment
          SlotStart/SlotEnd, cln.Consultation VisitDate) also carry TimeZoneId (IANA, e.g. America/Jamaica) and are
          shown in that timezone wherever the user is. DATE columns (ExpiryDate, LeaveDate, FollowupDate,
          AppointmentDate, VisitLocalDate …) are calendar dates in the hospital's timezone. Wall-clock settings
          (opd.DoctorSlotConfig StartTime/EndTime) are hospital-local. cfg.TimeZone maps IANA ids to the Windows
          names SQL Server's AT TIME ZONE needs.
   Audit (every table): CreatedAt NOT NULL, CreatedBy NOT NULL, ModifiedAt NULL, ModifiedBy NULL
          CreatedBy defaults to the System user 00000000-0000-0000-0000-000000000000 for rows with no acting user
   Row-level change audit (sec.AuditConfig, sec.DataAuditLog, generated triggers): audit-log.sql, run after this file
   Tenancy: every hospital-owned row carries HospitalId, even where the model reaches it through a parent
   List / nested model properties are stored as JSON (ISJSON-checked) or in a child table — noted per column

   Schemas: cfg(23), sec(7), opd(13), cln(18), bil(9), inv(4), msg(2) = 76 application tables
            + 5 reserved tables (section G) for planned features the application does not use yet
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
CREATE SCHEMA inv AUTHORIZATION dbo;
GO
CREATE SCHEMA msg AUTHORIZATION dbo;
GO

-- ============================================================
-- A. cfg — CONFIGURATION & REFERENCE DATA (23 tables)
-- ============================================================

-- Model: TimeZone (reference). IANA id (what the application, browsers and APIs use) → Windows id (what
-- SQL Server's AT TIME ZONE accepts; must exist in sys.time_zone_info). Natural key: the IANA id.
CREATE TABLE cfg.TimeZone (
    Id INT IDENTITY(1,1) NOT NULL,
    TimeZoneId NVARCHAR(50) NOT NULL,                       -- IANA, e.g. America/Jamaica
    WindowsTimeZoneId NVARCHAR(100) NOT NULL,               -- e.g. SA Pacific Standard Time
    DisplayName NVARCHAR(100) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_TZ_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_TZ_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_TZ_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_TZ PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_TZ_Id UNIQUE NONCLUSTERED (TimeZoneId)
);
INSERT INTO cfg.TimeZone (TimeZoneId, WindowsTimeZoneId, DisplayName) VALUES
    ('America/Jamaica',     'SA Pacific Standard Time', 'Jamaica (UTC−05:00, no daylight saving)'),
    ('America/New_York',    'Eastern Standard Time',    'US Eastern (New York)'),
    ('America/Chicago',     'Central Standard Time',    'US Central (Chicago)'),
    ('America/Denver',      'Mountain Standard Time',   'US Mountain (Denver)'),
    ('America/Los_Angeles', 'Pacific Standard Time',    'US Pacific (Los Angeles)'),
    ('America/Toronto',     'Eastern Standard Time',    'Canada Eastern (Toronto)'),
    ('Europe/London',       'GMT Standard Time',        'United Kingdom (London)'),
    ('Asia/Kolkata',        'India Standard Time',      'India (UTC+05:30)'),
    ('UTC',                 'UTC',                      'UTC');
GO

-- Model: Hospital. Multi-tenant root; country-specific behaviour is driven by these settings.
CREATE TABLE cfg.Hospital (
    Id            BIGINT IDENTITY(1,1) NOT NULL,
    HospitalId              UNIQUEIDENTIFIER    NOT NULL    CONSTRAINT DF_cfg_Hospital_Id DEFAULT NEWSEQUENTIALID(),
    Name                    NVARCHAR(200)       NOT NULL,
    LegalAddress            NVARCHAR(500)       NULL,
    ContactNumber           NVARCHAR(20)        NULL,       -- ENCRYPTION: DETERMINISTIC
    PrimaryEmail            NVARCHAR(100)       NULL,       -- ENCRYPTION: DETERMINISTIC
    Country                 NVARCHAR(5)         NOT NULL    CONSTRAINT DF_cfg_Hospital_Country DEFAULT 'XX',
    Currency                NVARCHAR(5)         NOT NULL    CONSTRAINT DF_cfg_Hospital_Currency DEFAULT 'USD',
    Timezone                NVARCHAR(50)        NOT NULL    CONSTRAINT DF_cfg_Hospital_Tz DEFAULT 'America/Jamaica',  -- cfg.TimeZone (IANA)
    LicenseNumber           NVARCHAR(50)        NULL,
    LogoBase64              NVARCHAR(MAX)       NULL,
    PatientIdLabel          NVARCHAR(30)        NOT NULL    CONSTRAINT DF_cfg_Hospital_PidLbl DEFAULT 'MRN',
    NationalHealthIdLabel   NVARCHAR(50)        NOT NULL    CONSTRAINT DF_cfg_Hospital_NhidLbl DEFAULT 'National Health ID',
    NationalHealthIdMandatory BIT               NOT NULL    CONSTRAINT DF_cfg_Hospital_NhidMand DEFAULT 0,
    GrowthChartStandard     NVARCHAR(5)         NOT NULL    CONSTRAINT DF_cfg_Hospital_Growth DEFAULT 'WHO'
                            CONSTRAINT CK_cfg_Hospital_Growth CHECK (GrowthChartStandard IN ('WHO','CDC')),
    DefaultTempUnit         NVARCHAR(1)         NOT NULL    CONSTRAINT DF_cfg_Hospital_TempUnit DEFAULT 'F'
                            CONSTRAINT CK_cfg_Hospital_TempUnit CHECK (DefaultTempUnit IN ('F','C')),
    DefaultRxValidityDays   INT                 NOT NULL    CONSTRAINT DF_cfg_Hospital_RxVal DEFAULT 30,
    RxPrefix                NVARCHAR(10)        NULL,       -- document number prefixes
    ReceiptPrefix           NVARCHAR(10)        NULL,
    InvoicePrefix           NVARCHAR(10)        NULL,
    LabChargePerTest        DECIMAL(10,2)       NOT NULL    CONSTRAINT DF_cfg_Hospital_LabCharge DEFAULT 0,
    IsActive                BIT                 NOT NULL    CONSTRAINT DF_cfg_Hospital_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_Hospital_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_Hospital_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_Hospital PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_Hospital_Id UNIQUE NONCLUSTERED (HospitalId),
    CONSTRAINT FK_cfg_Hospital_Tz FOREIGN KEY (Timezone) REFERENCES cfg.TimeZone(TimeZoneId)
);

-- Model: HospitalSetting. Key/value hospital configuration (Schedule Settings + Settings → Hospital & Practice).
-- Keys: APPT_SLOT_MINS*, APPT_BUFFER_MINS, FREE_FOLLOWUP_ALLOWED*, FREE_FOLLOWUP_DAYS*,
-- DATE_FORMAT, TIME_FORMAT, MRN_PREFIX, MRN_FORMAT, MRN_SEQ (system-managed),
-- DOCTOR_NAME_TEMPLATE.  (* = can be overridden per doctor in opd.DoctorSetting)
CREATE TABLE cfg.HospitalSetting (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    HospitalId      UNIQUEIDENTIFIER    NOT NULL,
    SettingKey      NVARCHAR(40)        NOT NULL,
    [Value]         NVARCHAR(200)       NULL,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_HSet_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_HSet_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_HSet PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_HSet_Key UNIQUE NONCLUSTERED (HospitalId, SettingKey),
    CONSTRAINT FK_cfg_HSet_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId)
);

-- Model: AppointmentType. opd.Appointment.[Type] stores Code.
-- DurationMinutes NULL = the doctor's slot length; FeeCategory picks the
-- bil.DoctorFeeSchedule row; IsChargeable = 0 → no consultation fee.
CREATE TABLE cfg.AppointmentType (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    AppointmentTypeId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_AT_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    Code NVARCHAR(20) NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    DurationMinutes INT NULL CONSTRAINT CK_cfg_AT_Dur CHECK (DurationMinutes IS NULL OR DurationMinutes >= 5),
    Color NVARCHAR(7) NOT NULL CONSTRAINT DF_cfg_AT_Color DEFAULT '#0f766e',
    IsChargeable BIT NOT NULL CONSTRAINT DF_cfg_AT_Charge DEFAULT 1,
    FeeCategory NVARCHAR(10) NOT NULL CONSTRAINT DF_cfg_AT_FeeCat DEFAULT 'NEW'
        CONSTRAINT CK_cfg_AT_FeeCat CHECK (FeeCategory IN ('NEW','FOLLOWUP')),
    SortOrder INT NOT NULL CONSTRAINT DF_cfg_AT_Sort DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_AT_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_AT_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_AT_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_AT PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_AT_Id UNIQUE NONCLUSTERED (AppointmentTypeId),
    CONSTRAINT UQ_cfg_AT_Code UNIQUE NONCLUSTERED (HospitalId, Code),
    CONSTRAINT FK_cfg_AT_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId)
);

-- Model: Payer. Insurers (PRIVATE) and government schemes (GOVT_SCHEME: CGHS / Aarogyasri, NHF, Medicare …), per country.
-- Used by the coverage / N-way bill split (Charge = Σ coverage liabilities + Patient).
CREATE TABLE cfg.Payer (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    PayerId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_Payer_Id DEFAULT NEWSEQUENTIALID(),
    Name NVARCHAR(150) NOT NULL,
    Category NVARCHAR(15) NOT NULL CONSTRAINT CK_cfg_Payer_Cat CHECK (Category IN ('PRIVATE','GOVT_SCHEME')),
    Country NVARCHAR(5) NOT NULL,                 -- IN, JM, US … (cfg.Hospital.Country)
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_Payer_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_Payer_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_Payer_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_Payer PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_Payer_Id UNIQUE NONCLUSTERED (PayerId),
    CONSTRAINT UQ_cfg_Payer_Country_Name UNIQUE (Country, Name),
    INDEX IX_cfg_Payer_Country NONCLUSTERED (Country, Category)
);

-- Model: IntakeQuestion. Pre-visit intake form questions (answered on online booking).
CREATE TABLE cfg.IntakeQuestion (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    QuestionId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_IQ_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    Label NVARCHAR(200) NOT NULL,
    QuestionType NVARCHAR(10) NOT NULL CONSTRAINT CK_cfg_IQ_Type CHECK (QuestionType IN ('TEXT','NUMBER','CHOICE')),
    Options NVARCHAR(500) NULL,            -- comma-separated, CHOICE only
    SortOrder INT NOT NULL CONSTRAINT DF_cfg_IQ_Sort DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_IQ_Active DEFAULT 1,   -- soft delete keeps old answers readable
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_IQ_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_IQ_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_IQ PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_IQ_Id UNIQUE NONCLUSTERED (QuestionId),
    CONSTRAINT FK_cfg_IQ_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId)
);

-- Model: ConsentForm. Consent text signed at the visit; Version bumps on every edit.
CREATE TABLE cfg.ConsentForm (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    ConsentFormId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_CF_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    Title NVARCHAR(200) NOT NULL,
    BodyText NVARCHAR(MAX) NOT NULL,
    Version INT NOT NULL CONSTRAINT DF_cfg_CF_Ver DEFAULT 1,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_CF_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_CF_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_CF_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_CF PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_CF_Id UNIQUE NONCLUSTERED (ConsentFormId),
    CONSTRAINT FK_cfg_CF_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId)
);

-- Model: Compliance. Registration types a hospital must / may record, per country ('ALL' = every country).
CREATE TABLE cfg.Compliance (
    Id          BIGINT IDENTITY(1,1) NOT NULL,
    ComplianceId            UNIQUEIDENTIFIER    NOT NULL    CONSTRAINT DF_cfg_Comp_Id DEFAULT NEWSEQUENTIALID(),
    ComplianceName          NVARCHAR(50)        NOT NULL,
    Country                 NVARCHAR(5)         NOT NULL    CONSTRAINT DF_cfg_Comp_Country DEFAULT 'ALL',
    IsMandatory             BIT                 NOT NULL    CONSTRAINT DF_cfg_Comp_Mand DEFAULT 0,
    DisplayLabel            NVARCHAR(100)       NOT NULL,
    PrintLabel              NVARCHAR(30)        NOT NULL,
    SortOrder               INT                 NOT NULL    CONSTRAINT DF_cfg_Comp_Sort DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_Comp_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_Comp_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_Comp_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_Comp PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_Comp_Id UNIQUE NONCLUSTERED (ComplianceId)
);

-- Model: HospitalCompliance. A hospital's registration number for one compliance type.
CREATE TABLE cfg.HospitalCompliance (
    Id  BIGINT IDENTITY(1,1) NOT NULL,
    HospitalComplianceId    UNIQUEIDENTIFIER    NOT NULL    CONSTRAINT DF_cfg_HC_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId              UNIQUEIDENTIFIER    NOT NULL,
    ComplianceId            UNIQUEIDENTIFIER    NOT NULL,
    RegistrationNumber      NVARCHAR(100)       NOT NULL,
    IssuedDate              DATE                NULL,
    ExpiryDate              DATE                NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_HC_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_HC_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_HC_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_HC PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_HC_Id UNIQUE NONCLUSTERED (HospitalComplianceId),
    CONSTRAINT FK_cfg_HC_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cfg_HC_ComplianceId FOREIGN KEY (ComplianceId) REFERENCES cfg.Compliance(ComplianceId),
    CONSTRAINT UQ_cfg_HC_Hosp_Comp UNIQUE (HospitalId, ComplianceId),
    INDEX IX_cfg_HC_HospitalId NONCLUSTERED (HospitalId)
);

-- Model: Specialty.
CREATE TABLE cfg.Specialty (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    SpecialtyId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_Spec_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    SpecialtyName NVARCHAR(100) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_Spec_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_Spec_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_Spec_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_Spec PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_Spec_Id UNIQUE NONCLUSTERED (SpecialtyId),
    CONSTRAINT FK_cfg_Spec_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    INDEX IX_cfg_Spec_HospitalId NONCLUSTERED (HospitalId)
);

-- Model: VitalConfig. The hospital's vitals list. Built-in vitals have fixed ranges in
-- ConsultationService.VitalRanges; custom vitals (IsCustom = 1) carry their own Min/Max.
CREATE TABLE cfg.VitalDefinition (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    VitalDefinitionId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_VD_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    VitalCode NVARCHAR(40) NOT NULL,
    DisplayLabel NVARCHAR(50) NOT NULL,
    AllowedUnits NVARCHAR(20) NULL,
    MinValue DECIMAL(10,2) NULL, MaxValue DECIMAL(10,2) NULL,
    IsCustom BIT NOT NULL CONSTRAINT DF_cfg_VD_Custom DEFAULT 0,   -- added by the hospital in Consultation Setup → Vitals
    IsPediatricOnly BIT NOT NULL CONSTRAINT DF_cfg_VD_Peds DEFAULT 0,
    SortOrder INT NOT NULL CONSTRAINT DF_cfg_VD_Sort DEFAULT 0,
    IsEnabled BIT NOT NULL CONSTRAINT DF_cfg_VD_Enabled DEFAULT 1,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_VD_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_VD_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_VD_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_VD PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_VD_Id UNIQUE NONCLUSTERED (VitalDefinitionId),
    CONSTRAINT UQ_cfg_VD_Hosp_Code UNIQUE (HospitalId, VitalCode),
    CONSTRAINT FK_cfg_VD_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    INDEX IX_cfg_VD_HospitalId NONCLUSTERED (HospitalId)
);

-- Model: MedicalHistoryItem. Chronic conditions offered on the Health Record (quick picks first).
CREATE TABLE cfg.MedicalHistoryItem (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    ItemId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_MHI_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    ItemName NVARCHAR(100) NOT NULL,
    IsQuickPick BIT NOT NULL CONSTRAINT DF_cfg_MHI_Quick DEFAULT 0,
    SortOrder INT NOT NULL CONSTRAINT DF_cfg_MHI_Sort DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_MHI_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_MHI_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_MHI_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_MHI PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_MHI_Id UNIQUE NONCLUSTERED (ItemId),
    CONSTRAINT FK_cfg_MHI_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    INDEX IX_cfg_MHI_HospitalId NONCLUSTERED (HospitalId)
);

-- Model: MedicalEventItem. Quick-pick medical events (surgeries, hospitalisations …).
CREATE TABLE cfg.MedicalEventType (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    EventId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_MET_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    EventName NVARCHAR(100) NOT NULL,
    SortOrder INT NOT NULL CONSTRAINT DF_cfg_MET_Sort DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_MET_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_MET_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_MET_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_MET PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_MET_Id UNIQUE NONCLUSTERED (EventId),
    CONSTRAINT FK_cfg_MET_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    INDEX IX_cfg_MET_HospitalId NONCLUSTERED (HospitalId)
);

-- Model: SymptomCatalogItem. Complaints offered in the consultation; cln.ConsultationSymptom stores SymptomCode.
CREATE TABLE cfg.SymptomCatalog (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    SymptomCatalogId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_Sym_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    SymptomCode NVARCHAR(40) NOT NULL,
    DisplayLabel NVARCHAR(100) NOT NULL,
    IsQuickPick BIT NOT NULL CONSTRAINT DF_cfg_Sym_Quick DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_Sym_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_Sym_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_Sym_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_Sym PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_Sym_Id UNIQUE NONCLUSTERED (SymptomCatalogId),
    CONSTRAINT UQ_cfg_Sym_Hosp_Code UNIQUE (HospitalId, SymptomCode),
    CONSTRAINT FK_cfg_Sym_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId)
);

-- Model: Icd10Entry. Global (not per hospital).
CREATE TABLE cfg.ICD10Catalog (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    Icd10CatalogId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_ICD_Id DEFAULT NEWSEQUENTIALID(),
    Icd10Code NVARCHAR(10) NOT NULL,
    [Description] NVARCHAR(300) NOT NULL,
    SynonymTerms NVARCHAR(500) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_ICD_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_ICD_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_ICD_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_ICD PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_ICD_Id UNIQUE NONCLUSTERED (Icd10CatalogId),
    CONSTRAINT UQ_cfg_ICD_Code UNIQUE (Icd10Code)
);

-- Model: Drug. The hospital's formulary.
CREATE TABLE cfg.DrugFormulary (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    DrugId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_Drug_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    BrandName NVARCHAR(200) NOT NULL,
    GenericName NVARCHAR(200) NULL,
    Composition NVARCHAR(500) NULL,
    TherapeuticClass NVARCHAR(300) NULL,
    GenericClass NVARCHAR(200) NULL,
    DrugForm NVARCHAR(30) NULL,
    DefaultRoute NVARCHAR(30) NULL,
    DefaultDosage NVARCHAR(20) NULL,
    DefaultFrequency NVARCHAR(20) NULL,
    DefaultDurationDays INT NULL,
    DefaultInstructions NVARCHAR(300) NULL,
    IsControlledSubstance BIT NOT NULL CONSTRAINT DF_cfg_Drug_Ctrl DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_Drug_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_Drug_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_Drug_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_Drug PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_Drug_Id UNIQUE NONCLUSTERED (DrugId),
    CONSTRAINT FK_cfg_Drug_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    INDEX IX_cfg_Drug_BrandName NONCLUSTERED (HospitalId, BrandName)
);

-- Model: LabPartner. External lab / imaging centres. sec.ApplicationUser.LabPartnerId links LAB_PARTNER
-- accounts (several staff per partner) to one row here.
CREATE TABLE cfg.LabPartner (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    LabPartnerId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_LP_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_LP_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_LP_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_LP_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_LP PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_LP_Id UNIQUE NONCLUSTERED (LabPartnerId),
    CONSTRAINT FK_cfg_LP_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT UQ_cfg_LP_Hosp_Name UNIQUE (HospitalId, Name)
);

-- Model: LabTest. Orderable lab / imaging tests. LabTest.SpecialtyIds → cfg.LabTestSpecialty.
CREATE TABLE cfg.LabTestCatalog (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    TestId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_LT_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    TestName NVARCHAR(100) NOT NULL,
    TestType NVARCHAR(20) NOT NULL CONSTRAINT CK_cfg_LT_Type CHECK (TestType IN ('LAB','IMAGING','PATHOLOGY')),
    Category NVARCHAR(50) NULL,            -- Hematology, Biochemistry, Radiology …
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_LT_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_LT_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_LT_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_LT PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_LT_Id UNIQUE NONCLUSTERED (TestId),
    CONSTRAINT FK_cfg_LT_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    INDEX IX_cfg_LT_HospitalId NONCLUSTERED (HospitalId)
);

-- Model: LabTest.SpecialtyIds — the specialties a test is suggested for.
CREATE TABLE cfg.LabTestSpecialty (
    TestId UNIQUEIDENTIFIER NOT NULL, SpecialtyId UNIQUEIDENTIFIER NOT NULL,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_LTS_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_LTS_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_LTS PRIMARY KEY CLUSTERED (TestId, SpecialtyId),
    CONSTRAINT FK_cfg_LTS_TestId FOREIGN KEY (TestId) REFERENCES cfg.LabTestCatalog(TestId),
    CONSTRAINT FK_cfg_LTS_SpecialtyId FOREIGN KEY (SpecialtyId) REFERENCES cfg.Specialty(SpecialtyId)
);

-- Model: ClinicalCondition. Groups phrases ("/" macros) by condition. Matches a consultation when one of
-- its diagnoses' ICD-10 codes starts with a prefix in Icd10Prefixes.
CREATE TABLE cfg.ClinicalCondition (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    ConditionId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_CC_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    Icd10Prefixes NVARCHAR(200) NULL,      -- comma-separated, e.g. 'I10,I11'
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_CC_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_CC_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_CC_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_CC PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_CC_Id UNIQUE NONCLUSTERED (ConditionId),
    CONSTRAINT FK_cfg_CC_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId)
);

-- Model: Phrase. SOAP phrase / "/" macro. Precedence DOCTOR > SPECIALTY > GLOBAL.
CREATE TABLE cfg.PhraseBank (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    PhraseId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_PB_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    Scope NVARCHAR(10) NOT NULL CONSTRAINT CK_cfg_PB_Scope CHECK (Scope IN ('GLOBAL','SPECIALTY','DOCTOR')),
    SpecialtyId UNIQUEIDENTIFIER NULL,     -- SPECIALTY scope
    DoctorId UNIQUEIDENTIFIER NULL,        -- DOCTOR scope
    ConditionId UNIQUEIDENTIFIER NULL,     -- NULL = any condition
    Section NVARCHAR(5) NOT NULL CONSTRAINT CK_cfg_PB_Section CHECK (Section IN ('S','O','P','ANY')) CONSTRAINT DF_cfg_PB_Section DEFAULT 'ANY',
    TriggerKeyword NVARCHAR(30) NULL,
    PhraseText NVARCHAR(500) NOT NULL,
    SortOrder INT NOT NULL CONSTRAINT DF_cfg_PB_Sort DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_PB_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_PB_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_PB_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_PB PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_PB_Id UNIQUE NONCLUSTERED (PhraseId),
    CONSTRAINT FK_cfg_PB_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cfg_PB_SpecialtyId FOREIGN KEY (SpecialtyId) REFERENCES cfg.Specialty(SpecialtyId),
    CONSTRAINT FK_cfg_PB_ConditionId FOREIGN KEY (ConditionId) REFERENCES cfg.ClinicalCondition(ConditionId),
    CONSTRAINT CK_cfg_PB_ScopeTarget CHECK ((Scope <> 'SPECIALTY' OR SpecialtyId IS NOT NULL) AND (Scope <> 'DOCTOR' OR DoctorId IS NOT NULL)),
    INDEX IX_cfg_PB_HospitalId NONCLUSTERED (HospitalId)
    -- FK DoctorId → sec.ApplicationUser(UserId) is added after sec is created (see end of section B)
);

-- Model: PaymentMethod.
CREATE TABLE cfg.PaymentMethod (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    PaymentMethodId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_PM_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    MethodName NVARCHAR(30) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_PM_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_PM_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_PM_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_PM PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_PM_Id UNIQUE NONCLUSTERED (PaymentMethodId),
    CONSTRAINT FK_cfg_PM_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    INDEX IX_cfg_PM_HospitalId NONCLUSTERED (HospitalId)
);

-- Model: MessageTemplate. [Trigger]: APPOINTMENT_REMINDER, FOLLOWUP_REMINDER, LAB_RESULTS_READY, NO_SHOW, PRESCRIPTION_READY …
CREATE TABLE cfg.MessageTemplate (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    TemplateId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_MT_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    [Trigger] NVARCHAR(30) NOT NULL,
    Channel NVARCHAR(10) NOT NULL CONSTRAINT CK_cfg_MT_Ch CHECK (Channel IN ('WHATSAPP','EMAIL','BOTH')),
    [Text] NVARCHAR(1000) NOT NULL,
    IsEnabled BIT NOT NULL CONSTRAINT DF_cfg_MT_Enabled DEFAULT 1,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_MT_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_MT_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_MT_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_MT PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_MT_Id UNIQUE NONCLUSTERED (TemplateId),
    CONSTRAINT FK_cfg_MT_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    INDEX IX_cfg_MT_HospitalId NONCLUSTERED (HospitalId)
);

-- Model: ConsultFormConfig (DataStore.ConsultFormPublished / ConsultFormDrafts).
-- Which sections, fields and option values the Consultation 360 page shows, per hospital. The configuration
-- page edits the DRAFT row; only the PUBLISHED row drives the consultation. The nested model
-- (Sections → Fields → Values, VitalUnits → Units; array position = order) is stored as JSON.
CREATE TABLE cfg.ConsultationFormConfig (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    ConfigState NVARCHAR(10) NOT NULL CONSTRAINT CK_cfg_CFC_State CHECK (ConfigState IN ('PUBLISHED','DRAFT')),
    Version INT NOT NULL CONSTRAINT DF_cfg_CFC_Ver DEFAULT 1,
    PublishedAt DATETIMEOFFSET(3) NULL,
    PublishedBy UNIQUEIDENTIFIER NULL,
    Sections NVARCHAR(MAX) NOT NULL CONSTRAINT CK_cfg_CFC_Sections CHECK (ISJSON(Sections) = 1),     -- List<ConsultFormSection>
    VitalUnits NVARCHAR(MAX) NULL CONSTRAINT CK_cfg_CFC_Units CHECK (VitalUnits IS NULL OR ISJSON(VitalUnits) = 1),  -- List<VitalUnitSetting>
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_CFC_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_CFC_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_CFC PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_CFC_Hosp_State UNIQUE (HospitalId, ConfigState),
    CONSTRAINT FK_cfg_CFC_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId)
);

-- ============================================================
-- B. sec — SECURITY (7 tables)
-- ============================================================

-- Model: AppUser. Roles live in sec.UserRole (AppUser.Roles; a user may hold several); permissions are the
-- union of the user's roles. Role / RolesLabel / SeesOwnPatientsOnly are derived from Roles and not stored.
-- FullName is rendered from the name parts via the DOCTOR_NAME_TEMPLATE hospital setting unless DisplayNameOverride is set.
CREATE TABLE sec.ApplicationUser (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    UserId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_Usr_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    FullName NVARCHAR(200) NOT NULL,                        -- ENCRYPTION: DETERMINISTIC
    Email NVARCHAR(100) NOT NULL,                           -- ENCRYPTION: DETERMINISTIC
    PasswordHash NVARCHAR(200) NOT NULL,                    -- AppUser.Password — store a salted hash, never the plain text
    Phone NVARCHAR(20) NULL,                                -- ENCRYPTION: DETERMINISTIC
    SpecialtyId UNIQUEIDENTIFIER NULL,                      -- primary specialty; others in sec.DoctorSpecialty
    LicenseNumber NVARCHAR(50) NULL,
    LabPartnerId UNIQUEIDENTIFIER NULL,                     -- LAB_PARTNER accounts only
    Prefix NVARCHAR(20) NULL, FirstName NVARCHAR(100) NULL, MiddleName NVARCHAR(100) NULL, LastName NVARCHAR(100) NULL,
    Qualifications NVARCHAR(200) NULL, DisplayNameOverride NVARCHAR(200) NULL,
    Room NVARCHAR(50) NULL, Languages NVARCHAR(200) NULL, Bio NVARCHAR(1000) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_sec_Usr_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_sec_Usr_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_Usr_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_sec_Usr PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_sec_Usr_Id UNIQUE NONCLUSTERED (UserId),
    CONSTRAINT UQ_sec_Usr_Email UNIQUE (Email),
    CONSTRAINT FK_sec_Usr_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_sec_Usr_SpecialtyId FOREIGN KEY (SpecialtyId) REFERENCES cfg.Specialty(SpecialtyId),
    CONSTRAINT FK_sec_Usr_LabPartnerId FOREIGN KEY (LabPartnerId) REFERENCES cfg.LabPartner(LabPartnerId),
    INDEX IX_sec_Usr_HospitalId NONCLUSTERED (HospitalId)
);

-- Model: AppUser.Roles. One row per role a user holds. LAB_PARTNER is exclusive: a lab partner account
-- holds no other role (enforced in UserService.NormalizeRoles).
CREATE TABLE sec.UserRole (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    UserId UNIQUEIDENTIFIER NOT NULL,
    [Role] NVARCHAR(20) NOT NULL CONSTRAINT CK_sec_UsrRole_Role CHECK ([Role] IN ('ADMIN','DOCTOR','RECEPTIONIST','BILLING','NURSE','LAB_PARTNER')),
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_sec_UsrRole_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_UsrRole_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_sec_UsrRole PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_sec_UsrRole_User_Role UNIQUE (UserId, [Role]),
    CONSTRAINT FK_sec_UsrRole_UserId FOREIGN KEY (UserId) REFERENCES sec.ApplicationUser(UserId)
);

-- Model: DoctorSpecialty. A doctor may have several specialties; ApplicationUser.SpecialtyId stays the primary.
CREATE TABLE sec.DoctorSpecialty (
    DoctorId UNIQUEIDENTIFIER NOT NULL, SpecialtyId UNIQUEIDENTIFIER NOT NULL,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_sec_DS_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_DS_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_sec_DS PRIMARY KEY CLUSTERED (DoctorId, SpecialtyId),
    CONSTRAINT FK_sec_DS_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId),
    CONSTRAINT FK_sec_DS_SpecialtyId FOREIGN KEY (SpecialtyId) REFERENCES cfg.Specialty(SpecialtyId)
);

-- DataStore.Permissions (feature → role → granted). PermissionRow is the grid view of this table
-- (one row per Feature, Access keyed by Role); its Label is display text and not stored.
CREATE TABLE sec.PermissionMatrix (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    Feature NVARCHAR(30) NOT NULL,
    [Role] NVARCHAR(20) NOT NULL,
    IsGranted BIT NOT NULL CONSTRAINT DF_sec_Perm_Granted DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_sec_Perm_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_Perm_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_sec_Perm PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_sec_Perm_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT UQ_sec_Perm_Feature_Role UNIQUE (HospitalId, Feature, [Role])
);

-- Model: AuditLogEntry. Append-only: no IsActive; ModifiedAt/ModifiedBy stay NULL. Written by AuditService.Record.
CREATE TABLE sec.AuditLog (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    AuditLogId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_Aud_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, UserId UNIQUEIDENTIFIER NULL,   -- NULL for failed logins of unknown users
    [Action] NVARCHAR(20) NOT NULL CONSTRAINT CK_sec_Aud_Action CHECK ([Action] IN ('CREATE','READ','UPDATE','DELETE','LOGIN','LOGIN_FAIL','LOGOUT','EXPORT','PRINT','OVERRIDE','SIGNOFF','SUBMIT','APPROVE','REJECT')),
    EntityType NVARCHAR(50) NOT NULL, EntityId NVARCHAR(50) NULL, Details NVARCHAR(500) NULL,
    IpAddress NVARCHAR(45) NULL, UserAgent NVARCHAR(200) NULL,
    [Timestamp] DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_sec_Aud_Ts DEFAULT SYSUTCDATETIME(),
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_sec_Aud_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_Aud_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_sec_Aud PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_sec_Aud_Id UNIQUE NONCLUSTERED (AuditLogId),
    INDEX IX_sec_Aud_HospitalId NONCLUSTERED (HospitalId, [Timestamp] DESC),
    INDEX IX_sec_Aud_UserId NONCLUSTERED (UserId),
    INDEX IX_sec_Aud_Entity NONCLUSTERED (EntityType, EntityId)
);

-- Model: UserNotification. In-app notification for a staff user, shown under the top-bar bell.
-- EntityId = what it is about (e.g. a ResultId), so it can be cleared once that is dealt with.
CREATE TABLE sec.UserNotification (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    NotificationId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_UN_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, UserId UNIQUEIDENTIFIER NOT NULL,
    Kind NVARCHAR(30) NOT NULL,                -- LAB_RESULT …
    Title NVARCHAR(200) NOT NULL,
    [Message] NVARCHAR(500) NULL,
    Url NVARCHAR(500) NULL,                    -- app-absolute URL opened on click
    IsCritical BIT NOT NULL CONSTRAINT DF_sec_UN_Crit DEFAULT 0,
    EntityId NVARCHAR(50) NULL,
    ReadAt DATETIMEOFFSET(3) NULL,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_sec_UN_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_UN_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_sec_UN PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_sec_UN_Id UNIQUE NONCLUSTERED (NotificationId),
    CONSTRAINT FK_sec_UN_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_sec_UN_UserId FOREIGN KEY (UserId) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_sec_UN_User_Unread NONCLUSTERED (UserId, ReadAt, CreatedAt DESC),
    INDEX IX_sec_UN_EntityId NONCLUSTERED (EntityId)
);

-- Model: none (v4.1). One row per lab-partner lookup of a requisition by mobile number (cln.usp_SearchLabOrdersByMobile):
-- 5 lookups that find nothing within 15 minutes lock the user out of looking up. Replaces LabService.ClaimFailures,
-- which lived in process memory. Append-only.
CREATE TABLE sec.LabClaimAttempt (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    LabClaimAttemptId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_LCA_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, UserId UNIQUEIDENTIFIER NOT NULL,
    IsSuccess BIT NOT NULL,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_sec_LCA_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_LCA_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_sec_LCA PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_sec_LCA_Id UNIQUE NONCLUSTERED (LabClaimAttemptId),
    CONSTRAINT FK_sec_LCA_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_sec_LCA_UserId FOREIGN KEY (UserId) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_sec_LCA_User_CreatedAt NONCLUSTERED (UserId, CreatedAt DESC) INCLUDE (IsSuccess)
);

ALTER TABLE cfg.PhraseBank ADD CONSTRAINT FK_cfg_PB_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId);
ALTER TABLE cfg.ConsultationFormConfig ADD CONSTRAINT FK_cfg_CFC_PublishedBy FOREIGN KEY (PublishedBy) REFERENCES sec.ApplicationUser(UserId);
GO

-- ============================================================
-- C. opd — OPD CORE (13 tables)
-- ============================================================

-- Model: Patient. AgeYears is computed from Dob in the application and not stored.
-- FullName / Phone / Address hold the composed values the app reads; the structured parts are what the
-- registration form edits.
CREATE TABLE opd.Patient (  -- TEMPORAL: Phase 4
    Id BIGINT IDENTITY(1,1) NOT NULL,
    PatientId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_Pat_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, Mrn NVARCHAR(20) NOT NULL,
    FullName NVARCHAR(200) NOT NULL,                        -- ENCRYPTION: DETERMINISTIC
    Gender NVARCHAR(10) NOT NULL CONSTRAINT CK_opd_Pat_Gender CHECK (Gender IN ('MALE','FEMALE','OTHER')),
    Dob DATE NULL,
    Phone NVARCHAR(20) NULL,                                -- ENCRYPTION: DETERMINISTIC
    Email NVARCHAR(100) NULL,                               -- ENCRYPTION: DETERMINISTIC
    [Address] NVARCHAR(500) NULL,                           -- ENCRYPTION: RANDOMIZED
    NationalHealthId NVARCHAR(50) NULL,                     -- ENCRYPTION: DETERMINISTIC
    InsuranceProvider NVARCHAR(100) NULL,                   -- ENCRYPTION: RANDOMIZED (legacy; coverage lives in opd.PatientCoverage)
    InsurancePolicyNo NVARCHAR(50) NULL,                    -- ENCRYPTION: RANDOMIZED
    EmergencyContactName NVARCHAR(200) NULL,                -- ENCRYPTION: RANDOMIZED
    EmergencyContactPhone NVARCHAR(20) NULL,                -- ENCRYPTION: RANDOMIZED
    BloodGroup NVARCHAR(5) NULL,                            -- A+, O-, …
    RegisteredVia NVARCHAR(20) NULL CONSTRAINT CK_opd_Pat_RegVia CHECK (RegisteredVia IN ('FRONT_DESK','PATIENT_PORTAL','CSV_IMPORT')),
    -- Structured registration fields
    Prefix NVARCHAR(10) NULL, FirstName NVARCHAR(60) NULL, MiddleName NVARCHAR(60) NULL, LastName NVARCHAR(60) NULL,
    Trn NVARCHAR(20) NULL,                                  -- ENCRYPTION: DETERMINISTIC
    CountryCode NVARCHAR(5) NULL, Mobile NVARCHAR(16) NULL, -- ENCRYPTION: DETERMINISTIC (Mobile)
    PreferredLanguage NVARCHAR(40) NULL,
    AddrLine1 NVARCHAR(150) NULL, AddrLine2 NVARCHAR(150) NULL, City NVARCHAR(80) NULL, Region NVARCHAR(80) NULL, Country NVARCHAR(60) NULL,
    MaritalStatus NVARCHAR(10) NULL CONSTRAINT CK_opd_Pat_Marital CHECK (MaritalStatus IN ('SINGLE','MARRIED','OTHER')),
    ReferralSource NVARCHAR(80) NULL, PreferredDoctorId UNIQUEIDENTIFIER NULL,
    -- Social history (Health Record → Family & Social)
    SmokingStatus NVARCHAR(10) NULL CONSTRAINT CK_opd_Pat_Smoking CHECK (SmokingStatus IN ('NEVER','FORMER','CURRENT')),
    SmokingPackYears INT NULL, AlcoholUse NVARCHAR(12) NULL CONSTRAINT CK_opd_Pat_Alcohol CHECK (AlcoholUse IN ('NONE','OCCASIONAL','REGULAR')),
    Occupation NVARCHAR(80) NULL,
    PhysicalActivity NVARCHAR(12) NULL CONSTRAINT CK_opd_Pat_Activity CHECK (PhysicalActivity IN ('SEDENTARY','LIGHT','MODERATE','ACTIVE')),
    Diet NVARCHAR(16) NULL CONSTRAINT CK_opd_Pat_Diet CHECK (Diet IN ('VEGETARIAN','EGGETARIAN','NON_VEGETARIAN','VEGAN')),
    IsActive BIT NOT NULL CONSTRAINT DF_opd_Pat_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_opd_Pat_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_Pat_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_Pat PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_opd_Pat_Id UNIQUE NONCLUSTERED (PatientId),
    CONSTRAINT FK_opd_Pat_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_Pat_PrefDoctor FOREIGN KEY (PreferredDoctorId) REFERENCES sec.ApplicationUser(UserId),
    CONSTRAINT UQ_opd_Pat_Hosp_Mrn UNIQUE (HospitalId, Mrn),
    INDEX IX_opd_Pat_Hosp_Phone NONCLUSTERED (HospitalId, Phone),
    INDEX IX_opd_Pat_Hosp_Mobile NONCLUSTERED (HospitalId, Mobile),            -- duplicate check (PatientService.FindDuplicate)
    INDEX IX_opd_Pat_Hosp_Nhid NONCLUSTERED (HospitalId, NationalHealthId),
    INDEX IX_opd_Pat_Hosp_Trn NONCLUSTERED (HospitalId, Trn)
);

-- Model: PatientAllergy.
CREATE TABLE opd.PatientAllergy (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    AllergyId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_PA_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL,
    AllergyType NVARCHAR(20) NOT NULL CONSTRAINT CK_opd_PA_Type CHECK (AllergyType IN ('DRUG','FOOD','ENVIRONMENT','OTHER')),
    AllergyName NVARCHAR(100) NOT NULL,
    ReactionType NVARCHAR(30) NULL,            -- Allergy, Intolerance, …
    ReactionDescription NVARCHAR(200) NULL,
    Severity NVARCHAR(10) NOT NULL CONSTRAINT CK_opd_PA_Sev CHECK (Severity IN ('MILD','MODERATE','SEVERE')),
    Status NVARCHAR(10) NOT NULL CONSTRAINT CK_opd_PA_Status CHECK (Status IN ('ACTIVE','RESOLVED','INACTIVE')) CONSTRAINT DF_opd_PA_Status DEFAULT 'ACTIVE',
    IsTreated BIT NOT NULL CONSTRAINT DF_opd_PA_Treated DEFAULT 0,
    IsFamilyHistory BIT NOT NULL CONSTRAINT DF_opd_PA_FamHx DEFAULT 0,
    SinceWhen DATE NULL,
    Notes NVARCHAR(500) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_opd_PA_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_opd_PA_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_PA_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_PA PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_opd_PA_Id UNIQUE NONCLUSTERED (AllergyId),
    CONSTRAINT FK_opd_PA_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_PA_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    INDEX IX_opd_PA_PatientId NONCLUSTERED (PatientId)
);

-- Model: PatientMedicalHistory. ItemId → cfg.MedicalHistoryItem when picked from the list (NULL = free text).
CREATE TABLE opd.PatientMedicalHistory (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    PatientMedHistoryId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_PMH_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL,
    ItemId UNIQUEIDENTIFIER NULL,
    ConditionName NVARCHAR(100) NOT NULL,
    OnsetMonth INT NULL CONSTRAINT CK_opd_PMH_Month CHECK (OnsetMonth BETWEEN 1 AND 12),
    OnsetYear INT NULL,
    Status NVARCHAR(12) NULL CONSTRAINT CK_opd_PMH_Status CHECK (Status IN ('ACTIVE','CONTROLLED','RESOLVED')),  -- NULL on older records = ACTIVE
    Notes NVARCHAR(500) NULL,
    RecordedByUserId UNIQUEIDENTIFIER NULL,
    RecordedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_opd_PMH_RecAt DEFAULT SYSUTCDATETIME(),
    IsActive BIT NOT NULL CONSTRAINT DF_opd_PMH_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_opd_PMH_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_PMH_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_PMH PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_opd_PMH_Id UNIQUE NONCLUSTERED (PatientMedHistoryId),
    CONSTRAINT FK_opd_PMH_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_PMH_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT FK_opd_PMH_ItemId FOREIGN KEY (ItemId) REFERENCES cfg.MedicalHistoryItem(ItemId),
    CONSTRAINT FK_opd_PMH_RecordedBy FOREIGN KEY (RecordedByUserId) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_opd_PMH_PatientId NONCLUSTERED (PatientId)
);

-- Model: PatientMedicalEvent. EventId → cfg.MedicalEventType when picked from the list (NULL = free text).
CREATE TABLE opd.PatientMedicalEvent (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    PatientMedEventId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_PME_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL,
    EventId UNIQUEIDENTIFIER NULL,
    EventName NVARCHAR(100) NOT NULL,
    EventMonth INT NULL CONSTRAINT CK_opd_PME_Month CHECK (EventMonth BETWEEN 1 AND 12),
    EventYear INT NULL,
    Notes NVARCHAR(500) NULL,
    RecordedByUserId UNIQUEIDENTIFIER NULL,
    RecordedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_opd_PME_RecAt DEFAULT SYSUTCDATETIME(),
    IsActive BIT NOT NULL CONSTRAINT DF_opd_PME_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_opd_PME_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_PME_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_PME PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_opd_PME_Id UNIQUE NONCLUSTERED (PatientMedEventId),
    CONSTRAINT FK_opd_PME_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_PME_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT FK_opd_PME_EventId FOREIGN KEY (EventId) REFERENCES cfg.MedicalEventType(EventId),
    CONSTRAINT FK_opd_PME_RecordedBy FOREIGN KEY (RecordedByUserId) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_opd_PME_PatientId NONCLUSTERED (PatientId)
);

-- Model: PatientFamilyHistory. RelationshipType: FATHER, MOTHER, SIBLING, …
CREATE TABLE opd.PatientFamilyHistory (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    FamilyHistoryId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_PFH_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL,
    RelationshipType NVARCHAR(30) NOT NULL,
    ConditionName NVARCHAR(100) NOT NULL,
    Notes NVARCHAR(300) NULL,
    RecordedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_opd_PFH_RecAt DEFAULT SYSUTCDATETIME(),
    IsActive BIT NOT NULL CONSTRAINT DF_opd_PFH_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_opd_PFH_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_PFH_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_PFH PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_opd_PFH_Id UNIQUE NONCLUSTERED (FamilyHistoryId),
    CONSTRAINT FK_opd_PFH_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_PFH_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    INDEX IX_opd_PFH_PatientId NONCLUSTERED (PatientId)
);

-- Model: PatientConsentDocument. Scanned copy of the patient's signed "Consent for Sharing Health Information"
-- form (printed from the coverage page). The bytes live outside the database
-- (App_Data/consent-forms/{StoredFileName}) and are only streamed after a permission check.
CREATE TABLE opd.PatientConsentDocument (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    ConsentDocumentId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_PCD_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL,
    FormVersion NVARCHAR(20) NULL,
    FileName NVARCHAR(255) NOT NULL,
    ContentType NVARCHAR(100) NOT NULL,
    SizeBytes BIGINT NOT NULL CONSTRAINT CK_opd_PCD_Size CHECK (SizeBytes >= 0),
    StoredFileName NVARCHAR(100) NOT NULL,
    UploadedByUserId UNIQUEIDENTIFIER NULL,
    UploadedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_opd_PCD_UpAt DEFAULT SYSUTCDATETIME(),
    IsActive BIT NOT NULL CONSTRAINT DF_opd_PCD_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_opd_PCD_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_PCD_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_PCD PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_opd_PCD_Id UNIQUE NONCLUSTERED (ConsentDocumentId),
    CONSTRAINT UQ_opd_PCD_Stored UNIQUE (StoredFileName),
    CONSTRAINT FK_opd_PCD_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_PCD_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT FK_opd_PCD_UploadedBy FOREIGN KEY (UploadedByUserId) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_opd_PCD_PatientId NONCLUSTERED (PatientId, UploadedAt DESC)
);

-- Model: Appointment. Business event: SlotStart / SlotEnd are UTC instants, shown in TimeZoneId (the clinic's
-- timezone) wherever the user is. AppointmentDate = calendar date of SlotStart in TimeZoneId, set by
-- opd.usp_CreateAppointment; it backs the day lists.
CREATE TABLE opd.Appointment (  -- TEMPORAL: Phase 4
    Id BIGINT IDENTITY(1,1) NOT NULL,
    AppointmentId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_Appt_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL, DoctorId UNIQUEIDENTIFIER NOT NULL,
    SlotStart DATETIMEOFFSET(0) NOT NULL,
    SlotEnd DATETIMEOFFSET(0) NOT NULL,
    TimeZoneId NVARCHAR(50) NOT NULL,                       -- cfg.TimeZone (IANA), the clinic's timezone when booked
    AppointmentDate DATE NOT NULL,                          -- local calendar date of SlotStart in TimeZoneId
    [Type] NVARCHAR(20) NOT NULL,          -- cfg.AppointmentType.Code (NEW, FOLLOWUP, PROC, …)
    -- Lifecycle: BOOKED → CHECKED_IN (front desk) → IN_CONSULT (doctor opens consultation)
    --            → COMPLETED (consultation signed off). CANCELLED / NO_SHOW only from BOOKED or CHECKED_IN.
    Status NVARCHAR(15) NOT NULL CONSTRAINT CK_opd_Appt_Status CHECK (Status IN ('BOOKED','CHECKED_IN','IN_CONSULT','COMPLETED','CANCELLED','NO_SHOW')) CONSTRAINT DF_opd_Appt_Status DEFAULT 'BOOKED',
    CheckedInAt DATETIMEOFFSET(3) NULL,
    CheckedInBy UNIQUEIDENTIFIER NULL,
    ConsultStartedAt DATETIMEOFFSET(3) NULL,
    CompletedAt DATETIMEOFFSET(3) NULL,
    Notes NVARCHAR(300) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_opd_Appt_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_opd_Appt_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_Appt_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_Appt PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_opd_Appt_Id UNIQUE NONCLUSTERED (AppointmentId),
    CONSTRAINT CK_opd_Appt_Slot CHECK (SlotEnd > SlotStart),
    CONSTRAINT FK_opd_Appt_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_Appt_Tz FOREIGN KEY (TimeZoneId) REFERENCES cfg.TimeZone(TimeZoneId),
    CONSTRAINT FK_opd_Appt_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT FK_opd_Appt_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId),
    CONSTRAINT FK_opd_Appt_CheckedInBy FOREIGN KEY (CheckedInBy) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_opd_Appt_DoctorDate NONCLUSTERED (DoctorId, AppointmentDate),
    INDEX IX_opd_Appt_HospitalDate NONCLUSTERED (HospitalId, AppointmentDate),  -- front-desk day list, all doctors
    INDEX IX_opd_Appt_PatientId NONCLUSTERED (PatientId),
    INDEX IX_opd_Appt_Status NONCLUSTERED (Status)
);

-- Model: PatientCoverage. No active row = Self-Pay. A patient may hold Private Insurance and a
-- Govt scheme at the same time; Priority orders how liabilities are allocated at billing.
CREATE TABLE opd.PatientCoverage (
    Id BIGINT IDENTITY(1,1) NOT NULL,
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
    EffectiveDate DATE NULL, ExpiryDate DATE NULL,
    Status NVARCHAR(10) NOT NULL CONSTRAINT CK_opd_Cov_Status CHECK (Status IN ('ACTIVE','PENDING','INACTIVE','EXPIRED')) CONSTRAINT DF_opd_Cov_Status DEFAULT 'ACTIVE',
    Priority INT NOT NULL CONSTRAINT DF_opd_Cov_Priority DEFAULT 1,
    IsActive BIT NOT NULL CONSTRAINT DF_opd_Cov_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_opd_Cov_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_Cov_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_Cov PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_opd_Cov_Id UNIQUE NONCLUSTERED (CoverageId),
    CONSTRAINT FK_opd_Cov_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_Cov_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT FK_opd_Cov_PayerId FOREIGN KEY (PayerId) REFERENCES cfg.Payer(PayerId),
    INDEX IX_opd_Cov_PatientId NONCLUSTERED (PatientId, IsActive)
);

-- Model: DoctorSlotConfig. Weekly availability: one row per time window, several windows per weekday
-- allowed, no row for a weekday = closed. The slot length comes from APPT_SLOT_MINS
-- (opd.DoctorSetting → cfg.HospitalSetting) or the appointment type's duration.
CREATE TABLE opd.DoctorSlotConfig (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    SlotConfigId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_SC_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, DoctorId UNIQUEIDENTIFIER NOT NULL,
    DayOfWeek NVARCHAR(3) NOT NULL CONSTRAINT CK_opd_SC_Day CHECK (DayOfWeek IN ('MON','TUE','WED','THU','FRI','SAT','SUN')),
    StartTime NVARCHAR(5) NOT NULL, EndTime NVARCHAR(5) NOT NULL,     -- 'HH:mm'
    IsActive BIT NOT NULL CONSTRAINT DF_opd_SC_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_opd_SC_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_SC_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_SC PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_opd_SC_Id UNIQUE NONCLUSTERED (SlotConfigId),
    CONSTRAINT CK_opd_SC_Window CHECK (EndTime > StartTime),
    CONSTRAINT FK_opd_SC_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_SC_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_opd_SC_DoctorDay NONCLUSTERED (DoctorId, DayOfWeek)
);

-- Model: DoctorSetting. Per-doctor overrides of APPT_SLOT_MINS / FREE_FOLLOWUP_ALLOWED / FREE_FOLLOWUP_DAYS and
-- the doctor's charge model (CHARGE_MODEL FLAT|UNIT, CHARGE_UNIT_MINS, CHARGE_UNIT_RATE).
-- No row = inherit cfg.HospitalSetting.
CREATE TABLE opd.DoctorSetting (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    HospitalId UNIQUEIDENTIFIER NOT NULL, DoctorId UNIQUEIDENTIFIER NOT NULL,
    SettingKey NVARCHAR(40) NOT NULL CONSTRAINT CK_opd_DSet_Key CHECK (SettingKey IN
        ('APPT_SLOT_MINS','FREE_FOLLOWUP_ALLOWED','FREE_FOLLOWUP_DAYS','CHARGE_MODEL','CHARGE_UNIT_MINS','CHARGE_UNIT_RATE')),
    [Value] NVARCHAR(200) NOT NULL,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_opd_DSet_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_DSet_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_DSet PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_opd_DSet_Key UNIQUE NONCLUSTERED (HospitalId, DoctorId, SettingKey),
    CONSTRAINT FK_opd_DSet_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_DSet_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId)
);

-- Model: DoctorLeave. Full-day leave / holidays: no slots are offered on these dates.
CREATE TABLE opd.DoctorLeave (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    LeaveId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_DL_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, DoctorId UNIQUEIDENTIFIER NOT NULL,
    LeaveDate DATE NOT NULL, Reason NVARCHAR(200) NULL,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_opd_DL_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_DL_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_DL PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_opd_DL_Id UNIQUE NONCLUSTERED (LeaveId),
    CONSTRAINT UQ_opd_DL_Day UNIQUE NONCLUSTERED (HospitalId, DoctorId, LeaveDate),
    CONSTRAINT FK_opd_DL_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_DL_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId)
);

-- Model: IntakeResponse. Patient answers to the intake form, one per appointment.
-- Answers = JSON List<IntakeAnswer> [{ QuestionId, Label, Answer }] — labels snapshotted.
CREATE TABLE opd.IntakeResponse (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    ResponseId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_IR_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, AppointmentId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL,
    Answers NVARCHAR(MAX) NOT NULL CONSTRAINT CK_opd_IR_Json CHECK (ISJSON(Answers) = 1),
    SubmittedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_opd_IR_At DEFAULT SYSUTCDATETIME(),
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_opd_IR_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_IR_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_IR PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_opd_IR_Id UNIQUE NONCLUSTERED (ResponseId),
    CONSTRAINT UQ_opd_IR_Appt UNIQUE NONCLUSTERED (AppointmentId),
    CONSTRAINT FK_opd_IR_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_IR_AppointmentId FOREIGN KEY (AppointmentId) REFERENCES opd.Appointment(AppointmentId),
    CONSTRAINT FK_opd_IR_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId)
);

-- Model: ConsentRecord. Signed consents — append-only (no update/delete); title/text snapshotted at signing.
-- FK ConsultationId → cln.Consultation is added at the end of section D.
CREATE TABLE opd.ConsentRecord (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    ConsentRecordId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_CR_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsentFormId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL,
    AppointmentId UNIQUEIDENTIFIER NULL, ConsultationId UNIQUEIDENTIFIER NULL,
    SignedByName NVARCHAR(200) NOT NULL,
    Method NVARCHAR(10) NOT NULL CONSTRAINT CK_opd_CR_Method CHECK (Method IN ('IN_CLINIC','PORTAL')),
    FormVersion INT NOT NULL, TitleSnapshot NVARCHAR(200) NOT NULL, BodySnapshot NVARCHAR(MAX) NOT NULL,
    SignedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_opd_CR_At DEFAULT SYSUTCDATETIME(),
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_opd_CR_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_CR_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_CR PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_opd_CR_Id UNIQUE NONCLUSTERED (ConsentRecordId),
    CONSTRAINT FK_opd_CR_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_opd_CR_FormId FOREIGN KEY (ConsentFormId) REFERENCES cfg.ConsentForm(ConsentFormId),
    CONSTRAINT FK_opd_CR_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT FK_opd_CR_ApptId FOREIGN KEY (AppointmentId) REFERENCES opd.Appointment(AppointmentId),
    INDEX IX_opd_CR_Appt NONCLUSTERED (AppointmentId),
    INDEX IX_opd_CR_PatientId NONCLUSTERED (PatientId)
);

-- ============================================================
-- D. cln — CLINICAL (18 tables)
-- ============================================================

-- Model: Consultation. Status: IN_PROGRESS → LOCKED (signed off).
-- *Personal = the doctor's private working notes per SOAP section; SkippedSteps = JSON array of section keys.
CREATE TABLE cln.Consultation (  -- TEMPORAL: Phase 4
    Id BIGINT IDENTITY(1,1) NOT NULL,
    ConsultationId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Con_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL, DoctorId UNIQUEIDENTIFIER NOT NULL,
    AppointmentId UNIQUEIDENTIFIER NULL,
    VisitDate DATETIMEOFFSET(0) NOT NULL,                   -- UTC instant the visit started
    TimeZoneId NVARCHAR(50) NOT NULL,                       -- cfg.TimeZone (IANA), the clinic's timezone
    VisitLocalDate DATE NOT NULL,                           -- calendar date of VisitDate in TimeZoneId (day lists, reports)
    Status NVARCHAR(15) NOT NULL CONSTRAINT CK_cln_Con_Status CHECK (Status IN ('IN_PROGRESS','COMPLETED','LOCKED')) CONSTRAINT DF_cln_Con_Status DEFAULT 'IN_PROGRESS',
    IsLocked BIT NOT NULL CONSTRAINT DF_cln_Con_Locked DEFAULT 0,
    SubjectiveText NVARCHAR(MAX) NULL, ObjectiveText NVARCHAR(MAX) NULL, PlanText NVARCHAR(MAX) NULL,
    SubjectivePersonal NVARCHAR(MAX) NULL, ObjectivePersonal NVARCHAR(MAX) NULL, PlanPersonal NVARCHAR(MAX) NULL,
    FollowupDate DATE NULL, FollowupNotes NVARCHAR(500) NULL,
    SkippedSteps NVARCHAR(1000) NULL CONSTRAINT CK_cln_Con_Skipped CHECK (SkippedSteps IS NULL OR ISJSON(SkippedSteps) = 1),
    IsActive BIT NOT NULL CONSTRAINT DF_cln_Con_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_Con_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Con_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_Con PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cln_Con_Id UNIQUE NONCLUSTERED (ConsultationId),
    CONSTRAINT FK_cln_Con_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_Con_Tz FOREIGN KEY (TimeZoneId) REFERENCES cfg.TimeZone(TimeZoneId),
    CONSTRAINT FK_cln_Con_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT FK_cln_Con_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId),
    CONSTRAINT FK_cln_Con_ApptId FOREIGN KEY (AppointmentId) REFERENCES opd.Appointment(AppointmentId),
    INDEX IX_cln_Con_HospitalId NONCLUSTERED (HospitalId, VisitLocalDate),
    INDEX IX_cln_Con_PatientId NONCLUSTERED (PatientId),
    INDEX IX_cln_Con_DoctorDate NONCLUSTERED (DoctorId, VisitDate),
    INDEX IX_cln_Con_Status NONCLUSTERED (Status)
);

-- Model: ConsultationVitals — one row per consultation, values in the base unit (°F, mmHg, cm, kg, …).
-- ConsultationVitals.CustomValues → cln.ConsultationCustomVital.
CREATE TABLE cln.ConsultationVital (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    VitalId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_CV_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL,
    TempValue DECIMAL(5,1) NULL, BpSystolic DECIMAL(5,1) NULL, BpDiastolic DECIMAL(5,1) NULL,
    PulseBpm DECIMAL(5,1) NULL, SpO2 DECIMAL(5,1) NULL, RespRate DECIMAL(5,1) NULL,
    HeightCm DECIMAL(5,1) NULL, WeightKg DECIMAL(6,2) NULL, Bmi DECIMAL(4,1) NULL,
    PainScale DECIMAL(4,1) NULL, WaistCircCm DECIMAL(5,1) NULL, HeadCircCm DECIMAL(5,1) NULL,
    RecordedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_CV_RecAt DEFAULT SYSUTCDATETIME(),
    RecordedBy UNIQUEIDENTIFIER NULL,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_CV_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_CV_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_CV PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cln_CV_Id UNIQUE NONCLUSTERED (VitalId),
    CONSTRAINT FK_cln_CV_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_CV_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT FK_cln_CV_RecordedBy FOREIGN KEY (RecordedBy) REFERENCES sec.ApplicationUser(UserId),
    CONSTRAINT UQ_cln_CV_ConsultId UNIQUE (ConsultationId)
);

-- Model: ConsultationVitals.CustomValues (keyed by vital code). One row per consultation per custom vital.
CREATE TABLE cln.ConsultationCustomVital (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL,
    VitalCode NVARCHAR(40) NOT NULL,
    VitalValue DECIMAL(10,2) NULL,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_CCV_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_CCV_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_CCV PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_cln_CCV_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_CCV_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT UQ_cln_CCV_Consult_Code UNIQUE (ConsultationId, VitalCode)
);

-- Model: ConsultationSymptom. Tri-state complaint: UNSET → YES → NO → UNSET.
CREATE TABLE cln.ConsultationSymptom (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    SymptomId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_CS_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL,
    SymptomCode NVARCHAR(40) NOT NULL,         -- cfg.SymptomCatalog.SymptomCode
    [State] NVARCHAR(10) NOT NULL CONSTRAINT CK_cln_CS_State CHECK ([State] IN ('UNSET','YES','NO')) CONSTRAINT DF_cln_CS_State DEFAULT 'UNSET',
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_CS_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_CS_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_CS PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cln_CS_Id UNIQUE NONCLUSTERED (SymptomId),
    CONSTRAINT UQ_cln_CS_Consult_Code UNIQUE (ConsultationId, SymptomCode),
    CONSTRAINT FK_cln_CS_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_CS_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId)
);

-- Model: ConsultationDiagnosis. Laterality / AcuteChronic options are hospital-extensible
-- (cfg.ConsultationFormConfig), so they are not CHECK-constrained.
CREATE TABLE cln.ConsultationDiagnosis (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    DiagnosisId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Dx_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL,
    Icd10Code NVARCHAR(10) NOT NULL,
    Icd10Description NVARCHAR(300) NOT NULL,
    ClinicalNotes NVARCHAR(500) NULL,
    Laterality NVARCHAR(20) NULL,
    AcuteChronic NVARCHAR(20) NULL,
    IsPrimary BIT NOT NULL CONSTRAINT DF_cln_Dx_Primary DEFAULT 0,
    SequenceOrder INT NOT NULL CONSTRAINT DF_cln_Dx_Seq DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_cln_Dx_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_Dx_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Dx_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_Dx PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cln_Dx_Id UNIQUE NONCLUSTERED (DiagnosisId),
    CONSTRAINT FK_cln_Dx_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_Dx_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    INDEX IX_cln_Dx_ConsultId NONCLUSTERED (ConsultationId)
);

-- Model: Prescription. Source NULL = prescribed in a consultation here; 'OUTSIDE' = medicine from elsewhere,
-- recorded on the Health Record (no consultation). IsOutside / TimingLabel are derived and not stored.
CREATE TABLE cln.Prescription (  -- TEMPORAL: Phase 4
    Id BIGINT IDENTITY(1,1) NOT NULL,
    PrescriptionId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Rx_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    ConsultationId UNIQUEIDENTIFIER NULL,
    PatientId UNIQUEIDENTIFIER NOT NULL,
    Source NVARCHAR(10) NULL CONSTRAINT CK_cln_Rx_Source CHECK (Source IN ('OUTSIDE')),
    SourceDetail NVARCHAR(120) NULL,                       -- who prescribed an outside medicine
    Category NVARCHAR(20) NULL,
    MedicineName NVARCHAR(200) NOT NULL, Strength NVARCHAR(50) NULL,
    GenericClass NVARCHAR(200) NULL, TherapeuticClass NVARCHAR(300) NULL,
    DrugForm NVARCHAR(30) NULL, DeliveryRoute NVARCHAR(30) NULL, TargetArea NVARCHAR(50) NULL,
    TakeInUnit NVARCHAR(20) NULL, TakeInQty DECIMAL(6,2) NOT NULL CONSTRAINT DF_cln_Rx_Qty DEFAULT 1,
    TimesPerDay INT NOT NULL CONSTRAINT DF_cln_Rx_Times DEFAULT 1,
    DurationValue INT NOT NULL CONSTRAINT DF_cln_Rx_Dur DEFAULT 0, DurationUnit NVARCHAR(10) NULL,
    PeriodStart DATE NULL, PeriodEnd DATE NULL,
    FrequencyMode NVARCHAR(10) NULL, FrequencyPeriod NVARCHAR(10) NULL,
    TimingBf BIT NOT NULL CONSTRAINT DF_cln_Rx_Bf DEFAULT 0,
    TimingAn BIT NOT NULL CONSTRAINT DF_cln_Rx_An DEFAULT 0,
    TimingNi BIT NOT NULL CONSTRAINT DF_cln_Rx_Ni DEFAULT 0,
    TimingEm BIT NOT NULL CONSTRAINT DF_cln_Rx_Em DEFAULT 0,
    Prn BIT NOT NULL CONSTRAINT DF_cln_Rx_Prn DEFAULT 0,
    Instructions NVARCHAR(500) NULL, DynamicInstruction NVARCHAR(500) NULL,
    ValidityDays INT NOT NULL CONSTRAINT DF_cln_Rx_Valid DEFAULT 30,
    ValidUntil DATE NULL,
    TotalDispenseQty DECIMAL(8,2) NOT NULL CONSTRAINT DF_cln_Rx_Disp DEFAULT 0,
    PrescribedDate DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_cln_Rx_PrescAt DEFAULT SYSUTCDATETIME(),
    RepeatedFromConsultationId UNIQUEIDENTIFIER NULL,      -- "repeat previous Rx" source visit
    Status NVARCHAR(15) NOT NULL CONSTRAINT CK_cln_Rx_Status CHECK (Status IN ('ACTIVE','DISCONTINUED','COMPLETED')) CONSTRAINT DF_cln_Rx_Status DEFAULT 'ACTIVE',
    IsActive BIT NOT NULL CONSTRAINT DF_cln_Rx_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_Rx_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Rx_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_Rx PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cln_Rx_Id UNIQUE NONCLUSTERED (PrescriptionId),
    CONSTRAINT FK_cln_Rx_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_Rx_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT FK_cln_Rx_RepeatedFrom FOREIGN KEY (RepeatedFromConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT FK_cln_Rx_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT CK_cln_Rx_Origin CHECK (ConsultationId IS NOT NULL OR Source = 'OUTSIDE'),
    INDEX IX_cln_Rx_ConsultId NONCLUSTERED (ConsultationId),
    INDEX IX_cln_Rx_PatientId NONCLUSTERED (PatientId, Status)
);

-- Model: InvestigationOrder. Lab / imaging order. Status: ORDERED → SENT → RESULTS_READY.
CREATE TABLE cln.InvestigationOrder (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    OrderId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_IO_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL,
    TestId UNIQUEIDENTIFIER NULL,          -- cfg.LabTestCatalog; NULL for a free-text test
    TestName NVARCHAR(100) NOT NULL,
    TestType NVARCHAR(20) NOT NULL,
    Status NVARCHAR(15) NOT NULL CONSTRAINT CK_cln_IO_Status CHECK (Status IN ('ORDERED','SENT','RESULTS_READY')) CONSTRAINT DF_cln_IO_Status DEFAULT 'ORDERED',
    OrderedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_IO_OrderedAt DEFAULT SYSUTCDATETIME(),
    LabPartnerId UNIQUEIDENTIFIER NULL,    -- set when a lab partner claims the order from the patient's requisition
    IsActive BIT NOT NULL CONSTRAINT DF_cln_IO_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_IO_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_IO_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_IO PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cln_IO_Id UNIQUE NONCLUSTERED (OrderId),
    CONSTRAINT FK_cln_IO_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_IO_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT FK_cln_IO_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT FK_cln_IO_TestId FOREIGN KEY (TestId) REFERENCES cfg.LabTestCatalog(TestId),
    CONSTRAINT FK_cln_IO_LabPartnerId FOREIGN KEY (LabPartnerId) REFERENCES cfg.LabPartner(LabPartnerId),
    INDEX IX_cln_IO_ConsultId NONCLUSTERED (ConsultationId),
    INDEX IX_cln_IO_PatientId NONCLUSTERED (PatientId),
    INDEX IX_cln_IO_LabPartner NONCLUSTERED (LabPartnerId, Status)
);

-- Model: LabResult. Uploaded by a lab partner. Status: PENDING_REVIEW → REVIEWED.
CREATE TABLE cln.LabResult (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    ResultId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_LR_Id DEFAULT NEWSEQUENTIALID(),
    OrderId UNIQUEIDENTIFIER NOT NULL,
    HospitalId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NULL,
    TestName NVARCHAR(100) NOT NULL,
    ResultSummary NVARCHAR(500) NULL,
    ResultValue NVARCHAR(100) NULL, ReferenceRange NVARCHAR(100) NULL,
    Interpretation NVARCHAR(10) NULL CONSTRAINT CK_cln_LR_Interp CHECK (Interpretation IN ('NORMAL','ABNORMAL','CRITICAL')),
    Technician NVARCHAR(100) NULL,
    Notes NVARCHAR(500) NULL,
    UploadedByUserId UNIQUEIDENTIFIER NULL,
    LabPartnerId UNIQUEIDENTIFIER NULL,
    UploadedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_LR_UpAt DEFAULT SYSUTCDATETIME(),
    Status NVARCHAR(15) NOT NULL CONSTRAINT CK_cln_LR_Status CHECK (Status IN ('PENDING_REVIEW','REVIEWED')) CONSTRAINT DF_cln_LR_Status DEFAULT 'PENDING_REVIEW',
    ReviewedAt DATETIMEOFFSET(3) NULL, ReviewNotes NVARCHAR(300) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_cln_LR_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_LR_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_LR_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_LR PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cln_LR_Id UNIQUE NONCLUSTERED (ResultId),
    CONSTRAINT FK_cln_LR_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_LR_OrderId FOREIGN KEY (OrderId) REFERENCES cln.InvestigationOrder(OrderId),
    CONSTRAINT FK_cln_LR_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT FK_cln_LR_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT FK_cln_LR_UploadedBy FOREIGN KEY (UploadedByUserId) REFERENCES sec.ApplicationUser(UserId),
    CONSTRAINT FK_cln_LR_LabPartnerId FOREIGN KEY (LabPartnerId) REFERENCES cfg.LabPartner(LabPartnerId),
    INDEX IX_cln_LR_OrderId NONCLUSTERED (OrderId),
    INDEX IX_cln_LR_PatientId NONCLUSTERED (PatientId),
    INDEX IX_cln_LR_Status NONCLUSTERED (HospitalId, Status)
);

-- Model: LabResultAttachment. Report files (PDF / JPG / PNG / DICOM) uploaded with a lab result. The bytes are
-- stored outside the database (App_Data/lab-reports/{HospitalId}/{StoredFileName}); this row holds the metadata.
CREATE TABLE cln.LabResultAttachment (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    AttachmentId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_LRA_Id DEFAULT NEWSEQUENTIALID(),
    ResultId UNIQUEIDENTIFIER NOT NULL, OrderId UNIQUEIDENTIFIER NOT NULL,
    HospitalId UNIQUEIDENTIFIER NOT NULL, LabPartnerId UNIQUEIDENTIFIER NULL,
    FileName NVARCHAR(255) NOT NULL, ContentType NVARCHAR(100) NOT NULL, SizeBytes BIGINT NOT NULL,
    StoredFileName NVARCHAR(100) NOT NULL,
    UploadedByUserId UNIQUEIDENTIFIER NULL,
    UploadedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_LRA_UpAt DEFAULT SYSUTCDATETIME(),
    IsActive BIT NOT NULL CONSTRAINT DF_cln_LRA_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_LRA_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_LRA_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_LRA PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cln_LRA_Id UNIQUE NONCLUSTERED (AttachmentId),
    CONSTRAINT FK_cln_LRA_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_LRA_ResultId FOREIGN KEY (ResultId) REFERENCES cln.LabResult(ResultId),
    CONSTRAINT FK_cln_LRA_OrderId FOREIGN KEY (OrderId) REFERENCES cln.InvestigationOrder(OrderId),
    CONSTRAINT FK_cln_LRA_UploadedBy FOREIGN KEY (UploadedByUserId) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_cln_LRA_ResultId NONCLUSTERED (ResultId)
);

-- Model: Referral. Urgency options are hospital-extensible (cfg.ConsultationFormConfig).
CREATE TABLE cln.Referral (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    ReferralId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Ref_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL,
    Specialty NVARCHAR(100) NOT NULL,
    DoctorName NVARCHAR(200) NULL,
    Urgency NVARCHAR(20) NOT NULL CONSTRAINT DF_cln_Ref_Urg DEFAULT 'ROUTINE',
    Reason NVARCHAR(500) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_cln_Ref_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_Ref_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Ref_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_Ref PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cln_Ref_Id UNIQUE NONCLUSTERED (ReferralId),
    CONSTRAINT FK_cln_Ref_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_Ref_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT FK_cln_Ref_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    INDEX IX_cln_Ref_ConsultId NONCLUSTERED (ConsultationId)
);

-- Model: ConsultationServiceItem. A service or procedure done during the visit, picked from
-- bil.ServiceChargeMaster. Holds no price: sign-off turns each row into an invoice line at the
-- master's amount and tax. ServiceName / Category are snapshotted from the master.
CREATE TABLE cln.ConsultationServiceItem (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    ConsultationServiceId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_CSI_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NOT NULL,
    ServiceChargeId UNIQUEIDENTIFIER NOT NULL,
    ServiceName NVARCHAR(200) NOT NULL,
    Category NVARCHAR(20) NOT NULL,
    Qty INT NOT NULL CONSTRAINT DF_cln_CSI_Qty DEFAULT 1 CONSTRAINT CK_cln_CSI_Qty CHECK (Qty > 0),
    -- Optional stocked item used for the service (one linked to it in inv.InventoryItemService), with its own
    -- quantity: deducted from stock at sign-off and billed on its own invoice line.
    InventoryItemId UNIQUEIDENTIFIER NULL,
    ItemName NVARCHAR(100) NULL,
    ItemUnit NVARCHAR(20) NULL,
    ItemQty INT NULL,
    Notes NVARCHAR(300) NULL,
    AddedByUserId UNIQUEIDENTIFIER NULL,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_CSI_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_CSI_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_CSI PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cln_CSI_Id UNIQUE NONCLUSTERED (ConsultationServiceId),
    CONSTRAINT FK_cln_CSI_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_CSI_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT FK_cln_CSI_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT FK_cln_CSI_AddedBy FOREIGN KEY (AddedByUserId) REFERENCES sec.ApplicationUser(UserId),
    CONSTRAINT CK_cln_CSI_Item CHECK ((InventoryItemId IS NULL AND ItemQty IS NULL)
                                   OR (InventoryItemId IS NOT NULL AND ItemQty > 0 AND ItemName IS NOT NULL)),
    INDEX IX_cln_CSI_ConsultId NONCLUSTERED (ConsultationId)
    -- FK ServiceChargeId → bil.ServiceChargeMaster is added at the end of section E,
    -- FK InventoryItemId → inv.InventoryItem at the end of section E2
);

-- Model: ConsultationAttestation. The doctor's sign-off; one per consultation.
CREATE TABLE cln.ConsultationAttestation (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    AttestationId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Att_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL,
    DoctorId UNIQUEIDENTIFIER NOT NULL,
    SignedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_Att_SignedAt DEFAULT SYSUTCDATETIME(),
    SignatureHash NVARCHAR(128) NOT NULL,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_Att_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Att_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_Att PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cln_Att_Id UNIQUE NONCLUSTERED (AttestationId),
    CONSTRAINT FK_cln_Att_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_Att_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT FK_cln_Att_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId),
    CONSTRAINT UQ_cln_Att_ConsultId UNIQUE (ConsultationId)
);

-- Model: Addendum. Append-only post-lock correction.
CREATE TABLE cln.ConsultationAddendum (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    AddendumId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Add_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL,
    DoctorId UNIQUEIDENTIFIER NOT NULL,
    Category NVARCHAR(30) NULL,
    AddendumText NVARCHAR(MAX) NOT NULL,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_Add_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Add_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_Add PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cln_Add_Id UNIQUE NONCLUSTERED (AddendumId),
    CONSTRAINT FK_cln_Add_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_Add_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT FK_cln_Add_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_cln_Add_ConsultId NONCLUSTERED (ConsultationId)
);

-- Model: ConfidentialNote. Author-only note; never shown to anyone but the writing doctor.
CREATE TABLE cln.ConfidentialNote (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    ConfidentialNoteId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_CN_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL,
    DoctorId UNIQUEIDENTIFIER NOT NULL,
    NoteText NVARCHAR(MAX) NOT NULL,                        -- ENCRYPTION: RANDOMIZED
    UpdatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_CN_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_CN_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_CN_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_CN PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cln_CN_Id UNIQUE NONCLUSTERED (ConfidentialNoteId),
    CONSTRAINT FK_cln_CN_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_CN_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT FK_cln_CN_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId),
    CONSTRAINT UQ_cln_CN_Consult_Doctor UNIQUE (ConsultationId, DoctorId)
);

-- Model: ConsultationSketch. Drawing-pad sketch saved with a consultation (ImageData = JPEG data URL).
CREATE TABLE cln.ConsultationSketch (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    SketchId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Sk_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL,
    PatientId UNIQUEIDENTIFIER NOT NULL, DoctorId UNIQUEIDENTIFIER NOT NULL,
    Title NVARCHAR(100) NULL,
    ImageData NVARCHAR(MAX) NOT NULL,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_Sk_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_Sk_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_Sk PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cln_Sk_Id UNIQUE NONCLUSTERED (SketchId),
    CONSTRAINT FK_cln_Sk_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_Sk_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT FK_cln_Sk_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT FK_cln_Sk_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_cln_Sk_PatientId NONCLUSTERED (PatientId)
);

-- Model: DoctorLabFavorite.
CREATE TABLE cln.DoctorLabFavorite (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    FavoriteId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_DLF_Id DEFAULT NEWSEQUENTIALID(),
    DoctorId UNIQUEIDENTIFIER NOT NULL, TestId UNIQUEIDENTIFIER NOT NULL,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_DLF_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_DLF_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_DLF PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cln_DLF_Id UNIQUE NONCLUSTERED (FavoriteId),
    CONSTRAINT FK_cln_DLF_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId),
    CONSTRAINT FK_cln_DLF_TestId FOREIGN KEY (TestId) REFERENCES cfg.LabTestCatalog(TestId),
    CONSTRAINT UQ_cln_DLF_Doc_Test UNIQUE (DoctorId, TestId)
);

-- Model: ConsultationCustomValue (DataStore.CustomFieldValues). A custom consultation field's value
-- (multiple-choice keys joined with "|"). Labels and DisplayValue are captured when saved, so prints of
-- the visit don't change if the form setup does.
CREATE TABLE cln.ConsultationCustomValue (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    HospitalId UNIQUEIDENTIFIER NOT NULL, ConsultationId UNIQUEIDENTIFIER NOT NULL,
    SectionKey NVARCHAR(60) NOT NULL, FieldKey NVARCHAR(60) NOT NULL,
    [Value] NVARCHAR(MAX) NULL,
    SectionLabel NVARCHAR(100) NULL, FieldLabel NVARCHAR(100) NULL,
    InputType NVARCHAR(10) NOT NULL CONSTRAINT CK_cln_CCVal_Type CHECK (InputType IN ('TEXT','TEXTAREA','NUMBER','DATE','YESNO','SELECT','MULTI')),
    DisplayValue NVARCHAR(MAX) NULL,
    UpdatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_CCVal_UpdAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy UNIQUEIDENTIFIER NULL,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_CCVal_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_CCVal_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_CCVal PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cln_CCVal_Field UNIQUE (ConsultationId, SectionKey, FieldKey),
    CONSTRAINT FK_cln_CCVal_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_CCVal_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT FK_cln_CCVal_UpdatedBy FOREIGN KEY (UpdatedBy) REFERENCES sec.ApplicationUser(UserId)
);

ALTER TABLE opd.ConsentRecord ADD CONSTRAINT FK_opd_CR_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId);
GO

-- ============================================================
-- E. bil — BILLING (9 tables)
-- ============================================================

-- Model: DoctorFee. Effective-dated doctor fee.
CREATE TABLE bil.DoctorFeeSchedule (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    FeeScheduleId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_FS_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, DoctorId UNIQUEIDENTIFIER NOT NULL,
    ConsultationType NVARCHAR(10) NOT NULL CONSTRAINT CK_bil_FS_Type CHECK (ConsultationType IN ('NEW','FOLLOWUP')),
    FeeAmount DECIMAL(10,2) NOT NULL, Currency NVARCHAR(5) NOT NULL CONSTRAINT DF_bil_FS_Curr DEFAULT 'USD',
    EffectiveFrom DATE NOT NULL, EffectiveTo DATE NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_bil_FS_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_bil_FS_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_FS_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_bil_FS PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_bil_FS_Id UNIQUE NONCLUSTERED (FeeScheduleId),
    CONSTRAINT CK_bil_FS_Range CHECK (EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom),
    CONSTRAINT FK_bil_FS_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_bil_FS_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_bil_FS_Doctor NONCLUSTERED (DoctorId, ConsultationType, EffectiveFrom DESC)
);

-- Model: ServiceCharge. Priced in the hospital's currency (cfg.Hospital.Currency).
CREATE TABLE bil.ServiceChargeMaster (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    ServiceChargeId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_SC_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    ServiceCode NVARCHAR(20) NOT NULL, ServiceName NVARCHAR(200) NOT NULL,
    Category NVARCHAR(20) NOT NULL CONSTRAINT CK_bil_SC_Cat CHECK (Category IN ('CONSULTATION','LAB','IMAGING','PROCEDURE','SUPPLY','INJECTION','OTHER')),
    DefaultAmount DECIMAL(10,2) NOT NULL,
    TaxPercent DECIMAL(5,2) NOT NULL CONSTRAINT DF_bil_SC_Tax DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_bil_SC_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_bil_SC_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_SC_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_bil_SC PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_bil_SC_Id UNIQUE NONCLUSTERED (ServiceChargeId),
    CONSTRAINT FK_bil_SC_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT UQ_bil_SC_Hosp_Code UNIQUE (HospitalId, ServiceCode),
    INDEX IX_bil_SC_HospitalId NONCLUSTERED (HospitalId)
);

-- Model: Invoice. Status: PENDING → PARTIAL → PAID (or CANCELLED with a credit note).
CREATE TABLE bil.Invoice (  -- TEMPORAL: Phase 4
    Id BIGINT IDENTITY(1,1) NOT NULL,
    InvoiceId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_Inv_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, InvoiceNumber NVARCHAR(30) NOT NULL,
    ConsultationId UNIQUEIDENTIFIER NULL, PatientId UNIQUEIDENTIFIER NOT NULL,
    DoctorId UNIQUEIDENTIFIER NULL,
    ConsultationType NVARCHAR(10) NULL,
    IsFreeFollowup BIT NOT NULL CONSTRAINT DF_bil_Inv_FreeFU DEFAULT 0,   -- billed 0 under the free follow-up policy
    ChargeNote NVARCHAR(300) NULL,                                        -- why the consultation fee is what it is
    Currency NVARCHAR(5) NOT NULL CONSTRAINT DF_bil_Inv_Curr DEFAULT 'USD',
    SubTotal DECIMAL(10,2) NOT NULL CONSTRAINT DF_bil_Inv_Sub DEFAULT 0,
    DiscountAmount DECIMAL(10,2) NOT NULL CONSTRAINT DF_bil_Inv_Disc DEFAULT 0,
    TaxAmount DECIMAL(10,2) NOT NULL CONSTRAINT DF_bil_Inv_Tax DEFAULT 0,
    TotalAmount DECIMAL(10,2) NOT NULL,
    PaidAmount DECIMAL(10,2) NOT NULL CONSTRAINT DF_bil_Inv_Paid DEFAULT 0,       -- all non-voided payments (patient + payer settlements)
    -- N-way split: TotalAmount = CoveredAmount + PatientLiability
    CoveredAmount DECIMAL(10,2) NOT NULL CONSTRAINT DF_bil_Inv_Covered DEFAULT 0,     -- Σ verified bil.InvoiceCoverageLine.Liability
    PatientLiability DECIMAL(10,2) NOT NULL CONSTRAINT DF_bil_Inv_PatLiab DEFAULT 0,  -- TotalAmount − CoveredAmount
    PatientPaidAmount DECIMAL(10,2) NOT NULL CONSTRAINT DF_bil_Inv_PatPaid DEFAULT 0, -- non-voided PATIENT payments
    Status NVARCHAR(10) NOT NULL CONSTRAINT CK_bil_Inv_Status CHECK (Status IN ('DRAFT','PENDING','PAID','PARTIAL','CANCELLED')) CONSTRAINT DF_bil_Inv_Status DEFAULT 'PENDING',
    IsActive BIT NOT NULL CONSTRAINT DF_bil_Inv_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_bil_Inv_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_Inv_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_bil_Inv PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_bil_Inv_Id UNIQUE NONCLUSTERED (InvoiceId),
    CONSTRAINT FK_bil_Inv_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_bil_Inv_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT FK_bil_Inv_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT FK_bil_Inv_DoctorId FOREIGN KEY (DoctorId) REFERENCES sec.ApplicationUser(UserId),
    CONSTRAINT UQ_bil_Inv_Hosp_Number UNIQUE (HospitalId, InvoiceNumber),
    INDEX IX_bil_Inv_PatientId NONCLUSTERED (PatientId),
    INDEX IX_bil_Inv_ConsultId NONCLUSTERED (ConsultationId),
    INDEX IX_bil_Inv_Status NONCLUSTERED (HospitalId, Status),
    INDEX IX_bil_Inv_CreatedAt NONCLUSTERED (HospitalId, CreatedAt DESC)
);

-- Model: InvoiceLineItem.
CREATE TABLE bil.InvoiceLineItem (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    LineItemId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_LI_Id DEFAULT NEWSEQUENTIALID(),
    InvoiceId UNIQUEIDENTIFIER NOT NULL, ServiceChargeId UNIQUEIDENTIFIER NULL,
    InventoryItemId UNIQUEIDENTIFIER NULL,                  -- set for a stocked item billed from inv.InventoryItem (FK at the end of section E2)
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
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_bil_LI_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_LI_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_bil_LI PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_bil_LI_Id UNIQUE NONCLUSTERED (LineItemId),
    CONSTRAINT FK_bil_LI_InvoiceId FOREIGN KEY (InvoiceId) REFERENCES bil.Invoice(InvoiceId),
    CONSTRAINT FK_bil_LI_ServiceChargeId FOREIGN KEY (ServiceChargeId) REFERENCES bil.ServiceChargeMaster(ServiceChargeId),
    INDEX IX_bil_LI_InvoiceId NONCLUSTERED (InvoiceId)
);

-- Model: EligibilityVerification. Every insurer / scheme eligibility response recorded at billing (history kept, never updated).
CREATE TABLE bil.EligibilityVerification (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    VerificationId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_ELV_Id DEFAULT NEWSEQUENTIALID(),
    CoverageId UNIQUEIDENTIFIER NOT NULL, InvoiceId UNIQUEIDENTIFIER NULL,
    Status NVARCHAR(10) NOT NULL CONSTRAINT CK_bil_ELV_Status CHECK (Status IN ('VERIFIED','PENDING','REJECTED')),
    AuthorizationNumber NVARCHAR(50) NULL,
    ApprovedType NVARCHAR(10) NOT NULL CONSTRAINT CK_bil_ELV_Type CHECK (ApprovedType IN ('PERCENT','AMOUNT')) CONSTRAINT DF_bil_ELV_Type DEFAULT 'PERCENT',
    ApprovedValue DECIMAL(10,2) NOT NULL CONSTRAINT DF_bil_ELV_Value DEFAULT 0,   -- 70 (%) or 3500 (amount)
    Notes NVARCHAR(300) NULL,
    VerifiedBy UNIQUEIDENTIFIER NULL,
    VerifiedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_bil_ELV_At DEFAULT SYSUTCDATETIME(),
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_bil_ELV_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_ELV_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_bil_ELV PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_bil_ELV_Id UNIQUE NONCLUSTERED (VerificationId),
    CONSTRAINT FK_bil_ELV_CoverageId FOREIGN KEY (CoverageId) REFERENCES opd.PatientCoverage(CoverageId),
    CONSTRAINT FK_bil_ELV_InvoiceId FOREIGN KEY (InvoiceId) REFERENCES bil.Invoice(InvoiceId),
    CONSTRAINT FK_bil_ELV_VerifiedBy FOREIGN KEY (VerifiedBy) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_bil_ELV_CoverageId NONCLUSTERED (CoverageId, VerifiedAt)
);

-- Model: InvoiceCoverageLine. A verified coverage applied to an invoice. Liability = ApprovedValue % of the
-- invoice total (re-derived when charges change) or the fixed amount, capped so Σ liabilities ≤ TotalAmount.
-- PayerName on the model is joined from cfg.Payer for display and not stored.
CREATE TABLE bil.InvoiceCoverageLine (
    Id BIGINT IDENTITY(1,1) NOT NULL,
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
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_bil_ICL_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_ICL_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_bil_ICL PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_bil_ICL_Id UNIQUE NONCLUSTERED (CoverageLineId),
    CONSTRAINT UQ_bil_ICL_Inv_Cov UNIQUE (InvoiceId, CoverageId),
    CONSTRAINT FK_bil_ICL_InvoiceId FOREIGN KEY (InvoiceId) REFERENCES bil.Invoice(InvoiceId),
    CONSTRAINT FK_bil_ICL_CoverageId FOREIGN KEY (CoverageId) REFERENCES opd.PatientCoverage(CoverageId),
    CONSTRAINT FK_bil_ICL_PayerId FOREIGN KEY (PayerId) REFERENCES cfg.Payer(PayerId),
    INDEX IX_bil_ICL_InvoiceId NONCLUSTERED (InvoiceId)
);

-- Model: Payment. ComponentType PATIENT, or INSURANCE for a payer settlement (CoverageLineId = the claim settled).
CREATE TABLE bil.Payment (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    TransactionId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_Pay_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, InvoiceId UNIQUEIDENTIFIER NOT NULL,
    TransactionNumber NVARCHAR(30) NOT NULL,
    ComponentType NVARCHAR(15) NULL CONSTRAINT CK_bil_Pay_Comp CHECK (ComponentType IN ('PATIENT','INSURANCE')),
    CoverageLineId UNIQUEIDENTIFIER NULL,
    PaymentMethod NVARCHAR(30) NOT NULL,
    Amount DECIMAL(10,2) NOT NULL,
    Reference NVARCHAR(100) NULL,
    PaidAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_bil_Pay_PaidAt DEFAULT SYSUTCDATETIME(),
    ReceivedBy UNIQUEIDENTIFIER NULL,
    IsVoided BIT NOT NULL CONSTRAINT DF_bil_Pay_Voided DEFAULT 0,
    VoidedAt DATETIMEOFFSET(3) NULL,
    VoidedBy UNIQUEIDENTIFIER NULL,
    VoidReason NVARCHAR(300) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_bil_Pay_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_bil_Pay_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_Pay_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_bil_Pay PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_bil_Pay_Id UNIQUE NONCLUSTERED (TransactionId),
    CONSTRAINT UQ_bil_Pay_Hosp_Number UNIQUE (HospitalId, TransactionNumber),
    CONSTRAINT FK_bil_Pay_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_bil_Pay_InvoiceId FOREIGN KEY (InvoiceId) REFERENCES bil.Invoice(InvoiceId),
    CONSTRAINT FK_bil_Pay_CoverageLineId FOREIGN KEY (CoverageLineId) REFERENCES bil.InvoiceCoverageLine(CoverageLineId),
    CONSTRAINT FK_bil_Pay_ReceivedBy FOREIGN KEY (ReceivedBy) REFERENCES sec.ApplicationUser(UserId),
    CONSTRAINT FK_bil_Pay_VoidedBy FOREIGN KEY (VoidedBy) REFERENCES sec.ApplicationUser(UserId),
    CONSTRAINT CK_bil_Pay_CovLine CHECK (CoverageLineId IS NULL OR ComponentType = 'INSURANCE'),
    CONSTRAINT CK_bil_Pay_Void CHECK (IsVoided = 0 OR VoidedAt IS NOT NULL),
    INDEX IX_bil_Pay_InvoiceId NONCLUSTERED (InvoiceId),
    INDEX IX_bil_Pay_PaidAt NONCLUSTERED (HospitalId, PaidAt DESC)
);

-- Model: Receipt.
CREATE TABLE bil.Receipt (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    ReceiptId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_Rcp_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, ReceiptNumber NVARCHAR(30) NOT NULL,
    InvoiceId UNIQUEIDENTIFIER NOT NULL,
    PatientId UNIQUEIDENTIFIER NULL,
    TotalAmount DECIMAL(10,2) NOT NULL,
    Currency NVARCHAR(5) NOT NULL CONSTRAINT DF_bil_Rcp_Curr DEFAULT 'USD',
    GeneratedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_bil_Rcp_GenAt DEFAULT SYSUTCDATETIME(),
    IsActive BIT NOT NULL CONSTRAINT DF_bil_Rcp_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_bil_Rcp_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_Rcp_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_bil_Rcp PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_bil_Rcp_Id UNIQUE NONCLUSTERED (ReceiptId),
    CONSTRAINT FK_bil_Rcp_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_bil_Rcp_InvoiceId FOREIGN KEY (InvoiceId) REFERENCES bil.Invoice(InvoiceId),
    CONSTRAINT FK_bil_Rcp_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    CONSTRAINT UQ_bil_Rcp_Hosp_Number UNIQUE (HospitalId, ReceiptNumber),
    INDEX IX_bil_Rcp_InvoiceId NONCLUSTERED (InvoiceId)
);

-- Model: CreditNote.
CREATE TABLE bil.CreditNote (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    CreditNoteId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_CN_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, CreditNoteNumber NVARCHAR(30) NOT NULL,
    InvoiceId UNIQUEIDENTIFIER NOT NULL,
    Amount DECIMAL(10,2) NOT NULL, Reason NVARCHAR(300) NOT NULL,
    IssuedBy UNIQUEIDENTIFIER NOT NULL,
    IssuedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_bil_CN_IssuedAt DEFAULT SYSUTCDATETIME(),
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_bil_CN_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_bil_CN_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_bil_CN PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_bil_CN_Id UNIQUE NONCLUSTERED (CreditNoteId),
    CONSTRAINT FK_bil_CN_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_bil_CN_InvoiceId FOREIGN KEY (InvoiceId) REFERENCES bil.Invoice(InvoiceId),
    CONSTRAINT FK_bil_CN_IssuedBy FOREIGN KEY (IssuedBy) REFERENCES sec.ApplicationUser(UserId),
    CONSTRAINT UQ_bil_CN_Hosp_Number UNIQUE (HospitalId, CreditNoteNumber),
    INDEX IX_bil_CN_InvoiceId NONCLUSTERED (InvoiceId)
);

ALTER TABLE cln.ConsultationServiceItem ADD CONSTRAINT FK_cln_CSI_ServiceChargeId FOREIGN KEY (ServiceChargeId) REFERENCES bil.ServiceChargeMaster(ServiceChargeId);
GO

-- ============================================================
-- E2. inv — CLINIC INVENTORY (4 tables)
-- Stock the clinic uses during a visit (injections, medications, consumables), kept apart from
-- cfg.DrugFormulary, which lists what doctors prescribe for the patient to buy outside.
-- Stock is held per batch; a batch is usable through its ExpiryDate and used earliest-expiry first.
-- inv.StockMovement is the append-only ledger: a batch's QtyOnHand equals the sum of its movements.
-- ============================================================

-- Model: InventoryItem. Appears in the consultation's Services & Procedures step; billed at UnitPrice + TaxPercent.
CREATE TABLE inv.InventoryItem (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    InventoryItemId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_inv_Itm_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    ItemCode NVARCHAR(30) NOT NULL,
    ItemName NVARCHAR(100) NOT NULL,
    Category NVARCHAR(20) NOT NULL CONSTRAINT CK_inv_Itm_Cat CHECK (Category IN ('MEDICATION','INJECTION','CONSUMABLE','OTHER')),
    Unit NVARCHAR(20) NOT NULL,                             -- vial, ampoule, tablet, piece …
    UnitPrice DECIMAL(10,2) NOT NULL CONSTRAINT CK_inv_Itm_Price CHECK (UnitPrice >= 0),
    TaxPercent DECIMAL(5,2) NOT NULL CONSTRAINT DF_inv_Itm_Tax DEFAULT 0 CONSTRAINT CK_inv_Itm_Tax CHECK (TaxPercent BETWEEN 0 AND 100),
    ReorderLevel INT NOT NULL CONSTRAINT DF_inv_Itm_Reorder DEFAULT 0 CONSTRAINT CK_inv_Itm_Reorder CHECK (ReorderLevel >= 0),
    IsActive BIT NOT NULL CONSTRAINT DF_inv_Itm_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_inv_Itm_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_inv_Itm_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_inv_Itm PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_inv_Itm_Id UNIQUE NONCLUSTERED (InventoryItemId),
    CONSTRAINT UQ_inv_Itm_Code UNIQUE (HospitalId, ItemCode),
    CONSTRAINT FK_inv_Itm_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId)
);

-- Model: InventoryItem.ServiceChargeIds ("Used with"). In Services & Procedures, picking one of these services
-- offers the item. Re-saved as a whole when the item is edited (rows replaced, not soft-deleted).
CREATE TABLE inv.InventoryItemService (
    InventoryItemId UNIQUEIDENTIFIER NOT NULL, ServiceChargeId UNIQUEIDENTIFIER NOT NULL,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_inv_IIS_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_inv_IIS_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_inv_IIS PRIMARY KEY CLUSTERED (InventoryItemId, ServiceChargeId),
    CONSTRAINT FK_inv_IIS_ItemId FOREIGN KEY (InventoryItemId) REFERENCES inv.InventoryItem(InventoryItemId),
    CONSTRAINT FK_inv_IIS_ServiceChargeId FOREIGN KEY (ServiceChargeId) REFERENCES bil.ServiceChargeMaster(ServiceChargeId),
    INDEX IX_inv_IIS_ServiceChargeId NONCLUSTERED (ServiceChargeId)
);

-- Model: StockBatch. One delivery of an item; receiving the same BatchNo again tops it up (same expiry only).
CREATE TABLE inv.StockBatch (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    StockBatchId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_inv_Bat_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, InventoryItemId UNIQUEIDENTIFIER NOT NULL,
    BatchNo NVARCHAR(40) NOT NULL,
    ExpiryDate DATE NOT NULL,
    QtyReceived INT NOT NULL CONSTRAINT CK_inv_Bat_Rcv CHECK (QtyReceived >= 0),
    QtyOnHand INT NOT NULL CONSTRAINT CK_inv_Bat_OnHand CHECK (QtyOnHand >= 0),    -- stock never goes negative
    UnitCost DECIMAL(10,2) NOT NULL CONSTRAINT DF_inv_Bat_Cost DEFAULT 0 CONSTRAINT CK_inv_Bat_Cost CHECK (UnitCost >= 0),
    Supplier NVARCHAR(100) NULL,
    ReceivedByUserId UNIQUEIDENTIFIER NULL,
    ReceivedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_inv_Bat_RcvAt DEFAULT SYSUTCDATETIME(),
    IsActive BIT NOT NULL CONSTRAINT DF_inv_Bat_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_inv_Bat_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_inv_Bat_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_inv_Bat PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_inv_Bat_Id UNIQUE NONCLUSTERED (StockBatchId),
    CONSTRAINT UQ_inv_Bat_No UNIQUE (InventoryItemId, BatchNo),
    CONSTRAINT FK_inv_Bat_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_inv_Bat_ItemId FOREIGN KEY (InventoryItemId) REFERENCES inv.InventoryItem(InventoryItemId),
    CONSTRAINT FK_inv_Bat_ReceivedBy FOREIGN KEY (ReceivedByUserId) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_inv_Bat_Item_Expiry NONCLUSTERED (InventoryItemId, ExpiryDate) INCLUDE (QtyOnHand) WHERE QtyOnHand > 0
);

-- Model: StockMovement. Append-only. Qty is signed: + RECEIPT, − CONSUMPTION, ± ADJUSTMENT.
-- CONSUMPTION rows carry the consultation they were used in (written once, at sign-off).
CREATE TABLE inv.StockMovement (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    StockMovementId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_inv_Mov_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, InventoryItemId UNIQUEIDENTIFIER NOT NULL, StockBatchId UNIQUEIDENTIFIER NOT NULL,
    MovementType NVARCHAR(20) NOT NULL CONSTRAINT CK_inv_Mov_Type CHECK (MovementType IN ('RECEIPT','CONSUMPTION','ADJUSTMENT')),
    Qty INT NOT NULL,
    Reason NVARCHAR(200) NULL,
    ConsultationId UNIQUEIDENTIFIER NULL,
    UserId UNIQUEIDENTIFIER NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_inv_Mov_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_inv_Mov_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_inv_Mov_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_inv_Mov PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_inv_Mov_Id UNIQUE NONCLUSTERED (StockMovementId),
    CONSTRAINT CK_inv_Mov_Sign CHECK ((MovementType = 'RECEIPT' AND Qty > 0) OR (MovementType = 'CONSUMPTION' AND Qty < 0)
                                      OR (MovementType = 'ADJUSTMENT' AND Qty <> 0)),
    CONSTRAINT CK_inv_Mov_Consult CHECK (MovementType <> 'CONSUMPTION' OR ConsultationId IS NOT NULL),
    CONSTRAINT FK_inv_Mov_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_inv_Mov_ItemId FOREIGN KEY (InventoryItemId) REFERENCES inv.InventoryItem(InventoryItemId),
    CONSTRAINT FK_inv_Mov_BatchId FOREIGN KEY (StockBatchId) REFERENCES inv.StockBatch(StockBatchId),
    CONSTRAINT FK_inv_Mov_ConsultId FOREIGN KEY (ConsultationId) REFERENCES cln.Consultation(ConsultationId),
    CONSTRAINT FK_inv_Mov_UserId FOREIGN KEY (UserId) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_inv_Mov_Item_Date NONCLUSTERED (InventoryItemId, CreatedAt DESC),
    INDEX IX_inv_Mov_ConsultId NONCLUSTERED (ConsultationId) WHERE ConsultationId IS NOT NULL
);

ALTER TABLE cln.ConsultationServiceItem ADD CONSTRAINT FK_cln_CSI_InventoryItemId FOREIGN KEY (InventoryItemId) REFERENCES inv.InventoryItem(InventoryItemId);
ALTER TABLE bil.InvoiceLineItem ADD CONSTRAINT FK_bil_LI_InventoryItemId FOREIGN KEY (InventoryItemId) REFERENCES inv.InventoryItem(InventoryItemId);
GO

-- ============================================================
-- F. msg — MESSAGING (2 tables)
-- ============================================================

-- Model: ChannelConfig (DataStore.WhatsAppConfig / EmailConfig) — one row per hospital per channel.
-- WHATSAPP uses PhoneNumberId / BusinessAccountId; EMAIL uses the Smtp* / From* columns.
CREATE TABLE msg.NotificationConfig (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    Channel NVARCHAR(10) NOT NULL CONSTRAINT CK_msg_NC_Ch CHECK (Channel IN ('WHATSAPP','EMAIL')),
    Provider NVARCHAR(20) NULL,
    ApiKey NVARCHAR(500) NULL,                              -- ENCRYPTION: RANDOMIZED
    PhoneNumberId NVARCHAR(50) NULL,
    BusinessAccountId NVARCHAR(50) NULL,
    SmtpHost NVARCHAR(200) NULL,
    SmtpPort INT NULL CONSTRAINT CK_msg_NC_Port CHECK (SmtpPort IS NULL OR SmtpPort BETWEEN 0 AND 65535),
    FromEmail NVARCHAR(100) NULL,
    FromName NVARCHAR(100) NULL,
    Enabled BIT NOT NULL CONSTRAINT DF_msg_NC_Enabled DEFAULT 0,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_msg_NC_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_msg_NC_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_msg_NC PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_msg_NC_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT UQ_msg_NC_Hosp_Ch UNIQUE (HospitalId, Channel)
);

-- Model: QueuedMessage. Outgoing WhatsApp / email messages (sent or simulated).
CREATE TABLE msg.MessageQueue (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    MessageId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_msg_MQ_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, PatientId UNIQUEIDENTIFIER NULL,
    Channel NVARCHAR(10) NOT NULL CONSTRAINT CK_msg_MQ_Ch CHECK (Channel IN ('WHATSAPP','EMAIL')),
    TemplateName NVARCHAR(100) NULL,
    PatientName NVARCHAR(200) NULL,                         -- ENCRYPTION: RANDOMIZED
    PatientPhone NVARCHAR(20) NULL,                         -- ENCRYPTION: RANDOMIZED
    MessageText NVARCHAR(MAX) NULL,                         -- ENCRYPTION: RANDOMIZED
    Status NVARCHAR(15) NOT NULL CONSTRAINT CK_msg_MQ_Status CHECK (Status IN ('QUEUED','SENDING','SENT','FAILED','SIMULATED')) CONSTRAINT DF_msg_MQ_Status DEFAULT 'QUEUED',
    QueuedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_msg_MQ_QueuedAt DEFAULT SYSUTCDATETIME(),
    SentAt DATETIMEOFFSET(3) NULL,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_msg_MQ_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_msg_MQ_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_msg_MQ PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_msg_MQ_Id UNIQUE NONCLUSTERED (MessageId),
    CONSTRAINT FK_msg_MQ_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_msg_MQ_PatientId FOREIGN KEY (PatientId) REFERENCES opd.Patient(PatientId),
    INDEX IX_msg_MQ_Status NONCLUSTERED (HospitalId, Status),
    INDEX IX_msg_MQ_QueuedAt NONCLUSTERED (HospitalId, QueuedAt DESC)
);
GO

-- ============================================================
-- G. RESERVED — designed for planned features (5 tables; sec.LoginAttempt is in use since procedures v3.6)
--    Nothing in Models/ or Services/ reads or writes these. Create them only when the feature is built.
-- ============================================================

-- Planned: data-driven contradiction rules (today hard-coded in Services/ClinicalEngines.cs).
CREATE TABLE cfg.ContradictionRule (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    ContradictionRuleId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_CR_Id DEFAULT NEWSEQUENTIALID(),
    RuleType NVARCHAR(30) NOT NULL, ConditionJson NVARCHAR(MAX) NOT NULL,
    MessageTemplate NVARCHAR(300) NOT NULL,
    Severity NVARCHAR(15) NOT NULL CONSTRAINT CK_cfg_CR_Sev CHECK (Severity IN ('WARNING','HIGH_WARNING','CRITICAL')),
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_CR_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_CR_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_CR_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_CR PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_CR_Id UNIQUE NONCLUSTERED (ContradictionRuleId)
);

-- Planned: data-driven branching of the consultation flow.
CREATE TABLE cfg.BranchFlowRule (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    BranchFlowRuleId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_BF_Id DEFAULT NEWSEQUENTIALID(),
    RuleType NVARCHAR(30) NOT NULL, ConditionJson NVARCHAR(MAX) NOT NULL, ActionJson NVARCHAR(MAX) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_cfg_BF_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cfg_BF_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cfg_BF_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cfg_BF PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cfg_BF_Id UNIQUE NONCLUSTERED (BranchFlowRuleId)
);

-- Planned: server-side sessions (today ASP.NET session state).
CREATE TABLE sec.UserSession (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    UserSessionId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_Ses_Id DEFAULT NEWSEQUENTIALID(),
    UserId UNIQUEIDENTIFIER NOT NULL, SessionToken NVARCHAR(100) NOT NULL,
    Browser NVARCHAR(100) NULL, IpAddress NVARCHAR(45) NULL, UserAgent NVARCHAR(200) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_sec_Ses_Active DEFAULT 1,
    ExpiresAt DATETIMEOFFSET(3) NOT NULL,
    InvalidatedAt DATETIMEOFFSET(3) NULL,
    InvalidationReason NVARCHAR(30) NULL CONSTRAINT CK_sec_Ses_Reason CHECK (InvalidationReason IN ('NEW_LOGIN','LOGOUT','TIMEOUT','ADMIN_REVOKE')),
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_sec_Ses_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_Ses_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_sec_Ses PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_sec_Ses_Id UNIQUE NONCLUSTERED (UserSessionId),
    CONSTRAINT UQ_sec_Ses_Token UNIQUE (SessionToken),
    CONSTRAINT FK_sec_Ses_UserId FOREIGN KEY (UserId) REFERENCES sec.ApplicationUser(UserId),
    INDEX IX_sec_Ses_UserId NONCLUSTERED (UserId)
);

-- In use since procedures v3.6: sign-in lockout (sec.usp_RecordLoginAttempt / sec.usp_GetUserCredential —
-- 5 failures in 15 minutes). Kept in section G for numbering; no longer reserved.
CREATE TABLE sec.LoginAttempt (  -- append-only
    Id BIGINT IDENTITY(1,1) NOT NULL,
    LoginAttemptId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_LA_Id DEFAULT NEWSEQUENTIALID(),
    EmailAttempted NVARCHAR(100) NOT NULL, IsSuccess BIT NOT NULL,
    IpAddress NVARCHAR(45) NULL,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_sec_LA_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_LA_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_sec_LA PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_sec_LA_Id UNIQUE NONCLUSTERED (LoginAttemptId),
    INDEX IX_sec_LA_Email NONCLUSTERED (EmailAttempted, CreatedAt DESC)
);

-- Planned: persisting prescription safety alerts and their overrides (today ClinicalAlert is computed
-- on the fly by Services/ClinicalEngines.cs and not stored).
CREATE TABLE cln.PrescriptionSafetyAlert (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    SafetyAlertId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_SA_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL, PrescriptionId UNIQUEIDENTIFIER NOT NULL,
    AlertType NVARCHAR(30) NOT NULL, AlertMessage NVARCHAR(500) NOT NULL,
    Severity NVARCHAR(15) NOT NULL CONSTRAINT CK_cln_SA_Sev CHECK (Severity IN ('WARNING','HIGH_WARNING','CRITICAL')),
    IsOverridden BIT NOT NULL CONSTRAINT DF_cln_SA_Over DEFAULT 0,
    OverrideReason NVARCHAR(300) NULL, OverriddenBy UNIQUEIDENTIFIER NULL,
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_cln_SA_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cln_SA_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_cln_SA PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_cln_SA_Id UNIQUE NONCLUSTERED (SafetyAlertId),
    CONSTRAINT FK_cln_SA_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT FK_cln_SA_RxId FOREIGN KEY (PrescriptionId) REFERENCES cln.Prescription(PrescriptionId),
    INDEX IX_cln_SA_RxId NONCLUSTERED (PrescriptionId)
);
GO

-- ============================================================
-- SUMMARY
-- ============================================================
-- Application tables: cfg 23 | sec 6 | opd 13 | cln 18 | bil 9 | inv 4 | msg 2 = 75;  reserved (G): 5;  total 80
-- Dual-key: BIGINT IDENTITY clustered + UNIQUEIDENTIFIER NEWSEQUENTIALID() external; all FKs reference the
--   UNIQUEIDENTIFIER column. Column names = model property names.
-- Not stored (computed / display-only on the model): Patient.AgeYears, AppUser.Role / RolesLabel /
--   SeesOwnPatientsOnly, Prescription.IsOutside / TimingLabel, InvoiceCoverageLine.PayerName,
--   PermissionRow.Label; view models (AppointmentView, TimeSlot, DoctorSchedule, BillingSplit,
--   ConsultationCharge, PastVisit, ClinicalAlert, SoapDraft, PatientDuplicate, ResolvedSetting, …).
-- Stored elsewhere: AppUser.Roles → sec.UserRole; LabTest.SpecialtyIds → cfg.LabTestSpecialty;
--   ConsultationVitals.CustomValues → cln.ConsultationCustomVital; AppUser.Password → PasswordHash (hashed).
-- JSON columns: cfg.ConsultationFormConfig.Sections / VitalUnits, opd.IntakeResponse.Answers,
--   cln.Consultation.SkippedSteps.
-- Temporal (Phase 4): Patient, Appointment, Consultation, Prescription, Invoice.
-- Files outside the DB: App_Data/consent-forms (opd.PatientConsentDocument), App_Data/lab-reports (cln.LabResultAttachment).
-- v4.0 changes vs v3.0: added cfg.ConsultationFormConfig, cfg.LabTestSpecialty, sec.UserNotification,
--   opd.PatientConsentDocument, cln.ConsultationServiceItem, cln.ConsultationCustomValue; renamed columns to the
--   model names; added missing model columns; dropped columns the application does not have; aligned CHECK values
--   with the statuses the application writes; moved 5 unused tables to section G.
-- NOTE: docs/stored-procedures.sql was written against v3.0 column names and must be updated to match.
