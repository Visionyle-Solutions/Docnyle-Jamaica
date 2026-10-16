<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="patient-form.ascx.cs" Inherits="OpdProject.templates.patients.patient_form" %>
<%-- Patient demographic fields shared by staff registration/edit (patient-registration.aspx) and the public
     self-registration portal (self-register.aspx), so both forms stay identical. Sections follow the MediSlot
     layout: Identity, National identifiers, Contact, Address, Emergency contact, Other. The host page owns the
     heading, validation summary, duplicate warning and save button, and calls Setup / Fill / ApplyTo. --%>
<div class="reg-section-title">Identity</div>
<div class="reg-fields">
  <div class="form-group">
    <label class="form-label" for="<%= PrefixInput.ClientID %>">Prefix</label>
    <asp:DropDownList ID="PrefixInput" runat="server" CssClass="form-select">
      <asp:ListItem Value="">—</asp:ListItem>
      <asp:ListItem>Mr.</asp:ListItem><asp:ListItem>Ms.</asp:ListItem><asp:ListItem>Mrs.</asp:ListItem>
      <asp:ListItem>Master</asp:ListItem><asp:ListItem>Baby</asp:ListItem>
    </asp:DropDownList>
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= FirstNameInput.ClientID %>">First name <span class="required">*</span></label>
    <asp:TextBox ID="FirstNameInput" runat="server" CssClass="form-control" MaxLength="60" />
    <asp:RequiredFieldValidator runat="server" ControlToValidate="FirstNameInput" ErrorMessage="First name is required" Text="Required" CssClass="field-error" Display="Dynamic" />
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= MiddleNameInput.ClientID %>">Middle name</label>
    <asp:TextBox ID="MiddleNameInput" runat="server" CssClass="form-control" MaxLength="60" />
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= LastNameInput.ClientID %>">Last name <span class="required">*</span></label>
    <asp:TextBox ID="LastNameInput" runat="server" CssClass="form-control" MaxLength="60" />
    <asp:RequiredFieldValidator runat="server" ControlToValidate="LastNameInput" ErrorMessage="Last name is required" Text="Required" CssClass="field-error" Display="Dynamic" />
  </div>
</div>
<div class="reg-fields">
  <div class="form-group">
    <label class="form-label" for="<%= GenderInput.ClientID %>">Gender <span class="required">*</span></label>
    <asp:DropDownList ID="GenderInput" runat="server" CssClass="form-select">
      <asp:ListItem Value="">Select</asp:ListItem>
      <asp:ListItem Value="FEMALE">Female</asp:ListItem>
      <asp:ListItem Value="MALE">Male</asp:ListItem>
      <asp:ListItem Value="OTHER">Other</asp:ListItem>
    </asp:DropDownList>
    <asp:RequiredFieldValidator runat="server" ControlToValidate="GenderInput" ErrorMessage="Gender is required" Text="Required" CssClass="field-error" Display="Dynamic" />
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= DobInput.ClientID %>">Date of birth <span class="required">*</span></label>
    <asp:TextBox ID="DobInput" runat="server" CssClass="form-control" TextMode="Date" />
    <small id="<%= DobInput.ClientID %>_hint" class="text-xs text-muted"></small>
    <asp:RequiredFieldValidator runat="server" ControlToValidate="DobInput" ErrorMessage="Date of birth is required" Text="Required" CssClass="field-error" Display="Dynamic" />
    <asp:CustomValidator ID="DobValidator" runat="server" ControlToValidate="DobInput" OnServerValidate="DobValidator_ServerValidate" ErrorMessage="Date of birth cannot be in the future or more than 130 years ago" Text="Invalid date" CssClass="field-error" Display="Dynamic" />
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= BloodGroupInput.ClientID %>">Blood group</label>
    <asp:DropDownList ID="BloodGroupInput" runat="server" CssClass="form-select">
      <asp:ListItem Value="">—</asp:ListItem>
      <asp:ListItem>A+</asp:ListItem><asp:ListItem>A-</asp:ListItem>
      <asp:ListItem>B+</asp:ListItem><asp:ListItem>B-</asp:ListItem>
      <asp:ListItem>O+</asp:ListItem><asp:ListItem>O-</asp:ListItem>
      <asp:ListItem>AB+</asp:ListItem><asp:ListItem>AB-</asp:ListItem>
    </asp:DropDownList>
  </div>
</div>

<div class="reg-section-title">National identifiers — <%: CountryName %></div>
<div class="reg-fields">
  <asp:PlaceHolder ID="TrnGroup" runat="server">
  <div class="form-group">
    <label class="form-label" for="<%= TrnInput.ClientID %>">TRN</label>
    <asp:TextBox ID="TrnInput" runat="server" CssClass="form-control" MaxLength="20" placeholder="Tax Registration Number" />
  </div>
  </asp:PlaceHolder>
  <div class="form-group">
    <label class="form-label" for="<%= HealthIdInput.ClientID %>"><%: HealthIdLabel %> <asp:PlaceHolder ID="HealthIdRequiredMark" runat="server"><span class="required">*</span></asp:PlaceHolder></label>
    <asp:TextBox ID="HealthIdInput" runat="server" CssClass="form-control" MaxLength="40" />
    <asp:RequiredFieldValidator ID="HealthIdRequired" runat="server" ControlToValidate="HealthIdInput" Text="Required" CssClass="field-error" Display="Dynamic" />
  </div>
</div>

