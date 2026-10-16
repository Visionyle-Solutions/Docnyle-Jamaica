using System;
using System.Collections.Generic;
using System.Linq;
using OpdProject.Models;

namespace OpdProject.Services
{
    /// <summary>Valid range for one vital sign, used for server-side validation.</summary>
    public class VitalRange
    {
        public double Min { get; set; }
        public double Max { get; set; }
        public string Label { get; set; }
    }

    /// <summary>The consultation workspace: vitals, complaints, diagnoses, SOAP, prescriptions, labs, referrals, follow-up, sign-off.</summary>
    public static class ConsultationService
    {
        public static readonly Dictionary<string, VitalRange> VitalRanges = new Dictionary<string, VitalRange>
        {
            { "TEMP", new VitalRange { Min = 90, Max = 110, Label = "90-110°F" } },
            { "BP_SYS", new VitalRange { Min = 50, Max = 300, Label = "50-300 mmHg" } },
            { "BP_DIA", new VitalRange { Min = 20, Max = 200, Label = "20-200 mmHg" } },
            { "PULSE", new VitalRange { Min = 20, Max = 250, Label = "20-250 bpm" } },
            { "SPO2", new VitalRange { Min = 0, Max = 100, Label = "0-100%" } },
            { "HEIGHT", new VitalRange { Min = 20, Max = 250, Label = "20-250 cm" } },
            { "WEIGHT", new VitalRange { Min = 0.5, Max = 500, Label = "0.5-500 kg" } },
            { "RESP_RATE", new VitalRange { Min = 5, Max = 60, Label = "12-20 normal" } },
            { "PAIN_SCALE", new VitalRange { Min = 0, Max = 10, Label = "0-10" } },
            { "WAIST_CIRC", new VitalRange { Min = 30, Max = 200, Label = "30-200 cm" } },
            { "HEAD_CIRC", new VitalRange { Min = 20, Max = 70, Label = "20-70 cm" } },
        };

        /* ── Consultation record ── */

        public static Consultation Get(string consultationId)
        {
            lock (DataStore.Sync) return DataStore.Consultations.FirstOrDefault(c => c.ConsultationId == consultationId);
        }

        /// <summary>Returns the doctor's IN_PROGRESS consultation for the patient, creating one if none exists.</summary>
        public static Consultation GetOrCreate(string patientId, string doctorId, string hospitalId)
        {
            lock (DataStore.Sync)
            {
                var c = DataStore.Consultations.FirstOrDefault(x => x.PatientId == patientId && x.DoctorId == doctorId && x.Status == "IN_PROGRESS");
                if (c != null) return c;
                var zone = TimeZoneService.HospitalZoneId(hospitalId);
                c = new Consultation
                {
                    ConsultationId = DataStore.NextId("CONS"), HospitalId = hospitalId, PatientId = patientId, DoctorId = doctorId,
                    VisitDate = DateTime.UtcNow, TimeZoneId = zone, VisitLocalDate = TimeZoneService.Today(hospitalId),
                    Status = "IN_PROGRESS", IsLocked = false
                };
                DataStore.Consultations.Add(c);
                return c;
            }
        }

        /// <summary>The doctor's IN_PROGRESS consultation for the patient, or null (never creates one).</summary>
        public static Consultation FindInProgress(string patientId, string doctorId)
        {
            lock (DataStore.Sync) return DataStore.Consultations.FirstOrDefault(x => x.PatientId == patientId && x.DoctorId == doctorId && x.Status == "IN_PROGRESS");
        }

        /// <summary>
        /// Opens the doctor's consultation for a patient and links today's checked-in appointment:
        /// opening it moves CHECKED_IN → IN_CONSULT (sign-off later moves it to COMPLETED).
        /// </summary>
        public static Consultation Open(string patientId, string doctorId, string hospitalId)
        {
            var c = GetOrCreate(patientId, doctorId, hospitalId);
            var appt = SchedulingService.ListAppointments(hospitalId, doctorId, TimeZoneService.Today(hospitalId))
                .FirstOrDefault(a => a.PatientId == patientId && (a.Status == "CHECKED_IN" || a.Status == "IN_CONSULT"));
            if (appt != null && !c.IsLocked)
            {
                lock (DataStore.Sync) c.AppointmentId = appt.AppointmentId;
                if (appt.Status == "CHECKED_IN") SchedulingService.UpdateStatus(appt.AppointmentId, "IN_CONSULT", doctorId);
            }
            return c;
        }

