<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="consultation-symptoms.ascx.cs" Inherits="OpdProject.templates.consultation.consultation_symptoms" %>
<%-- Presenting complaints — tri-state chips (unset → yes → no → unset), persisted on every tap --%>
<div class="consult-section" id="sectionComplaints" data-section="complaints">
  <div class="consult-section-body">
    <div style="display:flex; flex-wrap:wrap; gap:6px;" id="symptomChipRow">
      <asp:Repeater ID="QuickPicks" runat="server" ItemType="OpdProject.Web.SymptomChip" OnItemCommand="Chip_ItemCommand" ClientIDMode="Predictable">
        <ItemTemplate>
          <asp:LinkButton runat="server" CssClass="chip" data-state="<%# Item.State %>" CommandName="Cycle" CommandArgument="<%# Item.Code %>" Enabled="<%# !Locked %>" ToolTip='<%# Item.Label + ", currently " + Item.State %>'>
            <%#: Item.Label %><%# Item.State == "YES" ? " <i class=\"ti ti-check\" aria-hidden=\"true\"></i>" : Item.State == "NO" ? " <i class=\"ti ti-x\" aria-hidden=\"true\"></i>" : "" %>
          </asp:LinkButton>
        </ItemTemplate>
      </asp:Repeater>
      <asp:LinkButton ID="MoreToggle" runat="server" CssClass="zone-a-more" OnClick="MoreToggle_Click" CausesValidation="false" />
    </div>
    <asp:Panel ID="ExpandedPanel" runat="server" CssClass="complaints-expanded" Visible="false" DefaultButton="AddCustomButton">
      <asp:PlaceHolder ID="OtherHolder" runat="server">
      <div style="display:flex; flex-wrap:wrap; gap:6px; margin:8px 0;">
        <asp:Repeater ID="OtherSymptoms" runat="server" ItemType="OpdProject.Web.SymptomChip" OnItemCommand="Chip_ItemCommand" ClientIDMode="Predictable">
          <ItemTemplate>
            <asp:LinkButton runat="server" CssClass="chip" data-state="<%# Item.State %>" CommandName="Cycle" CommandArgument="<%# Item.Code %>" Enabled="<%# !Locked %>">
              <%#: Item.Label %>
            </asp:LinkButton>
          </ItemTemplate>
        </asp:Repeater>
      </div>
      </asp:PlaceHolder>
      <asp:PlaceHolder ID="CustomHolder" runat="server">
        <div style="display:flex; gap:8px; align-items:center;">
          <asp:TextBox ID="CustomComplaintInput" runat="server" CssClass="form-control" style="max-width:250px;" placeholder="Other complaint not listed" MaxLength="60" />
          <asp:LinkButton ID="AddCustomButton" runat="server" CssClass="btn btn--xs btn--primary" OnClick="AddCustomButton_Click">Add</asp:LinkButton>
        </div>
      </asp:PlaceHolder>
    </asp:Panel>
    <div class="text-xs text-muted mt-2">Tap to cycle: unset → yes → no → unset</div>
  </div>
</div>
