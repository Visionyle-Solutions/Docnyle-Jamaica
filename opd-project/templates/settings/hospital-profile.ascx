<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="hospital-profile.ascx.cs" Inherits="OpdProject.templates.settings.hospital_profile" %>
<%-- Settings → Hospital & Practice: profile, logo, display formats (date/time/MRN/doctor names), statutory registrations --%>
<h3 style="margin-top:0;">Hospital &amp; Practice</h3>
<asp:Panel ID="ProfilePanel" runat="server" DefaultButton="SaveProfileButton">
  <asp:ValidationSummary runat="server" ValidationGroup="Profile" CssClass="validation-summary" />
  <div class="form-grid">
    <div class="form-group"><label class="form-label" for="<%= HpName.ClientID %>">Hospital name <span class="required">*</span></label><asp:TextBox ID="HpName" runat="server" CssClass="form-control" MaxLength="120" />
      <asp:RequiredFieldValidator runat="server" ControlToValidate="HpName" ValidationGroup="Profile" ErrorMessage="Hospital name is required" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
    <div class="form-group"><label class="form-label" for="<%= HpAddress.ClientID %>">Address</label><asp:TextBox ID="HpAddress" runat="server" CssClass="form-control" MaxLength="250" /></div>
    <div class="form-group"><label class="form-label" for="<%= HpPhone.ClientID %>">Phone</label><asp:TextBox ID="HpPhone" runat="server" CssClass="form-control" MaxLength="30" /></div>
    <div class="form-group"><label class="form-label" for="<%= HpEmail.ClientID %>">Email</label><asp:TextBox ID="HpEmail" runat="server" CssClass="form-control" TextMode="Email" MaxLength="120" />
      <asp:RegularExpressionValidator runat="server" ControlToValidate="HpEmail" ValidationGroup="Profile" ValidationExpression="^[^@\s]+@[^@\s]+\.[^@\s]+$" ErrorMessage="Enter a valid email" Text="Invalid email" CssClass="field-error" Display="Dynamic" /></div>
    <div class="form-group"><label class="form-label" for="<%= HpCountry.ClientID %>">Country</label>
      <asp:DropDownList ID="HpCountry" runat="server" CssClass="form-select"><asp:ListItem Value="JM">Jamaica</asp:ListItem><asp:ListItem Value="IN">India</asp:ListItem><asp:ListItem Value="US">United States</asp:ListItem><asp:ListItem Value="GB">United Kingdom</asp:ListItem><asp:ListItem Value="AE">UAE</asp:ListItem></asp:DropDownList></div>
    <div class="form-group"><label class="form-label" for="<%= HpCurrency.ClientID %>">Currency</label>
      <asp:DropDownList ID="HpCurrency" runat="server" CssClass="form-select"><asp:ListItem Value="JMD">JMD ($)</asp:ListItem><asp:ListItem Value="INR">INR (₹)</asp:ListItem><asp:ListItem Value="USD">USD ($)</asp:ListItem><asp:ListItem Value="GBP">GBP (£)</asp:ListItem><asp:ListItem Value="AED">AED</asp:ListItem></asp:DropDownList>
      <div class="text-xs text-muted mt-1">Set automatically from the country — change it here if you bill in another currency.</div></div>
    <div class="form-group"><label class="form-label" for="<%= HpTimeZone.ClientID %>">Time zone</label>
      <asp:DropDownList ID="HpTimeZone" runat="server" CssClass="form-select" />
      <div class="text-xs text-muted mt-1">Appointments and visits are shown in this zone for every user; "today", expiry and report dates follow it too.</div></div>
    <div class="form-group"><label class="form-label" for="<%= HpLicense.ClientID %>">License / Reg. No.</label><asp:TextBox ID="HpLicense" runat="server" CssClass="form-control" MaxLength="60" /></div>
    <div class="form-group"><label class="form-label" for="<%= HpRxValidity.ClientID %>">Rx validity (days)</label><asp:TextBox ID="HpRxValidity" runat="server" CssClass="form-control" TextMode="Number" />
      <asp:RangeValidator runat="server" ControlToValidate="HpRxValidity" ValidationGroup="Profile" Type="Integer" MinimumValue="1" MaximumValue="365" ErrorMessage="Rx validity must be 1–365 days" Text="1–365" CssClass="field-error" Display="Dynamic" /></div>
    <div class="form-group"><label class="form-label" for="<%= HpTempUnit.ClientID %>">Default temperature unit</label>
      <asp:DropDownList ID="HpTempUnit" runat="server" CssClass="form-select"><asp:ListItem Value="F">°F (Fahrenheit)</asp:ListItem><asp:ListItem Value="C">°C (Celsius)</asp:ListItem></asp:DropDownList></div>
    <div class="form-group"><label class="form-label" for="<%= HpPatientIdLabel.ClientID %>">Patient ID label</label><asp:TextBox ID="HpPatientIdLabel" runat="server" CssClass="form-control" placeholder="e.g. MRN, Hospital No" MaxLength="30" /></div>
    <div class="form-group"><label class="form-label" for="<%= HpHealthIdLabel.ClientID %>">Health ID label</label><asp:TextBox ID="HpHealthIdLabel" runat="server" CssClass="form-control" placeholder="e.g. ABHA ID, TRN" MaxLength="40" /></div>
    <div class="form-group"><label class="form-label" for="<%= HpHealthIdMandatory.ClientID %>">Health ID mandatory?</label>
      <asp:DropDownList ID="HpHealthIdMandatory" runat="server" CssClass="form-select"><asp:ListItem Value="0">Optional</asp:ListItem><asp:ListItem Value="1">Mandatory</asp:ListItem></asp:DropDownList></div>
    <div class="form-group"><label class="form-label" for="<%= HpGrowthChart.ClientID %>">Growth chart standard</label>
      <asp:DropDownList ID="HpGrowthChart" runat="server" CssClass="form-select"><asp:ListItem>WHO</asp:ListItem><asp:ListItem>CDC</asp:ListItem></asp:DropDownList></div>
  </div>
  <div class="text-xs text-muted mt-3 mb-2" style="text-transform:uppercase; font-weight:600;">Hospital Logo</div>
  <div style="display:flex; gap:16px; align-items:center; flex-wrap:wrap;">
    <div style="width:120px; height:60px; border:2px dashed var(--border-default); border-radius:6px; overflow:hidden; display:flex; align-items:center; justify-content:center;">
      <asp:Literal ID="LogoPreview" runat="server" />
    </div>
    <div>
      <asp:FileUpload ID="LogoUpload" runat="server" accept="image/png,image/jpeg" CssClass="form-control" />
      <asp:CheckBox ID="RemoveLogo" runat="server" Text=" Remove current logo" CssClass="text-xs" />
      <div class="text-xs text-muted mt-1">PNG or JPG, up to 200 KB. Shown in the top bar and on printed documents.</div>
    </div>
  </div>
  <asp:LinkButton ID="SaveProfileButton" runat="server" CssClass="btn btn--primary mt-3" ValidationGroup="Profile" OnClick="SaveProfileButton_Click"><i class="ti ti-device-floppy" aria-hidden="true"></i> Save hospital settings</asp:LinkButton>
  <script>
    /* Country → currency: picking a country selects its currency (the user can still override it). */
    (function () {
      var country = document.getElementById('<%= HpCountry.ClientID %>'), currency = document.getElementById('<%= HpCurrency.ClientID %>');
      var map = <%= CountryCurrencyJson %>;
      if (!country || !currency) return;
      country.addEventListener('change', function () { if (map[country.value]) currency.value = map[country.value]; });
    })();
  </script>
