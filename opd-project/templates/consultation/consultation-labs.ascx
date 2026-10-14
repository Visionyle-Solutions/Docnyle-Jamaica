<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="consultation-labs.ascx.cs" Inherits="OpdProject.templates.consultation.consultation_labs" %>
<div class="consult-section" id="sectionLabs" data-section="labs">
  <div class="consult-section-body">
    <asp:PlaceHolder ID="OrderingHolder" runat="server">
      <div class="hint-banner" style="margin-bottom:8px; background:var(--color-info-bg); border-left-color:var(--color-info);">
        <i class="ti ti-building-hospital" aria-hidden="true"></i>
        <span><strong>Done outside the clinic, not billed here.</strong> Ordered tests and imaging go on the lab requisition; the patient gets them done at an outside lab or imaging centre and pays there.</span>
      </div>
      <div class="hint-banner" style="margin-bottom:8px;">
        <i class="ti ti-info-circle" aria-hidden="true"></i>
        Your frequent tests are shown as quick-pick buttons. Search for others or add a custom test. Click ★ on any result to add it to your favorites.
      </div>

      <asp:PlaceHolder ID="FavoritesHolder" runat="server">
      <div class="text-xs text-muted mb-1" style="font-weight:600;">Your Frequent Tests</div>
      <div style="display:flex; flex-wrap:wrap; gap:4px; margin-bottom:8px;">
        <asp:Repeater ID="FavoritesRepeater" runat="server" ItemType="OpdProject.Models.LabTest" OnItemCommand="Order_ItemCommand" ClientIDMode="Predictable">
          <ItemTemplate>
            <asp:LinkButton runat="server" CssClass="btn btn--xs" style="font-size:11px;" CommandName="Order" CommandArgument="<%# Item.TestId %>"><%#: Item.TestName %></asp:LinkButton>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="NoFavorites" runat="server"><span class="zone-a-empty">No favorites yet — click ★ on any search result to add</span></asp:PlaceHolder>
      </div>
      </asp:PlaceHolder>

      <asp:PlaceHolder ID="SuggestedHolder" runat="server">
        <div class="text-xs text-muted mb-1" style="font-weight:600;"><i class="ti ti-route" aria-hidden="true"></i> Suggested For This Visit</div>
        <div style="display:flex; flex-wrap:wrap; gap:4px; margin-bottom:8px;">
          <asp:Repeater ID="SuggestedRepeater" runat="server" ItemType="OpdProject.Models.LabTest" OnItemCommand="Order_ItemCommand" ClientIDMode="Predictable">
            <ItemTemplate>
              <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" style="font-size:11px; border:1px dashed var(--color-primary);" CommandName="Order" CommandArgument="<%# Item.TestId %>"><i class="ti ti-plus" aria-hidden="true"></i> <%#: Item.TestName %></asp:LinkButton>
            </ItemTemplate>
          </asp:Repeater>
        </div>
      </asp:PlaceHolder>

      <asp:PlaceHolder ID="SearchHolder" runat="server">
      <asp:Panel ID="SearchPanel" runat="server" CssClass="form-group mb-2" DefaultButton="LabSearchButton" style="display:flex; gap:8px;">
        <asp:TextBox ID="LabSearchInput" runat="server" CssClass="form-control" placeholder="Search All Tests (e.g. CBC, Thyroid, X-Ray)..." autocomplete="off" />
        <asp:LinkButton ID="LabSearchButton" runat="server" CssClass="btn btn--sm btn--ghost" OnClick="LabSearchButton_Click"><i class="ti ti-search" aria-hidden="true"></i></asp:LinkButton>
      </asp:Panel>
      <div style="max-height:180px; overflow-y:auto;">
        <asp:Repeater ID="SearchResults" runat="server" ItemType="OpdProject.Models.LabTest" OnItemCommand="Order_ItemCommand" ClientIDMode="Predictable">
          <ItemTemplate>
            <div class="text-sm" style="display:flex; justify-content:space-between; align-items:center; padding:6px 8px; border-bottom:1px solid var(--border-default);">
              <div><strong><%#: Item.TestName %></strong> <span class="text-xs text-muted"><%#: Item.Category %> · <%#: Item.TestType %></span></div>
              <div style="display:flex; gap:4px;">
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Favorite" CommandArgument="<%# Item.TestId %>"
                  ToolTip='<%# IsFavorite(Item.TestId) ? "Remove from favorites" : "Add to favorites" %>' style='<%# "color:" + (IsFavorite(Item.TestId) ? "var(--color-warning)" : "var(--text-muted)") %>'>★</asp:LinkButton>
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--success" CommandName="Order" CommandArgument="<%# Item.TestId %>">Order</asp:LinkButton>
              </div>
            </div>
          </ItemTemplate>
        </asp:Repeater>
        <asp:Literal ID="SearchHint" runat="server" />
      </div>
      </asp:PlaceHolder>

      <asp:Panel ID="CustomPanel" runat="server" DefaultButton="AddCustomTestButton" style="display:flex; gap:8px; align-items:center; margin-top:8px;">
        <asp:TextBox ID="CustomTestName" runat="server" CssClass="form-control" style="max-width:250px;" placeholder="Custom Test Not In Catalog" MaxLength="100" />
        <asp:DropDownList ID="CustomTestType" runat="server" CssClass="form-select" style="width:100px;">
          <asp:ListItem Value="LAB">Lab</asp:ListItem>
          <asp:ListItem Value="IMAGING">Imaging</asp:ListItem>
        </asp:DropDownList>
        <asp:LinkButton ID="AddCustomTestButton" runat="server" CssClass="btn btn--xs btn--primary" OnClick="AddCustomTestButton_Click">Add To Catalog &amp; Order</asp:LinkButton>
      </asp:Panel>

      <asp:LinkButton ID="SkipLabsButton" runat="server" CssClass="btn btn--xs btn--ghost mt-2" OnClick="SkipLabsButton_Click">Not Needed For This Visit</asp:LinkButton>
    </asp:PlaceHolder>

    <div class="text-xs text-muted mt-3 mb-1" style="font-weight:600;">Ordered Tests</div>
    <asp:Repeater ID="OrdersRepeater" runat="server" ItemType="OpdProject.Models.InvestigationOrder" OnItemCommand="OrdersRepeater_ItemCommand" ClientIDMode="Predictable">
      <ItemTemplate>
        <div class="zone-a-item" style="display:flex; justify-content:space-between; align-items:center; padding:4px 0;">
          <span><%#: Item.TestName %> <span class="badge <%# Item.TestType == "IMAGING" ? "badge--warning" : "badge--info" %>" style="font-size:10px;"><%#: Item.TestType %></span></span>
          <div style="display:flex; gap:4px; align-items:center;">
            <span class="badge <%#: OpdProject.Web.BasePage.StatusBadge(Item.Status) %>" style="font-size:9px;"><%#: Item.Status %></span>
            <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Remove" CommandArgument="<%# Item.OrderId %>" Visible='<%# Item.Status == "ORDERED" && !Host.IsLocked %>' ToolTip="Remove order"><i class="ti ti-x" aria-hidden="true"></i></asp:LinkButton>
          </div>
        </div>
      </ItemTemplate>
    </asp:Repeater>
    <asp:PlaceHolder ID="NoOrders" runat="server"><div class="zone-a-empty">No tests ordered yet.</div></asp:PlaceHolder>
  </div>
</div>