        public static List<Consultation> ListForDate(string hospitalId, DateTime date)
        {
            lock (DataStore.Sync) return DataStore.Consultations.Where(c => c.HospitalId == hospitalId && c.VisitLocalDate == date.Date).ToList();
        }

        /// <summary>Signed visits of the patient, newest first, summarised for the context panel.</summary>
        public static List<PastVisit> ListPastVisits(string patientId)
        {
            lock (DataStore.Sync)
            {
                return DataStore.Consultations.Where(c => c.PatientId == patientId && c.Status == "LOCKED")
                    .OrderByDescending(c => c.VisitDate).Select(c =>
                    {
                        var dx = DataStore.Diagnoses.Where(d => d.ConsultationId == c.ConsultationId).ToList();
                        var primary = dx.FirstOrDefault(d => d.IsPrimary);
                        var complaints = DataStore.Symptoms.Where(s => s.ConsultationId == c.ConsultationId && s.State == "YES")
                            .Select(s => SymptomLabel(c.HospitalId, s.SymptomCode)).ToList();
                        return new PastVisit
                        {
                            ConsultationId = c.ConsultationId, VisitDate = c.VisitLocalDate, DoctorName = UserService.UserName(c.DoctorId),
                            ChiefComplaint = complaints.Count > 0 ? string.Join(", ", complaints) : "—",
                            PrimaryDiagnosis = primary != null ? primary.Icd10Code + " " + primary.Icd10Description : "—",
                            FollowupNotes = c.FollowupNotes,
                        };
                    }).ToList();
            }
        }

        public static void SkipStep(string consultationId, string step)
        {
            lock (DataStore.Sync)
            {
                var c = Get(consultationId);
                if (c != null && !c.SkippedSteps.Contains(step)) c.SkippedSteps.Add(step);
            }
        }

        /* ── Vitals ── */

        public static ConsultationVitals GetVitals(string consultationId)
        {
            lock (DataStore.Sync) return DataStore.Vitals.FirstOrDefault(v => v.ConsultationId == consultationId);
        }

        /// <summary>
        /// Valid range for a vital: the fixed range of a built-in vital, or the range set for a custom vital in Settings.
        /// Returns null when there is no range to enforce.
        /// </summary>
        public static VitalRange RangeFor(string hospitalId, string code)
        {
            VitalRange r;
            if (VitalRanges.TryGetValue(code, out r)) return r;
            VitalConfig cfg;
            lock (DataStore.Sync) cfg = DataStore.VitalsConfig.FirstOrDefault(v => v.HospitalId == hospitalId && v.VitalCode == code);
            if (cfg == null || (!cfg.MinValue.HasValue && !cfg.MaxValue.HasValue)) return null;
            var unit = string.IsNullOrEmpty(cfg.AllowedUnits) ? "" : " " + cfg.AllowedUnits;
            Func<double, string> n = d => d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new VitalRange
            {
                Min = cfg.MinValue ?? double.MinValue, Max = cfg.MaxValue ?? double.MaxValue,
                Label = cfg.MinValue.HasValue && cfg.MaxValue.HasValue ? n(cfg.MinValue.Value) + "-" + n(cfg.MaxValue.Value) + unit
                    : cfg.MinValue.HasValue ? "≥ " + n(cfg.MinValue.Value) + unit : "≤ " + n(cfg.MaxValue.Value) + unit
            };
        }

