using System.Collections.Generic;
using System.Linq;
using System;
using OpdProject.Helpers;
using OpdProject.Models;
using OpdProject.Services.Api;
using OpdProject.Web;

namespace OpdProject.Services
{
    /// <summary>Drug formulary, lab test catalog and doctor lab favourites.</summary>
    public static partial class CatalogService
    {
        private static readonly CommonHelper cnHelperObj = new CommonHelper();

        /* ── Drug formulary ── */

        public static List<Drug> SearchFormulary(string hospitalId, string term, bool includeInactive)
        {
            var t = (term ?? "").Trim().ToLowerInvariant();
            if (Api)
                // SearchFormulary has no "include inactive" input; the service decides which rows it returns.
                return (cnHelperObj.SearchFormulary(AppSession.ApiHospitalId, AppSession.ApiUserId, (term ?? "").Trim(), AppSession.ApiHashValue).EnsureSuccess().Drugs ?? new List<CFG_DrugInfo>()).Select(ToModel)
                    .Where(d => includeInactive || d.IsActive).OrderBy(d => d.BrandName).ToList();
            lock (DataStore.Sync)
                return DataStore.Formulary.Where(d => (d.HospitalId == null || d.HospitalId == hospitalId) && (includeInactive || d.IsActive)
                        && (t.Length == 0 || d.BrandName.ToLowerInvariant().Contains(t) || d.GenericName.ToLowerInvariant().Contains(t)
                            || (d.TherapeuticClass ?? "").ToLowerInvariant().Contains(t)))
                    .OrderBy(d => d.BrandName).ToList();
        }

        /// <summary>API mode: the drug as last listed in this session (there is no get-by-id operation).</summary>
        public static Drug GetDrug(string drugId)
        {
            if (Api) return SessionIndex.Get<Drug>("drug", drugId);
            lock (DataStore.Sync) return DataStore.Formulary.FirstOrDefault(d => d.DrugId == drugId);
        }

