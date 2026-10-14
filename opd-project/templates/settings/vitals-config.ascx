<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="vitals-config.ascx.cs" Inherits="OpdProject.templates.settings.vitals_config" %>
<%-- Settings → Clinical Config → Vitals: the hospital's vitals, read-only. Vitals are added, switched on/off and
     given their units in Consultation Setup. --%>
<div class="settings-section">
  <div class="settings-section-header">Vitals — which vitals to capture</div>
  <div class="text-xs text-muted mb-2"><i class="ti ti-info-circle" aria-hidden="true"></i> Vitals are added, switched on and off, and given their units in
    <a href="<%: OpdProject.Web.Urls.ConsultationConfig %>">Consultation Setup → Vitals</a> (changes go live when published).</div>
  <table class="data-table" style="font-size:12px;">
    <thead><tr><th>Code</th><th>Vital</th><th>Unit</th><th>Valid range</th><th>Recorded</th></tr></thead>
    <tbody>
      <asp:Repeater ID="VitalsRepeater" runat="server" ItemType="OpdProject.Models.VitalConfig">
        <ItemTemplate>
          <tr>
            <td class="text-xs"><%#: Item.VitalCode %></td>
            <td><%#: Item.DisplayLabel %><%#: Item.IsPediatricOnly ? " (pediatric)" : "" %>
              <%# Item.IsCustom ? "<span class=\"badge badge--info\" style=\"font-size:10px; padding:0 6px; margin-left:4px;\">Custom</span>" : "" %></td>
            <td><%#: Item.AllowedUnits %></td>
            <td class="text-xs"><%#: RangeLabel(Item.VitalCode) %></td>
            <td><span class="badge <%# Item.IsEnabled ? "badge--success" : "badge--info" %>"><%# Item.IsEnabled ? "On" : "Off" %></span></td>
          </tr>
        </ItemTemplate>
      </asp:Repeater>
    </tbody>
  </table>

</div>