</asp:Panel>

<div class="text-xs text-muted mt-4 mb-2" style="text-transform:uppercase; font-weight:600;">Formats</div>
<asp:Panel ID="FormatsPanel" runat="server" CssClass="settings-section" DefaultButton="SaveFormatsButton">
  <div class="form-grid">
    <div class="form-group"><label class="form-label" for="<%= FmtDateFormat.ClientID %>">Date format</label>
      <asp:DropDownList ID="FmtDateFormat" runat="server" CssClass="form-select"><asp:ListItem>DD/MM/YYYY</asp:ListItem><asp:ListItem>MM/DD/YYYY</asp:ListItem><asp:ListItem>YYYY-MM-DD</asp:ListItem></asp:DropDownList></div>
    <div class="form-group"><label class="form-label" for="<%= FmtTimeFormat.ClientID %>">Time format</label>
      <asp:DropDownList ID="FmtTimeFormat" runat="server" CssClass="form-select"><asp:ListItem Value="12h">12-hour (2:30 PM)</asp:ListItem><asp:ListItem Value="24h">24-hour (14:30)</asp:ListItem></asp:DropDownList></div>
    <div class="form-group"><label class="form-label" for="<%= FmtMrnPrefix.ClientID %>">MRN prefix</label><asp:TextBox ID="FmtMrnPrefix" runat="server" CssClass="form-control" MaxLength="10" /></div>
    <div class="form-group"><label class="form-label" for="<%= FmtMrnFormat.ClientID %>">MRN format</label><asp:TextBox ID="FmtMrnFormat" runat="server" CssClass="form-control" MaxLength="40" />
      <asp:RegularExpressionValidator runat="server" ControlToValidate="FmtMrnFormat" ValidationGroup="Formats" ValidationExpression=".*\{seq(:\d+)?\}.*" Text="Must contain {seq} or {seq:N} so every MRN is unique" CssClass="field-error" Display="Dynamic" />
      <asp:RequiredFieldValidator runat="server" ControlToValidate="FmtMrnFormat" ValidationGroup="Formats" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
    <div class="form-group form-group--full"><label class="form-label" for="<%= FmtNameTemplate.ClientID %>">Doctor display-name template</label><asp:TextBox ID="FmtNameTemplate" runat="server" CssClass="form-control" MaxLength="80" />
      <asp:RequiredFieldValidator runat="server" ControlToValidate="FmtNameTemplate" ValidationGroup="Formats" Text="Enter a doctor display-name template" CssClass="field-error" Display="Dynamic" /></div>
  </div>
  <div class="text-xs text-muted">MRN tokens: <code>{prefix}</code> <code>{year}</code> <code>{seq:5}</code> (zero-padded). Name tokens: <code>{prefix} {first} {middle} {last} {finitial} {minitial} {suffix} {suffixComma}</code> — suffix = qualifications. A doctor's "display name override" (Schedule Settings → Doctors) wins over the template.</div>
  <div class="text-sm mt-2"><asp:Literal ID="FormatPreview" runat="server" /></div>
  <div style="display:flex; gap:8px;" class="mt-2">
    <asp:LinkButton ID="PreviewFormatsButton" runat="server" CssClass="btn btn--ghost" ValidationGroup="Formats" OnClick="PreviewFormatsButton_Click"><i class="ti ti-eye" aria-hidden="true"></i> Preview</asp:LinkButton>
    <asp:LinkButton ID="SaveFormatsButton" runat="server" CssClass="btn btn--primary" ValidationGroup="Formats" OnClick="SaveFormatsButton_Click"><i class="ti ti-device-floppy" aria-hidden="true"></i> Save formats</asp:LinkButton>
  </div>
