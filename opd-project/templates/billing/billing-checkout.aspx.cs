using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.billing
{
    /// <summary>
    /// Billing checkout for one consultation: itemised invoice (service charges, custom charges, discount, tax),
    /// coverage verification and the N-way bill split, payments (patient or payer settlement), voids,
    /// cancellation with credit note, and the receipt once the patient's share is settled.
    /// </summary>
    public partial class billing_checkout : BasePage
    {
        protected override string RequiredPermission { get { return "billing"; } }
        public override string PageTitle { get { return "Billing Checkout"; } }

        private Consultation _consultation;
        private Patient _patient;
        private BillingSplit _split;

        protected Invoice Inv { get; private set; }
        protected bool Editable { get { return Inv != null && Inv.Status != "CANCELLED" && Inv.Status != "PAID"; } }

        private static string H(string s) { return HttpUtility.HtmlEncode(s ?? ""); }

        protected void Page_Load(object sender, EventArgs e)
        {
            _consultation = ConsultationService.Get(RouteValue("consultationId"));
            if (_consultation == null || _consultation.HospitalId != HospitalId)
            {
                NotFound.Visible = true;
                Workspace.Visible = false;
                return;
            }
            _patient = PatientService.Get(_consultation.PatientId);
            Inv = BillingService.GetInvoiceForConsultation(_consultation.ConsultationId) ?? BillingService.GetLatestInvoiceForConsultation(_consultation.ConsultationId);
            if (!IsPostBack) Bind();
        }

        private void Bind()
        {
            Inv = BillingService.GetInvoiceForConsultation(_consultation.ConsultationId) ?? BillingService.GetLatestInvoiceForConsultation(_consultation.ConsultationId);
            PatientName.Text = H(_patient.FullName);
            PatientMeta.Text = H(PatientIdLabel + ": " + _patient.Mrn + (string.IsNullOrEmpty(_patient.Phone) ? "" : " · " + _patient.Phone));
            var appt = SchedulingService.AppointmentForConsultation(_consultation);
            ConsultationInfo.Text = H((appt != null ? TypeName(appt.Type) : TypeName("NEW")) + " · " + FmtDate(_consultation.VisitDate) + " · " + UserService.UserName(_consultation.DoctorId)
                + (_consultation.IsLocked ? "" : " · Not Yet Signed"));

            NoInvoiceHolder.Visible = Inv == null;
            InvoiceHolder.Visible = Inv != null;
            CoverageCard.Visible = PaymentCard.Visible = ReceiptCard.Visible = false;
            if (Inv == null) return;

            _split = BillingService.GetBillingSplit(Inv.InvoiceId);
            Inv = BillingService.GetInvoice(Inv.InvoiceId);
            var lines = BillingService.ListLineItems(Inv.InvoiceId);
            LinesRepeater.DataSource = lines;
            LinesRepeater.DataBind();
            NoLines.Visible = lines.Count == 0;
            ChargeNote.Text = string.IsNullOrEmpty(Inv.ChargeNote) ? "" : "<div class=\"text-xs mb-2\" style=\"color:" + (Inv.IsFreeFollowup ? "var(--color-success)" : "var(--text-muted)") + ";\"><i class=\"ti ti-info-circle\" aria-hidden=\"true\"></i> " + H(Inv.ChargeNote) + "</div>";
            InvoiceNumber.Text = H(Inv.InvoiceNumber);
            TotalsFooter.Text = lines.Count == 0 ? "" : Totals();
            EditActions.Visible = Editable;
            CapturePaymentButton.Visible = lines.Count > 0 && Editable;
            PrintLink.NavigateUrl = Urls.InvoicePrint(Inv.InvoiceId);
            PrintLink.Visible = lines.Count > 0;
            var cn = BillingService.ListCreditNotes(Inv.InvoiceId).FirstOrDefault();
            CreditNoteText.Text = cn == null ? "" : "<div class=\"hint-banner mt-2\" style=\"border-left:3px solid var(--color-danger);\"><i class=\"ti ti-receipt-refund\" aria-hidden=\"true\"></i> Cancelled — credit note <strong>" + H(cn.CreditNoteNumber) + "</strong> for " + H(Money(cn.Amount, Inv.Currency)) + ". Reason: " + H(cn.Reason) + "</div>";

            BindCoverage(lines.Count);
            var payments = BillingService.ListPayments(Inv.InvoiceId);
            PaymentCard.Visible = payments.Count > 0;
            PaymentsRepeater.DataSource = payments;
            PaymentsRepeater.DataBind();
            var receipt = Inv.Status == "PAID" ? BillingService.GetReceipt(Inv.InvoiceId) : null;
            ReceiptCard.Visible = receipt != null;
            if (receipt != null) ReceiptHtml.Text = Receipt(receipt);
        }

        private string Totals()
        {
            var c = Inv.Currency;
            var sb = new StringBuilder();
            Action<string, string, string> row = (label, value, style) => sb.Append("<tr" + (style == null ? "" : " style=\"" + style + "\"") + "><td colspan=\"7\" style=\"text-align:right;\">" + label + "</td><td style=\"text-align:right;\">" + value + "</td><td></td></tr>");
            if (Inv.DiscountAmount > 0)
            {
                row("Subtotal", H(Money(Inv.SubTotal, c)), null);
                row("Discount", "-" + H(Money(Inv.DiscountAmount, c)), "color:var(--color-success);");
            }
            if (Inv.TaxAmount > 0) row("Tax", "+" + H(Money(Inv.TaxAmount, c)), null);
            row("Grand Total", H(Money(Inv.TotalAmount, c)), "font-weight:700; border-top:2px solid var(--border-strong);");
            if (_split.CoveredAmount > 0)
            {
                row("Covered By Insurance / Scheme", "-" + H(Money(_split.CoveredAmount, c)), "color:var(--color-info);");
                row("Patient Share", H(Money(_split.PatientLiability, c)), "font-weight:600;");
            }
            if (_split.PatientPaid > 0) row("Paid By Patient", "<span style=\"color:var(--color-success);\">" + H(Money(_split.PatientPaid, c)) + "</span>", null);
            if (_split.PatientPaid > 0 || _split.CoveredAmount > 0)
                row("Patient Due", "<span style=\"color:" + (_split.PatientDue > 0 ? "var(--color-danger)" : "var(--color-success)") + ";\">" + H(Money(_split.PatientDue, c)) + "</span>", "font-weight:700;");
            return sb.ToString();
        }

        /* ── Coverage ── */

        private GovtSchemeProfile Scheme { get { return PatientService.GetGovtSchemeProfile(Hospital.Country); } }

        private void BindCoverage(int lineCount)
        {
            CoverageCard.Visible = Inv.Status != "CANCELLED" && lineCount > 0;
            if (!CoverageCard.Visible) return;
            var c = Inv.Currency;
            Func<string, string, string, string> kpi = (value, label, color) => "<div style=\"flex:1; min-width:110px; padding:8px 10px; border:1px solid var(--border-default); border-radius:var(--radius-md);\"><div style=\"font-weight:700; font-size:15px;" + (color == null ? "" : " color:" + color + ";") + "\">" + H(value) + "</div><div class=\"text-xs text-muted\">" + H(label) + "</div></div>";
            var sb = new StringBuilder("<div style=\"display:flex; gap:8px; flex-wrap:wrap; margin-bottom:12px;\">");
            sb.Append(kpi(Money(_split.GrossAmount, c), "Charge Amount", null));
            sb.Append(kpi(Money(_split.InsuranceLiability, c), "Insurance", "var(--color-info)"));
            sb.Append(kpi(Money(_split.GovtLiability, c), Scheme.Label, "var(--color-info)"));
            sb.Append(kpi(Money(_split.PatientLiability, c), "Patient Share", null));
            sb.Append(kpi(Money(_split.PatientDue, c), "Patient Due", _split.PatientDue > 0 ? "var(--color-danger)" : "var(--color-success)"));
            sb.Append("</div>");
            if (_split.PayerOutstanding > 0 && Inv.Status == "PAID")
                sb.Append("<div class=\"hint-banner mb-2\"><i class=\"ti ti-building-bank\" aria-hidden=\"true\"></i> Patient share settled. <strong>" + H(Money(_split.PayerOutstanding, c)) + "</strong> is receivable from the payer(s) — use Capture Payment to record the settlement.</div>");
            if (_split.PatientOverpaid > 0)
                sb.Append("<div class=\"hint-banner mb-2\" style=\"border-left:3px solid var(--color-warning);\"><i class=\"ti ti-alert-triangle\" aria-hidden=\"true\"></i> Patient has paid <strong>" + H(Money(_split.PatientOverpaid, c)) + "</strong> more than their share — refund it or void a payment.</div>");
            SplitKpis.Text = sb.ToString();
            var locked = Inv.Status == "PAID";
            AddCoverageButton.Visible = !locked;
            /* only payers of the hospital's country — plus any coverage already applied to this invoice, so the split stays explained */
            var billable = PatientService.ListCoverages(_patient.PatientId, Hospital.Country).Select(x => x.CoverageId).ToList();
            var coverages = _split.GrossAmount == 0 ? new System.Collections.Generic.List<PatientCoverage>()
                : PatientService.ListCoverages(_patient.PatientId).Where(cv => billable.Contains(cv.CoverageId) || _split.Lines.Any(l => l.CoverageId == cv.CoverageId)).ToList();
            CoverageRepeater.DataSource = coverages;
            CoverageRepeater.DataBind();
            CoverageFooter.Text = _split.GrossAmount == 0
                ? "<div class=\"text-sm text-muted\">No charge for this visit — nothing to bill to a payer.</div>"
                : coverages.Count == 0
                    ? "<div class=\"hint-banner\"><i class=\"ti ti-info-circle\" aria-hidden=\"true\"></i> No coverage on file — <strong>Self-Pay</strong>. The patient pays the full charge. Use <em>Add Coverage</em> for private insurance or " + H(Scheme.Label) + ".</div>"
                    : "<p class=\"text-xs text-muted mt-2\">Eligibility is recorded manually from the insurer / scheme response. Percent approvals follow the invoice total when charges change; applied amounts can be edited.</p>";
            if (Inv.Status == "PAID" && _split.PayerOutstanding > 0) { EditActions.Visible = true; CapturePaymentButton.Visible = true; AddChargeButton.Visible = CustomChargeButton.Visible = CancelInvoiceButton.Visible = false; }
        }

        protected InvoiceCoverageLine LineFor(PatientCoverage c)
        {
            return _split == null ? null : _split.Lines.FirstOrDefault(l => l.CoverageId == c.CoverageId && l.Status == "VERIFIED");
        }

        protected string CoverageLabel(PatientCoverage c) { return PatientService.CoverageLabel(c, Hospital.Country); }

        protected bool Expired(PatientCoverage c) { return c.ExpiryDate.HasValue && c.ExpiryDate.Value.Date < DateTime.Today; }

        protected string CoverageIdent(PatientCoverage c)
        {
            var ident = c.CoverageType == "GOVT_SCHEME"
                ? Scheme.CardLabel + ": " + (string.IsNullOrEmpty(c.GovtCardNumber) ? "—" : c.GovtCardNumber)
                : "Member: " + (string.IsNullOrEmpty(c.MemberId) ? "—" : c.MemberId) + " · Policy: " + (string.IsNullOrEmpty(c.PolicyNumber) ? "—" : c.PolicyNumber)
                  + (string.IsNullOrEmpty(c.GroupNumber) ? "" : " · Group: " + c.GroupNumber) + " · " + TitleCase(c.ClaimMode);
            if (c.EffectiveDate.HasValue || c.ExpiryDate.HasValue) ident += " · Valid " + (c.EffectiveDate.HasValue ? FmtDate(c.EffectiveDate.Value) : "—") + " → " + (c.ExpiryDate.HasValue ? FmtDate(c.ExpiryDate.Value) : "—");
            return ident;
        }

        protected void CoverageRepeater_ItemDataBound(object sender, RepeaterItemEventArgs e)
        {
            var cov = e.Item.DataItem as PatientCoverage;
            if (cov == null) return;
            var line = LineFor(cov);
            var anyLine = _split.Lines.FirstOrDefault(l => l.CoverageId == cov.CoverageId);
            bool locked = Inv.Status == "PAID";
            var state = (Literal)e.Item.FindControl("CoverageState");
            var last = BillingService.ListVerifications(cov.CoverageId).FirstOrDefault();
            state.Text = line != null ? "<span class=\"badge badge--success\">Applied</span>"
                : last != null ? "<span class=\"badge " + (last.Status == "REJECTED" ? "badge--danger" : "badge--warning") + "\" style=\"font-size:10px;\">Last: " + H(TitleCase(last.Status)) + "</span>"
                : "<span class=\"text-xs text-muted\">Not Verified</span>";
            ((Literal)e.Item.FindControl("LiabilityText")).Text = line == null ? "" :
                "<div class=\"text-sm\" style=\"margin-top:6px;\">Liability <strong>" + H(Money(line.Liability, Inv.Currency)) + "</strong> · Approved "
                + H(line.ApprovedType == "PERCENT" ? line.ApprovedValue.ToString(CultureInfo.InvariantCulture) + "%" : Money(line.ApprovedValue, Inv.Currency))
                + (line.IsManualAmount ? " (Amount Edited)" : "") + " · Auth <code>" + H(string.IsNullOrEmpty(line.AuthorizationNumber) ? "—" : line.AuthorizationNumber) + "</code>"
                + (line.SettledAmount > 0 ? " · Settled By Payer <strong>" + H(Money(line.SettledAmount, Inv.Currency)) + "</strong>" : "") + "</div>";
            var form = (Panel)e.Item.FindControl("VerifyForm");
            form.Visible = !locked;
            if (line != null)
            {
                ((TextBox)e.Item.FindControl("VAuth")).Text = line.AuthorizationNumber;
                ((DropDownList)e.Item.FindControl("VType")).SelectedValue = line.ApprovedType;
                ((TextBox)e.Item.FindControl("VValue")).Text = line.ApprovedValue.ToString(CultureInfo.InvariantCulture);
            }
            e.Item.FindControl("EditLineButton").Visible = line != null && !locked;
            e.Item.FindControl("RemoveLineButton").Visible = line != null && !locked && line.SettledAmount == 0;
            e.Item.FindControl("DeactivateButton").Visible = anyLine == null && !locked;
        }

        protected void CoverageRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var coverageId = (string)e.CommandArgument;
            var line = BillingService.GetCoverageLine(Inv.InvoiceId, coverageId);
            switch (e.CommandName)
            {
                case "Apply":
                    var status = ((DropDownList)e.Item.FindControl("VStatus")).SelectedValue;
                    var type = ((DropDownList)e.Item.FindControl("VType")).SelectedValue;
                    var value = ParseMoney(((TextBox)e.Item.FindControl("VValue")).Text);
                    if (status == "VERIFIED" && value <= 0) { ToastWarning("Enter the approved % or amount"); return; }
                    if (status == "VERIFIED" && type == "PERCENT" && value > 100) { ToastWarning("Percent cannot exceed 100"); return; }
                    var ok = BillingService.ApplyCoverage(Inv.InvoiceId, coverageId, status, ((TextBox)e.Item.FindControl("VAuth")).Text.Trim(), type, value,
                        ((TextBox)e.Item.FindControl("VNotes")).Text.Trim(), UserId);
                    if (!ok) { ToastWarning("Coverage could not be changed — the payer has already settled against it"); break; }
                    Audit("UPDATE", "Invoice", Inv.InvoiceId, "Coverage " + coverageId + " " + status + (status == "VERIFIED" ? " (" + (type == "PERCENT" ? value + "%" : value.ToString(CultureInfo.InvariantCulture)) + ")" : ""));
                    if (status == "VERIFIED") ToastSuccess("Coverage verified and applied"); else ToastInfo("Eligibility recorded as " + status + " — not applied");
                    break;
                case "EditLine":
                    if (line == null) return;
                    EditLineId.Value = line.CoverageLineId;
                    EditLineAmount.Text = line.Liability.ToString(CultureInfo.InvariantCulture);
                    EditLineModal.Visible = true;
                    return;
                case "RemoveLine":
                    if (line == null || !BillingService.RemoveCoverageLine(line.CoverageLineId)) { ToastWarning("Already settled by the payer — cannot remove"); break; }
                    Audit("UPDATE", "Invoice", Inv.InvoiceId, "Coverage " + coverageId + " removed");
                    ToastSuccess("Coverage removed from invoice");
                    break;
                case "History":
                    var cov = PatientService.GetCoverage(coverageId);
                    HistoryTitle.Text = "Eligibility History — " + H(CoverageLabel(cov));
                    var rows = BillingService.ListVerifications(coverageId);
                    HistoryRepeater.DataSource = rows;
                    HistoryRepeater.DataBind();
                    NoHistory.Visible = rows.Count == 0;
                    HistoryModal.Visible = true;
                    return;
                case "Deactivate":
                    PatientService.DeactivateCoverage(coverageId);
                    Audit("UPDATE", "PatientCoverage", coverageId, "Deactivated");
                    ToastSuccess("Coverage deactivated");
                    break;
            }
            Bind();
        }

        protected void ConfirmEditLineButton_Click(object sender, EventArgs e)
        {
            Validate("EditLine");
            if (!IsValid) { EditLineModal.Visible = true; return; }
            BillingService.EditCoverageLine(EditLineId.Value, ParseMoney(EditLineAmount.Text));
            Audit("UPDATE", "Invoice", Inv.InvoiceId, "Coverage amount edited to " + EditLineAmount.Text);
            ToastSuccess("Coverage amount updated");
            EditLineModal.Visible = false;
            Bind();
        }

        protected void AddCoverageButton_Click(object sender, EventArgs e)
        {
            CovType.Items.Clear();
            CovType.Items.Add(new ListItem("Private Insurance", "PRIVATE_INSURANCE"));
            CovType.Items.Add(new ListItem(Scheme.Label, "GOVT_SCHEME"));
            CovPayerPrivate.DataSource = PatientService.ListPayers(Hospital.Country, "PRIVATE");
            CovPayerPrivate.DataTextField = "Name"; CovPayerPrivate.DataValueField = "PayerId"; CovPayerPrivate.DataBind();
            CovPayerGovt.DataSource = PatientService.ListPayers(Hospital.Country, "GOVT_SCHEME");
            CovPayerGovt.DataTextField = "Name"; CovPayerGovt.DataValueField = "PayerId"; CovPayerGovt.DataBind();
            CardLabel.Text = H(Scheme.CardLabel);
            CovGovtValidator.ErrorMessage = "Enter the " + Scheme.CardLabel;
            CovPlan.Text = CovMember.Text = CovPolicy.Text = CovGroup.Text = CovCard.Text = CovEffective.Text = CovExpiry.Text = "";
            CovHolder.Text = _patient.FullName;
            CovType_Changed(null, EventArgs.Empty);
            AddCoverageModal.Visible = true;
        }

        protected void CovType_Changed(object sender, EventArgs e)
        {
            CovPrivate.Visible = CovType.SelectedValue != "GOVT_SCHEME";
            CovGovt.Visible = CovType.SelectedValue == "GOVT_SCHEME";
            AddCoverageModal.Visible = true;
        }

        protected void CovPrivateValidator_ServerValidate(object source, ServerValidateEventArgs args)
        {
            args.IsValid = CovType.SelectedValue != "PRIVATE_INSURANCE" || CovMember.Text.Trim().Length > 0 || CovPolicy.Text.Trim().Length > 0;
        }

        protected void CovGovtValidator_ServerValidate(object source, ServerValidateEventArgs args)
        {
            args.IsValid = CovType.SelectedValue != "GOVT_SCHEME" || CovCard.Text.Trim().Length > 0;
        }

        protected void ConfirmCoverageButton_Click(object sender, EventArgs e)
        {
            Validate("Coverage");
            if (!IsValid) { AddCoverageModal.Visible = true; return; }
            var govt = CovType.SelectedValue == "GOVT_SCHEME";
            var record = new PatientCoverage
            {
                HospitalId = HospitalId, PatientId = _patient.PatientId, CoverageType = CovType.SelectedValue,
                PayerId = govt ? CovPayerGovt.SelectedValue : CovPayerPrivate.SelectedValue,
                PlanName = govt ? Scheme.Label : CovPlan.Text.Trim(), MemberId = govt ? "" : CovMember.Text.Trim(), PolicyNumber = govt ? "" : CovPolicy.Text.Trim(),
                GroupNumber = govt ? "" : CovGroup.Text.Trim(), PolicyHolderName = govt ? _patient.FullName : CovHolder.Text.Trim(),
                Relationship = govt ? "Self" : CovRelation.SelectedValue, ClaimMode = govt ? "" : CovMode.SelectedValue,
                GovtCardNumber = govt ? CovCard.Text.Trim() : "", EffectiveDate = ParseDate(CovEffective.Text), ExpiryDate = ParseDate(CovExpiry.Text), Status = CovStatus.SelectedValue,
            };
            var saved = PatientService.AddCoverage(record);
            Audit("CREATE", "PatientCoverage", saved.CoverageId, CoverageLabel(saved));
            ToastSuccess("Coverage added", "Verify it to apply to this invoice.");
            AddCoverageModal.Visible = false;
            Bind();
        }

        /* ── Invoice & line items ── */

        protected void GenerateButton_Click(object sender, EventArgs e)
        {
            var inv = BillingService.GenerateInvoiceForConsultation(_consultation.ConsultationId);
            Audit("CREATE", "Invoice", inv.InvoiceId);
            ToastSuccess("Invoice " + inv.InvoiceNumber + " generated", BillingService.ListLineItems(inv.InvoiceId).Count + " line items · " + inv.ChargeNote);
            Bind();
        }

        protected void LinesRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName != "Remove" || !Editable) return;
            BillingService.RemoveLineItem((string)e.CommandArgument);
            BillingService.RecalculateTotals(Inv.InvoiceId);
            Audit("UPDATE", "Invoice", Inv.InvoiceId, "Line removed");
            ToastSuccess("Charge removed");
            Bind();
        }

        protected void AddChargeButton_Click(object sender, EventArgs e)
        {
            ServicePick.Items.Clear();
            foreach (var s in BillingService.ListServiceCharges(HospitalId))
                ServicePick.Items.Add(new ListItem(s.ServiceName + " — " + Money(s.DefaultAmount, Inv.Currency) + " [" + s.Category + "]", s.ServiceChargeId));
            ServiceQty.Text = "1";
            ServiceDiscount.Text = "0";
            AddChargeModal.Visible = true;
        }

        protected void ConfirmAddChargeButton_Click(object sender, EventArgs e)
        {
            Validate("AddCharge");
            if (!IsValid || !Editable) { AddChargeModal.Visible = IsValid ? false : true; return; }
            var svc = BillingService.GetServiceCharge(ServicePick.SelectedValue);
            if (svc == null) return;
            BillingService.AddLineItem(Inv.InvoiceId, svc.ServiceChargeId, svc.ServiceName, svc.Category, ParseInt(ServiceQty.Text, 1), svc.DefaultAmount, ParseMoney(ServiceDiscount.Text), svc.TaxPercent);
            BillingService.RecalculateTotals(Inv.InvoiceId);
            Audit("UPDATE", "Invoice", Inv.InvoiceId, "Added " + svc.ServiceName);
            ToastSuccess(svc.ServiceName + " added");
            AddChargeModal.Visible = false;
            Bind();
        }

        protected void CustomChargeButton_Click(object sender, EventArgs e)
        {
            CustomDesc.Text = CustomAmount.Text = "";
            CustomDiscount.Text = CustomTax.Text = "0";
            CustomChargeModal.Visible = true;
        }

        protected void ConfirmCustomChargeButton_Click(object sender, EventArgs e)
        {
            Validate("Custom");
            if (!IsValid) { CustomChargeModal.Visible = true; return; }
            if (!Editable) return;
            BillingService.AddLineItem(Inv.InvoiceId, null, CustomDesc.Text.Trim(), CustomCategory.SelectedValue, 1, ParseMoney(CustomAmount.Text), ParseMoney(CustomDiscount.Text), ParseMoney(CustomTax.Text));
            BillingService.RecalculateTotals(Inv.InvoiceId);
            Audit("UPDATE", "Invoice", Inv.InvoiceId, "Added custom charge " + CustomDesc.Text.Trim());
            ToastSuccess(CustomDesc.Text.Trim() + " added");
            CustomChargeModal.Visible = false;
            Bind();
        }

        protected void CancelInvoiceButton_Click(object sender, EventArgs e)
        {
            CancelReason.Text = "";
            CancelModal.Visible = true;
        }

        protected void ConfirmCancelButton_Click(object sender, EventArgs e)
        {
            Validate("Cancel");
            if (!IsValid) { CancelModal.Visible = true; return; }
            var reason = CancelReason.Text.Trim();
            var cn = BillingService.CancelInvoice(Inv.InvoiceId, reason, UserId);
            if (cn == null) { ToastWarning("Invoice is already cancelled"); return; }
            Audit("DELETE", "Invoice", Inv.InvoiceId, "Invoice cancelled. Credit note: " + cn.CreditNoteNumber + ". Reason: " + reason);
            ToastSuccess("Invoice cancelled", "Credit note " + cn.CreditNoteNumber + " issued.");
            CancelModal.Visible = false;
            Bind();
        }

        /* ── Payments ── */

        private class Component
        {
            public string Value; public string Label; public decimal Due;
        }

        private System.Collections.Generic.List<Component> Components()
        {
            var split = BillingService.GetBillingSplit(Inv.InvoiceId);
            var list = new System.Collections.Generic.List<Component> { new Component { Value = "PATIENT", Label = "Patient Share — Due " + Money(split.PatientDue, Inv.Currency), Due = split.PatientDue } };
            foreach (var l in split.Lines.Where(l => l.Status == "VERIFIED" && l.Liability - l.SettledAmount > 0))
                list.Add(new Component
                {
                    Value = l.CoverageLineId, Due = Math.Round(l.Liability - l.SettledAmount, 2),
                    Label = (l.CoverageType == "GOVT_SCHEME" ? "Scheme" : "Insurance") + " Settlement — " + l.PayerName + " (Due " + Money(l.Liability - l.SettledAmount, Inv.Currency) + ")",
                });
            return list;
        }

        protected void CapturePaymentButton_Click(object sender, EventArgs e)
        {
            var comps = Components();
            if (comps.All(c => c.Due <= 0)) { ToastWarning("Nothing is due on this invoice"); return; }
            var split = BillingService.GetBillingSplit(Inv.InvoiceId);
            PaymentHint.Text = "Patient Due: <strong>" + H(Money(split.PatientDue, Inv.Currency)) + "</strong>" + (split.PayerOutstanding > 0 ? " · Receivable From Payers: <strong>" + H(Money(split.PayerOutstanding, Inv.Currency)) + "</strong>" : "");
            PayComponent.Items.Clear();
            foreach (var c in comps)
            {
                var item = new ListItem(c.Label, c.Value);
                if (c.Due <= 0) item.Attributes["disabled"] = "disabled";
                PayComponent.Items.Add(item);
            }
            var first = comps.First(c => c.Due > 0);
            PayComponent.SelectedValue = first.Value;
            PayAmount.Text = first.Due.ToString("0.00", CultureInfo.InvariantCulture);
            PayMethod.Items.Clear();
            foreach (var m in HospitalService.ListPaymentMethods(HospitalId)) PayMethod.Items.Add(new ListItem(TitleCase(m.MethodName), m.MethodName));
            PayReference.Text = "";
            PaymentModal.Visible = true;
        }

        protected void PayComponent_Changed(object sender, EventArgs e)
        {
            var comp = Components().FirstOrDefault(c => c.Value == PayComponent.SelectedValue);
            if (comp != null) PayAmount.Text = comp.Due.ToString("0.00", CultureInfo.InvariantCulture);
            PaymentModal.Visible = true;
        }

        protected void PayAmountValidator_ServerValidate(object source, ServerValidateEventArgs args)
        {
            var amount = ParseMoney(args.Value);
            var comp = Components().FirstOrDefault(c => c.Value == PayComponent.SelectedValue);
            if (amount <= 0) { args.IsValid = false; PayAmountValidator.Text = "Enter a valid amount"; return; }
            if (comp == null || amount > comp.Due + 0.001m) { args.IsValid = false; PayAmountValidator.Text = "More than what is due (" + Money(comp != null ? comp.Due : 0, Inv.Currency) + ")"; return; }
            args.IsValid = true;
        }

        protected void ConfirmPaymentButton_Click(object sender, EventArgs e)
        {
            Validate("Pay");
            if (!IsValid) { PaymentModal.Visible = true; return; }
            var amount = ParseMoney(PayAmount.Text);
            var coverageLine = PayComponent.SelectedValue == "PATIENT" ? null : PayComponent.SelectedValue;
            var pay = BillingService.RecordPayment(Inv.InvoiceId, coverageLine, PayMethod.SelectedValue, amount, PayReference.Text.Trim(), UserId);
            Audit("CREATE", "Payment", Inv.InvoiceId, pay.TransactionNumber + " " + pay.PaymentMethod + " " + amount.ToString(CultureInfo.InvariantCulture));
            ToastSuccess("Payment recorded", pay.TransactionNumber);
            PaymentModal.Visible = false;
            Bind();
        }

        protected void PaymentsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName != "Void") return;
            VoidTransactionId.Value = (string)e.CommandArgument;
            VoidReason.Text = "";
            VoidModal.Visible = true;
        }

        protected void ConfirmVoidButton_Click(object sender, EventArgs e)
        {
            Validate("Void");
            if (!IsValid) { VoidModal.Visible = true; return; }
            var reason = VoidReason.Text.Trim();
            if (BillingService.VoidPayment(VoidTransactionId.Value, reason, UserId))
            {
                Audit("UPDATE", "Payment", VoidTransactionId.Value, "Payment voided: " + reason);
                ToastSuccess("Payment voided");
            }
            VoidModal.Visible = false;
            Bind();
        }

        /* ── Receipt ── */

        private string Receipt(Receipt r)
        {
            var covered = _split.Lines.Where(l => l.Status == "VERIFIED" && l.Liability > 0).ToList();
            var splitHtml = covered.Count == 0 ? "" : "<div style=\"text-align:left; margin-top:10px; font-size:12px;\">"
                + string.Join("", covered.Select(l => "<div style=\"display:flex; justify-content:space-between; padding:2px 0;\"><span>" + H((l.CoverageType == "GOVT_SCHEME" ? Scheme.Label : "Insurance") + " · " + l.PayerName) + "</span><span>" + H(Money(l.Liability, r.Currency)) + "</span></div>"))
                + "<div style=\"display:flex; justify-content:space-between; padding:2px 0; font-weight:600;\"><span>Patient</span><span>" + H(Money(_split.PatientLiability, r.Currency)) + "</span></div></div>";
            return "<div style=\"text-align:center; border:2px solid var(--border-strong); border-radius:8px; padding:16px;\">"
                + "<div style=\"font-weight:700; font-size:14px; margin-bottom:4px;\">" + H(Hospital.Name) + "</div>"
                + "<div class=\"text-xs text-muted mb-3\">" + H(Hospital.LegalAddress) + "</div>"
                + "<div style=\"font-weight:600; font-size:18px; color:var(--color-success); margin-bottom:8px;\">Payment Receipt</div>"
                + "<div class=\"text-sm mb-1\"><strong>Receipt #:</strong> " + H(r.ReceiptNumber) + "</div>"
                + "<div class=\"text-sm mb-1\"><strong>Date:</strong> " + H(FmtDate(r.GeneratedAt)) + "</div>"
                + "<div class=\"text-sm mb-1\"><strong>Patient:</strong> " + H(_patient.FullName) + "</div>"
                + "<div style=\"border-top:1px dashed var(--border-strong); margin:12px 0; padding-top:12px;\"><div style=\"font-size:20px; font-weight:700;\">" + H(Money(r.TotalAmount, r.Currency)) + "</div>"
                + "<div class=\"text-xs text-muted\">" + (covered.Count > 0 ? "Patient Share Paid" : "Paid In Full") + "</div>" + splitHtml + "</div>"
                + "<a class=\"btn btn--sm btn--ghost mt-2\" style=\"width:100%; justify-content:center;\" target=\"_blank\" href=\"" + H(Urls.InvoicePrint(r.InvoiceId)) + "\"><i class=\"ti ti-printer\"></i> Print Receipt</a></div>";
        }

        protected void CloseModals_Click(object sender, EventArgs e)
        {
            AddChargeModal.Visible = CustomChargeModal.Visible = CancelModal.Visible = PaymentModal.Visible = VoidModal.Visible =
                EditLineModal.Visible = HistoryModal.Visible = AddCoverageModal.Visible = false;
            Bind();
        }

        protected override void OnPreRender(EventArgs e)
        {
            base.OnPreRender(e);
            if (_consultation != null && IsPostBack && !Response.IsRequestBeingRedirected && _split == null && Inv != null) _split = BillingService.GetBillingSplit(Inv.InvoiceId);
        }
    }
}
