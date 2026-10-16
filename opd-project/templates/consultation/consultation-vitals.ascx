<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="consultation-vitals.ascx.cs" Inherits="OpdProject.templates.consultation.consultation_vitals" %>
<%-- Vitals — structured table with previous values; ranges validated on the server.
     The previous / range columns, the unit switch and calculated values follow the consultation configuration;
     each vital is entered in its default unit or any other enabled unit and stored in its base unit. --%>
<div class="consult-section" id="sectionVitals" data-section="vitals">
    <style>
  .vitals-table input.vital-ok  { border: 2px solid #16a34a !important; box-shadow: 0 0 0 2px rgba(22,163,74,.15) !important; }
  .vitals-table input.vital-bad { border: 2px solid #dc2626 !important; box-shadow: 0 0 0 2px rgba(220,38,38,.15) !important; }
</style>
  <asp:Panel ID="VitalsPanel" runat="server" CssClass="vitals-table" DefaultButton="SaveVitalsButton">
    <table style="width:100%; border-collapse:collapse; font-size:12px;">
      <thead>
        <tr style="background:var(--surface-page); border-bottom:2px solid var(--border-default);">
          <th style="text-align:left; padding:6px 8px; width:28%;">Vital Sign</th>
          <th class="vt-prev" style="text-align:center; padding:6px 8px; width:18%;">Previous</th>
          <th style="text-align:center; padding:6px 8px; width:28%;">Current</th>
          <th style="text-align:center; padding:6px 8px; width:12%;">Unit</th>
          <th class="vt-range" style="text-align:center; padding:6px 8px; width:14%;">Range</th>
        </tr>
      </thead>
      <tbody>
        <asp:Repeater ID="VitalsRepeater" runat="server" ItemType="OpdProject.Models.VitalConfig" ClientIDMode="Predictable" OnItemDataBound="VitalsRepeater_ItemDataBound">
          <ItemTemplate>
            <tr style="border-bottom:1px solid var(--border-subtle, #f1f5f9);">
              <td style="padding:6px 8px; font-weight:600;">
                <%#: Item.DisplayLabel %>
                <asp:HiddenField ID="VitalCode" runat="server" Value="<%# Item.VitalCode %>" />
              </td>
              <td class="vt-prev" style="text-align:center; color:var(--text-muted); font-size:11px;"><%#: PreviousValue(Item.VitalCode) %></td>
              <td style="text-align:center; padding:4px 6px;">
                <asp:TextBox ID="VitalInput" runat="server" CssClass="form-control" style="text-align:center; width:90px; margin:0 auto; padding:3px 6px; font-size:12px;" placeholder="—" />
                <span class="vital-error"><asp:Literal ID="VitalError" runat="server" /></span>
              </td>
              <td style="text-align:center; font-size:10px; color:var(--text-muted);">
                <asp:DropDownList ID="UnitInput" runat="server" CssClass="form-select vital-unit" />
                <asp:Literal ID="UnitLabel" runat="server" />
              </td>
              <td class="vt-range" style="text-align:center; font-size:10px; color:var(--text-muted);"><%#: RangeLabel(Item.VitalCode) %></td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
      </tbody>
    </table>
    <asp:PlaceHolder ID="CalcHolder" runat="server"><div class="vital-calc" id="vitalsCalcDisplay" style="padding:6px 8px; font-size:11px;"><asp:Literal ID="CalcText" runat="server" /></div></asp:PlaceHolder>
    <div class="text-xs text-muted" style="padding:0 8px;"><asp:Literal ID="RecordedText" runat="server" /></div>
    <asp:LinkButton ID="SaveVitalsButton" runat="server" CssClass="btn btn--sm btn--primary mt-2" OnClick="SaveVitalsButton_Click">
      <i class="ti ti-device-floppy" aria-hidden="true"></i> Save Vitals
    </asp:LinkButton>
  </asp:Panel>
    <script>
        (function () {
            if (window.__vitalsRangeColor) return;
            window.__vitalsRangeColor = true;

            function parseRange(text) {
                if (!text || text.indexOf("'") >= 0) return null;
                var nums = text.match(/\d+(\.\d+)?/g);
                if (!nums || nums.length < 2) return null;
                return { min: parseFloat(nums[0]), max: parseFloat(nums[1]) };
            }

            function evaluate(tr) {
                var input = tr.querySelector("input[type=text]");
                if (!input) return;
                input.classList.remove("vital-ok", "vital-bad");

                var raw = input.value.trim();
                if (raw === "") return;

                var unit = tr.querySelector("select.vital-unit");
                if (unit) {
                    if (!unit.getAttribute("data-def")) unit.setAttribute("data-def", unit.value);
                    if (unit.value !== unit.getAttribute("data-def")) return;
                }

                var rangeCell = tr.querySelector("td.vt-range");
                var range = rangeCell ? parseRange(rangeCell.textContent.trim()) : null;
                if (!range) return;

                if (!/^\d+(\.\d+)?$/.test(raw)) { input.classList.add("vital-bad"); return; }
                var v = parseFloat(raw);
                input.classList.add(v >= range.min && v <= range.max ? "vital-ok" : "vital-bad");
            }

            function initAll() {
                document.querySelectorAll(".vitals-table tbody tr").forEach(function (tr) {
                    var unit = tr.querySelector("select.vital-unit");
                    if (unit && !unit.getAttribute("data-def")) unit.setAttribute("data-def", unit.value);
                    evaluate(tr);
                });
            }

            document.addEventListener("input", function (e) {
                if (e.target.closest(".vitals-table") && e.target.matches("input[type=text]")) evaluate(e.target.closest("tr"));
            });
            document.addEventListener("change", function (e) {
                if (e.target.closest(".vitals-table") && e.target.matches("select.vital-unit")) evaluate(e.target.closest("tr"));
            });

            if (window.Sys && Sys.Application) Sys.Application.add_load(initAll);
            else if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", initAll);
            else initAll();
        })();
    </script>
</div>
