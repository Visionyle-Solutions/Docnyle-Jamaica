using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System;
using OpdProject.Helpers;
using OpdProject.Models;
using OpdProject.Services.Api;
using OpdProject.Web;

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
    public static partial class ConsultationService
    {
        private static readonly CommonHelper cnHelperObj = new CommonHelper();

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
            if (Api) return ApiGet(consultationId);
            lock (DataStore.Sync) return DataStore.Consultations.FirstOrDefault(c => c.ConsultationId == consultationId);
        }

        /// <summary>Returns the doctor's IN_PROGRESS consultation for the patient, creating one if none exists.</summary>
        public static Consultation GetOrCreate(string patientId, string doctorId, string hospitalId)
        {
            if (Api) return ApiGetOrCreate(patientId, doctorId, null);
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
            if (Api)
            {
                var c = DocNyle.OrNull(() => cnHelperObj.GetInProgressConsultation(AppSession.ApiHospitalId, AppSession.ApiUserId, patientId, doctorId, AppSession.ApiHashValue).EnsureSuccess())?.Consultation;
                return c != null ? (ApiGet(c.ConsultationId) ?? ToModel(c)) : null;
            }
            lock (DataStore.Sync) return DataStore.Consultations.FirstOrDefault(x => x.PatientId == patientId && x.DoctorId == doctorId && x.Status == "IN_PROGRESS");
        }

        /// <summary>
        /// Opens the doctor's consultation for a patient and links today's checked-in appointment:
        /// opening it moves CHECKED_IN → IN_CONSULT (sign-off later moves it to COMPLETED).
        /// </summary>
        public static Consultation Open(string patientId, string doctorId, string hospitalId)
        {
            if (Api) return ApiOpen(patientId, doctorId, hospitalId);
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
            if (Api)
                return (cnHelperObj.ListConsultationsForDate(AppSession.ApiHospitalId, AppSession.ApiUserId, DocNyle.Date(date), AppSession.ApiHashValue).EnsureSuccess().Consultations ?? new List<CON_ConsultationSummaryInfo>())
                    .Select(s => ToModel(new CON_ConsultationInfo
                    {
                        ConsultationId = s.ConsultationId, PatientId = s.PatientId, DoctorId = s.DoctorId, AppointmentId = s.AppointmentId,
                        Status = s.Status, VisitDate = s.VisitDate, VisitLocalDate = DocNyle.Date(date),
                    })).ToList();
            lock (DataStore.Sync) return DataStore.Consultations.Where(c => c.HospitalId == hospitalId && c.VisitLocalDate == date.Date).ToList();
        }

        /// <summary>Signed visits of the patient, newest first, summarised for the context panel.</summary>
        public static List<PastVisit> ListPastVisits(string patientId)
        {
            if (Api) return ApiPastVisits(patientId);
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
            if (Api)
            {
                var c = ApiGet(consultationId);
                if (c == null || c.SkippedSteps.Contains(step)) return;
                ApiSaveSoap(c, c.SubjectiveText, c.ObjectiveText, c.PlanText, c.SubjectivePersonal, c.ObjectivePersonal, c.PlanPersonal,
                    c.SkippedSteps.Concat(new[] { step }).ToList());
                return;
            }
            lock (DataStore.Sync)
            {
                var c = Get(consultationId);
                if (c != null && !c.SkippedSteps.Contains(step)) c.SkippedSteps.Add(step);
            }
        }

        /* ── Vitals ── */

        public static ConsultationVitals GetVitals(string consultationId)
        {
            if (Api) return ApiGetVitals(consultationId);
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
            if (DataSource.IsApi("Hospital")) cfg = HospitalService.ListVitalsConfig(hospitalId, false).FirstOrDefault(v => v.VitalCode == code);
            else lock (DataStore.Sync) cfg = DataStore.VitalsConfig.FirstOrDefault(v => v.HospitalId == hospitalId && v.VitalCode == code);
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
            if (Api) return ApiSaveVitals(consultationId, values);
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
            if (Api) return ApiSymptomCatalog();
            lock (DataStore.Sync) return DataStore.SymptomCatalog.Where(s => s.HospitalId == hospitalId && s.IsActive).ToList();
        }

        public static string SymptomLabel(string hospitalId, string code)
        {
            if (Api)
            {
                var hit = ApiSymptomCatalog().FirstOrDefault(x => x.SymptomCode == code);
                return hit != null ? hit.DisplayLabel : code;
            }
            lock (DataStore.Sync)
            {
                var s = DataStore.SymptomCatalog.FirstOrDefault(x => x.HospitalId == hospitalId && x.SymptomCode == code);
                return s != null ? s.DisplayLabel : code;
            }
        }

        public static Dictionary<string, string> GetSymptomStates(string consultationId)
        {
            if (Api) return new Dictionary<string, string>(ApiSymptomStates(consultationId));
            lock (DataStore.Sync)
                return DataStore.Symptoms.Where(s => s.ConsultationId == consultationId).ToDictionary(s => s.SymptomCode, s => s.State);
        }

        /// <summary>Cycles a complaint unset → yes → no → unset and returns the new state.</summary>
        public static string CycleSymptom(string consultationId, string code)
        {
            if (Api)
            {
                string cur;
                if (!ApiSymptomStates(consultationId).TryGetValue(code, out cur)) cur = "UNSET";
                var next = cur == "UNSET" ? "YES" : cur == "YES" ? "NO" : "UNSET";
                ApiSetSymptom(consultationId, code, next);
                return next;
            }
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
            if (Api)
            {
                var existing = ApiSymptomCatalog().FirstOrDefault(s => string.Equals(s.DisplayLabel, label, StringComparison.OrdinalIgnoreCase));
                var code = existing != null ? existing.SymptomCode : null;
                if (code == null)
                {
                    var created = cnHelperObj.CreateCustomSymptom(new CON_CreateCustomSymptomReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, DisplayLabel = label }).EnsureSuccess().Symptom;
                    HospitalCache.Clear("symptom-catalog");
                    code = created != null ? created.SymptomCode : label;
                }
                ApiSetSymptom(consultationId, code, "YES");
                return;
            }
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
            if (Api) return (cnHelperObj.SearchIcd10Catalog(AppSession.ApiHospitalId, AppSession.ApiUserId, term.Trim(), AppSession.ApiHashValue).EnsureSuccess().Icd10Codes ?? new List<CON_Icd10Info>()).Select(ToModel).ToList();
            lock (DataStore.Sync)
                return DataStore.Icd10Catalog.Where(r => r.Icd10Code.ToLowerInvariant().StartsWith(t) || r.Description.ToLowerInvariant().Contains(t)
                    || (r.SynonymTerms ?? "").ToLowerInvariant().Split(',').Any(s => s.Trim().Contains(t))).ToList();
        }

        public static Icd10Entry GetIcd10(string code)
        {
            if (Api)
                return string.IsNullOrEmpty(code) ? null
                    : (cnHelperObj.SearchIcd10Catalog(AppSession.ApiHospitalId, AppSession.ApiUserId, code, AppSession.ApiHashValue).EnsureSuccess().Icd10Codes ?? new List<CON_Icd10Info>()).Where(i => i.Code == code).Select(ToModel).FirstOrDefault();
            lock (DataStore.Sync) return DataStore.Icd10Catalog.FirstOrDefault(r => r.Icd10Code == code);
        }

        public static List<ConsultationDiagnosis> ListDiagnoses(string consultationId)
        {
            if (Api)
                return RequestCache.Get("diagnoses:" + consultationId, () =>
                    (cnHelperObj.ListConsultationDiagnoses(AppSession.ApiHospitalId, AppSession.ApiUserId, consultationId, AppSession.ApiHashValue).EnsureSuccess().Diagnoses ?? new List<CON_DiagnosisInfo>()).Select(ToModel).OrderBy(d => d.SequenceOrder).ToList());
            lock (DataStore.Sync) return DataStore.Diagnoses.Where(d => d.ConsultationId == consultationId).OrderBy(d => d.SequenceOrder).ToList();
        }

        /// <summary>Code + clinical reasoning saved as one record; the first diagnosis on a visit is primary.</summary>
        public static ConsultationDiagnosis AddDiagnosis(string consultationId, string code, string description, string notes, string laterality, string acuteChronic)
        {
            if (Api)
            {
                var d = cnHelperObj.CreateDiagnosis(new CON_CreateDiagnosisReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    ConsultationId = consultationId, Icd10Code = code, Icd10Description = description, ClinicalNotes = notes,
                    Laterality = string.IsNullOrEmpty(laterality) ? null : laterality, AcuteChronic = string.IsNullOrEmpty(acuteChronic) ? null : acuteChronic,
                }).EnsureSuccess().Diagnosis;
                RequestCache.Remove("diagnoses:" + consultationId);
                return d != null ? ToModel(d) : null;
            }
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
            if (Api) { cnHelperObj.DeleteDiagnosis(new CON_DeleteDiagnosisReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, DiagnosisId = diagnosisId }).EnsureSuccess(); RequestCache.RemovePrefix("diagnoses:"); return; }
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
            if (Api)
            {
                var c = ApiGet(consultationId);
                if (c != null) ApiSaveSoap(c, s, o, p, sPersonal, oPersonal, pPersonal, c.SkippedSteps);
                return;
            }
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
            if (Api) return RequestCache.Get("rx:" + consultationId, () => Map(cnHelperObj.ListPrescriptions(AppSession.ApiHospitalId, AppSession.ApiUserId, consultationId, AppSession.ApiHashValue).EnsureSuccess()).Where(p => p.Status == "ACTIVE").ToList());
            lock (DataStore.Sync) return DataStore.Prescriptions.Where(p => p.ConsultationId == consultationId && p.Status == "ACTIVE").ToList();
        }

        /// <summary>Medicines the patient is taking now: ACTIVE prescriptions from any visit whose course hasn't ended,
        /// plus outside medicines recorded on the patient record.</summary>
        public static List<Prescription> ListActiveMedications(string patientId)
        {
            if (Api) return Map(cnHelperObj.ListActiveMedicationsForPatient(AppSession.ApiHospitalId, AppSession.ApiUserId, patientId, AppSession.ApiHashValue).EnsureSuccess()).Where(IsCurrent).ToList();
            lock (DataStore.Sync) return DataStore.Prescriptions.Where(p => p.PatientId == patientId && IsCurrent(p)).ToList();
        }

        /// <summary>ACTIVE and its course end date (if any) not yet passed.</summary>
        public static bool IsCurrent(Prescription p)
        {
            if (p.Status != "ACTIVE" || !p.PeriodEnd.HasValue) return p.Status == "ACTIVE";
            if (Api) return p.PeriodEnd.Value.Date >= TimeZoneService.Today(CallerHospital);
            var c = Get(p.ConsultationId);
            return p.PeriodEnd.Value.Date >= TimeZoneService.Today(c != null ? c.HospitalId : null);
        }

        /// <summary>Every medicine on the patient's record (current, completed and discontinued), newest first.</summary>
        public static List<Prescription> ListMedicationHistory(string patientId)
        {
            if (Api)
                return RequestCache.Get("rx-history:" + patientId, () =>
                    Map(cnHelperObj.ListPatientMedicationHistory(AppSession.ApiHospitalId, AppSession.ApiUserId, patientId, AppSession.ApiHashValue).EnsureSuccess()).OrderByDescending(p => p.PeriodStart ?? p.PrescribedDate).ToList());
            lock (DataStore.Sync)
                return DataStore.Prescriptions.Where(p => p.PatientId == patientId)
                    .OrderByDescending(p => p.PeriodStart ?? p.PrescribedDate).ToList();
        }

        /// <summary>API mode reads the prescription from the patient's medication history, so <paramref name="patientId"/> is required there.</summary>
        public static Prescription GetPrescription(string prescriptionId, string patientId = null)
        {
            if (Api) return string.IsNullOrEmpty(patientId) ? null : ListMedicationHistory(patientId).FirstOrDefault(p => p.PrescriptionId == prescriptionId);
            lock (DataStore.Sync) return DataStore.Prescriptions.FirstOrDefault(p => p.PrescriptionId == prescriptionId);
        }

        /// <summary>Adds or updates a medicine prescribed elsewhere (no consultation). Only outside records can be edited here.</summary>
        public static Prescription SaveOutsideMedication(Prescription record)
        {
            if (Api) { RequestCache.Remove("rx-history:" + record.PatientId); return ApiSaveOutsideMedication(record); }
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
            if (Api) { RequestCache.Remove("rx:" + record.ConsultationId); return ApiAddPrescription(record); }
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
            if (Api)
            {
                cnHelperObj.CancelPrescription(new CON_CancelPrescriptionReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, PrescriptionId = prescriptionId }).EnsureSuccess();
                RequestCache.RemovePrefix("rx");
                return;
            }
            lock (DataStore.Sync)
            {
                var p = DataStore.Prescriptions.FirstOrDefault(x => x.PrescriptionId == prescriptionId);
                if (p != null) p.Status = "DISCONTINUED";
            }
        }

        /// <summary>Active prescriptions from this doctor's most recent signed visit with the patient ("Repeat previous").</summary>
        public static List<Prescription> ListPreviousPrescriptionsByDoctor(string patientId, string doctorId, out Consultation fromVisit)
        {
            if (Api)
            {
                var prev = cnHelperObj.ListPreviousPrescriptionsByDoctor(AppSession.ApiHospitalId, AppSession.ApiUserId, patientId, doctorId, AppSession.ApiHashValue).EnsureSuccess();
                fromVisit = prev.Found && prev.Consultation != null ? (ApiGet(prev.Consultation.ConsultationId) ?? new Consultation
                {
                    ConsultationId = prev.Consultation.ConsultationId, PatientId = patientId, DoctorId = doctorId, HospitalId = CallerHospital,
                    VisitLocalDate = DocNyle.LocalOrMin(prev.Consultation.VisitDate).Date, Status = "LOCKED", IsLocked = true,
                }) : null;
                return fromVisit == null ? new List<Prescription>() : (prev.Prescriptions ?? new List<CON_PrescriptionInfo>()).Select(ToModel).Where(p => p.Status == "ACTIVE").ToList();
            }
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
            if (Api)
                return (cnHelperObj.CheckDrugAllergyConflicts(AppSession.ApiHospitalId, AppSession.ApiUserId, patientId, medicineName, composition, AppSession.ApiHashValue).EnsureSuccess().Alerts ?? new List<CON_DrugAllergyAlertInfo>())
                    .Select(a => new ClinicalAlert { Severity = string.IsNullOrEmpty(a.Severity) ? "CRITICAL" : a.Severity, Type = "DRUG_ALLERGY", Message = a.Message }).ToList();
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
            if (Api)
                return RequestCache.Get("orders:" + consultationId, () =>
                    (cnHelperObj.ListInvestigationOrders(AppSession.ApiHospitalId, AppSession.ApiUserId, consultationId, AppSession.ApiHashValue).EnsureSuccess().Orders ?? new List<CON_InvestigationOrderInfo>()).Select(ToModel).OrderBy(o => o.OrderedAt).ToList());
            lock (DataStore.Sync) return DataStore.Orders.Where(o => o.ConsultationId == consultationId).OrderBy(o => o.OrderedAt).ToList();
        }

        public static InvestigationOrder AddOrder(Consultation c, LabTest test)
        {
            if (Api)
            {
                if (ListOrders(c.ConsultationId).Any(x => x.TestId == test.TestId)) return null;
                var created = cnHelperObj.CreateInvestigationOrder(new CON_CreateInvestigationOrderReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    ConsultationId = c.ConsultationId, PatientId = c.PatientId, TestId = test.TestId, TestName = test.TestName, TestType = test.TestType,
                }).EnsureSuccess().Order;
                RequestCache.Remove("orders:" + c.ConsultationId);
                return created != null ? ToModel(created) : null;
            }
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
            if (Api)
            {
                // 50218: the lab has already picked the order up (or the visit is signed).
                try { cnHelperObj.DeleteInvestigationOrder(new CON_DeleteInvestigationOrderReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, OrderId = orderId }).EnsureSuccess(); }
                catch (ApiException ex) when (ex.ErrorNumber == 50218) { return false; }
                RequestCache.RemovePrefix("orders:");
                return true;
            }
            lock (DataStore.Sync) return DataStore.Orders.RemoveAll(o => o.OrderId == orderId && o.Status == "ORDERED") > 0;
        }

        public static List<Referral> ListReferrals(string consultationId)
        {
            if (Api) return (cnHelperObj.ListReferrals(AppSession.ApiHospitalId, AppSession.ApiUserId, consultationId, AppSession.ApiHashValue).EnsureSuccess().Referrals ?? new List<CON_ReferralInfo>()).Select(ToModel).OrderBy(x => x.CreatedAt).ToList();
            lock (DataStore.Sync) return DataStore.Referrals.Where(r => r.ConsultationId == consultationId).OrderBy(r => r.CreatedAt).ToList();
        }

        public static Referral AddReferral(Consultation c, string specialty, string doctorName, string urgency, string reason)
        {
            if (Api)
            {
                var saved = cnHelperObj.CreateReferral(new CON_CreateReferralReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    ConsultationId = c.ConsultationId, PatientId = c.PatientId, Specialty = specialty, DoctorName = doctorName, Urgency = urgency, Reason = reason,
                }).EnsureSuccess().Referral;
                return saved != null ? ToModel(saved) : null;
            }
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
            if (Api) return ApiServiceItems(consultationId);
            lock (DataStore.Sync) return DataStore.ConsultationServiceItems.Where(s => s.ConsultationId == consultationId).OrderBy(s => s.CreatedAt).ToList();
        }

        /// <summary>A service / procedure, optionally with the stocked item used for it. Item stock is only checked by the caller; it is deducted at sign-off.</summary>
        public static ConsultationServiceItem AddServiceItem(Consultation c, ServiceCharge svc, int qty, string notes, string userId,
            InventoryItem item = null, int itemQty = 0)
        {
            if (Api)
            {
                var saved = cnHelperObj.CreateConsultationServiceItem(new CON_CreateConsultationServiceItemReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    ConsultationId = c.ConsultationId, ServiceChargeId = svc.ServiceChargeId, Qty = Math.Max(1, qty), Notes = notes ?? "",
                    InventoryItemId = item != null ? item.InventoryItemId : null, ItemQty = item != null ? Math.Max(1, itemQty) : (int?)null,
                }).EnsureSuccess().ServiceItem;
                RequestCache.Remove("service-items:" + c.ConsultationId);
                return saved != null ? ToModel(saved) : null;
            }
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
            if (Api)
            {
                cnHelperObj.DeleteConsultationServiceItem(new CON_DeleteConsultationServiceItemReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, ConsultationId = consultationId, ConsultationServiceId = itemId }).EnsureSuccess();
                RequestCache.Remove("service-items:" + consultationId);
                return;
            }
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
            if (Api)
            {
                cnHelperObj.SaveFollowup(new CON_SaveFollowupReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, ConsultationId = consultationId, FollowUpDate = DocNyle.Date(date), FollowUpNotes = notes ?? "" }).EnsureSuccess();
                Forget(consultationId);
                return;
            }
            lock (DataStore.Sync)
            {
                var c = Get(consultationId);
                if (c == null) return;
                c.FollowupDate = date;
                c.FollowupNotes = notes;
            }
        }

        /* ── Confidential note, addenda ── */

        /// <summary>API mode: only the author can read a confidential note (the service answers Forbidden for anyone else → null).</summary>
        public static ConfidentialNote GetConfidentialNote(string consultationId, string doctorId)
        {
            if (Api)
            {
                if (AppSession.ApiUser == null || AppSession.ApiUser.UserId != doctorId) return null;
                return RequestCache.Get("confidential:" + consultationId, () =>
                {
                    CON_ConfidentialNoteInfo n;
                    try { n = DocNyle.OrNull(() => cnHelperObj.GetConfidentialNote(AppSession.ApiHospitalId, AppSession.ApiUserId, consultationId, AppSession.ApiHashValue).EnsureSuccess())?.ConfidentialNote; }
                    catch (ApiException ex) when (ex.Kind == ApiFaultKind.Forbidden) { n = null; }
                    return n == null ? null : new ConfidentialNote
                    {
                        ConfidentialNoteId = n.ConfidentialNoteId, ConsultationId = n.ConsultationId, DoctorId = n.DoctorId, NoteText = n.NoteText,
                        CreatedAt = DocNyle.InstantOrMin(n.CreatedAt), UpdatedAt = DocNyle.InstantOrMin(n.UpdatedAt),
                    };
                });
            }
            lock (DataStore.Sync) return DataStore.ConfidentialNotes.FirstOrDefault(n => n.ConsultationId == consultationId && n.DoctorId == doctorId);
        }

        public static void SaveConfidentialNote(string consultationId, string doctorId, string text)
        {
            if (Api)
            {
                cnHelperObj.SaveConfidentialNote(new CON_SaveConfidentialNoteReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, ConsultationId = consultationId, NoteText = text }).EnsureSuccess();
                RequestCache.Remove("confidential:" + consultationId);
                return;
            }
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
            if (Api)
                return (cnHelperObj.ListAddendums(AppSession.ApiHospitalId, AppSession.ApiUserId, consultationId, AppSession.ApiHashValue).EnsureSuccess().Addendums ?? new List<CON_AddendumInfo>()).Select(a => new Addendum
                {
                    AddendumId = a.AddendumId, ConsultationId = a.ConsultationId, DoctorId = a.DoctorId, Category = a.Category, AddendumText = a.AddendumText,
                    CreatedAt = DocNyle.InstantOrMin(a.CreatedAt),
                }).OrderBy(a => a.CreatedAt).ToList();
            lock (DataStore.Sync) return DataStore.Addenda.Where(a => a.ConsultationId == consultationId).OrderBy(a => a.CreatedAt).ToList();
        }

        public static void AddAddendum(string consultationId, string doctorId, string category, string text)
        {
            if (Api) { cnHelperObj.CreateAddendum(new CON_CreateAddendumReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, ConsultationId = consultationId, Category = category, AddendumText = text }).EnsureSuccess(); return; }
            lock (DataStore.Sync)
                DataStore.Addenda.Add(new Addendum { AddendumId = DataStore.NextId("ADD"), ConsultationId = consultationId, DoctorId = doctorId, Category = category, AddendumText = text, CreatedAt = DateTime.UtcNow });
        }

        /* ── Sketches (drawing pad) ── */

        public static List<ConsultationSketch> ListPatientSketches(string patientId)
        {
            if (Api) return (cnHelperObj.ListPatientSketches(AppSession.ApiHospitalId, AppSession.ApiUserId, patientId, AppSession.ApiHashValue).EnsureSuccess().Sketches ?? new List<CON_SketchInfo>()).Select(ToModel).OrderByDescending(s => s.CreatedAt).ToList();
            lock (DataStore.Sync) return DataStore.Sketches.Where(s => s.PatientId == patientId).OrderByDescending(s => s.CreatedAt).ToList();
        }

        public static ConsultationSketch SaveSketch(Consultation c, string doctorId, string title, string imageData)
        {
            if (Api)
            {
                var saved = cnHelperObj.SaveSketch(new CON_SaveSketchReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    ConsultationId = c.ConsultationId, PatientId = c.PatientId, Title = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim(), ImageDataBase64 = imageData,
                }).EnsureSuccess().Sketch;
                return saved != null ? ToModel(saved) : null;
            }
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
        /// <summary>
        /// API mode: ConfirmSignOff locks the record, completes the linked appointment and deducts the stock of the visit's
        /// items in one transaction (fault 50040 when stock ran out).
        /// </summary>
        public static void SignOff(string consultationId, string doctorId)
        {
            if (Api)
            {
                cnHelperObj.ConfirmSignOff(new CON_ConfirmSignOffReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, ConsultationId = consultationId }).EnsureSuccess();
                Forget(consultationId);
                return;
            }
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

        private static bool Api { get { return DataSource.IsApi("Consultation"); } }

        private static string CallerHospital { get { return AppSession.ApiUser != null ? AppSession.ApiUser.HospitalId : null; } }

        /* ── Consultation record ── */

        private static Consultation ToModel(CON_ConsultationInfo c)
        {
            if (c == null) return null;
            var detail = c as CON_ConsultationDetailInfo;
            var zone = string.IsNullOrEmpty(c.TimeZoneId) ? TimeZoneService.HospitalZoneId(CallerHospital) : c.TimeZoneId;
            var visit = DocNyle.ParseInstant(c.VisitDate) ?? DateTime.UtcNow;
            var m = new Consultation
            {
                ConsultationId = c.ConsultationId, HospitalId = CallerHospital, PatientId = c.PatientId, DoctorId = c.DoctorId,
                AppointmentId = c.AppointmentId, VisitDate = visit, TimeZoneId = zone,
                VisitLocalDate = (DocNyle.ParseLocal(c.VisitLocalDate) ?? TimeZoneService.LocalDate(visit, zone)).Date,
                Status = c.Status, IsLocked = c.IsLocked || c.Status == "LOCKED",
            };
            if (detail != null)
            {
                m.SubjectiveText = detail.SubjectiveText; m.ObjectiveText = detail.ObjectiveText; m.PlanText = detail.PlanText;
                m.SubjectivePersonal = detail.SubjectivePersonal; m.ObjectivePersonal = detail.ObjectivePersonal; m.PlanPersonal = detail.PlanPersonal;
                m.FollowupDate = DocNyle.ParseLocal(detail.FollowupDate); m.FollowupNotes = detail.FollowupNotes;
                m.SkippedSteps = detail.SkippedSteps ?? new List<string>();
            }
            return m;
        }

        /// <summary>GetConsultationById (the full record with SOAP, follow-up and skipped steps), cached for the request.</summary>
        private static Consultation ApiGet(string consultationId)
        {
            if (string.IsNullOrEmpty(consultationId)) return null;
            return RequestCache.Get("consultation:" + consultationId,
                () => ToModel(DocNyle.OrNull(() => cnHelperObj.GetConsultationById(AppSession.ApiHospitalId, AppSession.ApiUserId, consultationId, AppSession.ApiHashValue).EnsureSuccess())?.Consultation));
        }

        private static void Forget(string consultationId) { RequestCache.Remove("consultation:" + consultationId); }

        private static Consultation ApiGetOrCreate(string patientId, string doctorId, string appointmentId)
        {
            var c = cnHelperObj.GetOrCreateConsultation(new CON_GetOrCreateConsultationReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, PatientId = patientId, DoctorId = doctorId, AppointmentId = appointmentId }).EnsureSuccess().Consultation;
            if (c == null) throw new ApiException(ApiFaultKind.BusinessRule, "GetOrCreateConsultation", "The consultation could not be opened.");
            Forget(c.ConsultationId);
            return ApiGet(c.ConsultationId) ?? ToModel(c);
        }

        /// <summary>
        /// GetOrCreateConsultation with today's checked-in / in-consult appointment (so the link is stored by the service,
        /// never by mutating a returned object), then CHECKED_IN → IN_CONSULT.
        /// </summary>
        private static Consultation ApiOpen(string patientId, string doctorId, string hospitalId)
        {
            var appt = SchedulingService.ListAppointments(hospitalId, doctorId, TimeZoneService.Today(hospitalId))
                .FirstOrDefault(a => a.PatientId == patientId && (a.Status == "CHECKED_IN" || a.Status == "IN_CONSULT"));
            var existing = DocNyle.OrNull(() => cnHelperObj.GetInProgressConsultation(AppSession.ApiHospitalId, AppSession.ApiUserId, patientId, doctorId, AppSession.ApiHashValue).EnsureSuccess())?.Consultation;
            var c = existing != null && (appt == null || existing.AppointmentId != null)
                ? ApiGet(existing.ConsultationId)
                : ApiGetOrCreate(patientId, doctorId, appt != null ? appt.AppointmentId : null);
            if (c != null && appt != null && !c.IsLocked && appt.Status == "CHECKED_IN")
                SchedulingService.UpdateStatus(appt.AppointmentId, "IN_CONSULT", doctorId);
            return c;
        }

        private static List<PastVisit> ApiPastVisits(string patientId)
        {
            return (cnHelperObj.ListPatientPastVisits(AppSession.ApiHospitalId, AppSession.ApiUserId, patientId, AppSession.ApiHashValue).EnsureSuccess().PastVisits ?? new List<CON_PastVisitInfo>()).Select(v => new PastVisit
            {
                ConsultationId = v.ConsultationId, VisitDate = (DocNyle.ParseLocal(v.VisitDate) ?? DocNyle.ParseInstant(v.VisitAt) ?? DateTime.MinValue).Date,
                DoctorName = v.DoctorName, ChiefComplaint = string.IsNullOrEmpty(v.ChiefComplaint) ? "—" : v.ChiefComplaint,
                PrimaryDiagnosis = string.IsNullOrEmpty(v.PrimaryDiagnosis) ? "—" : v.PrimaryDiagnosis,
            }).OrderByDescending(v => v.VisitDate).ToList();
        }

        /// <summary>SaveSoapNotes carries the SOAP text and the skipped steps together, so both are always sent.</summary>
        private static void ApiSaveSoap(Consultation c, string s, string o, string p, string sp, string op, string pp, List<string> skipped)
        {
            cnHelperObj.SaveSoapNotes(new CON_SaveSoapNotesReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                ConsultationId = c.ConsultationId, SubjectiveText = s ?? "", ObjectiveText = o ?? "", PlanText = p ?? "",
                SubjectivePersonal = sp ?? "", ObjectivePersonal = op ?? "", PlanPersonal = pp ?? "", SkippedSteps = skipped ?? new List<string>(),
            }).EnsureSuccess();
            Forget(c.ConsultationId);
        }

        /* ── Vitals ── */

        private static ConsultationVitals ToModel(CON_VitalsInfo v)
        {
            if (v == null) return null;
            Func<decimal?, double?> d = x => x.HasValue ? (double)x.Value : (double?)null;
            var m = new ConsultationVitals
            {
                VitalId = v.ConsultationVitalId, ConsultationId = v.ConsultationId, TempValue = d(v.TempValue), BpSystolic = d(v.BpSystolic),
                BpDiastolic = d(v.BpDiastolic), PulseBpm = d(v.PulseBpm), SpO2 = d(v.SpO2), HeightCm = d(v.HeightCm), WeightKg = d(v.WeightKg),
                RespRate = d(v.RespRate), PainScale = d(v.PainScale), WaistCircCm = d(v.WaistCircCm), HeadCircCm = d(v.HeadCircCm), Bmi = d(v.Bmi),
                RecordedAt = DocNyle.InstantOrMin(v.RecordedAt), RecordedBy = v.RecordedBy,
            };
            foreach (var kv in v.CustomValues ?? new List<KeyValueInfo>())
            {
                double x;
                if (kv.Key != null && double.TryParse(kv.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out x)) m.CustomValues[kv.Key] = x;
            }
            return m;
        }

        private static ConsultationVitals ApiGetVitals(string consultationId)
        {
            return RequestCache.Get("vitals:" + consultationId, () => ToModel(cnHelperObj.GetConsultationVitals(AppSession.ApiHospitalId, AppSession.ApiUserId, consultationId, AppSession.ApiHashValue).EnsureSuccess().Vitals));
        }

        /// <summary>SaveVitals replaces the visit's row with the merged values; BMI is computed by the procedure.</summary>
        private static ConsultationVitals ApiSaveVitals(string consultationId, IDictionary<string, double?> values)
        {
            var merged = ApiGetVitals(consultationId) ?? new ConsultationVitals { ConsultationId = consultationId };
            foreach (var kv in values) SetVital(merged, kv.Key, kv.Value);
            Func<double?, decimal?> m = x => x.HasValue ? (decimal)x.Value : (decimal?)null;
            var saved = cnHelperObj.SaveVitals(new CON_SaveVitalsReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                ConsultationId = consultationId, TempValue = m(merged.TempValue), BpSystolic = m(merged.BpSystolic), BpDiastolic = m(merged.BpDiastolic),
                PulseBpm = m(merged.PulseBpm), SpO2 = m(merged.SpO2), HeightCm = m(merged.HeightCm), WeightKg = m(merged.WeightKg),
                RespRate = m(merged.RespRate), PainScale = m(merged.PainScale), WaistCircCm = m(merged.WaistCircCm), HeadCircCm = m(merged.HeadCircCm),
                CustomValues = (merged.CustomValues ?? new Dictionary<string, double?>()).Where(kv => kv.Value.HasValue)
                    .Select(kv => new KeyValueInfo { Key = kv.Key, Value = kv.Value.Value.ToString(CultureInfo.InvariantCulture) }).ToList(),
            }).EnsureSuccess().Vitals;
            RequestCache.Remove("vitals:" + consultationId);
            return ToModel(saved) ?? merged;
        }

        /* ── Complaints ── */

        private static List<SymptomCatalogItem> ApiSymptomCatalog()
        {
            return HospitalCache.Get("symptom-catalog", () =>
                (cnHelperObj.ListSymptomCatalog(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Symptoms ?? new List<CON_SymptomInfo>()).Select(s => new SymptomCatalogItem
                {
                    SymptomCatalogId = s.SymptomCatalogId, SymptomCode = s.SymptomCode, DisplayLabel = s.SymptomName, IsQuickPick = s.IsQuickPick, IsActive = true,
                }).ToList());
        }

        /// <summary>The API's NOT_ASKED is the page's UNSET.</summary>
        private static string ToPageState(string apiState) { return apiState == "NOT_ASKED" || string.IsNullOrEmpty(apiState) ? "UNSET" : apiState; }

        private static Dictionary<string, string> ApiSymptomStates(string consultationId)
        {
            return RequestCache.Get("symptoms:" + consultationId, () =>
                (cnHelperObj.ListConsultationSymptoms(AppSession.ApiHospitalId, AppSession.ApiUserId, consultationId, AppSession.ApiHashValue).EnsureSuccess().Symptoms ?? new List<CON_ConsultationSymptomInfo>())
                    .Where(s => !string.IsNullOrEmpty(s.SymptomCode)).GroupBy(s => s.SymptomCode)
                    .ToDictionary(g => g.Key, g => ToPageState(g.First().State)));
        }

        private static void ApiSetSymptom(string consultationId, string code, string pageState)
        {
            cnHelperObj.UpdateSymptomState(new CON_UpdateSymptomStateReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                ConsultationId = consultationId, SymptomName = code, State = pageState == "UNSET" ? "NOT_ASKED" : pageState,
            }).EnsureSuccess();
            RequestCache.Remove("symptoms:" + consultationId);
        }

        /* ── Diagnoses ── */

        private static ConsultationDiagnosis ToModel(CON_DiagnosisInfo d)
        {
            return new ConsultationDiagnosis
            {
                DiagnosisId = d.ConsultationDiagnosisId, ConsultationId = d.ConsultationId, Icd10Code = d.Icd10Code, Icd10Description = d.Description,
                ClinicalNotes = d.ClinicalNotes, Laterality = d.Laterality, AcuteChronic = d.Chronicity, IsPrimary = d.IsPrimary, SequenceOrder = d.SequenceOrder ?? 0,
            };
        }

        private static Icd10Entry ToModel(CON_Icd10Info i)
        {
            return new Icd10Entry { Icd10CatalogId = i.Icd10CatalogId, Icd10Code = i.Code, Description = i.Description, SynonymTerms = i.Synonyms };
        }

        /* ── Prescriptions ── */

        private static Prescription ToModel(CON_PrescriptionInfo p)
        {
            return new Prescription
            {
                PrescriptionId = p.PrescriptionId, ConsultationId = p.ConsultationId, PatientId = p.PatientId, Category = p.Category,
                MedicineName = p.MedicineName, Strength = p.Strength, DrugForm = p.DrugForm, DeliveryRoute = p.DeliveryRoute, TakeInUnit = p.TakeInUnit,
                TakeInQty = (double)(p.TakeInQty ?? 0), TimesPerDay = p.TimesPerDay ?? 0, DurationValue = p.DurationValue ?? 0, DurationUnit = p.DurationUnit,
                PeriodStart = DocNyle.ParseLocal(p.PeriodStart), PeriodEnd = DocNyle.ParseLocal(p.PeriodEnd), TimingBf = p.TimingBf == true,
                TimingAn = p.TimingAn == true, TimingNi = p.TimingNi == true, TimingEm = p.TimingEm == true, Prn = p.Prn == true,
                Instructions = p.Instructions, FrequencyMode = p.FrequencyMode, FrequencyPeriod = p.FrequencyPeriod, DynamicInstruction = p.DynamicInstruction,
                GenericClass = p.GenericClass, TherapeuticClass = p.TherapeuticClass, ValidityDays = p.ValidityDays ?? 0,
                ValidUntil = DocNyle.ParseLocal(p.ValidUntil), TotalDispenseQty = (double)(p.TotalDispenseQty ?? 0), Status = p.Status,
                PrescribedDate = DocNyle.ParseInstant(p.PrescribedDate) ?? DocNyle.InstantOrMin(p.CreatedAt), Source = p.Source, SourceDetail = p.SourceDetail,
            };
        }

        private static List<Prescription> Map(CON_PrescriptionListResp r)
        {
            return (r.Prescriptions ?? new List<CON_PrescriptionInfo>()).Select(ToModel).ToList();
        }

        /// <summary>
        /// CreatePrescription (the service runs the drug-allergy check and fills the drug classes). The contract has no
        /// TakeInUnit, FrequencyMode / FrequencyPeriod, DynamicInstruction, TargetArea or PeriodStart members; those are derived by the service.
        /// </summary>
        private static Prescription ApiAddPrescription(Prescription r)
        {
            var saved = cnHelperObj.CreatePrescription(new CON_CreatePrescriptionReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                ConsultationId = r.ConsultationId, PatientId = r.PatientId, Source = string.IsNullOrEmpty(r.Source) ? null : r.Source, SourceDetail = r.SourceDetail,
                MedicineName = r.MedicineName, Strength = r.Strength, GenericClass = r.GenericClass, TherapeuticClass = r.TherapeuticClass, DrugForm = r.DrugForm,
                DeliveryRoute = r.DeliveryRoute, Category = r.Category, TakeInQty = (decimal)r.TakeInQty, TimesPerDay = r.TimesPerDay,
                DurationValue = r.DurationValue, DurationUnit = r.DurationUnit, Instructions = r.Instructions, Prn = r.Prn, TimingBf = r.TimingBf,
                TimingAn = r.TimingAn, TimingNi = r.TimingNi, TimingEm = r.TimingEm, ValidityDays = r.ValidityDays > 0 ? r.ValidityDays : (int?)null,
                RepeatedFromConsultationId = r.RepeatedFromConsultationId,
            }).EnsureSuccess().Prescription;
            return saved != null ? ToModel(saved) : r;
        }

        private static Prescription ApiSaveOutsideMedication(Prescription r)
        {
            var saved = cnHelperObj.SaveOutsideMedication(new CON_SaveOutsideMedicationReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                PrescriptionId = string.IsNullOrEmpty(r.PrescriptionId) ? null : r.PrescriptionId, PatientId = r.PatientId, MedicineName = r.MedicineName,
                Strength = r.Strength, DrugForm = r.DrugForm, TakeInQty = (decimal)r.TakeInQty, TakeInUnit = r.TakeInUnit, TimesPerDay = r.TimesPerDay,
                TimingNi = r.TimingNi, Prn = r.Prn, FrequencyMode = r.FrequencyMode, DynamicInstruction = r.DynamicInstruction, Instructions = r.Instructions,
                PeriodStart = DocNyle.Date(r.PeriodStart), SourceDetail = r.SourceDetail,
            }).EnsureSuccess().Prescription;
            return saved != null ? ToModel(saved) : r;
        }

        /* ── Orders, referrals, services ── */

        private static InvestigationOrder ToModel(CON_InvestigationOrderInfo o)
        {
            return new InvestigationOrder
            {
                OrderId = o.OrderId, HospitalId = CallerHospital, ConsultationId = o.ConsultationId, PatientId = o.PatientId, TestId = o.TestId,
                TestName = o.TestName, TestType = o.TestType, Status = o.Status, OrderedAt = DocNyle.InstantOrMin(o.OrderedAt), LabPartnerId = o.LabPartnerId,
            };
        }

        private static Referral ToModel(CON_ReferralInfo r)
        {
            return new Referral
            {
                ReferralId = r.ReferralId, ConsultationId = r.ConsultationId, PatientId = r.PatientId, Specialty = r.Specialty, DoctorName = r.DoctorName,
                Urgency = r.Urgency, Reason = r.Reason, CreatedAt = DocNyle.InstantOrMin(r.CreatedAt),
            };
        }

        internal static ConsultationServiceItem ToModel(CON_ServiceItemInfo s)
        {
            return new ConsultationServiceItem
            {
                ConsultationServiceId = s.ConsultationServiceId, ConsultationId = s.ConsultationId, PatientId = s.PatientId, ServiceChargeId = s.ServiceChargeId,
                ServiceName = s.ServiceName, Category = s.Category, Qty = s.Qty ?? 1, InventoryItemId = s.InventoryItemId, ItemName = s.ItemName,
                ItemUnit = s.ItemUnit, ItemQty = s.ItemQty, Notes = s.Notes ?? "", AddedByUserId = s.AddedByUserId, CreatedAt = DocNyle.InstantOrMin(s.CreatedAt),
            };
        }

        internal static List<ConsultationServiceItem> ApiServiceItems(string consultationId)
        {
            return RequestCache.Get("service-items:" + consultationId, () =>
                (cnHelperObj.ListConsultationServiceItems(AppSession.ApiHospitalId, AppSession.ApiUserId, consultationId, AppSession.ApiHashValue).EnsureSuccess().ServiceItems ?? new List<CON_ServiceItemInfo>()).Select(ToModel).OrderBy(s => s.CreatedAt).ToList());
        }

        private static ConsultationSketch ToModel(CON_SketchInfo s)
        {
            var data = s.ImageDataBase64 ?? "";
            if (data.Length > 0 && !data.StartsWith("data:")) data = "data:" + (string.IsNullOrEmpty(s.ContentType) ? "image/png" : s.ContentType) + ";base64," + data;
            return new ConsultationSketch
            {
                SketchId = s.SketchId, ConsultationId = s.ConsultationId, PatientId = s.PatientId, DoctorId = s.DoctorId, Title = s.Title,
                ImageData = data, CreatedAt = DocNyle.InstantOrMin(s.CreatedAt),
            };
        }
    }
}