<div class="reg-section-title">Contact</div>
<div class="reg-fields">
  <div class="form-group">
    <label class="form-label" for="<%= CountryCodeInput.ClientID %>">Country code</label>
    <asp:TextBox ID="CountryCodeInput" runat="server" CssClass="form-control" MaxLength="5" />
    <asp:RegularExpressionValidator runat="server" ControlToValidate="CountryCodeInput" ValidationExpression="^\+?[0-9]{1,4}$" ErrorMessage="Country code must look like +1 or +91" Text="Invalid code" CssClass="field-error" Display="Dynamic" />
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= MobileInput.ClientID %>">Mobile <span class="required">*</span></label>
    <asp:TextBox ID="MobileInput" runat="server" CssClass="form-control" MaxLength="16" />
    <asp:RequiredFieldValidator runat="server" ControlToValidate="MobileInput" ErrorMessage="Mobile number is required" Text="Required" CssClass="field-error" Display="Dynamic" />
    <asp:RegularExpressionValidator runat="server" ControlToValidate="MobileInput" ValidationExpression="^[0-9][0-9\- ]{5,15}$" ErrorMessage="Mobile number may contain only digits, spaces and '-'" Text="Invalid mobile" CssClass="field-error" Display="Dynamic" />
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= EmailInput.ClientID %>">Email</label>
    <asp:TextBox ID="EmailInput" runat="server" CssClass="form-control" TextMode="Email" MaxLength="100" />
    <asp:RegularExpressionValidator runat="server" ControlToValidate="EmailInput" ValidationExpression="^[^@\s]+@[^@\s]+\.[^@\s]+$" ErrorMessage="Enter a valid email address" Text="Invalid email" CssClass="field-error" Display="Dynamic" />
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= LanguageInput.ClientID %>">Preferred language</label>
    <asp:TextBox ID="LanguageInput" runat="server" CssClass="form-control" MaxLength="40" />
  </div>
</div>

<div class="reg-section-title">Address</div>
<div class="reg-fields" style="grid-template-columns: repeat(2, minmax(170px, 1fr));">
  <div class="form-group">
    <label class="form-label" for="<%= AddrLine1Input.ClientID %>">Line 1</label>
    <asp:TextBox ID="AddrLine1Input" runat="server" CssClass="form-control" MaxLength="150" />
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= AddrLine2Input.ClientID %>">Line 2</label>
    <asp:TextBox ID="AddrLine2Input" runat="server" CssClass="form-control" MaxLength="150" />
  </div>
</div>
<div class="reg-fields">
  <div class="form-group">
    <label class="form-label" for="<%= CityInput.ClientID %>">City / Town</label>
    <asp:TextBox ID="CityInput" runat="server" CssClass="form-control" MaxLength="80" />
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= RegionList.Visible ? RegionList.ClientID : RegionInput.ClientID %>"><%: RegionLabel %></label>
    <asp:DropDownList ID="RegionList" runat="server" CssClass="form-select" />
    <asp:TextBox ID="RegionInput" runat="server" CssClass="form-control" MaxLength="80" />
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= CountryInput.ClientID %>">Country</label>
    <asp:TextBox ID="CountryInput" runat="server" CssClass="form-control" MaxLength="60" />
  </div>
</div>

<div class="reg-section-title">Emergency contact</div>
<div class="reg-fields">
  <div class="form-group"><label class="form-label" for="<%= EmergencyNameInput.ClientID %>">Name</label><asp:TextBox ID="EmergencyNameInput" runat="server" CssClass="form-control" MaxLength="120" /></div>
  <div class="form-group"><label class="form-label" for="<%= EmergencyPhoneInput.ClientID %>">Phone</label><asp:TextBox ID="EmergencyPhoneInput" runat="server" CssClass="form-control" MaxLength="20" /></div>
</div>

<div class="reg-section-title">Other</div>
<div class="reg-fields">
  <div class="form-group">
    <label class="form-label" for="<%= MaritalInput.ClientID %>">Marital status</label>
    <asp:DropDownList ID="MaritalInput" runat="server" CssClass="form-select">
      <asp:ListItem Value="">—</asp:ListItem>
      <asp:ListItem Value="SINGLE">Single</asp:ListItem>
      <asp:ListItem Value="MARRIED">Married</asp:ListItem>
      <asp:ListItem Value="OTHER">Other</asp:ListItem>
    </asp:DropDownList>
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= OccupationInput.ClientID %>">Occupation</label>
    <asp:TextBox ID="OccupationInput" runat="server" CssClass="form-control" MaxLength="80" />
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= ReferralInput.ClientID %>">Referral source</label>
    <asp:TextBox ID="ReferralInput" runat="server" CssClass="form-control" MaxLength="80" />
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= PreferredDoctorInput.ClientID %>">Preferred physician</label>
    <asp:DropDownList ID="PreferredDoctorInput" runat="server" CssClass="form-select" />
  </div>
</div>
<script>
  /* Live age hint under the date of birth, like "→ age 34". */
  (function () {
    var dob = document.getElementById('<%= DobInput.ClientID %>'), hint = document.getElementById('<%= DobInput.ClientID %>_hint');
    if (!dob || !hint) return;
    function refresh() {
      if (!dob.value) { hint.textContent = ''; return; }
      var d = new Date(dob.value + 'T00:00:00'), t = new Date();
      if (isNaN(d)) { hint.textContent = 'unrecognised date'; return; }
      var age = t.getFullYear() - d.getFullYear();
      if (t.getMonth() < d.getMonth() || (t.getMonth() === d.getMonth() && t.getDate() < d.getDate())) age--;
      hint.textContent = age < 0 ? 'date is in the future' : '→ age ' + age;
    }
    dob.addEventListener('input', refresh); dob.addEventListener('change', refresh); refresh();
  })();
</script>
