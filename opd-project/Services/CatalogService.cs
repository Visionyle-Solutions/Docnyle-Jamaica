using System;
using System.Collections.Generic;
using System.Linq;
using OpdProject.Models;

namespace OpdProject.Services
{
    /// <summary>Drug formulary, lab test catalog and doctor lab favourites.</summary>
    public static class CatalogService
    {
        /* ── Drug formulary ── */

        public static List<Drug> SearchFormulary(string hospitalId, string term, bool includeInactive)
        {
            var t = (term ?? "").Trim().ToLowerInvariant();
            lock (DataStore.Sync)
                return DataStore.Formulary.Where(d => (d.HospitalId == null || d.HospitalId == hospitalId) && (includeInactive || d.IsActive)
                        && (t.Length == 0 || d.BrandName.ToLowerInvariant().Contains(t) || d.GenericName.ToLowerInvariant().Contains(t)
                            || (d.TherapeuticClass ?? "").ToLowerInvariant().Contains(t)))
                    .OrderBy(d => d.BrandName).ToList();
        }

        public static Drug GetDrug(string drugId)
        {
            lock (DataStore.Sync) return DataStore.Formulary.FirstOrDefault(d => d.DrugId == drugId);
        }

        public static Drug GetDrugByName(string name)
        {
            lock (DataStore.Sync) return DataStore.Formulary.FirstOrDefault(d => d.IsActive && string.Equals(d.BrandName, (name ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static bool BrandExists(string brand, string exceptDrugId)
        {
            lock (DataStore.Sync) return DataStore.Formulary.Any(d => d.DrugId != exceptDrugId && string.Equals(d.BrandName, brand, StringComparison.OrdinalIgnoreCase));
        }

        public static Drug SaveDrug(Drug record)
        {
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
            lock (DataStore.Sync)
                return DataStore.LabTests.Where(x => x.IsActive && (x.TestName.ToLowerInvariant().Contains(t) || x.Category.ToLowerInvariant().Contains(t))).ToList();
        }

        public static LabTest GetLabTest(string testId)
        {
            lock (DataStore.Sync) return DataStore.LabTests.FirstOrDefault(t => t.TestId == testId);
        }

        /// <summary>Doctor adds a test that is not in the catalog (reuses an existing one with the same name).</summary>
        public static LabTest AddCustomLabTest(string hospitalId, string name, string type)
        {
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
            lock (DataStore.Sync)
            {
                var ids = DataStore.LabFavorites.Where(f => f.DoctorId == doctorId).Select(f => f.TestId).ToList();
                return DataStore.LabTests.Where(t => t.IsActive && ids.Contains(t.TestId)).ToList();
            }
        }

        public static bool IsFavorite(string doctorId, string testId)
        {
            lock (DataStore.Sync) return DataStore.LabFavorites.Any(f => f.DoctorId == doctorId && f.TestId == testId);
        }

        /// <summary>Adds or removes a test from the doctor's quick-pick favourites; returns the new state.</summary>
        public static bool ToggleFavorite(string doctorId, string testId)
        {
            lock (DataStore.Sync)
            {
                if (DataStore.LabFavorites.RemoveAll(f => f.DoctorId == doctorId && f.TestId == testId) > 0) return false;
                DataStore.LabFavorites.Add(new DoctorLabFavorite { FavoriteId = DataStore.NextId("DLF"), DoctorId = doctorId, TestId = testId });
                return true;
            }
        }
    }

    /// <summary>Phrase library (SOAP "/" macros) and clinical conditions.</summary>
    public static class PhraseService
    {
        /// <summary>Conditions whose ICD-10 prefixes match the consultation's diagnoses.</summary>
        public static List<string> ConditionIdsForConsultation(string consultationId)
        {
            if (consultationId == null) return new List<string>();
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
            lock (DataStore.Sync) return DataStore.Phrases.Where(p => p.IsActive).OrderBy(p => p.TriggerKeyword).ToList();
        }

        public static Phrase Get(string phraseId)
        {
            lock (DataStore.Sync) return DataStore.Phrases.FirstOrDefault(p => p.PhraseId == phraseId);
        }

        public static Phrase Save(Phrase record)
        {
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
            lock (DataStore.Sync) DataStore.Phrases.RemoveAll(p => p.PhraseId == phraseId);
        }

        public static List<ClinicalCondition> ListConditions(string hospitalId)
        {
            lock (DataStore.Sync) return DataStore.Conditions.Where(c => c.HospitalId == hospitalId && c.IsActive).ToList();
        }

        public static bool AddCondition(string hospitalId, string name, string prefixes)
        {
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
            lock (DataStore.Sync)
            {
                var c = DataStore.Conditions.FirstOrDefault(x => x.ConditionId == conditionId);
                if (c != null) c.IsActive = false;
            }
        }
    }
}
