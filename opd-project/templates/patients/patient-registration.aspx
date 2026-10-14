<%@ Page Title="Patient Registration" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="patient-registration.aspx.cs" Inherits="OpdProject.templates.patients.patient_registration" %>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <div class="card">
    <div style="font-weight:700; margin-bottom:12px;"><asp:Literal ID="HeadingText" runat="server" /> <asp:Literal ID="MrnText" runat="server" /></div>
    <asp:ValidationSummary runat="server" CssClass="validation-summary" HeaderText="Please fix the following:" DisplayMode="BulletList" />
    <div class="form-grid">
      <div class="form-group">
        <label class="form-label" for="<%= FullNameInput.ClientID %>">Full name <span class="required">*</span></label>
        <asp:TextBox ID="FullNameInput" runat="server" CssClass="form-control" placeholder="Patient full name" MaxLength="120" />
        <asp:RequiredFieldValidator runat="server" ControlToValidate="FullNameInput" ErrorMessage="Full name is required" Text="Required" CssClass="field-error" Display="Dynamic" />
      </div>
      <div class="form-group">
        <label class="form-label" for="<%= GenderInput.ClientID %>">Gender <span class="required">*</span></label>
        <asp:DropDownList ID="GenderInput" runat="server" CssClass="form-select">
          <asp:ListItem Value="">Select</asp:ListItem>
          <asp:ListItem Value="MALE">Male</asp:ListItem>
          <asp:ListItem Value="FEMALE">Female</asp:ListItem>
          <asp:ListItem Value="OTHER">Other</asp:ListItem>
        </asp:DropDownList>
        <asp:RequiredFieldValidator runat="server" ControlToValidate="GenderInput" ErrorMessage="Gender is required" Text="Required" CssClass="field-error" Display="Dynamic" />
      </div>
      <div class="form-group">
        <label class="form-label" for="<%= DobInput.ClientID %>">Date of birth <span class="required">*</span></label>
        <asp:TextBox ID="DobInput" runat="server" CssClass="form-control" TextMode="Date" />
        <asp:RequiredFieldValidator runat="server" ControlToValidate="DobInput" ErrorMessage="Date of birth is required" Text="Required" CssClass="field-error" Display="Dynamic" />
        <asp:CustomValidator ID="DobValidator" runat="server" ControlToValidate="DobInput" OnServerValidate="DobValidator_ServerValidate" ErrorMessage="Date of birth cannot be in the future or more than 130 years ago" Text="Invalid date" CssClass="field-error" Display="Dynamic" />
      </div>
      <div class="form-group">
        <label class="form-label" for="<%= PhoneInput.ClientID %>">Phone <span class="required">*</span></label>
        <asp:TextBox ID="PhoneInput" runat="server" CssClass="form-control" placeholder="+91-9XXXXXXXXX" MaxLength="20" />
        <asp:RequiredFieldValidator runat="server" ControlToValidate="PhoneInput" ErrorMessage="Phone number is required" Text="Required" CssClass="field-error" Display="Dynamic" />
        <asp:RegularExpressionValidator runat="server" ControlToValidate="PhoneInput" ValidationExpression="^\+?[0-9][0-9\- ]{6,18}$" ErrorMessage="Phone number may contain only digits, spaces, '-' and a leading '+'" Text="Invalid phone" CssClass="field-error" Display="Dynamic" />
      </div>
      <div class="form-group">
        <label class="form-label" for="<%= EmailInput.ClientID %>">Email</label>
        <asp:TextBox ID="EmailInput" runat="server" CssClass="form-control" TextMode="Email" placeholder="patient@email.com" MaxLength="120" />
        <asp:RegularExpressionValidator runat="server" ControlToValidate="EmailInput" ValidationExpression="^[^@\s]+@[^@\s]+\.[^@\s]+$" ErrorMessage="Enter a valid email address" Text="Invalid email" CssClass="field-error" Display="Dynamic" />
      </div>
      <div class="form-group">
        <label class="form-label" for="<%= HealthIdInput.ClientID %>"><%: HealthIdLabel %> <asp:PlaceHolder ID="HealthIdRequiredMark" runat="server"><span class="required">*</span></asp:PlaceHolder></label>
        <asp:TextBox ID="HealthIdInput" runat="server" CssClass="form-control" MaxLength="40" />
        <asp:RequiredFieldValidator ID="HealthIdRequired" runat="server" ControlToValidate="HealthIdInput" Text="Required" CssClass="field-error" Display="Dynamic" />
      </div>
      <div class="form-group form-group--full">
        <label class="form-label" for="<%= AddressInput.ClientID %>">Address</label>
        <asp:TextBox ID="AddressInput" runat="server" CssClass="form-control" placeholder="Full address" MaxLength="250" />
      </div>
      <div class="form-group">
        <label class="form-label" for="<%= SmokingInput.ClientID %>">Smoking status</label>
        <asp:DropDownList ID="SmokingInput" runat="server" CssClass="form-select">
          <asp:ListItem Value="">—</asp:ListItem>
          <asp:ListItem Value="NEVER">Never</asp:ListItem>
          <asp:ListItem Value="FORMER">Former</asp:ListItem>
          <asp:ListItem Value="CURRENT">Current</asp:ListItem>
        </asp:DropDownList>
      </div>
      <div class="form-group">
        <label class="form-label" for="<%= AlcoholInput.ClientID %>">Alcohol use</label>
        <asp:DropDownList ID="AlcoholInput" runat="server" CssClass="form-select">
          <asp:ListItem Value="">—</asp:ListItem>
          <asp:ListItem Value="NONE">None</asp:ListItem>
          <asp:ListItem Value="OCCASIONAL">Occasional</asp:ListItem>
          <asp:ListItem Value="REGULAR">Regular</asp:ListItem>
        </asp:DropDownList>
      </div>
      <div class="form-group">
        <label class="form-label" for="<%= OccupationInput.ClientID %>">Occupation</label>
        <asp:TextBox ID="OccupationInput" runat="server" CssClass="form-control" placeholder="Occupation" MaxLength="80" />
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
    <div class="text-xs text-muted mt-3 mb-2" style="text-transform:uppercase; font-weight:600;">Emergency Contact &amp; Insurance</div>
    <div class="form-grid">
      <div class="form-group"><label class="form-label" for="<%= EmergencyNameInput.ClientID %>">Emergency contact name</label><asp:TextBox ID="EmergencyNameInput" runat="server" CssClass="form-control" placeholder="Contact name" /></div>
      <div class="form-group"><label class="form-label" for="<%= EmergencyPhoneInput.ClientID %>">Emergency contact phone</label><asp:TextBox ID="EmergencyPhoneInput" runat="server" CssClass="form-control" placeholder="Contact phone" /></div>
      <div class="form-group"><label class="form-label" for="<%= InsuranceProviderInput.ClientID %>">Insurance provider</label><asp:TextBox ID="InsuranceProviderInput" runat="server" CssClass="form-control" placeholder="e.g. Star Health, NHIS" /></div>
      <div class="form-group"><label class="form-label" for="<%= InsurancePolicyInput.ClientID %>">Policy number</label><asp:TextBox ID="InsurancePolicyInput" runat="server" CssClass="form-control" placeholder="Policy / Member ID" /></div>
    </div>
    <div style="display:flex; gap:8px; margin-top:12px;">
      <asp:LinkButton ID="SaveButton" runat="server" CssClass="btn btn--primary" OnClick="SaveButton_Click">
        <i class="ti ti-device-floppy" aria-hidden="true"></i> <asp:Literal ID="SaveLabel" runat="server" />
      </asp:LinkButton>
      <a class="btn btn--ghost" href="<%: Urls.PatientSearch %>">Cancel</a>
    </div>
  </div>
</div>
</asp:Content>
