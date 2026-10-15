<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="coverage-form.ascx.cs" Inherits="OpdProject.templates.billing.coverage_form" %>
<%-- Add-coverage fields (private insurance or the country's govt scheme, e.g. NHF) shared by the billing
     checkout's Add Coverage modal and the patient Coverage & Insurance page. Validators use the "Coverage"
     group; the host owns the validation summary and the save button and calls Reset / BuildRecord. --%>
<div class="form-group"><label class="form-label" for="<%= CovType.ClientID %>">Coverage Type</label>
  <asp:DropDownList ID="CovType" runat="server" CssClass="form-select" AutoPostBack="true" OnSelectedIndexChanged="CovType_Changed" /></div>
<asp:PlaceHolder ID="CovPrivate" runat="server">
  <div class="form-grid">
    <div class="form-group"><label class="form-label" for="<%= CovPayerPrivate.ClientID %>">Insurance Company *</label><asp:DropDownList ID="CovPayerPrivate" runat="server" CssClass="form-select" /></div>
    <div class="form-group"><label class="form-label" for="<%= CovPlan.ClientID %>">Plan Name</label><asp:TextBox ID="CovPlan" runat="server" CssClass="form-control" /></div>
    <div class="form-group"><label class="form-label" for="<%= CovMember.ClientID %>">Member ID *</label><asp:TextBox ID="CovMember" runat="server" CssClass="form-control" /></div>
    <div class="form-group"><label class="form-label" for="<%= CovPolicy.ClientID %>">Policy Number</label><asp:TextBox ID="CovPolicy" runat="server" CssClass="form-control" /></div>
    <div class="form-group"><label class="form-label" for="<%= CovGroup.ClientID %>">Group Number</label><asp:TextBox ID="CovGroup" runat="server" CssClass="form-control" /></div>
    <div class="form-group"><label class="form-label" for="<%= CovHolder.ClientID %>">Policy Holder</label><asp:TextBox ID="CovHolder" runat="server" CssClass="form-control" /></div>
    <div class="form-group"><label class="form-label" for="<%= CovRelation.ClientID %>">Relationship</label>
      <asp:DropDownList ID="CovRelation" runat="server" CssClass="form-select"><asp:ListItem>Self</asp:ListItem><asp:ListItem>Spouse</asp:ListItem><asp:ListItem>Child</asp:ListItem><asp:ListItem>Parent</asp:ListItem><asp:ListItem>Dependent</asp:ListItem></asp:DropDownList></div>
    <div class="form-group"><label class="form-label" for="<%= CovMode.ClientID %>">Claim Mode</label>
      <asp:DropDownList ID="CovMode" runat="server" CssClass="form-select"><asp:ListItem Value="CASHLESS">Cashless</asp:ListItem><asp:ListItem Value="REIMBURSEMENT">Reimbursement</asp:ListItem></asp:DropDownList></div>
  </div>
  <asp:CustomValidator ID="CovPrivateValidator" runat="server" ValidationGroup="Coverage" OnServerValidate="CovPrivateValidator_ServerValidate" ErrorMessage="Enter the member ID or policy number" Display="None" />
</asp:PlaceHolder>
<asp:PlaceHolder ID="CovGovt" runat="server" Visible="false">
  <div class="form-grid">
    <div class="form-group"><label class="form-label" for="<%= CovPayerGovt.ClientID %>">Scheme *</label><asp:DropDownList ID="CovPayerGovt" runat="server" CssClass="form-select" /></div>
    <div class="form-group"><label class="form-label" for="<%= CovCard.ClientID %>"><asp:Literal ID="CardLabel" runat="server" /> *</label><asp:TextBox ID="CovCard" runat="server" CssClass="form-control" /></div>
  </div>
  <asp:CustomValidator ID="CovGovtValidator" runat="server" ValidationGroup="Coverage" OnServerValidate="CovGovtValidator_ServerValidate" Display="None" />
</asp:PlaceHolder>
<div class="form-grid">
  <div class="form-group"><label class="form-label" for="<%= CovEffective.ClientID %>">Effective Date</label><asp:TextBox ID="CovEffective" runat="server" CssClass="form-control" TextMode="Date" /></div>
  <div class="form-group"><label class="form-label" for="<%= CovExpiry.ClientID %>">Expiry Date</label><asp:TextBox ID="CovExpiry" runat="server" CssClass="form-control" TextMode="Date" />
    <asp:CompareValidator runat="server" ControlToValidate="CovExpiry" ControlToCompare="CovEffective" Operator="GreaterThanEqual" Type="Date" ValidationGroup="Coverage" ErrorMessage="Expiry must be on or after the effective date" Text="Must be after effective date" CssClass="field-error" Display="Dynamic" /></div>
  <div class="form-group"><label class="form-label" for="<%= CovStatus.ClientID %>">Status</label>
    <asp:DropDownList ID="CovStatus" runat="server" CssClass="form-select"><asp:ListItem Value="ACTIVE">Active</asp:ListItem><asp:ListItem Value="PENDING">Pending</asp:ListItem><asp:ListItem Value="INACTIVE">Inactive</asp:ListItem><asp:ListItem Value="EXPIRED">Expired</asp:ListItem></asp:DropDownList></div>
</div>
