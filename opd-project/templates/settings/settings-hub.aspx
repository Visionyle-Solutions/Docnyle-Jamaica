<%@ Page Title="Settings" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="settings-hub.aspx.cs" Inherits="OpdProject.templates.settings.settings_hub" MaintainScrollPositionOnPostback="true" %>
<%@ Register TagPrefix="uc" TagName="HospitalProfile" Src="~/templates/settings/hospital-profile.ascx" %>
<%@ Register TagPrefix="uc" TagName="VitalsConfig" Src="~/templates/settings/vitals-config.ascx" %>
<%@ Register TagPrefix="uc" TagName="SpecialtyConfig" Src="~/templates/settings/specialty-config.ascx" %>
<%-- Unified settings hub (replaces the standalone hospital / vitals / specialty screens).
     Routes: /settings, /settings/hospital, /settings/vitals, /settings/specialties (?tab=…) --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="card" style="padding:0; overflow:hidden;">
  <div class="settings-hub">
    <div class="settings-sidebar">
      <asp:Repeater ID="TabsRepeater" runat="server" ItemType="OpdProject.Web.NavItem">
        <ItemTemplate>
          <a class="settings-tab<%# Item.Path == ActiveTab ? " settings-tab--active" : "" %>" href="<%#: Request.Url.AbsolutePath %>?tab=<%#: Item.Path %>"><i class="ti <%#: Item.Icon %>" aria-hidden="true"></i> <span><%#: Item.Label %></span></a>
        </ItemTemplate>
      </asp:Repeater>
    </div>
    <div class="settings-content">
      <%-- Hospital & Practice --%>
      <asp:PlaceHolder ID="HospitalTab" runat="server" Visible="false"><uc:HospitalProfile ID="HospitalProfile" runat="server" /></asp:PlaceHolder>

      <%-- Clinical Config --%>
      <asp:PlaceHolder ID="ClinicalTab" runat="server" Visible="false">
        <h3 style="margin-top:0;">Clinical Configuration</h3>
        <uc:VitalsConfig ID="VitalsConfig" runat="server" />
        <div class="settings-section mt-3">
          <div class="settings-section-header">Medical History — checklist items for patient intake</div>
          <table class="data-table" style="font-size:12px;"><thead><tr><th>#</th><th>Condition</th><th>Enabled</th></tr></thead><tbody>
            <asp:Repeater ID="HistoryItemsRepeater" runat="server" ItemType="OpdProject.Models.MedicalHistoryItem">
              <ItemTemplate><tr><td><%# Container.ItemIndex + 1 %></td><td><%#: Item.ItemName %><asp:HiddenField ID="ItemId" runat="server" Value="<%# Item.ItemId %>" /></td><td><asp:CheckBox ID="Active" runat="server" Checked="<%# Item.IsActive %>" /></td></tr></ItemTemplate>
            </asp:Repeater>
          </tbody></table>
          <asp:Panel ID="AddHistoryPanel" runat="server" DefaultButton="AddHistoryItemButton" style="display:flex; gap:8px; margin-top:8px; flex-wrap:wrap;">
            <asp:LinkButton ID="SaveHistoryItemsButton" runat="server" CssClass="btn btn--sm btn--primary" OnClick="SaveHistoryItemsButton_Click" CausesValidation="false"><i class="ti ti-device-floppy"></i> Save checklist</asp:LinkButton>
            <asp:TextBox ID="NewHistoryItem" runat="server" CssClass="form-control" style="width:250px;" placeholder="Add custom condition..." MaxLength="60" />
            <asp:LinkButton ID="AddHistoryItemButton" runat="server" CssClass="btn btn--sm btn--success" ValidationGroup="History" OnClick="AddHistoryItemButton_Click"><i class="ti ti-plus"></i> Add</asp:LinkButton>
            <asp:RequiredFieldValidator runat="server" ControlToValidate="NewHistoryItem" ValidationGroup="History" Text="Enter a condition" CssClass="field-error" Display="Dynamic" />
          </asp:Panel>
        </div>
        <uc:SpecialtyConfig ID="SpecialtyConfig" runat="server" />
      </asp:PlaceHolder>

      <%-- Billing & Numbers --%>
      <asp:PlaceHolder ID="BillingTab" runat="server" Visible="false">
        <h3 style="margin-top:0;">Billing &amp; Numbering</h3>
        <asp:Panel ID="PrefixPanel" runat="server" CssClass="settings-section" DefaultButton="SaveBillingButton">
          <div class="settings-section-header">Document Number Formats</div>
          <div class="text-xs text-muted mb-2">Prefix for new invoice and receipt numbers (e.g. INV-001001).</div>
          <div class="form-grid">
            <div class="form-group"><label class="form-label" for="<%= PrefixRx.ClientID %>">Prescription prefix</label><asp:TextBox ID="PrefixRx" runat="server" CssClass="form-control" MaxLength="8" /></div>
            <div class="form-group"><label class="form-label" for="<%= PrefixReceipt.ClientID %>">Receipt prefix</label><asp:TextBox ID="PrefixReceipt" runat="server" CssClass="form-control" MaxLength="8" /></div>
            <div class="form-group"><label class="form-label" for="<%= PrefixInvoice.ClientID %>">Invoice prefix</label><asp:TextBox ID="PrefixInvoice" runat="server" CssClass="form-control" MaxLength="8" /></div>
          </div>
          <asp:LinkButton ID="SaveBillingButton" runat="server" CssClass="btn btn--primary mt-2" ValidationGroup="Billing" OnClick="SaveBillingButton_Click"><i class="ti ti-device-floppy"></i> Save</asp:LinkButton>
        </asp:Panel>
        <div class="settings-section mt-3">
          <div class="settings-section-header">Service Charge Master</div>
          <table class="data-table" style="font-size:12px;"><thead><tr><th>Code</th><th>Service</th><th>Category</th><th style="text-align:right;">Amount</th><th style="text-align:right;">Tax%</th><th>Status</th><th></th></tr></thead><tbody>
            <asp:Repeater ID="ServicesRepeater" runat="server" ItemType="OpdProject.Models.ServiceCharge" OnItemCommand="ServicesRepeater_ItemCommand">
              <ItemTemplate><tr><td class="text-xs"><%#: Item.ServiceCode %></td><td><%#: Item.ServiceName %></td><td class="text-xs"><%#: Item.Category %></td><td style="text-align:right;"><%#: Money(Item.DefaultAmount) %></td><td style="text-align:right;"><%#: Item.TaxPercent %></td>
                <td><span class="badge <%# Item.IsActive ? "badge--success" : "badge--danger" %>"><%# Item.IsActive ? "Active" : "Inactive" %></span></td>
                <td><asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Toggle" CommandArgument="<%# Item.ServiceChargeId %>" CausesValidation="false"><%# Item.IsActive ? "Deactivate" : "Activate" %></asp:LinkButton></td></tr></ItemTemplate>
            </asp:Repeater>
          </tbody></table>
          <asp:Panel ID="AddServicePanel" runat="server" DefaultButton="AddServiceButton" style="display:flex; gap:8px; margin-top:8px; flex-wrap:wrap; align-items:flex-start;">
            <asp:TextBox ID="SvcCode" runat="server" CssClass="form-control" style="width:110px;" placeholder="Code" MaxLength="12" />
            <asp:TextBox ID="SvcName" runat="server" CssClass="form-control" style="width:220px;" placeholder="Service name" MaxLength="80" />
            <asp:DropDownList ID="SvcCategory" runat="server" CssClass="form-select" style="width:140px;"><asp:ListItem>CONSULTATION</asp:ListItem><asp:ListItem>LAB</asp:ListItem><asp:ListItem>IMAGING</asp:ListItem><asp:ListItem>PROCEDURE</asp:ListItem><asp:ListItem>INJECTION</asp:ListItem><asp:ListItem>SUPPLY</asp:ListItem><asp:ListItem>OTHER</asp:ListItem></asp:DropDownList>
            <asp:TextBox ID="SvcAmount" runat="server" CssClass="form-control" style="width:110px;" TextMode="Number" placeholder="Amount" />
            <asp:TextBox ID="SvcTax" runat="server" CssClass="form-control" style="width:80px;" TextMode="Number" placeholder="Tax %" Text="0" />
            <asp:LinkButton ID="AddServiceButton" runat="server" CssClass="btn btn--sm btn--success" ValidationGroup="Service" OnClick="AddServiceButton_Click"><i class="ti ti-plus"></i> Add service</asp:LinkButton>
            <asp:RequiredFieldValidator runat="server" ControlToValidate="SvcName" ValidationGroup="Service" Text="Name required" CssClass="field-error" Display="Dynamic" />
            <asp:RangeValidator runat="server" ControlToValidate="SvcAmount" ValidationGroup="Service" Type="Double" MinimumValue="0.01" MaximumValue="10000000" Text="Amount must be greater than 0" CssClass="field-error" Display="Dynamic" />
            <asp:RequiredFieldValidator runat="server" ControlToValidate="SvcAmount" ValidationGroup="Service" Text="Amount required" CssClass="field-error" Display="Dynamic" />
            <asp:RangeValidator runat="server" ControlToValidate="SvcTax" ValidationGroup="Service" Type="Double" MinimumValue="0" MaximumValue="100" Text="Tax 0–100" CssClass="field-error" Display="Dynamic" />
          </asp:Panel>
        </div>
        <div class="settings-section mt-3" id="insurers">
          <div class="settings-section-header">Insurance Companies <span class="text-xs text-muted" style="font-weight:400;">— offered under Private Insurance on the patient Coverage page and at billing (<%: Hospital.Country %>)</span></div>
          <table class="data-table" style="font-size:12px;"><thead><tr><th>Insurance company</th><th>Status</th><th></th></tr></thead><tbody>
            <asp:Repeater ID="PayersRepeater" runat="server" ItemType="OpdProject.Models.Payer" OnItemCommand="PayersRepeater_ItemCommand">
              <ItemTemplate><tr><td><%#: Item.Name %></td>
                <td><span class="badge <%# Item.IsActive ? "badge--success" : "badge--danger" %>"><%# Item.IsActive ? "Active" : "Inactive" %></span></td>
                <td><asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Toggle" CommandArgument="<%# Item.PayerId %>" CausesValidation="false"
                  OnClientClick='<%# Item.IsActive ? "return confirm(\"Stop offering this insurance company for new coverage? Coverage already on file is kept.\");" : "" %>'><%# Item.IsActive ? "Deactivate" : "Activate" %></asp:LinkButton></td></tr></ItemTemplate>
            </asp:Repeater>
            <asp:PlaceHolder ID="NoPayers" runat="server" Visible="false"><tr class="empty-row"><td colspan="3">No insurance companies yet.</td></tr></asp:PlaceHolder>
          </tbody></table>
          <asp:Panel ID="AddPayerPanel" runat="server" DefaultButton="AddPayerButton" style="display:flex; gap:8px; margin-top:8px; flex-wrap:wrap; align-items:flex-start;">
            <asp:TextBox ID="NewPayerName" runat="server" CssClass="form-control" style="width:280px;" placeholder="New insurance company name" MaxLength="100" />
            <asp:LinkButton ID="AddPayerButton" runat="server" CssClass="btn btn--sm btn--success" ValidationGroup="Payer" OnClick="AddPayerButton_Click"><i class="ti ti-plus"></i> Add insurance company</asp:LinkButton>
            <asp:RequiredFieldValidator runat="server" ControlToValidate="NewPayerName" ValidationGroup="Payer" Text="Enter the company name" CssClass="field-error" Display="Dynamic" />
            <asp:PlaceHolder ID="PayerErrorPanel" runat="server" Visible="false"><span class="field-error"><asp:Literal ID="PayerError" runat="server" Mode="Encode" /></span></asp:PlaceHolder>
          </asp:Panel>
        </div>
        <div class="settings-section mt-3">
          <div class="settings-section-header">Doctor Fee Schedule <span class="text-xs text-muted" style="font-weight:400;">— edit fees in <a href="<%: Urls.To("settings/scheduling") %>?tab=doctors">Schedule Settings → Doctors</a></span></div>
          <table class="data-table" style="font-size:12px;"><thead><tr><th>Doctor</th><th>Type</th><th style="text-align:right;">Amount</th><th>Effective</th></tr></thead><tbody>
            <asp:Repeater ID="FeesRepeater" runat="server" ItemType="OpdProject.Models.DoctorFee">
              <ItemTemplate><tr><td><%#: OpdProject.Services.UserService.UserName(Item.DoctorId) %></td><td><%#: Item.ConsultationType %></td><td style="text-align:right;"><%#: Money(Item.FeeAmount) %></td>
                <td class="text-xs"><%#: FmtDate(Item.EffectiveFrom) %> → <%#: Item.EffectiveTo.HasValue ? FmtDate(Item.EffectiveTo.Value) : "current" %></td></tr></ItemTemplate>
            </asp:Repeater>
          </tbody></table>
        </div>
      </asp:PlaceHolder>

      <%-- Notifications --%>
      <asp:PlaceHolder ID="NotificationsTab" runat="server" Visible="false">
        <h3 style="margin-top:0;">Notification Channels</h3>
        <div class="hint-banner mb-3"><i class="ti ti-info-circle"></i> No messaging provider is connected in this build: enabling a channel marks messages as SENT, otherwise they are logged as SIMULATED.</div>
        <asp:Panel ID="WhatsAppPanel" runat="server" CssClass="settings-section" DefaultButton="SaveWhatsAppButton">
          <div class="settings-section-header"><i class="ti ti-brand-whatsapp" style="color:#25D366;"></i> WhatsApp API</div>
          <div class="form-grid">
            <div class="form-group"><label class="form-label" for="<%= WaProvider.ClientID %>">Provider</label><asp:DropDownList ID="WaProvider" runat="server" CssClass="form-select"><asp:ListItem Value="META">Meta Cloud API</asp:ListItem><asp:ListItem Value="TWILIO">Twilio</asp:ListItem><asp:ListItem Value="GUPSHUP">Gupshup</asp:ListItem></asp:DropDownList></div>
            <div class="form-group"><label class="form-label" for="<%= WaApiKey.ClientID %>">API Key / Token</label><asp:TextBox ID="WaApiKey" runat="server" CssClass="form-control" TextMode="Password" /></div>
            <div class="form-group"><label class="form-label" for="<%= WaPhoneId.ClientID %>">Phone Number ID</label><asp:TextBox ID="WaPhoneId" runat="server" CssClass="form-control" /></div>
            <div class="form-group"><label class="form-label" for="<%= WaBusinessId.ClientID %>">Business Account ID</label><asp:TextBox ID="WaBusinessId" runat="server" CssClass="form-control" /></div>
            <div class="form-group"><asp:CheckBox ID="WaEnabled" runat="server" Text=" Active" /></div>
          </div>
          <asp:LinkButton ID="SaveWhatsAppButton" runat="server" CssClass="btn btn--sm btn--primary mt-2" OnClick="SaveWhatsAppButton_Click" CausesValidation="false"><i class="ti ti-device-floppy"></i> Save WhatsApp</asp:LinkButton>
        </asp:Panel>
        <asp:Panel ID="EmailPanel" runat="server" CssClass="settings-section mt-3" DefaultButton="SaveEmailButton">
          <div class="settings-section-header"><i class="ti ti-mail" style="color:var(--color-primary);"></i> Email (SMTP)</div>
          <div class="form-grid">
            <div class="form-group"><label class="form-label" for="<%= EmProvider.ClientID %>">Provider</label><asp:DropDownList ID="EmProvider" runat="server" CssClass="form-select"><asp:ListItem Value="SENDGRID">SendGrid</asp:ListItem><asp:ListItem Value="SMTP">Custom SMTP</asp:ListItem><asp:ListItem Value="SES">AWS SES</asp:ListItem></asp:DropDownList></div>
            <div class="form-group"><label class="form-label" for="<%= EmHost.ClientID %>">SMTP Host</label><asp:TextBox ID="EmHost" runat="server" CssClass="form-control" placeholder="smtp.sendgrid.net" /></div>
            <div class="form-group"><label class="form-label" for="<%= EmPort.ClientID %>">SMTP Port</label><asp:TextBox ID="EmPort" runat="server" CssClass="form-control" TextMode="Number" />
              <asp:RangeValidator runat="server" ControlToValidate="EmPort" ValidationGroup="Email" Type="Integer" MinimumValue="1" MaximumValue="65535" Text="1–65535" CssClass="field-error" Display="Dynamic" /></div>
            <div class="form-group"><label class="form-label" for="<%= EmApiKey.ClientID %>">API Key / Password</label><asp:TextBox ID="EmApiKey" runat="server" CssClass="form-control" TextMode="Password" /></div>
            <div class="form-group"><label class="form-label" for="<%= EmFromEmail.ClientID %>">From Email</label><asp:TextBox ID="EmFromEmail" runat="server" CssClass="form-control" placeholder="clinic@example.com" />
              <asp:RegularExpressionValidator runat="server" ControlToValidate="EmFromEmail" ValidationGroup="Email" ValidationExpression="^[^@\s]+@[^@\s]+\.[^@\s]+$" Text="Invalid email" CssClass="field-error" Display="Dynamic" /></div>
            <div class="form-group"><label class="form-label" for="<%= EmFromName.ClientID %>">From Name</label><asp:TextBox ID="EmFromName" runat="server" CssClass="form-control" /></div>
            <div class="form-group"><asp:CheckBox ID="EmEnabled" runat="server" Text=" Active" /></div>
          </div>
          <asp:LinkButton ID="SaveEmailButton" runat="server" CssClass="btn btn--sm btn--primary mt-2" ValidationGroup="Email" OnClick="SaveEmailButton_Click"><i class="ti ti-device-floppy"></i> Save Email</asp:LinkButton>
        </asp:Panel>
        <div class="settings-section mt-3">
          <div class="settings-section-header">Message Templates</div>
          <div class="text-xs text-muted mb-2">Placeholders: {PatientName}, {DoctorName}, {HospitalName}, {Time}, {Date}, {Phone}, {TestName}.</div>
          <table class="data-table" style="font-size:11px;"><thead><tr><th>Template</th><th>Trigger</th><th>Channel</th><th>Enabled</th><th>Edit</th></tr></thead><tbody>
            <asp:Repeater ID="TemplatesRepeater" runat="server" ItemType="OpdProject.Models.MessageTemplate" OnItemCommand="TemplatesRepeater_ItemCommand">
              <ItemTemplate><tr><td style="font-weight:600;"><%#: Item.Name %></td><td class="text-xs"><%#: Item.Trigger %></td><td><span class="badge badge--info" style="font-size:9px;"><%#: Item.Channel %></span></td>
                <td><asp:LinkButton runat="server" CssClass='<%# "btn btn--xs " + (Item.IsEnabled ? "btn--success" : "btn--ghost") %>' CommandName="Toggle" CommandArgument="<%# Item.TemplateId %>" CausesValidation="false"><%# Item.IsEnabled ? "On" : "Off" %></asp:LinkButton></td>
                <td><asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Edit" CommandArgument="<%# Item.TemplateId %>" CausesValidation="false"><i class="ti ti-edit"></i></asp:LinkButton></td></tr></ItemTemplate>
            </asp:Repeater>
          </tbody></table>
        </div>
      </asp:PlaceHolder>

      <%-- Users & Access --%>
      <asp:PlaceHolder ID="UsersTab" runat="server" Visible="false">
        <h3 style="margin-top:0;">Users &amp; Access</h3>
        <div class="settings-section">
          <div class="settings-section-header" style="display:flex; justify-content:space-between;"><span>Users</span>
            <span><a class="btn btn--sm btn--primary" href="<%: Urls.To("settings/users") %>"><i class="ti ti-users"></i> Manage users</a> <a class="btn btn--sm btn--ghost" href="<%: Urls.To("settings/permissions") %>"><i class="ti ti-shield-lock"></i> Permission matrix</a></span></div>
          <table class="data-table" style="font-size:11px;"><thead><tr><th>Name</th><th>Email</th><th>Role</th><th>Active</th><th></th></tr></thead><tbody>
            <asp:Repeater ID="UsersRepeater" runat="server" ItemType="OpdProject.Models.AppUser" OnItemCommand="UsersRepeater_ItemCommand">
              <ItemTemplate><tr><td><%#: Item.FullName %></td><td><%#: Item.Email %></td><td><%# RoleBadges(Item, "9px") %></td>
                <td><%# Item.IsActive ? "✓" : "✗" %></td>
                <td><asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Toggle" CommandArgument="<%# Item.UserId %>" CausesValidation="false" Visible="<%# Item.UserId != CurrentUserId %>"><%# Item.IsActive ? "Disable" : "Enable" %></asp:LinkButton></td></tr></ItemTemplate>
            </asp:Repeater>
          </tbody></table>
        </div>
      </asp:PlaceHolder>

      <%-- System & Data --%>
      <asp:PlaceHolder ID="SystemTab" runat="server" Visible="false">
        <h3 style="margin-top:0;">System &amp; Data</h3>
        <div class="settings-section">
          <div class="settings-section-header">Data Export (CSV)</div>
          <div class="text-xs text-muted mb-2">Download data tables as CSV files for backup or analysis.</div>
          <a class="btn btn--sm btn--primary" href="<%: Urls.To("data-export") %>"><i class="ti ti-download"></i> Open Data Export</a>
        </div>
        <div class="settings-section mt-3">
          <div class="settings-section-header">Data Import (CSV)</div>
          <div class="text-xs text-muted mb-2">Bulk-register patients from a CSV file (validated row by row before import).</div>
          <a class="btn btn--sm btn--primary" href="<%: Urls.To("data-import") %>"><i class="ti ti-upload"></i> Open Data Import</a>
        </div>
        <div class="settings-section mt-3">
          <div class="settings-section-header">Audit Log</div>
          <div class="text-xs text-muted">View the full audit trail on the <a href="<%: Urls.To("audit-log") %>" style="color:var(--color-primary);">Audit Log page</a>.</div>
        </div>
        <div class="settings-section mt-3">
          <div class="settings-section-header">Data storage</div>
          <div class="text-xs text-muted">This build keeps all records in server memory (no database). Restarting the application restores the demo data.</div>
        </div>
      </asp:PlaceHolder>
    </div>
  </div>
