<%@ Page Title="Nurse Vitals Entry" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="nurse-vitals.aspx.cs" Inherits="OpdProject.templates.scheduling.nurse_vitals" %>

<%-- Nurse vitals pre-entry for checked-in patients; saved into the visit the doctor will open. Units follow the
     Consultation Setup (default unit, or any enabled unit with the unit switch on). Route: /nurse/vitals --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
    <style>
        .vital-ok  { border: 2px solid #16a34a !important; box-shadow: 0 0 0 2px rgba(22,163,74,.15); }
        .vital-bad { border: 2px solid #dc2626 !important; box-shadow: 0 0 0 2px rgba(220,38,38,.15); }
    </style>
    <div class="page-container">
        <div style="display: grid; grid-template-columns: 280px 1fr; gap: 16px;">
            <div>
                <div class="card mb-2">
                    <div style="display: flex; justify-content: space-between; align-items: center;">
                        <div class="text-sm" style="font-weight: 600;">Checked-in Patients</div>
                        <asp:LinkButton ID="RefreshButton" runat="server" CssClass="btn btn--xs btn--ghost" OnClick="RefreshButton_Click" CausesValidation="false" ToolTip="Refresh"><i class="ti ti-refresh" aria-hidden="true"></i></asp:LinkButton>
                    </div>
                </div>
                <asp:Repeater ID="PatientsRepeater" runat="server" ItemType="OpdProject.Models.AppointmentView" OnItemCommand="PatientsRepeater_ItemCommand">
                    <ItemTemplate>
                        <asp:LinkButton runat="server" CssClass="card" CommandName="Select" CommandArgument="<%# Item.AppointmentId %>" CausesValidation="false"
                            Style='<%# "display:block; padding:10px; margin-bottom:6px; color:inherit; text-decoration:none; " + (Item.AppointmentId == SelectedAppointmentId ? "border:2px solid var(--color-primary); background:var(--color-primary-light,#e0f2fe);": "") %>'>
                            <div style="font-weight:600;"><%#: Item.PatientName %></div>
                        <div class="text-xs text-muted">Slot: <%#: FmtTime(Item.SlotStart) %> · <%#: Item.DoctorName %><%# HasVitals(Item) ? " · <span class=\"text-success\" style=\"font-weight:700;\">vitals ✓</span>" : "" %></div>                        </asp:LinkButton>
                    </ItemTemplate>
                </asp:Repeater>
                <asp:PlaceHolder ID="NoPatients" runat="server">
                    <div class="text-muted" style="padding: 12px;">No checked-in patients. Patients must be checked in at reception first.</div>
                </asp:PlaceHolder>
            </div>
            <div>
                <asp:PlaceHolder ID="SelectPrompt" runat="server">
                    <div class="card text-muted">Select a patient from the list.</div>
                </asp:PlaceHolder>
                <asp:Panel ID="VitalsCard" runat="server" CssClass="card" Visible="false" DefaultButton="SaveVitalsButton">
                    <div class="text-sm" style="font-weight: 600; margin-bottom: 12px;">Record vitals : <strong>
                        <asp:Literal ID="SelectedName" runat="server" /></strong></div>
                    <table style="width: 100%; border-collapse: collapse; font-size: 13px;">
                        <thead>
                            <tr style="border-bottom: 2px solid var(--border-default);">
                                <th style="text-align: left; padding: 6px;">Vital</th>
                                <th style="text-align: center;">Value</th>
                                <th style="text-align: center;">Unit</th>
                                <th style="text-align: center;">Range</th>
                            </tr>
                        </thead>
                        <tbody>
                            <asp:Repeater ID="VitalsRepeater" runat="server" ItemType="OpdProject.Web.NurseVital" OnItemDataBound="VitalsRepeater_ItemDataBound">
                                <ItemTemplate>
                                    <tr style="border-bottom: 1px solid var(--border-subtle,#f1f5f9);">
                                        <td style="padding: 8px 6px; font-weight: 600;"><%#: Item.Label %><asp:HiddenField ID="Code" runat="server" Value="<%# Item.Code %>" />
                                        </td>
                                        <td style="text-align: center;">
                                            <asp:TextBox ID="Value" runat="server" CssClass="form-control" Style="width: 90px; text-align: center; margin: 0 auto;" placeholder="—" Text="<%# Item.Value %>" />
                                            <div class="vital-error">
                                                <asp:Literal ID="Error" runat="server" /></div>
                                        </td>
                                        <td style="text-align: center; color: var(--text-muted); font-size: 11px;">
                                            <asp:DropDownList ID="UnitInput" runat="server" CssClass="form-select vital-unit" /><asp:Literal ID="UnitLabel" runat="server" /></td>
                                        <td style="text-align: center; color: var(--text-muted); font-size: 11px;"><%#: Item.Range %></td>
                                    </tr>
                                </ItemTemplate>
                            </asp:Repeater>
                        </tbody>
                    </table>
                    <asp:LinkButton ID="SaveVitalsButton" runat="server" CssClass="btn btn--primary mt-3" Style="width: 100%; justify-content: center;" OnClick="SaveVitalsButton_Click"><i class="ti ti-device-floppy" aria-hidden="true"></i> Save Vitals</asp:LinkButton>
                </asp:Panel>
            </div>
        </div>
    </div>
    <script>
        (function () {
            // Pull the two numbers out of range text like "90-110 °F" -> {min:90, max:110}.
            // Returns null for ranges we can't compare numerically (e.g. feet/inches 4'0"-7'0").
            function parseRange(text) {
                if (!text || text.indexOf("'") >= 0) return null;
                var nums = text.match(/\d+(\.\d+)?/g);
                if (!nums || nums.length < 2) return null;
                return { min: parseFloat(nums[0]), max: parseFloat(nums[1]) };
            }

            function rowParts(tr) {
                var tds = tr.children;
                if (tds.length < 4) return null;
                return {
                    input: tds[1].querySelector("input[type=text]"),
                    unit: tds[2].querySelector("select"),
                    rangeText: tds[3].textContent.trim()
                };
            }

            function evaluate(tr) {
                var p = rowParts(tr);
                if (!p || !p.input) return;
                var input = p.input;
                input.classList.remove("vital-ok", "vital-bad");

                var raw = input.value.trim();
                if (raw === "") return;                         // empty -> neutral

                // The displayed range is for the default unit; if the nurse switched
                // units, don't judge (the server still validates on save).
                if (p.unit) {
                    if (!p.unit.dataset.def) p.unit.dataset.def = p.unit.value;
                    if (p.unit.value !== p.unit.dataset.def) return;
                }

                var range = parseRange(p.rangeText);
                if (!range) return;
                if (!/^\d+(\.\d+)?$/.test(raw)) { input.classList.add("vital-bad"); return; }

                var v = parseFloat(raw);
                input.classList.add(v >= range.min && v <= range.max ? "vital-ok" : "vital-bad");
            }

            function rowOf(el) { return el.closest("tr"); }

            // Live feedback while typing / changing unit
            document.addEventListener("input", function (e) {
                if (e.target.matches("#<%= VitalsCard.ClientID %> input[type=text]")) evaluate(rowOf(e.target));
    });
    document.addEventListener("change", function (e) {
        if (e.target.matches("#<%= VitalsCard.ClientID %> select.vital-unit")) evaluate(rowOf(e.target));
    });

    // Remember each dropdown's default unit and color any prefilled values on load
    function init() {
        var card = document.getElementById("<%= VitalsCard.ClientID %>");
                if (!card) return;
                card.querySelectorAll("tbody tr").forEach(function (tr) {
                    var p = rowParts(tr);
                    if (p && p.unit && !p.unit.dataset.def) p.unit.dataset.def = p.unit.value;
                    evaluate(tr);
                });
            }
            if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
            else init();
        })();
    </script>
</asp:Content>
