using System;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.settings
{
    /// <summary>
    /// Add / edit / enable-disable users (validated: required fields, e-mail format and uniqueness, password length).
    /// LAB_PARTNER accounts are linked to a lab / imaging centre (existing, or created inline); an account cannot be
    /// switched between a lab partner role and a clinic role, so an outside lab never inherits clinical access.
    /// </summary>
    public partial class user_management : BasePage
    {
        protected override string RequiredPermission { get { return "admin_users"; } }
        public override string PageTitle { get { return "User Management"; } }

        protected string CurrentUserId { get { return UserId; } }

        private const string NewLabPartnerValue = "__new";

        protected string SpecialtyOrLab(AppUser u)
        {
            if (u.Role == "LAB_PARTNER")
            {
                var name = LabService.LabPartnerName(u.LabPartnerId);
                return name.Length > 0 ? name : "Not linked";
            }
            return string.IsNullOrEmpty(u.SpecialtyId) ? "—" : HospitalService.SpecialtyName(u.SpecialtyId);
        }

        private void BindLabPartners(string selected)
        {
            UserLabPartner.Items.Clear();
            UserLabPartner.Items.Add(new ListItem("— Select lab / imaging centre —", ""));
            foreach (var p in LabService.ListLabPartners(HospitalId)) UserLabPartner.Items.Add(new ListItem(p.Name, p.LabPartnerId));
            UserLabPartner.Items.Add(new ListItem("+ New lab / imaging centre…", NewLabPartnerValue));
            UserLabPartner.SelectedValue = UserLabPartner.Items.FindByValue(selected ?? "") != null ? (selected ?? "") : "";
            NewLabPartnerName.Text = "";
        }

        /// <summary>Shows the lab fields for LAB_PARTNER and the specialty field for clinic roles.</summary>
        private void ApplyRoleFields()
        {
            bool lab = UserRole.SelectedValue == "LAB_PARTNER";
            LabPartnerHolder.Visible = lab;
            SpecialtyHolder.Visible = !lab;
            NewLabPartnerHolder.Visible = lab && UserLabPartner.SelectedValue == NewLabPartnerValue;
        }

        protected void UserRole_SelectedIndexChanged(object sender, EventArgs e)
        {
            ApplyRoleFields();
            UserModal.Visible = true;
        }

        protected void UserLabPartner_SelectedIndexChanged(object sender, EventArgs e)
        {
            ApplyRoleFields();
            UserModal.Visible = true;
        }

        protected void LabPartnerValidator_ServerValidate(object source, ServerValidateEventArgs args)
        {
            if (UserRole.SelectedValue != "LAB_PARTNER") { args.IsValid = true; return; }
            var v = UserLabPartner.SelectedValue;
            if (v == NewLabPartnerValue)
            {
                args.IsValid = NewLabPartnerName.Text.Trim().Length > 0;
                LabPartnerValidator.ErrorMessage = "Enter the new lab / imaging centre's name";
                return;
            }
            var p = LabService.GetLabPartner(v);
            args.IsValid = p != null && p.HospitalId == HospitalId;
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (IsPostBack) return;
            foreach (var r in UserService.AssignableRoles) UserRole.Items.Add(r);
            UserSpecialty.Items.Add(new ListItem("—", ""));
            foreach (var s in HospitalService.ListSpecialties(HospitalId, true)) UserSpecialty.Items.Add(new ListItem(s.SpecialtyName, s.SpecialtyId));
            Bind();
        }

        private void Bind()
        {
            UsersRepeater.DataSource = UserService.ListUsers(HospitalId);
            UsersRepeater.DataBind();
        }

        protected void AddUserButton_Click(object sender, EventArgs e)
        {
            EditingUserId.Value = "";
            UserModalTitle.Text = "Add User";
            UserName.Text = UserEmail.Text = UserPassword.Text = "";
            UserRole.SelectedIndex = UserSpecialty.SelectedIndex = 0;
            BindLabPartners(null);
            ApplyRoleFields();
            PasswordHolder.Visible = PasswordRequired.Enabled = true;
            UserModal.Visible = true;
        }

        protected void UsersRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var u = UserService.GetUser((string)e.CommandArgument);
            if (u == null || u.HospitalId != HospitalId) return;
            if (e.CommandName == "Toggle")
            {
                if (u.UserId == UserId) { ToastWarning("You cannot disable your own account"); return; }
                UserService.ToggleActive(u.UserId);
                Audit("UPDATE", "User", u.UserId, u.FullName + (u.IsActive ? " enabled" : " disabled"));
                ToastSuccess(u.FullName + (u.IsActive ? " enabled" : " disabled"));
                Bind();
                return;
            }
            EditingUserId.Value = u.UserId;
            UserModalTitle.Text = "Edit: " + Server.HtmlEncode(u.FullName);
            UserName.Text = u.FullName;
            UserEmail.Text = u.Email;
            if (UserRole.Items.FindByValue(u.Role) != null) UserRole.SelectedValue = u.Role;
            UserSpecialty.SelectedValue = UserSpecialty.Items.FindByValue(u.SpecialtyId ?? "") != null ? (u.SpecialtyId ?? "") : "";
            BindLabPartners(u.LabPartnerId);
            ApplyRoleFields();
            PasswordHolder.Visible = PasswordRequired.Enabled = false;
            UserModal.Visible = true;
        }

        protected void EmailUniqueValidator_ServerValidate(object source, ServerValidateEventArgs args)
        {
            args.IsValid = !UserService.EmailInUse(args.Value.Trim(), string.IsNullOrEmpty(EditingUserId.Value) ? null : EditingUserId.Value);
        }

        protected void SaveUserButton_Click(object sender, EventArgs e)
        {
            Validate("User");
            if (!IsValid) { ApplyRoleFields(); UserModal.Visible = true; return; }
            bool lab = UserRole.SelectedValue == "LAB_PARTNER";
            if (!string.IsNullOrEmpty(EditingUserId.Value))
            {
                var existing = UserService.GetUser(EditingUserId.Value);
                if (existing == null || existing.HospitalId != HospitalId) { UserModal.Visible = false; return; }
                if ((existing.Role == "LAB_PARTNER") != lab)
                {
                    ToastWarning("A lab partner account can't be switched to a clinic role (or the reverse). Create a new account instead.");
                    ApplyRoleFields();
                    UserModal.Visible = true;
                    return;
                }
            }

            string labPartnerId = null;
            if (lab)
            {
                if (UserLabPartner.SelectedValue == NewLabPartnerValue)
                {
                    var partner = LabService.SaveLabPartner(HospitalId, NewLabPartnerName.Text);
                    Audit("CREATE", "LabPartner", partner.LabPartnerId, partner.Name);
                    labPartnerId = partner.LabPartnerId;
                }
                else labPartnerId = UserLabPartner.SelectedValue;
            }

            if (string.IsNullOrEmpty(EditingUserId.Value))
            {
                var u = UserService.CreateUser(HospitalId, UserName.Text.Trim(), UserEmail.Text.Trim(), UserRole.SelectedValue, UserSpecialty.SelectedValue, UserPassword.Text, labPartnerId);
                if (u.Role == "DOCTOR") SchedulingService.RefreshDoctorNames(HospitalId);
                Audit("CREATE", "User", u.UserId, u.FullName + " (" + u.Role + (lab ? " · " + LabService.LabPartnerName(labPartnerId) : "") + ")");
                ToastSuccess(u.FullName + " created");
            }
            else
            {
                if (EditingUserId.Value == UserId && UserRole.SelectedValue != Role) { ToastWarning("You cannot change your own role"); UserModal.Visible = true; return; }
                UserService.UpdateUser(EditingUserId.Value, UserName.Text.Trim(), UserEmail.Text.Trim(), UserRole.SelectedValue, UserSpecialty.SelectedValue, labPartnerId);
                Audit("UPDATE", "User", EditingUserId.Value, UserName.Text.Trim() + " (" + UserRole.SelectedValue + (lab ? " · " + LabService.LabPartnerName(labPartnerId) : "") + ")");
                ToastSuccess("User updated");
            }
            UserModal.Visible = false;
            Bind();
        }

        protected void CloseModal_Click(object sender, EventArgs e) { UserModal.Visible = false; }
    }
}