        /// <summary>
        /// Validates every value against its range (<see cref="RangeFor"/>) plus systolic &gt; diastolic. Returns error per vital code.
        /// Custom vitals are only range-checked when <paramref name="hospitalId"/> is given.
        /// </summary>
        public static Dictionary<string, string> ValidateVitals(IDictionary<string, double?> values, string hospitalId = null)
        {
            var errors = new Dictionary<string, string>();
            foreach (var kv in values)
            {
                VitalRange r;
                if (!VitalRanges.TryGetValue(kv.Key, out r)) r = hospitalId != null ? RangeFor(hospitalId, kv.Key) : null;
                if (kv.Value.HasValue && r != null && (kv.Value.Value < r.Min || kv.Value.Value > r.Max))
                    errors[kv.Key] = "Must be " + r.Label;
            }
            double? sys, dia;
            if (values.TryGetValue("BP_SYS", out sys) && values.TryGetValue("BP_DIA", out dia) && sys.HasValue && dia.HasValue && sys.Value <= dia.Value && !errors.ContainsKey("BP_SYS"))
                errors["BP_SYS"] = "Systolic must be greater than diastolic";
            return errors;
        }

        /// <summary>Upserts the visit's vitals (one row per consultation) and derives BMI.</summary>
        public static ConsultationVitals SaveVitals(string consultationId, IDictionary<string, double?> values, string userId)
        {
            lock (DataStore.Sync)
            {
                var row = GetVitals(consultationId);
                if (row == null)
                {
                    row = new ConsultationVitals { VitalId = DataStore.NextId("VIT"), ConsultationId = consultationId };
                    DataStore.Vitals.Add(row);
                }
                foreach (var kv in values) SetVital(row, kv.Key, kv.Value);
                row.Bmi = row.HeightCm.HasValue && row.WeightKg.HasValue && row.HeightCm.Value > 0
                    ? Math.Round(row.WeightKg.Value / Math.Pow(row.HeightCm.Value / 100.0, 2), 1) : (double?)null;
                row.RecordedAt = DateTime.UtcNow;
                row.RecordedBy = userId;
                return row;
            }
        }

        public static double? GetVital(ConsultationVitals v, string code)
        {
            if (v == null) return null;
            switch (code)
            {
                case "TEMP": return v.TempValue;
                case "BP_SYS": return v.BpSystolic;
                case "BP_DIA": return v.BpDiastolic;
                case "PULSE": return v.PulseBpm;
                case "SPO2": return v.SpO2;
                case "HEIGHT": return v.HeightCm;
                case "WEIGHT": return v.WeightKg;
                case "RESP_RATE": return v.RespRate;
                case "PAIN_SCALE": return v.PainScale;
                case "WAIST_CIRC": return v.WaistCircCm;
                case "HEAD_CIRC": return v.HeadCircCm;
                default:
                    double? custom;
                    return v.CustomValues != null && v.CustomValues.TryGetValue(code, out custom) ? custom : null;
            }
        }

        private static void SetVital(ConsultationVitals v, string code, double? value)
        {
            switch (code)
            {
                case "TEMP": v.TempValue = value; break;
                case "BP_SYS": v.BpSystolic = value; break;
                case "BP_DIA": v.BpDiastolic = value; break;
                case "PULSE": v.PulseBpm = value; break;
                case "SPO2": v.SpO2 = value; break;
                case "HEIGHT": v.HeightCm = value; break;
                case "WEIGHT": v.WeightKg = value; break;
                case "RESP_RATE": v.RespRate = value; break;
                case "PAIN_SCALE": v.PainScale = value; break;
                case "WAIST_CIRC": v.WaistCircCm = value; break;
                case "HEAD_CIRC": v.HeadCircCm = value; break;
                default:
                    if (v.CustomValues == null) v.CustomValues = new Dictionary<string, double?>();
                    if (value.HasValue) v.CustomValues[code] = value; else v.CustomValues.Remove(code);
                    break;
            }
        }

        public static bool HasAnyVital(ConsultationVitals v)
        {
            return v != null && (VitalRanges.Keys.Any(k => GetVital(v, k).HasValue) || (v.CustomValues != null && v.CustomValues.Values.Any(x => x.HasValue)));
        }

        /* ── Presenting complaints (tri-state) ── */

