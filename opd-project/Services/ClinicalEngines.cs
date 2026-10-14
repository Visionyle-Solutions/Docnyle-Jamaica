using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using OpdProject.Models;

namespace OpdProject.Services
{
    /// <summary>
    /// Silent background reviewer run before sign-off. Flags vitals-vs-diagnosis conflicts,
    /// laterality conflicts and pediatric weight-based dose overruns. Advisory only.
    /// </summary>
    public static class ContradictionEngine
    {
        private class VitalRule
        {
            public string Code; public bool GreaterThan; public double Threshold; public string[] ConflictIcd; public string Message;
        }

        private static readonly VitalRule[] Rules =
        {
            new VitalRule { Code = "PULSE", GreaterThan = true, Threshold = 100, ConflictIcd = new[] { "R00.1", "I49.5" }, Message = "Pulse is {value} bpm (tachycardic range), but diagnosis includes Bradycardia." },
            new VitalRule { Code = "PULSE", GreaterThan = false, Threshold = 60, ConflictIcd = new[] { "R00.0" }, Message = "Pulse is {value} bpm (bradycardic range), but diagnosis mentions Tachycardia." },
            new VitalRule { Code = "BP_SYS", GreaterThan = false, Threshold = 100, ConflictIcd = new[] { "I10", "I11", "I12", "I13", "I15" }, Message = "Systolic BP is {value} mmHg (hypotensive), but diagnosis includes Hypertension ({icd})." },
            new VitalRule { Code = "BP_SYS", GreaterThan = true, Threshold = 140, ConflictIcd = new[] { "I95" }, Message = "Systolic BP is {value} mmHg (hypertensive), but diagnosis includes Hypotension ({icd})." },
            new VitalRule { Code = "TEMP", GreaterThan = false, Threshold = 99, ConflictIcd = new[] { "R50", "A90", "A91", "A01" }, Message = "Temperature is {value}°F (afebrile), but diagnosis includes a fever-related condition ({icd})." },
            new VitalRule { Code = "SPO2", GreaterThan = true, Threshold = 95, ConflictIcd = new[] { "J96" }, Message = "SpO2 is {value}% (normal), but diagnosis includes Respiratory Failure ({icd})." },
        };

        private static readonly string[] LeftWords = { "left", "sinister", "l/", "lt ", "lt." };
        private static readonly string[] RightWords = { "right", "dexter", "r/", "rt ", "rt." };

        /// <summary>Common pediatric maximum doses (mg/kg/day).</summary>
        private static readonly Tuple<Regex, double, string>[] PediatricLimits =
        {
            Tuple.Create(new Regex("paracetamol|acetaminophen|calpol|crocin", RegexOptions.IgnoreCase), 60.0, "Paracetamol"),
            Tuple.Create(new Regex("ibuprofen|brufen", RegexOptions.IgnoreCase), 30.0, "Ibuprofen"),
            Tuple.Create(new Regex("amoxicillin|amoxyclav|augmentin", RegexOptions.IgnoreCase), 90.0, "Amoxicillin"),
            Tuple.Create(new Regex("azithromycin|azithral", RegexOptions.IgnoreCase), 12.0, "Azithromycin"),
            Tuple.Create(new Regex("metformin", RegexOptions.IgnoreCase), 35.0, "Metformin"),
        };

