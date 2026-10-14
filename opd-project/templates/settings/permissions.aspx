<%@ Page Title="Permission Matrix" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="permissions.aspx.cs" Inherits="OpdProject.templates.settings.permissions" %>
<%-- Role → feature access matrix, enforced on every page by the server. Route: /settings/permissions --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <div class="card">
    <div class="text-sm text-secondary mb-3">Configure which roles can access each feature. Changes take effect immediately on the next request (sidebar and page access). ADMIN always keeps full access.</div>
    <div style="overflow-x:auto;">
      <table class="data-table">
        <thead><tr><th style="min-width:140px;">Feature</th><asp:Repeater ID="RolesHeader" runat="server"><ItemTemplate><th style="text-align:center; min-width:90px;"><%#: Container.DataItem %></th></ItemTemplate></asp:Repeater></tr></thead>
        <tbody>
          <asp:Repeater ID="MatrixRepeater" runat="server" ItemType="OpdProject.Models.PermissionRow" OnItemDataBound="MatrixRepeater_ItemDataBound">
            <ItemTemplate>
              <tr>
                <td style="font-weight:600; font-size:12px;"><%#: Item.Label %></td>
                <asp:Repeater ID="CellsRepeater" runat="server" ItemType="OpdProject.Web.PermissionCell" OnItemCommand="CellsRepeater_ItemCommand">
                  <ItemTemplate>
                    <td style="text-align:center;">
                      <asp:LinkButton runat="server" CssClass='<%# "btn btn--xs " + (Item.Allowed ? "btn--success" : "btn--ghost") %>' CommandName="Toggle"
                        CommandArgument='<%# Item.Role + "|" + Item.Feature %>' Enabled='<%# Item.Role != "ADMIN" %>'
                        ToolTip='<%# Item.Role == "ADMIN" ? "Admin always has full access" : (Item.Allowed ? "Revoke" : "Grant") + " " + Item.Feature + " for " + Item.Role %>'
                        style="width:36px; font-size:14px; justify-content:center;"><%# Item.Allowed ? "✓" : "—" %></asp:LinkButton>
                    </td>
                  </ItemTemplate>
                </asp:Repeater>
              </tr>
            </ItemTemplate>
          </asp:Repeater>
        </tbody>
      </table>
    </div>
  </div>
</div>
</asp:Content>