        public static List<SymptomCatalogItem> ListSymptomCatalog(string hospitalId)
        {
            lock (DataStore.Sync) return DataStore.SymptomCatalog.Where(s => s.HospitalId == hospitalId && s.IsActive).ToList();
        }

        public static string SymptomLabel(string hospitalId, string code)
        {
            lock (DataStore.Sync)
            {
                var s = DataStore.SymptomCatalog.FirstOrDefault(x => x.HospitalId == hospitalId && x.SymptomCode == code);
                return s != null ? s.DisplayLabel : code;
            }
        }

        public static Dictionary<string, string> GetSymptomStates(string consultationId)
        {
            lock (DataStore.Sync)
                return DataStore.Symptoms.Where(s => s.ConsultationId == consultationId).ToDictionary(s => s.SymptomCode, s => s.State);
        }

        /// <summary>Cycles a complaint unset → yes → no → unset and returns the new state.</summary>
        public static string CycleSymptom(string consultationId, string code)
        {
            lock (DataStore.Sync)
            {
                var row = DataStore.Symptoms.FirstOrDefault(s => s.ConsultationId == consultationId && s.SymptomCode == code);
                if (row == null)
                {
                    row = new ConsultationSymptom { SymptomId = DataStore.NextId("SYMP"), ConsultationId = consultationId, SymptomCode = code, State = "UNSET" };
                    DataStore.Symptoms.Add(row);
                }
                row.State = row.State == "UNSET" ? "YES" : row.State == "YES" ? "NO" : "UNSET";
                return row.State;
            }
        }

        /// <summary>Adds a custom complaint to the catalog (if new) and marks it YES for the visit.</summary>
        public static void AddCustomSymptom(string hospitalId, string consultationId, string label)
        {
            lock (DataStore.Sync)
            {
                var code = System.Text.RegularExpressions.Regex.Replace(label.ToUpperInvariant(), "[^A-Z0-9]", "_");
                if (!DataStore.SymptomCatalog.Any(s => s.HospitalId == hospitalId && s.SymptomCode == code))
                    DataStore.SymptomCatalog.Add(new SymptomCatalogItem { SymptomCatalogId = DataStore.NextId("SYM"), HospitalId = hospitalId, SymptomCode = code, DisplayLabel = label, IsQuickPick = false, IsActive = true });
                var row = DataStore.Symptoms.FirstOrDefault(s => s.ConsultationId == consultationId && s.SymptomCode == code);
                if (row == null) DataStore.Symptoms.Add(new ConsultationSymptom { SymptomId = DataStore.NextId("SYMP"), ConsultationId = consultationId, SymptomCode = code, State = "YES" });
                else row.State = "YES";
            }
        }

        /* ── Diagnoses ── */

        /// <summary>Matches code prefix, description or synonym terms.</summary>
        public static List<Icd10Entry> SearchIcd10(string term)
        {
            var t = (term ?? "").Trim().ToLowerInvariant();
            if (t.Length < 2) return new List<Icd10Entry>();
            lock (DataStore.Sync)
                return DataStore.Icd10Catalog.Where(r => r.Icd10Code.ToLowerInvariant().StartsWith(t) || r.Description.ToLowerInvariant().Contains(t)
                    || (r.SynonymTerms ?? "").ToLowerInvariant().Split(',').Any(s => s.Trim().Contains(t))).ToList();
        }

        public static Icd10Entry GetIcd10(string code)
        {
            lock (DataStore.Sync) return DataStore.Icd10Catalog.FirstOrDefault(r => r.Icd10Code == code);
        }

        public static List<ConsultationDiagnosis> ListDiagnoses(string consultationId)
        {
            lock (DataStore.Sync) return DataStore.Diagnoses.Where(d => d.ConsultationId == consultationId).OrderBy(d => d.SequenceOrder).ToList();
        }