        public static Drug GetDrugByName(string name)
        {
            if (Api)
            {
                if (string.IsNullOrWhiteSpace(name)) return null;
                var d = ToModel(DocNyle.OrNull(() => cnHelperObj.GetDrugByName(AppSession.ApiHospitalId, AppSession.ApiUserId, name.Trim(), AppSession.ApiHashValue).EnsureSuccess())?.Drug);
                return d != null && d.IsActive ? d : null;
            }
            lock (DataStore.Sync) return DataStore.Formulary.FirstOrDefault(d => d.IsActive && string.Equals(d.BrandName, (name ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>API mode: CreateDrug / UpdateDrug enforce the unique brand and answer with a fault, so the pre-check passes.</summary>
        public static bool BrandExists(string brand, string exceptDrugId)
        {
            if (Api) return false;
            lock (DataStore.Sync) return DataStore.Formulary.Any(d => d.DrugId != exceptDrugId && string.Equals(d.BrandName, brand, StringComparison.OrdinalIgnoreCase));
        }

        public static Drug SaveDrug(Drug record)
        {
            if (Api) return ApiSaveDrug(record);
            lock (DataStore.Sync)
            {
                var d = string.IsNullOrEmpty(record.DrugId) ? null : DataStore.Formulary.FirstOrDefault(x => x.DrugId == record.DrugId);
                if (d == null)
                {
                    record.DrugId = DataStore.NextId("DRUG");
                    record.IsActive = true;
                    if (string.IsNullOrEmpty(record.DefaultRoute)) record.DefaultRoute = "Oral";
                    DataStore.Formulary.Add(record);
                    return record;
                }
                d.BrandName = record.BrandName; d.GenericName = record.GenericName; d.Composition = record.Composition;
                d.TherapeuticClass = record.TherapeuticClass; d.GenericClass = record.GenericClass; d.DrugForm = record.DrugForm;
                d.DefaultDosage = record.DefaultDosage; d.DefaultFrequency = record.DefaultFrequency;
                d.DefaultDurationDays = record.DefaultDurationDays; d.DefaultInstructions = record.DefaultInstructions;
                d.IsControlledSubstance = record.IsControlledSubstance;
                return d;
            }
        }

        public static void ToggleDrug(string drugId)
        {
            if (Api)
            {
                var updated = cnHelperObj.UpdateDrugStatus(new CFG_UpdateDrugStatusReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, DrugFormularyId = drugId }).EnsureSuccess().Drug;
                var shown = SessionIndex.Get<Drug>("drug", drugId);
                if (shown != null && updated != null) shown.IsActive = updated.IsActive;
                return;
            }
            lock (DataStore.Sync)
            {
                var d = DataStore.Formulary.FirstOrDefault(x => x.DrugId == drugId);
                if (d != null) d.IsActive = !d.IsActive;
            }
        }

        /* ── Lab tests ── */

        public static List<LabTest> SearchLabTests(string term)
        {
            var t = (term ?? "").Trim().ToLowerInvariant();
            if (t.Length < 2) return new List<LabTest>();
            if (Api) return (cnHelperObj.SearchLabTests(AppSession.ApiHospitalId, AppSession.ApiUserId, term.Trim(), null, AppSession.ApiHashValue).EnsureSuccess().LabTests ?? new List<CFG_LabTestInfo>()).Select(ToModel).ToList();
            lock (DataStore.Sync)
                return DataStore.LabTests.Where(x => x.IsActive && (x.TestName.ToLowerInvariant().Contains(t) || x.Category.ToLowerInvariant().Contains(t))).ToList();
        }

        /// <summary>API mode: the test as last listed in this session (search results or favourites).</summary>
        public static LabTest GetLabTest(string testId)
        {
            if (Api) return SessionIndex.Get<LabTest>("labtest", testId);
            lock (DataStore.Sync) return DataStore.LabTests.FirstOrDefault(t => t.TestId == testId);
        }

        /// <summary>Doctor adds a test that is not in the catalog (reuses an existing one with the same name).</summary>
        public static LabTest AddCustomLabTest(string hospitalId, string name, string type)
        {
            if (Api)
            {
                var existing = SearchLabTests(name).FirstOrDefault(t => string.Equals(t.TestName, name, StringComparison.OrdinalIgnoreCase));
                if (existing != null) return existing;
                return ToModel(cnHelperObj.CreateLabTest(new CFG_CreateLabTestReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, TestName = name, TestType = type, Category = "Custom" }).EnsureSuccess().LabTest);
            }
            lock (DataStore.Sync)
            {
                var existing = DataStore.LabTests.FirstOrDefault(t => string.Equals(t.TestName, name, StringComparison.OrdinalIgnoreCase));
                if (existing != null) return existing;
                var row = new LabTest { TestId = DataStore.NextId("LT"), HospitalId = hospitalId, TestName = name, TestType = type, Category = "Custom", SpecialtyIds = new List<string> { "SPEC-GEN" }, IsActive = true };
                DataStore.LabTests.Add(row);
                return row;
            }
        }

        public static List<LabTest> ListFavorites(string doctorId)
        {
            if (Api) return ApiFavorites(doctorId);
            lock (DataStore.Sync)
            {
                var ids = DataStore.LabFavorites.Where(f => f.DoctorId == doctorId).Select(f => f.TestId).ToList();
                return DataStore.LabTests.Where(t => t.IsActive && ids.Contains(t.TestId)).ToList();
            }
        }

        public static bool IsFavorite(string doctorId, string testId)
        {
            if (Api) return ApiFavorites(doctorId).Any(t => t.TestId == testId);
            lock (DataStore.Sync) return DataStore.LabFavorites.Any(f => f.DoctorId == doctorId && f.TestId == testId);
        }

        /// <summary>Adds or removes a test from the doctor's quick-pick favourites; returns the new state.</summary>
        public static bool ToggleFavorite(string doctorId, string testId)
        {
            if (Api)
            {
                bool was = IsFavorite(doctorId, testId);
                var req = new CFG_LabFavoriteReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    DoctorId = doctorId, TestId = testId,
                };
                if (was) cnHelperObj.DeleteLabFavorite(req).EnsureSuccess(); else cnHelperObj.CreateLabFavorite(req).EnsureSuccess();
                RequestCache.Remove("lab-favorites:" + doctorId);
                return !was;
            }
            lock (DataStore.Sync)
            {
                if (DataStore.LabFavorites.RemoveAll(f => f.DoctorId == doctorId && f.TestId == testId) > 0) return false;
                DataStore.LabFavorites.Add(new DoctorLabFavorite { FavoriteId = DataStore.NextId("DLF"), DoctorId = doctorId, TestId = testId });
                return true;
            }
        }

        internal static bool Api { get { return DataSource.IsApi("Catalog"); } }

        /// <summary>A formulary row, remembered in the session index so a later postback (pick / toggle / edit) can find it by id.</summary>
        internal static Drug ToModel(CFG_DrugInfo d)
        {
            if (d == null) return null;
            return SessionIndex.Put("drug", d.DrugFormularyId, new Drug
            {
                DrugId = d.DrugFormularyId, BrandName = d.BrandName, GenericName = d.GenericName, Composition = d.Composition,
                TherapeuticClass = d.TherapeuticClass, GenericClass = d.GenericClass, DrugForm = d.DrugForm, DefaultRoute = d.DeliveryRoute,
                DefaultDosage = string.IsNullOrEmpty(d.DefaultDosage) ? d.Strength : d.DefaultDosage, DefaultFrequency = d.DefaultFrequency,
                DefaultDurationDays = d.DefaultDuration, DefaultInstructions = d.DefaultInstructions, IsControlledSubstance = d.IsControlled,
                IsActive = d.IsActive,
            });
        }

        /// <summary>A lab test row (LabTestCatalogId), remembered in the session index for the order / favourite postback.</summary>
        internal static LabTest ToModel(CFG_LabTestInfo t)
        {
            if (t == null) return null;
            return SessionIndex.Put("labtest", t.LabTestCatalogId, new LabTest
            {
                TestId = t.LabTestCatalogId, TestName = t.TestName, TestType = t.TestType, Category = t.Category,
                SpecialtyIds = new List<string>(), IsActive = true,
            });
        }

        private static string DoctorSpecialty(string doctorId)
        {
            var me = AppSession.ApiUser;
            return me != null && me.UserId == doctorId ? me.SpecialtyId : null;
        }

        private static List<LabTest> ApiFavorites(string doctorId)
        {
            return RequestCache.Get("lab-favorites:" + doctorId, () =>
                (cnHelperObj.ListDoctorLabFavorites(AppSession.ApiHospitalId, AppSession.ApiUserId, doctorId, null, AppSession.ApiHashValue).EnsureSuccess().LabTests ?? new List<CFG_LabTestInfo>()).Select(ToModel).ToList());
        }

        private static Drug ApiSaveDrug(Drug r)
        {
            if (string.IsNullOrEmpty(r.DrugId))
                return ToModel(cnHelperObj.CreateDrug(new CFG_CreateDrugReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    BrandName = r.BrandName, GenericName = r.GenericName, Composition = r.Composition, TherapeuticClass = r.TherapeuticClass,
                    GenericClass = r.GenericClass, DrugForm = r.DrugForm, DefaultRoute = string.IsNullOrEmpty(r.DefaultRoute) ? "Oral" : r.DefaultRoute,
                    DefaultDosage = r.DefaultDosage, DefaultFrequency = r.DefaultFrequency, DefaultDurationDays = r.DefaultDurationDays,
                    DefaultInstructions = r.DefaultInstructions, IsControlledSubstance = r.IsControlledSubstance,
                }).EnsureSuccess().Drug) ?? r;
            return ToModel(cnHelperObj.UpdateDrug(new CFG_UpdateDrugReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                DrugId = r.DrugId, BrandName = r.BrandName, GenericName = r.GenericName, Composition = r.Composition, TherapeuticClass = r.TherapeuticClass,
                GenericClass = r.GenericClass, DrugForm = r.DrugForm, DefaultDosage = r.DefaultDosage, DefaultFrequency = r.DefaultFrequency,
                DefaultDurationDays = r.DefaultDurationDays, DefaultInstructions = r.DefaultInstructions, IsControlledSubstance = r.IsControlledSubstance,
            }).EnsureSuccess().Drug) ?? r;
        }
    }

    /// <summary>Phrase library (SOAP "/" macros) and clinical conditions.</summary>
    public static partial class PhraseService
    {
        private static readonly CommonHelper cnHelperObj = new CommonHelper();

        /// <summary>Conditions whose ICD-10 prefixes match the consultation's diagnoses.</summary>
        public static List<string> ConditionIdsForConsultation(string consultationId)
        {
            // API mode: ListPhrases matches conditions to the visit on the server.
            if (consultationId == null || CatalogService.Api) return new List<string>();
            lock (DataStore.Sync)
            {
                var codes = DataStore.Diagnoses.Where(d => d.ConsultationId == consultationId).Select(d => (d.Icd10Code ?? "").ToUpperInvariant()).ToList();
                return DataStore.Conditions.Where(c => c.IsActive && (c.Icd10Prefixes ?? "").Split(',').Select(x => x.Trim().ToUpperInvariant())
                        .Where(x => x.Length > 0).Any(pre => codes.Any(code => code.StartsWith(pre))))
                    .Select(c => c.ConditionId).ToList();
            }
        }

        /// <summary>
        /// Phrases visible to a doctor for one SOAP section: hospital-wide, own specialty and own phrases; condition-linked
        /// phrases only when the visit matches; a doctor's phrase hides a broader one with the same trigger.
        /// </summary>
        public static List<Phrase> ListVisiblePhrases(string section, string specialtyId, string doctorId, string consultationId)
        {
            if (CatalogService.Api) return ApiVisible(section, specialtyId, doctorId, consultationId);
            var conds = ConditionIdsForConsultation(consultationId);
            lock (DataStore.Sync)
            {
                var visible = DataStore.Phrases.Where(p => p.IsActive && (p.Section == section || p.Section == "ANY" || section == "ANY")
                    && (p.Scope == "GLOBAL" || (p.Scope == "SPECIALTY" && p.SpecialtyId == specialtyId) || (p.Scope == "DOCTOR" && p.DoctorId == doctorId))
                    && (p.ConditionId == null || conds.Contains(p.ConditionId))).ToList();
                var doctorTriggers = new HashSet<string>(visible.Where(p => p.Scope == "DOCTOR").Select(p => p.TriggerKeyword.ToLowerInvariant()));
                var order = new Dictionary<string, int> { { "DOCTOR", 0 }, { "SPECIALTY", 1 }, { "GLOBAL", 2 } };
                return visible.Where(p => p.Scope == "DOCTOR" || !doctorTriggers.Contains(p.TriggerKeyword.ToLowerInvariant()))
                    .OrderBy(p => order[p.Scope]).ThenBy(p => p.SortOrder).ToList();
            }
        }

        /// <summary>Expands "/trigger" words in free text to the matching visible phrase text.</summary>
        public static string ExpandTriggers(string text, string section, string specialtyId, string doctorId, string consultationId)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('/') < 0) return text;
            var phrases = ListVisiblePhrases(section, specialtyId, doctorId, consultationId);
            return System.Text.RegularExpressions.Regex.Replace(text, @"(?<![\w/])/[A-Za-z0-9_]+", m =>
            {
                var hit = phrases.FirstOrDefault(p => string.Equals(p.TriggerKeyword, m.Value, StringComparison.OrdinalIgnoreCase));
                return hit != null ? hit.PhraseText : m.Value;
            });
        }

        public static List<Phrase> ListAll()
        {
            if (CatalogService.Api)
                return (cnHelperObj.SearchPhrases(AppSession.ApiHospitalId, AppSession.ApiUserId, null, null, null, null, "", AppSession.ApiHashValue).EnsureSuccess().Phrases ?? new List<CFG_PhraseInfo>()).Select(ToModel).OrderBy(p => p.TriggerKeyword).ToList();
            lock (DataStore.Sync) return DataStore.Phrases.Where(p => p.IsActive).OrderBy(p => p.TriggerKeyword).ToList();
        }

        public static Phrase Get(string phraseId)
        {
            if (CatalogService.Api) return SessionIndex.Get<Phrase>("phrase", phraseId);
            lock (DataStore.Sync) return DataStore.Phrases.FirstOrDefault(p => p.PhraseId == phraseId);
        }

        /// <summary>API mode: there is no UpdatePhrase operation, so editing replaces the phrase (DeletePhrase + CreatePhrase, new id).</summary>
        public static Phrase Save(Phrase record)
        {
            if (CatalogService.Api)
            {
                if (!string.IsNullOrEmpty(record.PhraseId)) Delete(record.PhraseId);
                record.PhraseId = null;
                return ApiCreate(record);
            }
            lock (DataStore.Sync)
            {
                var p = string.IsNullOrEmpty(record.PhraseId) ? null : DataStore.Phrases.FirstOrDefault(x => x.PhraseId == record.PhraseId);
                if (p == null)
                {
                    record.PhraseId = DataStore.NextId("PH");
                    record.IsActive = true;
                    DataStore.Phrases.Add(record);
                    return record;
                }
                p.TriggerKeyword = record.TriggerKeyword; p.PhraseText = record.PhraseText; p.Section = record.Section;
                p.Scope = record.Scope; p.DoctorId = record.DoctorId; p.SpecialtyId = record.SpecialtyId; p.ConditionId = record.ConditionId;
                return p;
            }
        }

        public static void Delete(string phraseId)
        {
            if (CatalogService.Api) { cnHelperObj.DeletePhrase(new CFG_DeletePhraseReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, PhraseBankId = phraseId }).EnsureSuccess(); RequestCache.RemovePrefix("phrases:"); return; }
            lock (DataStore.Sync) DataStore.Phrases.RemoveAll(p => p.PhraseId == phraseId);
        }

        public static List<ClinicalCondition> ListConditions(string hospitalId)
        {
            if (CatalogService.Api)
                return HospitalCache.Get("conditions", () => cnHelperObj.ListClinicalConditions(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Conditions ?? new List<SCH_ClinicalConditionInfo>()).Select(ToModel).ToList();
            lock (DataStore.Sync) return DataStore.Conditions.Where(c => c.HospitalId == hospitalId && c.IsActive).ToList();
        }

        public static bool AddCondition(string hospitalId, string name, string prefixes)
        {
            if (CatalogService.Api)
            {
                if (ListConditions(hospitalId).Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) return false;
                cnHelperObj.CreateClinicalCondition(new SCH_CreateClinicalConditionReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, Name = name, Icd10Prefixes = prefixes }).EnsureSuccess();
                HospitalCache.Clear("conditions");
                return true;
            }
            lock (DataStore.Sync)
            {
                if (DataStore.Conditions.Any(c => c.HospitalId == hospitalId && c.IsActive && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) return false;
                DataStore.Conditions.Add(new ClinicalCondition { ConditionId = DataStore.NextId("COND"), HospitalId = hospitalId, Name = name, Icd10Prefixes = prefixes, IsActive = true });
                return true;
            }
        }

        /// <summary>Soft delete — linked phrases stay but are no longer offered until relinked.</summary>
        public static void RemoveCondition(string conditionId)
        {
            if (CatalogService.Api) { cnHelperObj.DeleteClinicalCondition(new SCH_DeleteClinicalConditionReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, ConditionId = conditionId }).EnsureSuccess(); HospitalCache.Clear("conditions"); return; }
            lock (DataStore.Sync)
            {
                var c = DataStore.Conditions.FirstOrDefault(x => x.ConditionId == conditionId);
                if (c != null) c.IsActive = false;
            }
        }

        private static Phrase ToModel(CFG_PhraseInfo p)
        {
            return SessionIndex.Put("phrase", p.PhraseBankId, new Phrase
            {
                PhraseId = p.PhraseBankId, Scope = p.Scope, SpecialtyId = p.SpecialtyId, DoctorId = p.UserId, ConditionId = p.ConditionId,
                Section = string.IsNullOrEmpty(p.Category) ? "ANY" : p.Category, TriggerKeyword = p.TriggerCode, PhraseText = p.PhraseText, IsActive = true,
            });
        }

        /// <summary>ListPhrases — the service applies scope, condition matching and the doctor-over-broader rule.</summary>
        private static List<Phrase> ApiVisible(string section, string specialtyId, string doctorId, string consultationId)
        {
            return RequestCache.Get("phrases:" + section + ":" + specialtyId + ":" + doctorId + ":" + consultationId, () =>
                (cnHelperObj.ListPhrases(AppSession.ApiHospitalId, AppSession.ApiUserId, section == "ANY" ? null : section, specialtyId, doctorId, consultationId, AppSession.ApiHashValue).EnsureSuccess().Phrases ?? new List<CFG_PhraseInfo>())
                    .Select(ToModel).ToList());
        }

        private static Phrase ApiCreate(Phrase r)
        {
            var saved = cnHelperObj.CreatePhrase(new CFG_CreatePhraseReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                Scope = r.Scope, SpecialtyId = r.SpecialtyId, DoctorId = r.DoctorId, ConditionId = r.ConditionId, Section = r.Section,
                TriggerKeyword = r.TriggerKeyword, PhraseText = r.PhraseText,
            }).EnsureSuccess().Phrase;
            RequestCache.RemovePrefix("phrases:");
            return saved != null ? ToModel(saved) : r;
        }

        private static ClinicalCondition ToModel(SCH_ClinicalConditionInfo c)
        {
            return new ClinicalCondition { ConditionId = c.ConditionId, Name = c.Name, Icd10Prefixes = c.Icd10Prefixes, IsActive = true };
        }
    }
}