</div>

<asp:Panel ID="TemplateModal" runat="server" CssClass="modal-overlay" Visible="false">
  <div class="modal-frame modal-frame--wide" role="dialog" aria-modal="true">
    <div class="modal-header"><strong>Edit message template</strong><asp:LinkButton runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseTemplate_Click" CausesValidation="false"><i class="ti ti-x"></i></asp:LinkButton></div>
    <div class="modal-body">
      <asp:HiddenField ID="TemplateId" runat="server" />
      <div class="form-group"><label class="form-label" for="<%= TplName.ClientID %>">Template name</label><asp:TextBox ID="TplName" runat="server" CssClass="form-control" />
        <asp:RequiredFieldValidator runat="server" ControlToValidate="TplName" ValidationGroup="Template" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
      <div class="form-group"><label class="form-label" for="<%= TplChannel.ClientID %>">Channel</label><asp:DropDownList ID="TplChannel" runat="server" CssClass="form-select"><asp:ListItem Value="WHATSAPP">WhatsApp</asp:ListItem><asp:ListItem Value="EMAIL">Email</asp:ListItem><asp:ListItem Value="BOTH">Both</asp:ListItem></asp:DropDownList></div>
      <div class="form-group"><label class="form-label" for="<%= TplText.ClientID %>">Message text</label><asp:TextBox ID="TplText" runat="server" CssClass="form-control" TextMode="MultiLine" style="min-height:90px;" />
        <asp:RequiredFieldValidator runat="server" ControlToValidate="TplText" ValidationGroup="Template" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
    </div>
    <div class="modal-footer">
      <asp:LinkButton runat="server" CssClass="btn btn--ghost" OnClick="CloseTemplate_Click" CausesValidation="false">Cancel</asp:LinkButton>
      <asp:LinkButton ID="SaveTemplateButton" runat="server" CssClass="btn btn--primary" ValidationGroup="Template" OnClick="SaveTemplateButton_Click">Save</asp:LinkButton>
    </div>
  </div>
</asp:Panel>
</asp:Content>