        /// <summary>Code + clinical reasoning saved as one record; the first diagnosis on a visit is primary.</summary>
        public static ConsultationDiagnosis AddDiagnosis(string consultationId, string code, string description, string notes, string laterality, string acuteChronic)
        {
            lock (DataStore.Sync)
            {
                var existing = DataStore.Diagnoses.Count(d => d.ConsultationId == consultationId);
                var row = new ConsultationDiagnosis
                {
                    DiagnosisId = DataStore.NextId("DX"), ConsultationId = consultationId, Icd10Code = code, Icd10Description = description,
                    ClinicalNotes = notes, Laterality = laterality, AcuteChronic = acuteChronic, IsPrimary = existing == 0, SequenceOrder = existing
                };
                DataStore.Diagnoses.Add(row);
                return row;
            }
        }

        public static void RemoveDiagnosis(string diagnosisId)
        {
            lock (DataStore.Sync)
            {
                var dx = DataStore.Diagnoses.FirstOrDefault(d => d.DiagnosisId == diagnosisId);
                if (dx == null) return;
                DataStore.Diagnoses.Remove(dx);
                if (dx.IsPrimary)
                {
                    var next = DataStore.Diagnoses.Where(d => d.ConsultationId == dx.ConsultationId).OrderBy(d => d.SequenceOrder).FirstOrDefault();
                    if (next != null) next.IsPrimary = true;
                }
            }
        }

        /* ── SOAP ── */

        public static void SaveSoap(string consultationId, string s, string o, string p, string sPersonal, string oPersonal, string pPersonal)
        {
            lock (DataStore.Sync)
            {
                var c = Get(consultationId);
                if (c == null) return;
                c.SubjectiveText = s; c.ObjectiveText = o; c.PlanText = p;
                c.SubjectivePersonal = sPersonal; c.ObjectivePersonal = oPersonal; c.PlanPersonal = pPersonal;
            }
        }

        /* ── Prescriptions ── */

        public static List<Prescription> ListPrescriptions(string consultationId)
        {
            lock (DataStore.Sync) return DataStore.Prescriptions.Where(p => p.ConsultationId == consultationId && p.Status == "ACTIVE").ToList();
        }

        /// <summary>Medicines the patient is taking now: ACTIVE prescriptions from any visit whose course hasn't ended,
        /// plus outside medicines recorded on the patient record.</summary>
        public static List<Prescription> ListActiveMedications(string patientId)
        {
            lock (DataStore.Sync) return DataStore.Prescriptions.Where(p => p.PatientId == patientId && IsCurrent(p)).ToList();
        }

        /// <summary>ACTIVE and its course end date (if any) not yet passed.</summary>
        public static bool IsCurrent(Prescription p)
        {
            if (p.Status != "ACTIVE" || !p.PeriodEnd.HasValue) return p.Status == "ACTIVE";
            var c = Get(p.ConsultationId);
            return p.PeriodEnd.Value.Date >= TimeZoneService.Today(c != null ? c.HospitalId : null);
        }

        /// <summary>Every medicine on the patient's record (current, completed and discontinued), newest first.</summary>
        public static List<Prescription> ListMedicationHistory(string patientId)
        {
            lock (DataStore.Sync)
                return DataStore.Prescriptions.Where(p => p.PatientId == patientId)
                    .OrderByDescending(p => p.PeriodStart ?? p.PrescribedDate).ToList();
        }

        public static Prescription GetPrescription(string prescriptionId)
        {
            lock (DataStore.Sync) return DataStore.Prescriptions.FirstOrDefault(p => p.PrescriptionId == prescriptionId);
        }

