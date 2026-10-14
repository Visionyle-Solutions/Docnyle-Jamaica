<%@ Page Title="Patient Search" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="patient-search.aspx.cs" Inherits="OpdProject.templates.patients.patient_search" %>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container titlecase-headings">
  <asp:Panel ID="SearchCard" runat="server" CssClass="card mb-3" DefaultButton="SearchButton">
    <div style="display:flex; gap:8px; align-items:center;">
      <asp:TextBox ID="SearchInput" runat="server" CssClass="form-control" style="flex:1;" placeholder="Search By Name, MRN, Phone, Or Health ID..." autocomplete="off" />
      <asp:LinkButton ID="SearchButton" runat="server" CssClass="btn btn--ghost" OnClick="SearchButton_Click"><i class="ti ti-search" aria-hidden="true"></i> Search</asp:LinkButton>
      <a class="btn btn--primary" href="<%: Urls.PatientRegister %>"><i class="ti ti-plus" aria-hidden="true"></i> New Patient</a>
    </div>
    <div class="text-xs text-muted mt-1"><asp:Literal ID="ResultCount" runat="server" /></div>
  </asp:Panel>
  <div class="card">
    <table class="data-table">
      <thead>
        <tr>
          <th scope="col">S.No</th>
          <th scope="col">Name</th>
          <th scope="col"><%: PatientIdLabel %></th>
          <th scope="col">Gender</th>
          <th scope="col">DOB</th>
          <th scope="col">Phone Number</th>
          <th scope="col">Actions</th>
        </tr>
      </thead>
      <tbody>
        <asp:Repeater ID="ResultsRepeater" runat="server" ItemType="OpdProject.Models.Patient">
          <ItemTemplate>
            <tr>
              <td><%# Container.ItemIndex + 1 %></td>
              <td><%#: Item.FullName %></td>
              <td><%#: Item.Mrn %></td>
              <td><%#: OpdProject.Web.BasePage.TitleCase(Item.Gender) %></td>
              <td><%#: FmtDate(Item.Dob) %></td>
              <td><%#: string.IsNullOrEmpty(Item.Phone) ? "—" : Item.Phone %></td>
              <td style="white-space:nowrap;">
                <asp:PlaceHolder runat="server" Visible="<%# CanConsult %>">
                  <a class="btn btn--xs btn--primary" href="<%#: Urls.Consultation(Item.PatientId) %>" title="Open consultation"><i class="ti ti-stethoscope" aria-hidden="true"></i></a>
                </asp:PlaceHolder>
                <a class="btn btn--xs btn--ghost" href="<%#: Urls.PatientEdit(Item.PatientId) %>" title="Edit patient"><i class="ti ti-edit" aria-hidden="true"></i></a>
                <a class="btn btn--xs" href="<%#: Urls.Allergies(Item.PatientId) %>" title="Allergies, history, medications"><i class="ti ti-clipboard-heart" aria-hidden="true"></i> Health Record</a>
              </td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="EmptyRow" runat="server">
          <tr class="empty-row"><td colspan="7">No patients found.</td></tr>
        </asp:PlaceHolder>
      </tbody>
    </table>
  </div>
</div>
</asp:Content>