</asp:Panel>

<div class="text-xs text-muted mt-4 mb-2" style="text-transform:uppercase; font-weight:600;">Statutory Registrations</div>
<div class="settings-section">
  <table class="data-table" style="font-size:12px;">
    <thead><tr><th>Registration</th><th>Number</th><th>Issued</th><th>Expiry</th><th>Status</th></tr></thead>
    <tbody>
      <asp:Repeater ID="ComplianceRepeater" runat="server" ItemType="OpdProject.Models.Compliance" OnItemDataBound="ComplianceRepeater_ItemDataBound">
        <ItemTemplate>
          <tr>
            <td style="font-weight:600;"><%#: Item.DisplayLabel %><asp:HiddenField ID="ComplianceId" runat="server" Value="<%# Item.ComplianceId %>" /></td>
            <td><asp:TextBox ID="RegNumber" runat="server" CssClass="form-control" style="font-size:11px;" placeholder="<%# Item.PrintLabel %>" /></td>
            <td><asp:TextBox ID="Issued" runat="server" CssClass="form-control" TextMode="Date" style="font-size:11px; width:140px;" /></td>
            <td><asp:TextBox ID="Expiry" runat="server" CssClass="form-control" TextMode="Date" style="font-size:11px; width:140px;" /></td>
            <td><%# Item.IsMandatory ? "<span class=\"badge badge--danger\" style=\"font-size:9px;\">Required</span>" : "<span class=\"text-xs text-muted\">Optional</span>" %></td>
          </tr>
        </ItemTemplate>
      </asp:Repeater>
    </tbody>
  </table>
  <asp:LinkButton ID="SaveComplianceButton" runat="server" CssClass="btn btn--sm btn--primary mt-2" OnClick="SaveComplianceButton_Click" CausesValidation="false"><i class="ti ti-device-floppy" aria-hidden="true"></i> Save registrations</asp:LinkButton>
</div>