        /// <summary>Adds or updates a medicine prescribed elsewhere (no consultation). Only outside records can be edited here.</summary>
        public static Prescription SaveOutsideMedication(Prescription record)
        {
            lock (DataStore.Sync)
            {
                var existing = string.IsNullOrEmpty(record.PrescriptionId) ? null
                    : DataStore.Prescriptions.FirstOrDefault(p => p.PrescriptionId == record.PrescriptionId && p.Source == "OUTSIDE");
                if (existing == null)
                {
                    record.PrescriptionId = DataStore.NextId("RX");
                    record.ConsultationId = null;
                    record.Source = "OUTSIDE";
                    record.Status = "ACTIVE";
                    record.PrescribedDate = DateTime.UtcNow;
                    var drug = CatalogService.GetDrugByName(record.MedicineName);
                    if (drug != null) { record.GenericClass = drug.GenericClass; record.TherapeuticClass = drug.TherapeuticClass; }
                    DataStore.Prescriptions.Add(record);
                    return record;
                }
                existing.MedicineName = record.MedicineName; existing.Strength = record.Strength; existing.DrugForm = record.DrugForm;
                existing.TakeInQty = record.TakeInQty; existing.TakeInUnit = record.TakeInUnit; existing.TimesPerDay = record.TimesPerDay;
                existing.TimingNi = record.TimingNi; existing.Prn = record.Prn; existing.FrequencyMode = record.FrequencyMode;
                existing.DynamicInstruction = record.DynamicInstruction; existing.Instructions = record.Instructions;
                existing.PeriodStart = record.PeriodStart; existing.SourceDetail = record.SourceDetail;
                return existing;
            }
        }

        public static Prescription AddPrescription(Prescription record)
        {
            lock (DataStore.Sync)
            {
                record.PrescriptionId = DataStore.NextId("RX");
                record.Status = "ACTIVE";
                record.PrescribedDate = DateTime.UtcNow;
                if (string.IsNullOrEmpty(record.GenericClass))
                {
                    var drug = CatalogService.GetDrugByName(record.MedicineName);
                    if (drug != null) { record.GenericClass = drug.GenericClass; record.TherapeuticClass = drug.TherapeuticClass; }
                }
                DataStore.Prescriptions.Add(record);
                return record;
            }
        }

        public static void DiscontinuePrescription(string prescriptionId)
        {
            lock (DataStore.Sync)
            {
                var p = DataStore.Prescriptions.FirstOrDefault(x => x.PrescriptionId == prescriptionId);
                if (p != null) p.Status = "DISCONTINUED";
            }
        }

        /// <summary>Active prescriptions from this doctor's most recent signed visit with the patient ("Repeat previous").</summary>
        public static List<Prescription> ListPreviousPrescriptionsByDoctor(string patientId, string doctorId, out Consultation fromVisit)
        {
            lock (DataStore.Sync)
            {
                fromVisit = DataStore.Consultations.Where(c => c.PatientId == patientId && c.DoctorId == doctorId && c.Status == "LOCKED")
                    .OrderByDescending(c => c.VisitDate).FirstOrDefault();
                if (fromVisit == null) return new List<Prescription>();
                var id = fromVisit.ConsultationId;
                return DataStore.Prescriptions.Where(p => p.ConsultationId == id && p.Status == "ACTIVE").ToList();
            }
        }

        /// <summary>Non-bypassable drug-allergy safety check (cln.usp_CheckDrugAllergyConflicts).</summary>
        public static List<ClinicalAlert> CheckDrugAllergyConflicts(string patientId, string medicineName, string composition)
        {
            var alerts = new List<ClinicalAlert>();
            var drug = (medicineName + " " + (composition ?? "")).ToLowerInvariant();
            var firstWord = (medicineName ?? "").ToLowerInvariant().Split(' ')[0];
            foreach (var a in PatientService.ListAllergies(patientId, true))
            {
                var allergy = a.AllergyName.ToLowerInvariant();
                if (drug.Contains(allergy) || (firstWord.Length > 0 && allergy.Contains(firstWord)))
                    alerts.Add(new ClinicalAlert { Severity = "CRITICAL", Type = "DRUG_ALLERGY", Message = "CRITICAL: " + medicineName + " may conflict with known allergy to " + a.AllergyName + " (" + a.Severity + ")." });
                if (allergy.Contains("penicillin") && new[] { "amoxicillin", "ampicillin", "penicillin", "augmentin" }.Any(drug.Contains)
                    && !alerts.Any(x => x.Message.Contains(a.AllergyName)))
                    alerts.Add(new ClinicalAlert { Severity = "CRITICAL", Type = "DRUG_ALLERGY", Message = "CRITICAL: " + medicineName + " belongs to the Penicillin class. Patient has Penicillin allergy (" + a.Severity + ")." });
            }
            return alerts;
        }