        public static List<ClinicalAlert> Evaluate(ConsultationVitals vitals, List<ConsultationDiagnosis> diagnoses,
            List<string> yesSymptomLabels, List<Prescription> medications, int? ageYears)
        {
            var alerts = new List<ClinicalAlert>();
            if (vitals != null && diagnoses.Count > 0)
            {
                foreach (var rule in Rules)
                {
                    var value = ConsultationService.GetVital(vitals, rule.Code);
                    if (!value.HasValue || value.Value == 0) continue;
                    if (rule.GreaterThan ? value.Value <= rule.Threshold : value.Value >= rule.Threshold) continue;
                    foreach (var dx in diagnoses.Where(d => rule.ConflictIcd.Any(c => (d.Icd10Code ?? "").ToUpperInvariant().StartsWith(c))))
                        alerts.Add(new ClinicalAlert
                        {
                            Severity = "WARNING", Type = "VITALS_VS_DIAGNOSIS",
                            Message = rule.Message.Replace("{value}", value.Value.ToString(CultureInfo.InvariantCulture)).Replace("{icd}", dx.Icd10Code + " " + dx.Icd10Description)
                        });
                }
            }

            if (yesSymptomLabels.Count > 0 && diagnoses.Count > 0)
            {
                var symptomSide = DetectSide(string.Join(" ", yesSymptomLabels).ToLowerInvariant());
                var dxSide = DetectSide(string.Join(" ", diagnoses.Select(d => (d.Icd10Description ?? "") + " " + (d.Laterality ?? ""))).ToLowerInvariant());
                if (symptomSide != null && dxSide != null && symptomSide != dxSide)
                    alerts.Add(new ClinicalAlert { Severity = "HIGH_WARNING", Type = "LATERALITY_CONFLICT", Message = "Laterality conflict: Symptoms indicate " + symptomSide + " side, but diagnosis specifies " + dxSide + " side. Please verify." });
            }

            var weight = vitals != null ? vitals.WeightKg : null;
            if (ageYears.HasValue && ageYears.Value <= 18 && weight.HasValue && weight.Value > 0)
            {
                foreach (var med in medications)
                {
                    foreach (var limit in PediatricLimits.Where(l => l.Item1.IsMatch(med.MedicineName ?? "")))
                    {
                        double mg;
                        if (!double.TryParse(Regex.Replace(med.Strength ?? "", "[^0-9.]", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out mg) || mg <= 0) continue;
                        int times = med.TimesPerDay > 0 ? med.TimesPerDay : 1;
                        double daily = mg * times, max = limit.Item2 * weight.Value;
                        if (daily > max)
                            alerts.Add(new ClinicalAlert
                            {
                                Severity = "CRITICAL", Type = "PEDIATRIC_DOSE",
                                Message = "Pediatric dose warning: " + med.MedicineName + " " + med.Strength + " × " + times + "/day = " + daily + "mg/day exceeds max " + limit.Item2 + "mg/kg/day (" + Math.Round(max) + "mg/day for " + weight.Value + "kg patient)."
                            });
                    }
                }
            }
            return alerts;
        }

        private static string DetectSide(string text)
        {
            bool left = LeftWords.Any(text.Contains), right = RightWords.Any(text.Contains);
            if (left && !right) return "LEFT";
            if (right && !left) return "RIGHT";
            return null;
        }
    }

