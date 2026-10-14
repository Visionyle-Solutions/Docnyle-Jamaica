using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.UI.WebControls;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.settings
{
    /// <summary>
    /// Settings hub: Hospital &amp; Practice, Clinical Config, Billing &amp; Numbers, Notifications, Users &amp; Access,
    /// System &amp; Data. Tabs are plain links (?tab=); each tab's forms post back to the server.
    /// </summary>
    public partial class settings_hub : BasePage
    {
        protected override string RequiredPermission { get { return "admin_hospital"; } }
        public override string PageTitle { get { return "Settings"; } }

        private static readonly string[][] Tabs =
        {
            new[] { "hospital", "Hospital & Practice", "ti-building" }, new[] { "clinical", "Clinical Config", "ti-stethoscope" },
            new[] { "billing", "Billing & Numbers", "ti-receipt" }, new[] { "notifications", "Notifications", "ti-bell" },
            new[] { "users", "Users & Access", "ti-users-group" }, new[] { "system", "System & Data", "ti-database" },
        };

        protected string ActiveTab
        {
            get
            {
                var t = Request.QueryString["tab"] ?? RouteValue("tab") ?? "hospital";
                return Tabs.Any(x => x[0] == t) ? t : "hospital";
            }
        }

        protected string CurrentUserId { get { return UserId; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            TabsRepeater.DataSource = Tabs.Select(t => new NavItem { Path = t[0], Label = t[1], Icon = t[2] }).ToList();
            TabsRepeater.DataBind();
            HospitalTab.Visible = ActiveTab == "hospital";
            ClinicalTab.Visible = ActiveTab == "clinical";
            BillingTab.Visible = ActiveTab == "billing";
            NotificationsTab.Visible = ActiveTab == "notifications";
            UsersTab.Visible = ActiveTab == "users";
            SystemTab.Visible = ActiveTab == "system";
            if (IsPostBack) return;
            switch (ActiveTab)
            {
                case "hospital": HospitalProfile.Bind(); break;
                case "clinical": VitalsConfig.Bind(); SpecialtyConfig.Bind(); BindHistoryItems(); break;
                case "billing": BindBilling(); break;
                case "notifications": BindNotifications(); break;
                case "users": BindUsers(); break;
            }
        }

        /* ── Clinical: medical history checklist ── */

        private void BindHistoryItems()
        {
            HistoryItemsRepeater.DataSource = HospitalService.ListMedicalHistoryItems(HospitalId, false);
            HistoryItemsRepeater.DataBind();
        }

        protected void SaveHistoryItemsButton_Click(object sender, EventArgs e)
        {
            foreach (RepeaterItem item in HistoryItemsRepeater.Items)
                HospitalService.SetMedicalHistoryItemActive(((HiddenField)item.FindControl("ItemId")).Value, ((CheckBox)item.FindControl("Active")).Checked);
            Audit("UPDATE", "MedicalHistoryItemConfig", HospitalId, "Checklist saved");
            ToastSuccess("Medical history checklist saved");
        }

        protected void AddHistoryItemButton_Click(object sender, EventArgs e)
        {
            Validate("History");
            if (!IsValid) return;
            var name = NewHistoryItem.Text.Trim();
            if (HospitalService.ListMedicalHistoryItems(HospitalId, false).Any(i => string.Equals(i.ItemName, name, StringComparison.OrdinalIgnoreCase)))
            {
                ToastWarning("\"" + name + "\" is already in the checklist");
                return;
            }
            HospitalService.AddMedicalHistoryItem(HospitalId, name);
            Audit("CREATE", "MedicalHistoryItemConfig", HospitalId, name);
            ToastSuccess("\"" + name + "\" added");
            NewHistoryItem.Text = "";
            BindHistoryItems();
        }

        /* ── Billing & numbers ── */

        private void BindBilling()
        {
            PrefixRx.Text = Hospital.RxPrefix;
            PrefixReceipt.Text = Hospital.ReceiptPrefix;
            PrefixInvoice.Text = Hospital.InvoicePrefix;
            ServicesRepeater.DataSource = BillingService.ListAllServiceCharges(HospitalId);
            ServicesRepeater.DataBind();
            FeesRepeater.DataSource = SchedulingService.ListFees(HospitalId);
            FeesRepeater.DataBind();
        }

        protected void SaveBillingButton_Click(object sender, EventArgs e)
        {
            Validate("Billing");
            if (!IsValid) return;
            HospitalService.UpdateHospital(HospitalId, h =>
            {
                h.RxPrefix = PrefixRx.Text.Trim().Length > 0 ? PrefixRx.Text.Trim().ToUpperInvariant() : "RX";
                h.ReceiptPrefix = PrefixReceipt.Text.Trim().Length > 0 ? PrefixReceipt.Text.Trim().ToUpperInvariant() : "RCP";
                h.InvoicePrefix = PrefixInvoice.Text.Trim().Length > 0 ? PrefixInvoice.Text.Trim().ToUpperInvariant() : "INV";
            });
            Audit("UPDATE", "Hospital", HospitalId, "Billing prefixes saved");
            ToastSuccess("Billing settings saved");
        }

        protected void AddServiceButton_Click(object sender, EventArgs e)
        {
            Validate("Service");
            if (!IsValid) return;
            var code = SvcCode.Text.Trim().Length > 0 ? SvcCode.Text.Trim().ToUpperInvariant() : "SVC-" + DateTime.Now.ToString("HHmmss");
            var svc = BillingService.AddServiceCharge(HospitalId, code, SvcName.Text.Trim(), SvcCategory.SelectedValue, ParseMoney(SvcAmount.Text), ParseMoney(SvcTax.Text));
            Audit("CREATE", "ServiceCharge", svc.ServiceChargeId, svc.ServiceName);
            ToastSuccess(svc.ServiceName + " added to the service master");
            SvcCode.Text = SvcName.Text = SvcAmount.Text = "";
            SvcTax.Text = "0";
            BindBilling();
        }

        protected void ServicesRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            BillingService.ToggleServiceCharge((string)e.CommandArgument);
            Audit("UPDATE", "ServiceCharge", (string)e.CommandArgument, "Toggled active");
            ToastSuccess("Service updated");
            BindBilling();
        }

        /* ── Notifications ── */

        private void BindNotifications()
        {
            var wa = NotificationService.WhatsApp;
            WaProvider.SelectedValue = wa.Provider; WaPhoneId.Text = wa.PhoneNumberId; WaBusinessId.Text = wa.BusinessAccountId; WaEnabled.Checked = wa.Enabled;
            WaApiKey.Attributes["placeholder"] = string.IsNullOrEmpty(wa.ApiKey) ? "Enter API key" : "••••" + wa.ApiKey.Substring(Math.Max(0, wa.ApiKey.Length - 4));
            var em = NotificationService.Email;
            EmProvider.SelectedValue = em.Provider; EmHost.Text = em.SmtpHost; EmPort.Text = em.SmtpPort.ToString(); EmFromEmail.Text = em.FromEmail; EmFromName.Text = em.FromName; EmEnabled.Checked = em.Enabled;
            EmApiKey.Attributes["placeholder"] = string.IsNullOrEmpty(em.ApiKey) ? "Enter key" : "••••" + em.ApiKey.Substring(Math.Max(0, em.ApiKey.Length - 4));
            TemplatesRepeater.DataSource = NotificationService.ListTemplates();
            TemplatesRepeater.DataBind();
        }

        protected void SaveWhatsAppButton_Click(object sender, EventArgs e)
        {
            lock (DataStore.Sync)
            {
                var wa = NotificationService.WhatsApp;
                wa.Provider = WaProvider.SelectedValue; wa.PhoneNumberId = WaPhoneId.Text.Trim(); wa.BusinessAccountId = WaBusinessId.Text.Trim(); wa.Enabled = WaEnabled.Checked;
                if (WaApiKey.Text.Length > 0) wa.ApiKey = WaApiKey.Text;
            }
            Audit("UPDATE", "NotificationConfig", "whatsapp", WaEnabled.Checked ? "Enabled" : "Disabled");
            ToastSuccess("WhatsApp configuration saved");
            BindNotifications();
        }

        protected void SaveEmailButton_Click(object sender, EventArgs e)
        {
            Validate("Email");
            if (!IsValid) return;
            lock (DataStore.Sync)
            {
                var em = NotificationService.Email;
                em.Provider = EmProvider.SelectedValue; em.SmtpHost = EmHost.Text.Trim(); em.SmtpPort = ParseInt(EmPort.Text, 587);
                em.FromEmail = EmFromEmail.Text.Trim(); em.FromName = EmFromName.Text.Trim(); em.Enabled = EmEnabled.Checked;
                if (EmApiKey.Text.Length > 0) em.ApiKey = EmApiKey.Text;
            }
            Audit("UPDATE", "NotificationConfig", "email", EmEnabled.Checked ? "Enabled" : "Disabled");
            ToastSuccess("Email configuration saved");
            BindNotifications();
        }

        protected void TemplatesRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var t = NotificationService.GetTemplate((string)e.CommandArgument);
            if (t == null) return;
            if (e.CommandName == "Toggle")
            {
                NotificationService.SetTemplateEnabled(t.TemplateId, !t.IsEnabled);
                Audit("UPDATE", "MessageTemplate", t.TemplateId, t.Name + (t.IsEnabled ? " enabled" : " disabled"));
                ToastSuccess(t.Name + (t.IsEnabled ? " enabled" : " disabled"));
                BindNotifications();
                return;
            }
            TemplateId.Value = t.TemplateId;
            TplName.Text = t.Name;
            TplChannel.SelectedValue = t.Channel;
            TplText.Text = t.Text;
            TemplateModal.Visible = true;
        }

        protected void SaveTemplateButton_Click(object sender, EventArgs e)
        {
            Validate("Template");
            if (!IsValid) { TemplateModal.Visible = true; return; }
            NotificationService.UpdateTemplate(TemplateId.Value, TplName.Text.Trim(), TplChannel.SelectedValue, TplText.Text.Trim());
            Audit("UPDATE", "MessageTemplate", TemplateId.Value, TplName.Text.Trim());
            ToastSuccess("Template updated");
            TemplateModal.Visible = false;
            BindNotifications();
        }

        protected void CloseTemplate_Click(object sender, EventArgs e) { TemplateModal.Visible = false; }

        /* ── Users ── */

        private void BindUsers()
        {
            UsersRepeater.DataSource = UserService.ListUsers(HospitalId);
            UsersRepeater.DataBind();
        }

        protected void UsersRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var id = (string)e.CommandArgument;
            if (id == UserId) { ToastWarning("You cannot disable your own account"); return; }
            var u = UserService.ToggleActive(id);
            Audit("UPDATE", "User", id, u.FullName + (u.IsActive ? " enabled" : " disabled"));
            ToastSuccess(u.FullName + (u.IsActive ? " enabled" : " disabled"));
            BindUsers();
        }
    }
}