        /* ── Labs, referrals, follow-up ── */

        public static List<InvestigationOrder> ListOrders(string consultationId)
        {
            lock (DataStore.Sync) return DataStore.Orders.Where(o => o.ConsultationId == consultationId).OrderBy(o => o.OrderedAt).ToList();
        }

        public static InvestigationOrder AddOrder(Consultation c, LabTest test)
        {
            lock (DataStore.Sync)
            {
                if (DataStore.Orders.Any(o => o.ConsultationId == c.ConsultationId && o.TestId == test.TestId)) return null;
                var row = new InvestigationOrder
                {
                    OrderId = DataStore.NextId("ORD"), HospitalId = c.HospitalId, ConsultationId = c.ConsultationId, PatientId = c.PatientId,
                    TestId = test.TestId, TestName = test.TestName, TestType = test.TestType, Status = "ORDERED", OrderedAt = DateTime.UtcNow
                };
                DataStore.Orders.Add(row);
                return row;
            }
        }

        /// <summary>Only an order the lab has not picked up yet (ORDERED) can be removed.</summary>
        public static bool RemoveOrder(string orderId)
        {
            lock (DataStore.Sync) return DataStore.Orders.RemoveAll(o => o.OrderId == orderId && o.Status == "ORDERED") > 0;
        }

        public static List<Referral> ListReferrals(string consultationId)
        {
            lock (DataStore.Sync) return DataStore.Referrals.Where(r => r.ConsultationId == consultationId).OrderBy(r => r.CreatedAt).ToList();
        }

        public static Referral AddReferral(Consultation c, string specialty, string doctorName, string urgency, string reason)
        {
            var row = new Referral
            {
                ReferralId = DataStore.NextId("REF"), ConsultationId = c.ConsultationId, PatientId = c.PatientId, Specialty = specialty,
                DoctorName = doctorName, Urgency = urgency, Reason = reason, CreatedAt = DateTime.UtcNow
            };
            lock (DataStore.Sync) DataStore.Referrals.Add(row);
            return row;
        }

        /* ── Services & procedures (billed at sign-off from the Service Charge Master or Clinic Inventory) ── */

        public static List<ConsultationServiceItem> ListServiceItems(string consultationId)
        {
            lock (DataStore.Sync) return DataStore.ConsultationServiceItems.Where(s => s.ConsultationId == consultationId).OrderBy(s => s.CreatedAt).ToList();
        }

        /// <summary>A service / procedure, optionally with the stocked item used for it. Item stock is only checked by the caller; it is deducted at sign-off.</summary>
        public static ConsultationServiceItem AddServiceItem(Consultation c, ServiceCharge svc, int qty, string notes, string userId,
            InventoryItem item = null, int itemQty = 0)
        {
            var row = new ConsultationServiceItem
            {
                ConsultationServiceId = DataStore.NextId("CSV"), ConsultationId = c.ConsultationId, PatientId = c.PatientId,
                ServiceChargeId = svc.ServiceChargeId, ServiceName = svc.ServiceName, Category = svc.Category,
                Qty = Math.Max(1, qty), Notes = notes ?? "", AddedByUserId = userId, CreatedAt = DateTime.UtcNow
            };
            if (item != null)
            {
                row.InventoryItemId = item.InventoryItemId;
                row.ItemName = item.ItemName;
                row.ItemUnit = item.Unit;
                row.ItemQty = Math.Max(1, itemQty);
            }
            lock (DataStore.Sync) DataStore.ConsultationServiceItems.Add(row);
            return row;
        }

        public static void RemoveServiceItem(string consultationId, string itemId)
        {
            lock (DataStore.Sync) DataStore.ConsultationServiceItems.RemoveAll(s => s.ConsultationServiceId == itemId && s.ConsultationId == consultationId);
        }

        /// <summary>"Injection Administration + Monocef 1g ×2 vial (left deltoid)".</summary>
        public static string ServiceItemLabel(ConsultationServiceItem s)
        {
            return s.ServiceName + (s.Qty > 1 ? " ×" + s.Qty : "") + ItemLabel(s) + (string.IsNullOrEmpty(s.Notes) ? "" : " (" + s.Notes + ")");
        }

