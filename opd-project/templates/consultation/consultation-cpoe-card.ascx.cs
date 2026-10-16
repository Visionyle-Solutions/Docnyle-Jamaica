using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.consultation
{
    /// <summary>
    /// Prescriptions (CPOE). Every "Add" runs the non-bypassable drug-allergy check and the medication
    /// reconciler on the server; alerts must be overridden/acknowledged with a reason, which is audited.
    /// </summary>
    public partial class consultation_cpoe_card : ConsultationSection
    {
        protected override string SectionKey { get { return "cpoe"; } }

        protected override void ApplyFormConfig()
        {
            RepeatPrevButton.Visible = FieldOn("repeat");
            ManualEntryButton.Visible = FieldOn("manual");
            CategoryHolder.Visible = FieldOn("category");
            ApplyOptions("category", RxCategory);
            TargetAreaHolder.Visible = FieldOn("targetArea");
            DoseTimingHolder.Visible = FieldOn("doseTiming");
            EventTimingHolder.Visible = FieldOn("eventTiming");
            RxPrn.Visible = FieldOn("prn");
            InstructionsHolder.Visible = FieldOn("instructions");
        }

        public override void Bind()
        {
            OrderingHolder.Visible = !Host.IsLocked;
            var meds = ConsultationService.ListPrescriptions(ConsultationId);
            MedsRepeater.DataSource = meds;
            MedsRepeater.DataBind();
            NoMeds.Visible = meds.Count == 0;
            DispensingNote.Visible = meds.Count > 0;
        }

        /// <summary>Renders a chip group that writes the chosen value into a hidden field (see site.js).</summary>
        protected string Chips(HiddenField target, params string[] options)
        {
            var sb = new StringBuilder();
            foreach (var o in options)
            {
                var parts = o.Split('|');
                sb.AppendFormat("<a href=\"#\" class=\"rx-chip{0}\" data-chip-for=\"{1}\" data-val=\"{2}\">{3}</a>",
                    parts[0] == target.Value ? " rx-chip--active" : "", target.ClientID, parts[0], HttpUtility.HtmlEncode(parts[1]));
            }
            return sb.ToString();
        }

        /* ── Search & pick ── */

        protected void RxSearchButton_Click(object sender, EventArgs e)
        {
            Host.ActivateStep("cpoe");
            var term = RxSearchInput.Text.Trim();
            var results = term.Length >= 2 ? CatalogService.SearchFormulary(HospitalId, term, false) : new List<Drug>();
            RxResults.DataSource = results;
            RxResults.DataBind();
            RxSearchHint.Text = term.Length < 2 ? "<div class=\"text-xs text-muted\">Type at least 2 characters to search.</div>"
                : results.Count == 0 ? "<div class=\"text-xs text-muted\">No formulary matches — try \"Manual\".</div>" : "";
        }

        protected void RxResults_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            Host.ActivateStep("cpoe");
            if (e.CommandName != "Pick" || !EnsureNotLocked()) return;
            var d = CatalogService.GetDrug((string)e.CommandArgument);
            if (d == null) return;
            ResetForm();
            RxMedicineName.Text = d.BrandName;
            RxMedicineName.ReadOnly = true;
            RxStrength.Text = d.DefaultDosage;
            RxDrugForm.Text = d.DrugForm;
            RxDeliveryRoute.Text = d.DefaultRoute;
            RxTakeInUnit.Text = d.DrugForm == "Syrup" ? "ml" : (d.DrugForm ?? "").Contains("Drops") ? "drop(s)" : d.DrugForm == "Capsule" ? "capsule(s)" : "tablet(s)";
            RxInstructions.Text = d.DefaultInstructions;
            RxDurationValue.Text = d.DefaultDurationDays.HasValue ? d.DefaultDurationDays.Value.ToString() : "";
            var perDay = (d.DefaultFrequency ?? "").Split('-').Select(x => { int n; return int.TryParse(x, out n) ? n : 0; }).Sum();
            if (perDay > 0) RxFreqValue.Text = perDay.ToString();
            var slots = (d.DefaultFrequency ?? "").Split('-');
            if (slots.Length == 3 && FieldOn("doseTiming"))
            {
                TimingBf.Checked = slots[0] != "0";
                TimingAn.Checked = slots[1] != "0";
                TimingNi.Checked = slots[2] != "0";
            }
            RxComposition.Value = d.Composition;
            RxGenericClass.Value = d.GenericClass;
            TherapeuticClassText.Text = HttpUtility.HtmlEncode(d.TherapeuticClass);
            CompositionText.Text = HttpUtility.HtmlEncode(string.IsNullOrEmpty(d.Composition) ? d.GenericName : d.Composition);
            ControlledWarning.Visible = d.IsControlledSubstance;
            if (d.IsControlledSubstance && RxCategory.Items.FindByValue("CONTROLLED") != null) RxCategory.SelectedValue = "CONTROLLED";
            PendingPanel.Visible = true;
            RxResults.DataSource = null;
            RxResults.DataBind();
            RxSearchInput.Text = RxSearchHint.Text = "";
        }

        protected void ManualEntryButton_Click(object sender, EventArgs e)
        {
            Host.ActivateStep("cpoe");
            if (!EnsureNotLocked()) return;
            ResetForm();
            RxMedicineName.ReadOnly = false;
            PendingPanel.Visible = true;
        }

        private void ResetForm()
        {
            foreach (var tb in new[] { RxMedicineName, RxStrength, RxDrugForm, RxDeliveryRoute, RxTargetArea, RxTakeInUnit, RxDurationValue, RxInstructions, RxTimingFrom, RxTimingTo, OverrideReason })
                tb.Text = "";
            RxTakeInQty.Text = "1";
            RxFreqValue.Text = "3";
            RxPeriodStart.Text = AppPage.Today.ToString("yyyy-MM-dd");
            RxDurationUnit.Value = "days"; RxFreqPeriod.Value = "day"; RxTimingUnit.Value = "minutes"; RxTimingDir.Value = "before"; RxEvent.Value = "meals";
            RxFreqType.SelectedValue = "times";
            RxCategory.SelectedIndex = 0;
            TimingBf.Checked = TimingAn.Checked = TimingNi.Checked = TimingEm.Checked = RxPrn.Checked = false;
            RxComposition.Value = RxGenericClass.Value = "";
            TherapeuticClassText.Text = CompositionText.Text = "";
            ControlledWarning.Visible = false;
            AlertPanel.Visible = false;
        }

        protected void ResetRxButton_Click(object sender, EventArgs e)
        {
            Host.ActivateStep("cpoe");
            var readOnly = RxMedicineName.ReadOnly;
            var name = RxMedicineName.Text;
            ResetForm();
            if (readOnly) { RxMedicineName.Text = name; RxMedicineName.ReadOnly = true; }
        }

        protected void CancelRxButton_Click(object sender, EventArgs e)
        {
            Host.ActivateStep("cpoe");
            ResetForm();
            PendingPanel.Visible = false;
        }

        /* ── Add with safety checks ── */

        private static double ParseDouble(string s, double fallback)
        {
            double d;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : fallback;
        }

        /// <summary>"Take 1 tablet(s) – 3 times per day 10 to 20 minutes before meals (morning, night) — as needed (PRN)".</summary>
        private string BuildInstruction()
        {
            var sb = new StringBuilder("Take " + (RxTakeInQty.Text.Trim().Length > 0 ? RxTakeInQty.Text.Trim() : "1") + " " + (RxTakeInUnit.Text.Trim().Length > 0 ? RxTakeInUnit.Text.Trim() : "tablet(s)"));
            if (RxFreqValue.Text.Trim().Length > 0) sb.Append(" – " + RxFreqValue.Text.Trim() + " " + RxFreqType.SelectedValue + " per " + RxFreqPeriod.Value);
            if (RxTimingFrom.Text.Trim().Length > 0)
                sb.Append(" " + RxTimingFrom.Text.Trim() + (RxTimingTo.Text.Trim().Length > 0 ? " to " + RxTimingTo.Text.Trim() : "") + " " + RxTimingUnit.Value + " " + RxTimingDir.Value + " " + RxEvent.Value);
            var timings = new List<string>();
            if (TimingBf.Checked) timings.Add("morning");
            if (TimingAn.Checked) timings.Add("afternoon");
            if (TimingNi.Checked) timings.Add("night");
            if (TimingEm.Checked) timings.Add("early morning");
            if (timings.Count > 0) sb.Append(" (" + string.Join(", ", timings) + ")");
            if (RxPrn.Checked) sb.Append(" — as needed (PRN)");
            return sb.ToString();
        }

        private Prescription BuildRecord()
        {
            var durationValue = ParseInt(RxDurationValue.Text, 1);
            var unit = RxDurationUnit.Value;
            var start = DateTime.ParseExact(RxPeriodStart.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            int durationDays = unit == "weeks" ? durationValue * 7 : unit == "months" ? durationValue * 30 : unit == "days" ? durationValue : 1;
            var timesPerDay = ParseInt(RxFreqValue.Text, 1);
            var qty = ParseDouble(RxTakeInQty.Text, 1);
            var validity = Host.ConsultHospital.DefaultRxValidityDays > 0 ? Host.ConsultHospital.DefaultRxValidityDays : 30;
            var instruction = BuildInstruction();
            return new Prescription
            {
                ConsultationId = ConsultationId, PatientId = Visit.PatientId, Category = RxCategory.SelectedValue,
                MedicineName = RxMedicineName.Text.Trim(), Strength = RxStrength.Text.Trim(), DrugForm = RxDrugForm.Text.Trim(),
                DeliveryRoute = RxDeliveryRoute.Text.Trim(), TargetArea = RxTargetArea.Text.Trim(), TakeInUnit = RxTakeInUnit.Text.Trim(),
                TakeInQty = qty, TimesPerDay = timesPerDay, DurationValue = durationValue,
                DurationUnit = unit == "days" ? "day(s)" : unit == "weeks" ? "week(s)" : unit == "months" ? "month(s)" : unit,
                PeriodStart = start, PeriodEnd = unit == "months" ? start.AddMonths(durationValue) : start.AddDays(durationDays),
                FrequencyMode = RxFreqType.SelectedValue, FrequencyPeriod = RxFreqPeriod.Value,
                TimingBf = TimingBf.Checked, TimingAn = TimingAn.Checked, TimingNi = TimingNi.Checked, TimingEm = TimingEm.Checked, Prn = RxPrn.Checked,
                Instructions = FieldOn("instructions") && RxInstructions.Text.Trim().Length > 0 ? RxInstructions.Text.Trim() : instruction, DynamicInstruction = instruction,
                GenericClass = RxGenericClass.Value, ValidityDays = validity, ValidUntil = AppPage.Today.AddDays(validity),
                TotalDispenseQty = qty * timesPerDay * durationDays,
            };
        }

        private static int ParseInt(string s, int fallback)
        {
            int i;
            return int.TryParse((s ?? "").Trim(), out i) ? i : fallback;
        }

        private List<ClinicalAlert> AllergyAlerts(Prescription rx)
        {
            return ConsultationService.CheckDrugAllergyConflicts(Visit.PatientId, rx.MedicineName, RxComposition.Value);
        }

        private List<ClinicalAlert> ReconAlerts(Prescription rx)
        {
            var drug = CatalogService.GetDrugByName(rx.MedicineName);
            var genericClass = drug != null ? drug.GenericClass : rx.GenericClass;
            return RxReconciler.Reconcile(rx.MedicineName, rx.Strength, genericClass,
                ConsultationService.ListActiveMedications(Visit.PatientId), ConsultationService.ListPrescriptions(ConsultationId), HospitalId);
        }

        protected void ConfirmRxButton_Click(object sender, EventArgs e)
        {
            Host.ActivateStep("cpoe");
            if (!EnsureNotLocked()) return;
            Page.Validate("Rx");
            if (!Page.IsValid) return;
            var rx = BuildRecord();
            var allergy = AllergyAlerts(rx);
            var recon = ReconAlerts(rx);
            if (allergy.Count + recon.Count > 0)
            {
                AlertTitle.Text = allergy.Count > 0 ? "Drug-Allergy Conflict Detected" : "Medication Reconciliation Alert";
                AlertsRepeater.DataSource = allergy.Concat(recon).ToList();
                AlertsRepeater.DataBind();
                ProceedOverrideButton.Text = allergy.Count > 0 ? "Override &amp; Proceed" : "Acknowledge &amp; Proceed";
                OverrideReason.Text = "";
                AlertPanel.Visible = true;
                return;
            }
            Save(rx);
        }

        protected void ProceedOverrideButton_Click(object sender, EventArgs e)
        {
            Host.ActivateStep("cpoe");
            if (!EnsureNotLocked()) return;
            Page.Validate("Override");
            Page.Validate("Rx");
            if (!Page.IsValid) return;
            var rx = BuildRecord();
            var reason = OverrideReason.Text.Trim();
            foreach (var a in AllergyAlerts(rx)) Audit("OVERRIDE_SAFETY", "DrugAllergyConflict", a.Message + " | Override reason: " + reason);
            foreach (var a in ReconAlerts(rx)) Audit("ACKNOWLEDGE_RECON", "RxReconciliation", a.Message + " | Override reason: " + reason);
            Save(rx);
        }

        protected void CancelOverrideButton_Click(object sender, EventArgs e)
        {
            Host.ActivateStep("cpoe");
            AlertPanel.Visible = false;
        }

        private void Save(Prescription rx)
        {
            ConsultationService.AddPrescription(rx);
            Audit("CREATE", "Prescription", rx.MedicineName + " " + rx.Strength);
            AppPage.ToastSuccess("Prescription added", rx.MedicineName + " " + rx.Strength);
            ResetForm();
            PendingPanel.Visible = false;
            Host.RefreshAll();
        }

        /* ── Repeat previous ── */

        protected void RepeatPrevButton_Click(object sender, EventArgs e)
        {
            Host.ActivateStep("cpoe");
            if (!EnsureNotLocked()) return;
            Consultation from;
            var previous = ConsultationService.ListPreviousPrescriptionsByDoctor(Visit.PatientId, Host.DoctorId, out from);
            if (previous.Count == 0)
            {
                AppPage.ToastWarning("No previous prescriptions", "No active prescriptions found from your previous consultations with this patient.");
                return;
            }
            RepeatIntro.Text = "Repeat prescriptions from visit " + HttpUtility.HtmlEncode(AppPage.FmtDate(from.VisitLocalDate)) + "?";
            RepeatList.Items.Clear();
            foreach (var p in previous)
                RepeatList.Items.Add(new ListItem(" " + p.MedicineName + " " + p.Strength + " · " + p.TimesPerDay + "x/day · " + p.DurationValue + " " + p.DurationUnit, p.PrescriptionId) { Selected = true });
            RepeatPanel.Visible = true;
        }

        protected void CancelRepeatButton_Click(object sender, EventArgs e)
        {
            Host.ActivateStep("cpoe");
            RepeatPanel.Visible = false;
        }

        protected void ConfirmRepeatButton_Click(object sender, EventArgs e)
        {
            Host.ActivateStep("cpoe");
            if (!EnsureNotLocked()) return;
            Consultation from;
            var previous = ConsultationService.ListPreviousPrescriptionsByDoctor(Visit.PatientId, Host.DoctorId, out from);
            var chosen = RepeatList.Items.Cast<ListItem>().Where(i => i.Selected).Select(i => i.Value).ToList();
            int added = 0;
            var validity = Host.ConsultHospital.DefaultRxValidityDays > 0 ? Host.ConsultHospital.DefaultRxValidityDays : 30;
            foreach (var p in previous.Where(x => chosen.Contains(x.PrescriptionId)))
            {
                int days = p.DurationUnit == "week(s)" ? p.DurationValue * 7 : p.DurationUnit == "month(s)" ? p.DurationValue * 30 : p.DurationValue;
                ConsultationService.AddPrescription(new Prescription
                {
                    ConsultationId = ConsultationId, PatientId = Visit.PatientId, Category = p.Category, MedicineName = p.MedicineName,
                    Strength = p.Strength, DrugForm = p.DrugForm, DeliveryRoute = p.DeliveryRoute, TakeInUnit = p.TakeInUnit, TakeInQty = p.TakeInQty,
                    TimesPerDay = p.TimesPerDay, DurationValue = p.DurationValue, DurationUnit = p.DurationUnit, FrequencyMode = p.FrequencyMode,
                    FrequencyPeriod = p.FrequencyPeriod, TimingBf = p.TimingBf, TimingAn = p.TimingAn, TimingNi = p.TimingNi, TimingEm = p.TimingEm,
                    Prn = p.Prn, Instructions = p.Instructions, GenericClass = p.GenericClass, PeriodStart = AppPage.Today, PeriodEnd = AppPage.Today.AddDays(days),
                    ValidityDays = validity, ValidUntil = AppPage.Today.AddDays(validity), TotalDispenseQty = p.TakeInQty * p.TimesPerDay * days,
                    RepeatedFromConsultationId = from.ConsultationId,
                });
                added++;
            }
            RepeatPanel.Visible = false;
            if (added == 0) { AppPage.ToastWarning("Nothing selected"); return; }
            Audit("CREATE", "Prescription", "Repeated " + added + " prescription(s) from " + from.ConsultationId);
            AppPage.ToastSuccess(added + " prescription" + (added > 1 ? "s" : "") + " repeated");
            Host.RefreshAll();
        }

        /* ── Discontinue ── */

        protected void MedsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            Host.ActivateStep("cpoe");
            if (e.CommandName != "Discontinue" || !EnsureNotLocked()) return;
            ConsultationService.DiscontinuePrescription((string)e.CommandArgument);
            Audit("UPDATE", "Prescription", "Discontinued " + e.CommandArgument);
            AppPage.ToastSuccess("Medication discontinued");
            Host.RefreshAll();
        }
    }
}
