<%@ Page Title="Schedule Settings" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="schedule-settings.aspx.cs" Inherits="OpdProject.templates.settings.schedule_settings" MaintainScrollPositionOnPostback="true" %>
<%-- Schedule Settings: hospital defaults, doctor profiles + weekly timetable / leave / overrides / charges,
     appointment types, intake & consent. Every value feeds the slot engine (booking) or the consultation charge
     (billing). Route: /settings/scheduling (?tab=hospital|doctors|types|intake) --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container titlecase-headings">
  <div class="card mb-3">
    <div style="font-size:var(--text-lg); font-weight:600;">Schedule Settings</div>
    <div class="text-xs text-muted">Doctor timetables, slot length, leave, appointment types, fees and pre-visit forms — used by every booking screen and by billing</div>
  </div>
  <div class="card" style="padding:0; overflow:hidden;">
    <div class="settings-hub">
      <div class="settings-sidebar">
        <asp:Repeater ID="TabsRepeater" runat="server" ItemType="OpdProject.Web.NavItem">
          <ItemTemplate><a class="settings-tab<%# Item.Path == ActiveTab ? " settings-tab--active" : "" %>" href="?tab=<%#: Item.Path %>"><i class="ti <%#: Item.Icon %>" aria-hidden="true"></i> <span><%#: Item.Label %></span></a></ItemTemplate>
        </asp:Repeater>
      </div>
      <div class="settings-content">

        <%-- ═══ Hospital defaults ═══ --%>
        <asp:Panel ID="HospitalTab" runat="server" Visible="false" DefaultButton="SaveDefaultsButton">
          <h3 style="margin-top:0;">Hospital Defaults</h3>
          <div class="settings-section mb-3">
            <div class="settings-section-header"><i class="ti ti-clock-hour-4" aria-hidden="true"></i> Appointment Policy</div>
            <div class="form-grid">
              <div class="form-group"><label class="form-label" for="<%= SlotMins.ClientID %>">Default Slot (Minutes)</label><asp:TextBox ID="SlotMins" runat="server" CssClass="form-control" TextMode="Number" />
                <asp:RangeValidator runat="server" ControlToValidate="SlotMins" ValidationGroup="Defaults" Type="Integer" MinimumValue="5" MaximumValue="240" Text="Default slot must be 5–240 minutes" CssClass="field-error" Display="Dynamic" />
                <asp:RequiredFieldValidator runat="server" ControlToValidate="SlotMins" ValidationGroup="Defaults" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
              <div class="form-group"><label class="form-label" for="<%= BufferMins.ClientID %>">Buffer Between Appointments (Minutes)</label><asp:TextBox ID="BufferMins" runat="server" CssClass="form-control" TextMode="Number" />
                <asp:RangeValidator runat="server" ControlToValidate="BufferMins" ValidationGroup="Defaults" Type="Integer" MinimumValue="0" MaximumValue="120" Text="Buffer must be 0–120 minutes" CssClass="field-error" Display="Dynamic" /></div>
            </div>
            <p class="text-xs text-muted" style="margin:0;">Each doctor's weekly timetable decides when slots are offered. Doctors can override the slot length; the buffer is kept free after every appointment.</p>
          </div>
          <div class="settings-section">
            <div class="settings-section-header"><i class="ti ti-receipt" aria-hidden="true"></i> Free Follow-Up Policy</div>
            <asp:CheckBox ID="FreeAllowed" runat="server" Text=" Allow Free Follow-Ups After An Initial Consultation" />
            <div class="form-group" style="max-width:240px; margin-top:10px;"><label class="form-label" for="<%= FreeDays.ClientID %>">Free Within (Days)</label><asp:TextBox ID="FreeDays" runat="server" CssClass="form-control" TextMode="Number" />
              <asp:RangeValidator runat="server" ControlToValidate="FreeDays" ValidationGroup="Defaults" Type="Integer" MinimumValue="0" MaximumValue="365" Text="0–365 days" CssClass="field-error" Display="Dynamic" /></div>
            <p class="text-xs text-muted" style="margin:0;">A visit with the same doctor inside this window of the last paid consultation is billed at 0. Each doctor can override this under Doctors → Scheduling &amp; charges.</p>
          </div>
          <asp:LinkButton ID="SaveDefaultsButton" runat="server" CssClass="btn btn--primary mt-3" ValidationGroup="Defaults" OnClick="SaveDefaultsButton_Click"><i class="ti ti-device-floppy" aria-hidden="true"></i> Save Hospital Defaults</asp:LinkButton>
        </asp:Panel>

        <%-- ═══ Doctors ═══ --%>
        <asp:PlaceHolder ID="DoctorsTab" runat="server" Visible="false">
          <div style="display:flex; justify-content:space-between; align-items:center;"><h3 style="margin-top:0;">Doctors</h3>
            <asp:LinkButton ID="AddDoctorButton" runat="server" CssClass="btn btn--sm btn--primary" OnClick="AddDoctorButton_Click" CausesValidation="false"><i class="ti ti-user-plus" aria-hidden="true"></i> Add Doctor</asp:LinkButton></div>
          <table class="data-table" style="font-size:12px;">
            <thead><tr><th>Doctor</th><th>Specialties</th><th>Reg. No.</th><th>Room</th><th>Days</th><th>Slot</th><th>Charge</th><th></th></tr></thead>
            <tbody>
              <asp:Repeater ID="DoctorsRepeater" runat="server" ItemType="OpdProject.Models.AppUser" OnItemCommand="DoctorsRepeater_ItemCommand">
                <ItemTemplate>
                  <tr style='<%# Item.UserId == SelectedDoctorId ? "background:var(--color-primary-light,#e0f2fe);" : "" %>'>
                    <td><div style="font-weight:600;"><%#: Item.FullName %></div><div class="text-xs text-muted"><%#: Item.Qualifications %></div></td>
                    <td class="text-xs"><%#: SpecialtyNames(Item.UserId) %></td>
                    <td class="text-xs"><%#: Item.LicenseNumber %></td>
                    <td class="text-xs"><%#: Item.Room %></td>
                    <td class="text-xs"><%#: DaysText(Item.UserId) %></td>
                    <td class="text-xs"><%#: SlotText(Item.UserId) %></td>
                    <td class="text-xs"><%#: ChargeText(Item.UserId) %></td>
                    <td style="white-space:nowrap;">
                      <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Profile" CommandArgument="<%# Item.UserId %>" CausesValidation="false"><i class="ti ti-edit" aria-hidden="true"></i> Profile</asp:LinkButton>
                      <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Schedule" CommandArgument="<%# Item.UserId %>" CausesValidation="false" style="color:var(--color-primary);"><i class="ti ti-calendar-cog" aria-hidden="true"></i> Scheduling &amp; Charges</asp:LinkButton>
                    </td>
                  </tr>
                </ItemTemplate>
              </asp:Repeater>
            </tbody>
          </table>

          <%-- Doctor profile --%>
          <asp:Panel ID="ProfilePanel" runat="server" CssClass="card mt-3" Visible="false" DefaultButton="SaveProfileButton" style="border-left:4px solid var(--color-primary);">
            <div style="display:flex; justify-content:space-between; align-items:center;"><strong><asp:Literal ID="ProfileTitle" runat="server" /></strong>
              <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" OnClick="ClosePanels_Click" CausesValidation="false"><i class="ti ti-x" aria-hidden="true"></i></asp:LinkButton></div>
            <asp:ValidationSummary runat="server" ValidationGroup="DoctorProfile" CssClass="validation-summary mt-2" />
            <div class="form-grid mt-2">
              <div class="form-group"><label class="form-label" for="<%= DpPrefix.ClientID %>">Prefix</label><asp:TextBox ID="DpPrefix" runat="server" CssClass="form-control" MaxLength="10" /></div>
              <div class="form-group"><label class="form-label" for="<%= DpFirst.ClientID %>">First Name *</label><asp:TextBox ID="DpFirst" runat="server" CssClass="form-control" MaxLength="40" />
                <asp:RequiredFieldValidator runat="server" ControlToValidate="DpFirst" ValidationGroup="DoctorProfile" ErrorMessage="First name is required" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
              <div class="form-group"><label class="form-label" for="<%= DpMiddle.ClientID %>">Middle Name</label><asp:TextBox ID="DpMiddle" runat="server" CssClass="form-control" MaxLength="40" /></div>
              <div class="form-group"><label class="form-label" for="<%= DpLast.ClientID %>">Last Name *</label><asp:TextBox ID="DpLast" runat="server" CssClass="form-control" MaxLength="40" />
                <asp:RequiredFieldValidator runat="server" ControlToValidate="DpLast" ValidationGroup="DoctorProfile" ErrorMessage="Last name is required" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
              <div class="form-group"><label class="form-label" for="<%= DpQualifications.ClientID %>">Qualifications</label><asp:TextBox ID="DpQualifications" runat="server" CssClass="form-control" placeholder="MBBS, MD" MaxLength="80" /></div>
              <div class="form-group"><label class="form-label" for="<%= DpDisplayOverride.ClientID %>">Display Name Override</label><asp:TextBox ID="DpDisplayOverride" runat="server" CssClass="form-control" placeholder="Leave Blank To Use The Hospital Template" MaxLength="80" /></div>
              <div class="form-group"><label class="form-label" for="<%= DpLicense.ClientID %>"><%: RegLabel %></label><asp:TextBox ID="DpLicense" runat="server" CssClass="form-control" MaxLength="30" /></div>
              <div class="form-group"><label class="form-label" for="<%= DpRoom.ClientID %>">Room</label><asp:TextBox ID="DpRoom" runat="server" CssClass="form-control" MaxLength="30" /></div>
              <div class="form-group"><label class="form-label" for="<%= DpEmail.ClientID %>">Email (Login) *</label><asp:TextBox ID="DpEmail" runat="server" CssClass="form-control" TextMode="Email" MaxLength="120" />
                <asp:RequiredFieldValidator runat="server" ControlToValidate="DpEmail" ValidationGroup="DoctorProfile" ErrorMessage="Email is required" Text="Required" CssClass="field-error" Display="Dynamic" />
                <asp:RegularExpressionValidator runat="server" ControlToValidate="DpEmail" ValidationGroup="DoctorProfile" ValidationExpression="^[^@\s]+@[^@\s]+\.[^@\s]+$" ErrorMessage="Enter a valid email" Text="Invalid email" CssClass="field-error" Display="Dynamic" />
                <asp:CustomValidator runat="server" ControlToValidate="DpEmail" ValidationGroup="DoctorProfile" OnServerValidate="DoctorEmailUnique_ServerValidate" ErrorMessage="Another user already has this email" Text="Already in use" CssClass="field-error" Display="Dynamic" /></div>
              <asp:PlaceHolder ID="DpPasswordGroup" runat="server" Visible="false"><div class="form-group"><label class="form-label" for="<%= DpPassword.ClientID %>">Password *</label><asp:TextBox ID="DpPassword" runat="server" CssClass="form-control" TextMode="Password" MaxLength="100" autocomplete="new-password" />
                <asp:CustomValidator runat="server" ValidationGroup="DoctorProfile" OnServerValidate="DoctorPassword_ServerValidate" ErrorMessage="Set a password of at least 8 characters for the new doctor" Text="At least 8 characters" CssClass="field-error" Display="Dynamic" /></div></asp:PlaceHolder>
              <div class="form-group"><label class="form-label" for="<%= DpPhone.ClientID %>">Phone</label><asp:TextBox ID="DpPhone" runat="server" CssClass="form-control" MaxLength="20" placeholder="876-555-1234" />
                <c360:PhoneValidator runat="server" ControlToValidate="DpPhone" ValidationGroup="DoctorProfile" /></div>
              <div class="form-group"><label class="form-label" for="<%= DpLanguages.ClientID %>">Languages</label><asp:TextBox ID="DpLanguages" runat="server" CssClass="form-control" placeholder="English, Telugu" /></div>
              <div class="form-group"><label class="form-label">Specialties *</label><asp:CheckBoxList ID="DpSpecialties" runat="server" RepeatLayout="Flow" RepeatDirection="Horizontal" CssClass="inline-list" />
                <asp:CustomValidator runat="server" ValidationGroup="DoctorProfile" OnServerValidate="SpecialtiesRequired_ServerValidate" ErrorMessage="Choose at least one specialty" Text="Choose at least one" CssClass="field-error" Display="Dynamic" /></div>
              <div class="form-group form-group--full"><label class="form-label" for="<%= DpBio.ClientID %>">Bio</label><asp:TextBox ID="DpBio" runat="server" CssClass="form-control" TextMode="MultiLine" /></div>
            </div>
            <div class="text-xs text-muted">New doctors sign in with the e-mail and password set here. Display name preview follows Settings → Formats.</div>
            <asp:LinkButton ID="SaveProfileButton" runat="server" CssClass="btn btn--primary mt-2" ValidationGroup="DoctorProfile" OnClick="SaveProfileButton_Click"><i class="ti ti-device-floppy" aria-hidden="true"></i> Save Doctor</asp:LinkButton>
          </asp:Panel>

          <%-- Scheduling & charges --%>
          <asp:Panel ID="SchedulePanel" runat="server" CssClass="card mt-3" Visible="false" style="border-left:4px solid var(--color-primary);">
            <div style="display:flex; justify-content:space-between; align-items:center;"><strong><asp:Literal ID="ScheduleTitle" runat="server" /></strong>
              <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" OnClick="ClosePanels_Click" CausesValidation="false"><i class="ti ti-x" aria-hidden="true"></i></asp:LinkButton></div>

            <div class="text-xs text-muted" style="font-weight:600; margin:12px 0 6px;">Weekly Availability</div>
            <table class="data-table" style="font-size:12px;"><thead><tr><th style="width:80px;">Day</th><th>Windows</th></tr></thead><tbody>
              <asp:Repeater ID="WeekRepeater" runat="server" ItemType="OpdProject.Web.DayWindows" OnItemCommand="WeekRepeater_ItemCommand" OnItemDataBound="WeekRepeater_ItemDataBound">
                <ItemTemplate><tr><td style="font-weight:600;"><%#: Item.Label %></td><td>
                  <asp:Repeater ID="WindowsRepeater" runat="server" ItemType="OpdProject.Models.DoctorSlotConfig" OnItemCommand="WindowsRepeater_ItemCommand">
                    <ItemTemplate><span class="badge badge--info" style="display:inline-flex; align-items:center; gap:4px; margin:2px;"><%#: Item.StartTime %>–<%#: Item.EndTime %>
                      <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="RemoveWindow" CommandArgument="<%# Item.SlotConfigId %>" CausesValidation="false" style="height:18px; padding:0 4px;" ToolTip="Remove window">×</asp:LinkButton></span></ItemTemplate>
                  </asp:Repeater>
                  <asp:Literal ID="ClosedText" runat="server" />
                  <span style="display:inline-flex; gap:4px; align-items:center; margin-left:6px;">
                    <asp:TextBox ID="WinStart" runat="server" CssClass="form-control" TextMode="Time" style="width:110px; height:28px; font-size:11px;" />
                    <asp:TextBox ID="WinEnd" runat="server" CssClass="form-control" TextMode="Time" style="width:110px; height:28px; font-size:11px;" />
                    <asp:LinkButton runat="server" CssClass="btn btn--xs btn--primary" CommandName="AddWindow" CommandArgument="<%# Item.Code %>" CausesValidation="false">Add</asp:LinkButton>
                  </span>
                </td></tr></ItemTemplate>
              </asp:Repeater>
            </tbody></table>

            <div class="form-grid mt-3">
              <div>
                <div class="text-xs text-muted" style="font-weight:600; margin-bottom:6px;">Overrides</div>
                <div class="form-group"><label class="form-label" for="<%= OvSlot.ClientID %>">Slot Length (Minutes) — Blank = Hospital Default (<asp:Literal ID="HospitalSlotText" runat="server" />)</label><asp:TextBox ID="OvSlot" runat="server" CssClass="form-control" TextMode="Number" />
                  <asp:RangeValidator runat="server" ControlToValidate="OvSlot" ValidationGroup="Overrides" Type="Integer" MinimumValue="5" MaximumValue="240" Text="5–240" CssClass="field-error" Display="Dynamic" /></div>
                <div class="form-group"><label class="form-label" for="<%= OvFreeAllowed.ClientID %>">Free Follow-Up</label>
                  <asp:DropDownList ID="OvFreeAllowed" runat="server" CssClass="form-select"><asp:ListItem Value="">Inherit Hospital Policy</asp:ListItem><asp:ListItem Value="true">Allowed</asp:ListItem><asp:ListItem Value="false">Not Allowed</asp:ListItem></asp:DropDownList></div>
                <div class="form-group"><label class="form-label" for="<%= OvFreeDays.ClientID %>">Free Within (Days) — Blank = Inherit</label><asp:TextBox ID="OvFreeDays" runat="server" CssClass="form-control" TextMode="Number" />
                  <asp:RangeValidator runat="server" ControlToValidate="OvFreeDays" ValidationGroup="Overrides" Type="Integer" MinimumValue="0" MaximumValue="365" Text="0–365" CssClass="field-error" Display="Dynamic" /></div>
              </div>
              <div>
                <div class="text-xs text-muted" style="font-weight:600; margin-bottom:6px;">Charges</div>
                <div class="form-group"><label class="form-label" for="<%= ChModel.ClientID %>">Charge Model</label>
                  <asp:DropDownList ID="ChModel" runat="server" CssClass="form-select" AutoPostBack="true" OnSelectedIndexChanged="ChModel_Changed"><asp:ListItem Value="FLAT">Flat Fee Per Visit</asp:ListItem><asp:ListItem Value="UNIT">Per Time Unit</asp:ListItem></asp:DropDownList></div>
                <div class="form-group"><label class="form-label" for="<%= ChNewFee.ClientID %>">New Consultation Fee</label><asp:TextBox ID="ChNewFee" runat="server" CssClass="form-control" TextMode="Number" step="0.01" />
                  <asp:RangeValidator runat="server" ControlToValidate="ChNewFee" ValidationGroup="Overrides" Type="Double" MinimumValue="0" MaximumValue="10000000" Text="0 or more" CssClass="field-error" Display="Dynamic" /></div>
                <div class="form-group"><label class="form-label" for="<%= ChFollowFee.ClientID %>">Follow-Up Fee</label><asp:TextBox ID="ChFollowFee" runat="server" CssClass="form-control" TextMode="Number" step="0.01" />
                  <asp:RangeValidator runat="server" ControlToValidate="ChFollowFee" ValidationGroup="Overrides" Type="Double" MinimumValue="0" MaximumValue="10000000" Text="0 or more" CssClass="field-error" Display="Dynamic" /></div>
                <asp:PlaceHolder ID="UnitHolder" runat="server">
                  <div class="form-group"><label class="form-label" for="<%= ChUnitMins.ClientID %>">Unit (Minutes)</label><asp:TextBox ID="ChUnitMins" runat="server" CssClass="form-control" TextMode="Number" />
                    <asp:RangeValidator runat="server" ControlToValidate="ChUnitMins" ValidationGroup="Overrides" Type="Integer" MinimumValue="5" MaximumValue="240" Text="5–240" CssClass="field-error" Display="Dynamic" /></div>
                  <div class="form-group"><label class="form-label" for="<%= ChUnitRate.ClientID %>">Rate Per Unit</label><asp:TextBox ID="ChUnitRate" runat="server" CssClass="form-control" TextMode="Number" step="0.01" />
                    <asp:RangeValidator runat="server" ControlToValidate="ChUnitRate" ValidationGroup="Overrides" Type="Double" MinimumValue="0" MaximumValue="10000000" Text="0 or more" CssClass="field-error" Display="Dynamic" /></div>
                </asp:PlaceHolder>
              </div>
            </div>
            <asp:LinkButton ID="SaveOverridesButton" runat="server" CssClass="btn btn--primary" ValidationGroup="Overrides" OnClick="SaveOverridesButton_Click"><i class="ti ti-device-floppy" aria-hidden="true"></i> Save Overrides &amp; Charges</asp:LinkButton>

            <div class="text-xs text-muted" style="font-weight:600; margin:16px 0 6px;">Leave / Holidays</div>
            <div style="display:flex; flex-wrap:wrap; gap:6px; margin-bottom:8px;">
              <asp:Repeater ID="LeaveRepeater" runat="server" ItemType="OpdProject.Models.DoctorLeave" OnItemCommand="LeaveRepeater_ItemCommand">
                <ItemTemplate><span class="badge badge--warning" style="display:inline-flex; align-items:center; gap:4px;"><%#: FmtDate(Item.LeaveDate) %> · <%#: Item.Reason %>
                  <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="RemoveLeave" CommandArgument="<%# Item.LeaveId %>" CausesValidation="false" style="height:18px; padding:0 4px;" ToolTip="Remove leave">×</asp:LinkButton></span></ItemTemplate>
              </asp:Repeater>
              <asp:Literal ID="NoLeave" runat="server" />
            </div>
            <asp:Panel ID="LeavePanel" runat="server" DefaultButton="AddLeaveButton" style="display:flex; gap:6px; align-items:flex-start; flex-wrap:wrap;">
              <asp:TextBox ID="LeaveDate" runat="server" CssClass="form-control" TextMode="Date" style="width:160px;" />
              <asp:TextBox ID="LeaveReason" runat="server" CssClass="form-control" style="width:220px;" placeholder="Reason (Optional)" MaxLength="80" />
              <asp:LinkButton ID="AddLeaveButton" runat="server" CssClass="btn btn--sm" ValidationGroup="Leave" OnClick="AddLeaveButton_Click"><i class="ti ti-calendar-off" aria-hidden="true"></i> Add</asp:LinkButton>
              <asp:RequiredFieldValidator runat="server" ControlToValidate="LeaveDate" ValidationGroup="Leave" Text="Choose a date" CssClass="field-error" Display="Dynamic" />
            </asp:Panel>
          </asp:Panel>
        </asp:PlaceHolder>

        <%-- ═══ Appointment types ═══ --%>
        <asp:PlaceHolder ID="TypesTab" runat="server" Visible="false">
          <h3 style="margin-top:0;">Appointment Types</h3>
          <table class="data-table" style="font-size:12px;">
            <thead><tr><th>Type</th><th>Code</th><th>Duration</th><th>Fee Category</th><th>Chargeable</th><th>Status</th><th></th></tr></thead>
            <tbody>
              <asp:Repeater ID="TypesRepeater" runat="server" ItemType="OpdProject.Models.AppointmentType" OnItemCommand="TypesRepeater_ItemCommand">
                <ItemTemplate><tr>
                  <td><span style="display:inline-block; width:10px; height:10px; border-radius:2px; background:<%#: Item.Color %>;"></span> <%#: Item.Name %></td>
                  <td class="text-xs"><%#: Item.Code %></td>
                  <td><%#: Item.DurationMinutes.HasValue ? Item.DurationMinutes + " min" : "Doctor's Slot" %></td>
                  <td><%#: Item.FeeCategory == "FOLLOWUP" ? "Follow-Up" : OpdProject.Web.BasePage.TitleCase(Item.FeeCategory) %></td><td><%# Item.IsChargeable ? "Yes" : "No" %></td>
                  <td><span class="badge <%# Item.IsActive ? "badge--success" : "badge--danger" %>"><%# Item.IsActive ? "Active" : "Inactive" %></span></td>
                  <td style="white-space:nowrap;"><asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="EditType" CommandArgument="<%# Item.AppointmentTypeId %>" CausesValidation="false"><i class="ti ti-edit"></i></asp:LinkButton>
                    <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Toggle" CommandArgument="<%# Item.AppointmentTypeId %>" CausesValidation="false"><%# Item.IsActive ? "Deactivate" : "Activate" %></asp:LinkButton></td>
                </tr></ItemTemplate>
              </asp:Repeater>
            </tbody>
          </table>
          <asp:Panel ID="TypeForm" runat="server" CssClass="settings-section mt-3" DefaultButton="SaveTypeButton">
            <div class="settings-section-header"><asp:Literal ID="TypeFormTitle" runat="server" Text="Add Appointment Type" /></div>
            <asp:HiddenField ID="EditingTypeId" runat="server" />
            <div class="form-grid">
              <div class="form-group"><label class="form-label" for="<%= TypeName.ClientID %>">Name *</label><asp:TextBox ID="TypeName" runat="server" CssClass="form-control" MaxLength="40" />
                <asp:RequiredFieldValidator runat="server" ControlToValidate="TypeName" ValidationGroup="Type" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
              <div class="form-group"><label class="form-label" for="<%= TypeDuration.ClientID %>">Duration (Minutes) — Blank = Doctor's Slot</label><asp:TextBox ID="TypeDuration" runat="server" CssClass="form-control" TextMode="Number" />
                <asp:RangeValidator runat="server" ControlToValidate="TypeDuration" ValidationGroup="Type" Type="Integer" MinimumValue="5" MaximumValue="240" Text="5–240" CssClass="field-error" Display="Dynamic" /></div>
              <div class="form-group"><label class="form-label" for="<%= TypeColor.ClientID %>">Colour</label><asp:TextBox ID="TypeColor" runat="server" CssClass="form-control" TextMode="Color" Text="#0f766e" /></div>
              <div class="form-group"><label class="form-label" for="<%= TypeFeeCategory.ClientID %>">Fee Category</label>
                <asp:DropDownList ID="TypeFeeCategory" runat="server" CssClass="form-select"><asp:ListItem Value="NEW">New Consultation Fee</asp:ListItem><asp:ListItem Value="FOLLOWUP">Follow-Up Fee</asp:ListItem></asp:DropDownList></div>
              <div class="form-group"><asp:CheckBox ID="TypeChargeable" runat="server" Checked="true" Text=" Chargeable" /></div>
            </div>
            <div style="display:flex; gap:8px;">
              <asp:LinkButton ID="SaveTypeButton" runat="server" CssClass="btn btn--primary" ValidationGroup="Type" OnClick="SaveTypeButton_Click"><i class="ti ti-device-floppy" aria-hidden="true"></i> Save Type</asp:LinkButton>
              <asp:LinkButton ID="CancelTypeButton" runat="server" CssClass="btn btn--ghost" OnClick="CancelTypeButton_Click" CausesValidation="false" Visible="false">Cancel Edit</asp:LinkButton>
            </div>
          </asp:Panel>
        </asp:PlaceHolder>

        <%-- ═══ Intake & consent ═══ --%>
        <asp:PlaceHolder ID="IntakeTab" runat="server" Visible="false">
          <h3 style="margin-top:0;">Intake &amp; Consent</h3>
          <div class="settings-section">
            <div class="settings-section-header">Pre-Visit Intake Questions (Shown When Patients Book Online)</div>
            <table class="data-table" style="font-size:12px;"><thead><tr><th>#</th><th>Question</th><th>Type</th><th>Options</th><th></th></tr></thead><tbody>
              <asp:Repeater ID="QuestionsRepeater" runat="server" ItemType="OpdProject.Models.IntakeQuestion" OnItemCommand="QuestionsRepeater_ItemCommand">
                <ItemTemplate><tr><td><%# Container.ItemIndex + 1 %></td><td><%#: Item.Label %></td><td class="text-xs"><%#: OpdProject.Web.BasePage.TitleCase(Item.QuestionType) %></td><td class="text-xs"><%#: Item.Options %></td>
                  <td><asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Remove" CommandArgument="<%# Item.QuestionId %>" CausesValidation="false" style="color:var(--color-danger);"
                    OnClientClick="return confirm('Remove this question? Past answers keep their labels.');">Remove</asp:LinkButton></td></tr></ItemTemplate>
              </asp:Repeater>
            </tbody></table>
            <asp:Panel ID="QuestionPanel" runat="server" DefaultButton="AddQuestionButton" style="display:flex; gap:6px; flex-wrap:wrap; margin-top:8px; align-items:flex-start;">
              <asp:TextBox ID="QuestionLabel" runat="server" CssClass="form-control" style="width:260px;" placeholder="Question" MaxLength="120" />
              <asp:DropDownList ID="QuestionType" runat="server" CssClass="form-select" style="width:120px;"><asp:ListItem Value="TEXT">Text</asp:ListItem><asp:ListItem Value="NUMBER">Number</asp:ListItem><asp:ListItem Value="CHOICE">Choice</asp:ListItem></asp:DropDownList>
              <asp:TextBox ID="QuestionOptions" runat="server" CssClass="form-control" style="width:200px;" placeholder="Choices, Comma-Separated" />
              <asp:LinkButton ID="AddQuestionButton" runat="server" CssClass="btn btn--sm btn--primary" ValidationGroup="Question" OnClick="AddQuestionButton_Click"><i class="ti ti-plus"></i> Add Question</asp:LinkButton>
              <asp:RequiredFieldValidator runat="server" ControlToValidate="QuestionLabel" ValidationGroup="Question" Text="Enter the question" CssClass="field-error" Display="Dynamic" />
              <asp:CustomValidator runat="server" ValidationGroup="Question" OnServerValidate="ChoiceOptions_ServerValidate" Text="Choice questions need at least two options" CssClass="field-error" Display="Dynamic" />
            </asp:Panel>
          </div>
          <asp:Panel ID="ConsentPanel" runat="server" CssClass="settings-section mt-3" DefaultButton="SaveConsentButton">
            <div class="settings-section-header">Consent Form <span class="text-xs text-muted" style="font-weight:400;">(version <asp:Literal ID="ConsentVersion" runat="server" /> — edits create a new version)</span></div>
            <div class="form-group"><label class="form-label" for="<%= ConsentTitle.ClientID %>">Title</label><asp:TextBox ID="ConsentTitle" runat="server" CssClass="form-control" MaxLength="120" />
              <asp:RequiredFieldValidator runat="server" ControlToValidate="ConsentTitle" ValidationGroup="Consent" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
            <div class="form-group"><label class="form-label" for="<%= ConsentBody.ClientID %>">Consent Text</label><asp:TextBox ID="ConsentBody" runat="server" CssClass="form-control" TextMode="MultiLine" style="min-height:110px;" />
              <asp:RequiredFieldValidator runat="server" ControlToValidate="ConsentBody" ValidationGroup="Consent" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
            <asp:LinkButton ID="SaveConsentButton" runat="server" CssClass="btn btn--primary" ValidationGroup="Consent" OnClick="SaveConsentButton_Click"><i class="ti ti-device-floppy" aria-hidden="true"></i> Save Consent Text</asp:LinkButton>
          </asp:Panel>
        </asp:PlaceHolder>
      </div>
    </div>
  </div>
</div>
</asp:Content>
