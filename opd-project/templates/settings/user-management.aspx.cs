using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.settings
{
    /// <summary>
    /// Add / edit / enable-disable users (validated: required fields, e-mail format and uniqueness, password length).
    /// A user may hold several clinic roles (e.g. DOCTOR + ADMIN); access is the union of what those roles grant.
    /// LAB_PARTNER accounts are linked to a lab / imaging centre (existing, or created inline), can't hold any other
    /// role, and can't be switched to or from a clinic account, so an outside lab never inherits clinical access.
    /// </summary>
    public partial class user_management : BasePage
    {
        protected override string RequiredPermission { get { return "admin_users"; } }
        public override string PageTitle { get { return "User Management"; } }

        protected string CurrentUserId { get { return UserId; } }

        private const string NewLabPartnerValue = "__new";

        protected string SpecialtyOrLab(AppUser u)
        {
            if (u.HasRole("LAB_PARTNER"))
            {
                var name = LabService.LabPartnerName(u.LabPartnerId);
                return name.Length > 0 ? name : "Not linked";
            }
            return string.IsNullOrEmpty(u.SpecialtyId) ? "—" : HospitalService.SpecialtyName(u.SpecialtyId);
        }

        private List<string> SelectedRoles
        {
            get { return UserRoles.Items.Cast<ListItem>().Where(i => i.Selected).Select(i => i.Value).ToList(); }
        }

        private void SelectRoles(IEnumerable<string> roles)
        {
            var set = new HashSet<string>(roles ?? Enumerable.Empty<string>());
            foreach (ListItem i in UserRoles.Items) i.Selected = set.Contains(i.Value);
        }

        private bool LabSelected { get { return SelectedRoles.Contains("LAB_PARTNER"); } }

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
            bool lab = LabSelected;
            LabPartnerHolder.Visible = lab;
            SpecialtyHolder.Visible = !lab;
            NewLabPartnerHolder.Visible = lab && UserLabPartner.SelectedValue == NewLabPartnerValue;
        }

        protected void UserRoles_SelectedIndexChanged(object sender, EventArgs e)
        {
            ApplyRoleFields();
            UserModal.Visible = true;
        }

        protected void UserLabPartner_SelectedIndexChanged(object sender, EventArgs e)
        {
            ApplyRoleFields();
            UserModal.Visible = true;
        }

        /// <summary>At least one role, and LAB_PARTNER only on its own.</summary>
        protected void RolesValidator_ServerValidate(object source, ServerValidateEventArgs args)
        {
            var roles = SelectedRoles;
            if (roles.Count == 0)
            {
                RolesValidator.ErrorMessage = "Select at least one role";
                RolesValidator.Text = "Required";
                args.IsValid = false;
                return;
            }
            if (UserService.NormalizeRoles(roles) == null)
            {
                RolesValidator.ErrorMessage = "LAB_PARTNER can't be combined with other roles";
                RolesValidator.Text = "Lab partner must be the only role";
                args.IsValid = false;
                return;
            }
            args.IsValid = true;
        }

        protected void LabPartnerValidator_ServerValidate(object source, ServerValidateEventArgs args)
        {
            if (!LabSelected) { args.IsValid = true; return; }
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
            foreach (var r in UserService.ListRoles()) UserRoles.Items.Add(r);
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
            SelectRoles(null);
            UserSpecialty.SelectedIndex = 0;
            BindLabPartners(null);
            ApplyRoleFields();
            PasswordHolder.Visible = PasswordRequired.Enabled = true;
            UserEmail.ReadOnly = false;
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
            SelectRoles(u.Roles);
            UserSpecialty.SelectedValue = UserSpecialty.Items.FindByValue(u.SpecialtyId ?? "") != null ? (u.SpecialtyId ?? "") : "";
            BindLabPartners(u.LabPartnerId);
            ApplyRoleFields();
            PasswordHolder.Visible = PasswordRequired.Enabled = false;
            // API mode: UpdateUser has no e-mail member, so the sign-in e-mail is fixed once the account exists.
            UserEmail.ReadOnly = DataSource.IsApi("Users");
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
            var roles = UserService.NormalizeRoles(SelectedRoles);
            bool lab = roles.Contains("LAB_PARTNER");
            var rolesLabel = string.Join(", ", roles);
            if (!string.IsNullOrEmpty(EditingUserId.Value))
            {
                var existing = UserService.GetUser(EditingUserId.Value);
                if (existing == null || existing.HospitalId != HospitalId) { UserModal.Visible = false; return; }
                if (existing.HasRole("LAB_PARTNER") != lab)
                {
                    ToastWarning("A lab partner account can't be switched to a clinic role (or the reverse). Create a new account instead.");
                    ApplyRoleFields();
                    UserModal.Visible = true;
                    return;
                }
                if (existing.UserId == UserId && !roles.SequenceEqual(existing.Roles))
                {
                    ToastWarning("You cannot change your own roles");
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
                var u = UserService.CreateUser(HospitalId, UserName.Text.Trim(), UserEmail.Text.Trim(), roles, UserSpecialty.SelectedValue, UserPassword.Text, labPartnerId);
                if (u.HasRole("DOCTOR")) SchedulingService.RefreshDoctorNames(HospitalId);
                Audit("CREATE", "User", u.UserId, u.FullName + " (" + rolesLabel + (lab ? " · " + LabService.LabPartnerName(labPartnerId) : "") + ")");
                ToastSuccess(u.FullName + " created");
            }
            else
            {
                UserService.UpdateUser(EditingUserId.Value, UserName.Text.Trim(), UserEmail.Text.Trim(), roles, UserSpecialty.SelectedValue, labPartnerId);
                Audit("UPDATE", "User", EditingUserId.Value, UserName.Text.Trim() + " (" + rolesLabel + (lab ? " · " + LabService.LabPartnerName(labPartnerId) : "") + ")");
                ToastSuccess("User updated");
            }
            UserModal.Visible = false;
            Bind();
        }

        protected void CloseModal_Click(object sender, EventArgs e) { UserModal.Visible = false; }
    }
}