    /// <summary>
    /// Medication reconciliation run when a prescription is added: dosage delta vs. the last prescription,
    /// duplicate active drug, duplicate within the visit, and therapeutic (same generic class) duplication.
    /// Non-blocking — the doctor acknowledges with a reason, which is audited.
    /// </summary>
    public static class RxReconciler
    {
        private static double Number(string s)
        {
            double v;
            return double.TryParse(Regex.Replace(s ?? "", "[^0-9.]", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        public static List<ClinicalAlert> Reconcile(string medicineName, string strength, string genericClass, List<Prescription> activePatientMeds, List<Prescription> currentVisitMeds)
        {
            var alerts = new List<ClinicalAlert>();
            var name = (medicineName ?? "").ToLowerInvariant();
            var strengthNum = Number(strength);

            var sameDrug = activePatientMeds.Where(m => (m.MedicineName ?? "").ToLowerInvariant() == name).ToList();
            if (sameDrug.Count > 0 && strengthNum > 0)
            {
                var last = sameDrug.Last();
                var lastNum = Number(last.Strength);
                if (lastNum > 0 && lastNum != strengthNum)
                {
                    int pct = (int)Math.Round((strengthNum - lastNum) / lastNum * 100);
                    alerts.Add(new ClinicalAlert
                    {
                        Severity = Math.Abs(pct) >= 100 ? "WARNING" : "INFO", Type = "DOSAGE_DELTA",
                        Message = "Dosage " + (strengthNum > lastNum ? "increase" : "decrease") + " detected: " + medicineName + " changed from " + last.Strength + " to " + strength + " (" + (pct > 0 ? "+" : "") + pct + "%)."
                    });
                }
                var dup = sameDrug.FirstOrDefault(m => m.Strength == strength);
                if (dup != null)
                    alerts.Add(new ClinicalAlert { Severity = "CRITICAL", Type = "DUPLICATE_ACTIVE", Message = "Duplicate active drug: Patient is already on " + medicineName + " " + dup.Strength + " prescribed on " + dup.PrescribedDate.ToString("yyyy-MM-dd") + "." });
            }
            if (currentVisitMeds.Any(m => (m.MedicineName ?? "").ToLowerInvariant() == name))
                alerts.Add(new ClinicalAlert { Severity = "WARNING", Type = "DUPLICATE_VISIT", Message = medicineName + " has already been added in this consultation." });
            if (!string.IsNullOrEmpty(genericClass))
            {
                var same = activePatientMeds.FirstOrDefault(m => string.Equals(m.GenericClass, genericClass, StringComparison.OrdinalIgnoreCase) && (m.MedicineName ?? "").ToLowerInvariant() != name);
                if (same != null)
                    alerts.Add(new ClinicalAlert { Severity = "HIGH_WARNING", Type = "THERAPEUTIC_DUPLICATE", Message = "Therapeutic duplication: " + medicineName + " belongs to the same class (" + genericClass + ") as active " + same.MedicineName + "." });
            }
            return alerts;
        }
    }

    /// <summary>Auto-SOAP composer: drafts S/O/A/P from the structured visit data for the doctor to edit.</summary>
    public static class SoapComposer
    {
        public static SoapDraft Compose(ConsultationVitals v, List<string> yes, List<string> no, List<ConsultationDiagnosis> diagnoses,
            List<Prescription> meds, List<InvestigationOrder> orders, DateTime? followupDate, string followupNotes)
        {
            var s = "";
            if (yes.Count > 0) s += "Patient presents with " + string.Join(", ", yes) + ".";
            if (no.Count > 0) s += " Denies " + string.Join(", ", no) + ".";
            if (s.Length == 0) s = "No specific complaints reported.";

            var parts = new List<string>();
            if (v != null)
            {
                if (v.TempValue.HasValue) parts.Add("Temp " + v.TempValue + "°F");
                if (v.BpSystolic.HasValue && v.BpDiastolic.HasValue) parts.Add("BP " + v.BpSystolic + "/" + v.BpDiastolic + " mmHg");
                if (v.PulseBpm.HasValue) parts.Add("Pulse " + v.PulseBpm + " bpm");
                if (v.SpO2.HasValue) parts.Add("SpO2 " + v.SpO2 + "%");
                if (v.WeightKg.HasValue) parts.Add("Weight " + v.WeightKg + " kg");
                if (v.Bmi.HasValue) parts.Add("BMI " + v.Bmi + " (" + BmiClass(v.Bmi.Value) + ")");
            }
            var o = (parts.Count > 0 ? "Vitals: " + string.Join(", ", parts) + ".\n" : "") + "General examination findings as documented above.";

            var a = diagnoses.Count == 0 ? "Assessment pending — no diagnosis recorded."
                : string.Join("\n", diagnoses.Select((d, i) => (i + 1) + ". " + d.Icd10Description + " (" + d.Icd10Code + ")"
                    + (string.IsNullOrEmpty(d.AcuteChronic) ? "" : " — " + d.AcuteChronic) + (string.IsNullOrEmpty(d.ClinicalNotes) ? "" : ". " + d.ClinicalNotes)));

            var plan = new List<string>();
            if (meds.Count > 0)
            {
                plan.Add("Medications:");
                plan.AddRange(meds.Select(m => "• " + m.MedicineName + (string.IsNullOrEmpty(m.Strength) ? "" : " " + m.Strength)
                    + (m.TimesPerDay > 0 ? " — " + m.TimesPerDay + "x/day" : "") + (m.DurationValue > 0 ? " for " + m.DurationValue + " " + (m.DurationUnit ?? "day(s)") : "")
                    + (string.IsNullOrEmpty(m.Instructions) ? "" : " (" + m.Instructions + ")")));
            }
            if (orders.Count > 0)
            {
                plan.Add("Investigations ordered:");
                plan.AddRange(orders.Select(x => "• " + x.TestName + " (" + x.TestType + ")"));
            }
            if (followupDate.HasValue) plan.Add("Follow-Up: " + followupDate.Value.ToString("yyyy-MM-dd") + (string.IsNullOrEmpty(followupNotes) ? "" : " — " + followupNotes));
            return new SoapDraft { Subjective = s.Trim(), Objective = o.Trim(), Assessment = a, Plan = plan.Count > 0 ? string.Join("\n", plan) : "Plan to be determined." };
        }

        public static string BmiClass(double bmi)
        {
            return bmi < 18.5 ? "Underweight" : bmi < 25 ? "Normal" : bmi < 30 ? "Overweight" : "Obese";
        }
    }

    public class BranchedFlowResult
    {
        public List<string> Banners = new List<string>();
        public List<string> SuggestedLabs = new List<string>();
        public List<string> SuggestedIcd = new List<string>();
        public List<string> HighlightVitals = new List<string>();
    }

    /// <summary>
    /// Adaptive consultation hints: complaint-driven pathways (banner, suggested labs/ICD-10, vitals to watch),
    /// demographic rules (pediatric, women 15-49, 40+) and country lab bundles for fever. Advisory only.
    /// </summary>
    public static class BranchedFlowEngine
    {
        private class Rule
        {
            public string[] Triggers; public string[] Vitals; public string[] Labs; public string[] Icd; public string Banner;
        }

        private static readonly Rule[] ComplaintRules =
        {
            new Rule { Triggers = new[] { "fever", "pyrexia" }, Vitals = new[] { "TEMP" }, Labs = new[] { "Complete Blood Count (CBC)", "Dengue NS1 Antigen", "Malarial Smear (MP/MF)" }, Icd = new[] { "A90 Dengue fever", "R50.9 Fever, unspecified", "A01 Typhoid fever" }, Banner = "Fever pathway activated — temperature vitals highlighted, infectious disease labs suggested." },
            new Rule { Triggers = new[] { "chest pain", "chest discomfort", "angina" }, Vitals = new[] { "BP_SYS", "BP_DIA", "PULSE", "SPO2" }, Labs = new[] { "ECG (12-lead)", "Chest X-ray (PA view)", "Lipid Profile" }, Icd = new[] { "I20.9 Angina pectoris", "I25.9 Chronic ischemic heart disease", "R07.9 Chest pain" }, Banner = "Chest pain pathway — cardiac vitals highlighted, ECG and lipid labs suggested." },
            new Rule { Triggers = new[] { "cough", "breathlessness", "dyspnea", "shortness of breath" }, Vitals = new[] { "SPO2", "TEMP" }, Labs = new[] { "Chest X-ray (PA view)", "Complete Blood Count (CBC)", "CRP (C-Reactive Protein)" }, Icd = new[] { "J06.9 Upper respiratory infection", "J18.9 Pneumonia", "J45.9 Asthma" }, Banner = "Respiratory pathway — SpO2 highlighted, chest X-ray and CBC suggested." },
            new Rule { Triggers = new[] { "headache", "migraine" }, Vitals = new[] { "BP_SYS", "BP_DIA", "TEMP" }, Labs = new[] { "Complete Blood Count (CBC)", "ESR" }, Icd = new[] { "G43.9 Migraine", "R51 Headache", "G44.1 Tension headache" }, Banner = "Headache pathway — BP and temperature highlighted." },
            new Rule { Triggers = new[] { "abdominal pain", "stomach pain", "gastric" }, Vitals = new[] { "TEMP" }, Labs = new[] { "Liver Function Test (LFT)", "Ultrasound Abdomen", "Urine Routine & Microscopy" }, Icd = new[] { "K30 Dyspepsia", "K21.0 GERD", "K35.9 Acute appendicitis" }, Banner = "Abdominal pathway — LFT and ultrasound suggested." },
            new Rule { Triggers = new[] { "joint pain", "arthralgia", "body aches" }, Vitals = new[] { "TEMP" }, Labs = new[] { "ESR", "CRP (C-Reactive Protein)", "Uric Acid" }, Icd = new[] { "M25.5 Pain in joint", "M79.3 Myalgia", "M10.9 Gout" }, Banner = "Musculoskeletal pathway — inflammatory markers suggested." },
            new Rule { Triggers = new[] { "diabetes", "sugar", "hyperglycemia" }, Vitals = new[] { "WEIGHT", "HEIGHT" }, Labs = new[] { "Fasting Blood Sugar (FBS)", "HbA1c", "Renal Function Test (RFT/KFT)", "Lipid Profile" }, Icd = new[] { "E11.9 Type 2 diabetes", "E11.65 DM with hyperglycemia" }, Banner = "Diabetes pathway — FBS, HbA1c, renal and lipid panels suggested." },
        };

        public static BranchedFlowResult Evaluate(List<string> yesComplaintLabels, int? ageYears, string gender, string country)
        {
            var r = new BranchedFlowResult();
            var text = string.Join(" ", yesComplaintLabels).ToLowerInvariant();
            foreach (var rule in ComplaintRules.Where(x => x.Triggers.Any(text.Contains)))
            {
                r.Banners.Add(rule.Banner);
                r.SuggestedLabs.AddRange(rule.Labs);
                r.SuggestedIcd.AddRange(rule.Icd);
                r.HighlightVitals.AddRange(rule.Vitals);
            }
            if (ageYears.HasValue && ageYears.Value < 5) r.Banners.Add("Pediatric patient (< 5 years) — head circumference and growth percentile charts relevant.");
            if (ageYears.HasValue && ageYears.Value < 18) r.Banners.Add("Pediatric patient — weight-based dosing calculator activated.");
            if (gender == "FEMALE" && ageYears.HasValue && ageYears.Value >= 15 && ageYears.Value <= 49) r.Banners.Add("Female patient (15-49) — Last Menstrual Period (LMP) field available.");
            if (ageYears.HasValue && ageYears.Value >= 40) r.SuggestedLabs.AddRange(new[] { "Lipid Profile", "Fasting Blood Sugar (FBS)", "ECG (12-lead)" });
            if (text.Contains("fever"))
            {
                if (country == "IN")
                {
                    r.SuggestedLabs.AddRange(new[] { "Dengue NS1 Antigen", "Malarial Smear (MP/MF)", "Widal Test" });
                    r.Banners.Add("Tropical region: Dengue, Malaria, and Typhoid bundles auto-suggested for fever.");
                }
                else if (country == "JM")
                {
                    r.SuggestedLabs.AddRange(new[] { "Zika IgM/IgG", "Chikungunya IgM", "Dengue NS1 Antigen" });
                    r.Banners.Add("Caribbean region: Zika, Chikungunya, and Dengue bundles auto-suggested for fever.");
                }
            }
            r.SuggestedLabs = r.SuggestedLabs.Distinct().ToList();
            r.SuggestedIcd = r.SuggestedIcd.Distinct().ToList();
            r.HighlightVitals = r.HighlightVitals.Distinct().ToList();
            return r;
        }
    }
}