        /// <summary>" + Monocef 1g ×2 vial", or "" when no item was used.</summary>
        public static string ItemLabel(ConsultationServiceItem s)
        {
            return s.InventoryItemId == null ? "" : " + " + s.ItemName + " ×" + s.ItemQty + " " + s.ItemUnit;
        }

        public static void SaveFollowup(string consultationId, DateTime? date, string notes)
        {
            lock (DataStore.Sync)
            {
                var c = Get(consultationId);
                if (c == null) return;
                c.FollowupDate = date;
                c.FollowupNotes = notes;
            }
        }

        /* ── Confidential note, addenda ── */

        public static ConfidentialNote GetConfidentialNote(string consultationId, string doctorId)
        {
            lock (DataStore.Sync) return DataStore.ConfidentialNotes.FirstOrDefault(n => n.ConsultationId == consultationId && n.DoctorId == doctorId);
        }

        public static void SaveConfidentialNote(string consultationId, string doctorId, string text)
        {
            lock (DataStore.Sync)
            {
                var n = GetConfidentialNote(consultationId, doctorId);
                if (n == null)
                {
                    n = new ConfidentialNote { ConfidentialNoteId = DataStore.NextId("CNOTE"), ConsultationId = consultationId, DoctorId = doctorId, CreatedAt = DateTime.UtcNow };
                    DataStore.ConfidentialNotes.Add(n);
                }
                n.NoteText = text;
                n.UpdatedAt = DateTime.UtcNow;
            }
        }

        public static List<Addendum> ListAddenda(string consultationId)
        {
            lock (DataStore.Sync) return DataStore.Addenda.Where(a => a.ConsultationId == consultationId).OrderBy(a => a.CreatedAt).ToList();
        }

        public static void AddAddendum(string consultationId, string doctorId, string category, string text)
        {
            lock (DataStore.Sync)
                DataStore.Addenda.Add(new Addendum { AddendumId = DataStore.NextId("ADD"), ConsultationId = consultationId, DoctorId = doctorId, Category = category, AddendumText = text, CreatedAt = DateTime.UtcNow });
        }

        /* ── Sketches (drawing pad) ── */

        public static List<ConsultationSketch> ListPatientSketches(string patientId)
        {
            lock (DataStore.Sync) return DataStore.Sketches.Where(s => s.PatientId == patientId).OrderByDescending(s => s.CreatedAt).ToList();
        }

        public static ConsultationSketch SaveSketch(Consultation c, string doctorId, string title, string imageData)
        {
            var row = new ConsultationSketch
            {
                SketchId = DataStore.NextId("SKT"), ConsultationId = c.ConsultationId, PatientId = c.PatientId, DoctorId = doctorId,
                Title = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim(), ImageData = imageData, CreatedAt = DateTime.UtcNow
            };
            lock (DataStore.Sync) DataStore.Sketches.Add(row);
            return row;
        }

        /* ── Sign-off ── */

        /// <summary>Four-point attestation: locks the record and completes the linked appointment (IN_CONSULT → COMPLETED).</summary>
        public static void SignOff(string consultationId, string doctorId)
        {
            lock (DataStore.Sync)
            {
                var c = Get(consultationId);
                if (c == null || c.IsLocked) return;
                var hash = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(consultationId + ":" + doctorId + ":" + DateTime.UtcNow.Ticks));
                DataStore.Attestations.Add(new ConsultationAttestation { AttestationId = DataStore.NextId("ATT"), ConsultationId = consultationId, DoctorId = doctorId, SignedAt = DateTime.UtcNow, SignatureHash = hash });
                c.IsLocked = true;
                c.Status = "LOCKED";
                var appt = SchedulingService.AppointmentForConsultation(c);
                if (appt != null && appt.Status == "IN_CONSULT")
                {
                    appt.Status = "COMPLETED";
                    appt.CompletedAt = DateTime.UtcNow;
                }
            }
        }
    }
}
