<%@ Page Title="Patient Registration" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="self-register.aspx.cs" Inherits="OpdProject.templates.portal.self_register" %>

<%-- Public patient self-registration (no sign-in). Route: /portal/register (?h=HOSP-002 for another hospital) --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
    <div class="page-container--narrow" style="padding: 24px 16px;">
        <div class="card mb-3" style="text-align: center;">
            <asp:Literal ID="LogoHtml" runat="server" />
            <h3 style="margin: 4px 0;"><%: PortalHospital.Name %></h3>
            <div class="text-xs text-muted"><%: PortalHospital.LegalAddress %> · <%: PortalHospital.ContactNumber %></div>
            <div class="text-sm text-secondary mt-2">New patient registration</div>
        </div>
        <asp:Panel ID="FormPanel" runat="server" CssClass="card" DefaultButton="SubmitButton">
            <asp:ValidationSummary runat="server" CssClass="validation-summary" HeaderText="Please fix the following:" />
            <div class="form-grid">
                <div class="form-group">
                    <label class="form-label" for="<%= FullName.ClientID %>">Full Name *</label><asp:TextBox ID="FullName" runat="server" CssClass="form-control" placeholder="First and last name" MaxLength="120" />
                    <asp:RequiredFieldValidator runat="server" ControlToValidate="FullName" ErrorMessage="Full name is required" Text="Required" CssClass="field-error" Display="Dynamic" />
                </div>
                <div class="form-group">
                    <label class="form-label" for="<%= Phone.ClientID %>">Phone *</label><asp:TextBox ID="Phone" runat="server" CssClass="form-control" placeholder="+91-9000000000" MaxLength="20" />
                    <asp:RequiredFieldValidator runat="server" ControlToValidate="Phone" ErrorMessage="Phone is required" Text="Required" CssClass="field-error" Display="Dynamic" />
                    <asp:RegularExpressionValidator runat="server" ControlToValidate="Phone" ValidationExpression="^\+?[0-9][0-9\- ]{6,18}$" ErrorMessage="Enter a valid phone number" Text="Invalid phone" CssClass="field-error" Display="Dynamic" />
                </div>
                <div class="form-group">
                    <label class="form-label" for="<%= Gender.ClientID %>">Gender *</label>
                    <asp:DropDownList ID="Gender" runat="server" CssClass="form-select">
                        <asp:ListItem Value="">Select</asp:ListItem>
                        <asp:ListItem Value="MALE">Male</asp:ListItem>
                        <asp:ListItem Value="FEMALE">Female</asp:ListItem>
                        <asp:ListItem Value="OTHER">Other</asp:ListItem>
                    </asp:DropDownList>
                    <asp:RequiredFieldValidator runat="server" ControlToValidate="Gender" ErrorMessage="Gender is required" Text="Required" CssClass="field-error" Display="Dynamic" />
                </div>
                <div class="form-group">
                    <label class="form-label" for="<%= Dob.ClientID %>">Date of Birth *</label><asp:TextBox ID="Dob" runat="server" CssClass="form-control" TextMode="Date" />
                    <asp:RequiredFieldValidator runat="server" ControlToValidate="Dob" ErrorMessage="Date of birth is required" Text="Required" CssClass="field-error" Display="Dynamic" />
                    <asp:CustomValidator runat="server" ControlToValidate="Dob" OnServerValidate="Dob_ServerValidate" ErrorMessage="Date of birth cannot be in the future" Text="Invalid date" CssClass="field-error" Display="Dynamic" />
                </div>
                <div class="form-group">
                    <label class="form-label" for="<%= Email.ClientID %>">Email</label><asp:TextBox ID="Email" runat="server" CssClass="form-control" TextMode="Email" placeholder="email@example.com" MaxLength="120" />
                    <asp:RegularExpressionValidator runat="server" ControlToValidate="Email" ValidationExpression="^[^@\s]+@[^@\s]+\.[^@\s]+$" ErrorMessage="Enter a valid email" Text="Invalid email" CssClass="field-error" Display="Dynamic" />
                </div>
                <div class="form-group">
                    <label class="form-label" for="<%= Address.ClientID %>">Address</label><asp:TextBox ID="Address" runat="server" CssClass="form-control" placeholder="City, State" MaxLength="250" /></div>
                <div class="form-group">
                    <label class="form-label" for="<%= HealthId.ClientID %>"><%: HealthIdLabelText %><asp:PlaceHolder ID="HealthIdMark" runat="server">*</asp:PlaceHolder>
                    </label>
                    <asp:TextBox ID="HealthId" runat="server" CssClass="form-control" MaxLength="40" />
                    <asp:RequiredFieldValidator ID="HealthIdRequired" runat="server" ControlToValidate="HealthId" Text="Required" CssClass="field-error" Display="Dynamic" />
                </div>
            </div>
            <div class="text-xs text-muted mt-2 mb-2" style="text-transform: uppercase; font-weight: 600;">Emergency contact &amp; insurance (optional)</div>
            <div class="form-grid">
                <div class="form-group">
                    <label class="form-label" for="<%= EmergencyName.ClientID %>">Emergency contact name</label><asp:TextBox ID="EmergencyName" runat="server" CssClass="form-control" /></div>
                <div class="form-group">
                    <label class="form-label" for="<%= EmergencyPhone.ClientID %>">Emergency contact phone</label><asp:TextBox ID="EmergencyPhone" runat="server" CssClass="form-control" /></div>
                <div class="form-group">
                    <label class="form-label" for="<%= Insurance.ClientID %>">Insurance provider</label><asp:TextBox ID="Insurance" runat="server" CssClass="form-control" /></div>
                <div class="form-group">
                    <label class="form-label" for="<%= PolicyNo.ClientID %>">Policy number</label><asp:TextBox ID="PolicyNo" runat="server" CssClass="form-control" /></div>
            </div>
            <asp:CheckBox ID="ConsentCheck" runat="server" Text=" I confirm these details are correct and consent to the clinic storing them for my care." CssClass="text-xs" />
            <asp:CustomValidator runat="server" OnServerValidate="Consent_ServerValidate" ErrorMessage="Please confirm the declaration" Text="Please confirm" CssClass="field-error" Display="Dynamic" />
            <asp:Button ID="SubmitButton" runat="server" Text="Register" CssClass="btn btn--primary mt-3" Style="width: 100%; justify-content: center;" OnClick="SubmitButton_Click" />
        </asp:Panel>
        <asp:Panel ID="SuccessPanel" runat="server" CssClass="card" Visible="false" Style="text-align: center; padding: 32px;">
            <div style="font-size: 48px;">✅</div>
            <h2 style="margin: 16px 0 8px;">Registration Successful</h2>
            <p class="text-secondary">Your <%: PatientIdLabelText %> is</p>
            <div style="font-size: 24px; font-weight: 700; color: var(--color-primary);">
                <asp:Literal ID="NewMrn" runat="server" /></div>
            <p class="text-xs text-muted mt-2">Keep this number — use it (or your phone number) to book appointments online.</p>
            <a class="btn btn--primary mt-2" href="<%: BookUrl %>"><i class="ti ti-calendar-plus" aria-hidden="true"></i>Book an appointment</a>
        </asp:Panel>
    </div>
</asp:Content>
