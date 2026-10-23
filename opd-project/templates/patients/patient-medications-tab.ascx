<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="patient-medications-tab.ascx.cs" Inherits="OpdProject.templates.patients.patient_medications_tab" %>
<%-- Health record → Current Medications: prescriptions from consultations (read-only here) plus medicines
     prescribed elsewhere, which can be added, edited and discontinued on this tab. --%>
<div class="split-layout">
    <div class="split-main">
        <div style="display: flex; justify-content: flex-end; margin-bottom: 6px;">
            <asp:CheckBox ID="ShowPast" runat="server" Text=" Show Discontinued / Completed" CssClass="text-xs text-muted" AutoPostBack="true" OnCheckedChanged="ShowPast_CheckedChanged" />
        </div>
        <table class="data-table">
            <thead>
                <tr>
                    <th scope="col">Medicine</th>
                    <th scope="col">Dose &amp; Frequency</th>
                    <th scope="col">Since</th>
                    <th scope="col">Until</th>
                    <th scope="col">Source</th>
                    <th scope="col">Actions</th>
                </tr>
            </thead>
            <tbody>
                <asp:Repeater ID="MedsRepeater" runat="server" ItemType="OpdProject.Models.Prescription" OnItemCommand="MedsRepeater_ItemCommand" ClientIDMode="Predictable">
                    <ItemTemplate>
                        <tr class="<%# IsCurrent(Item) ? "" : "record-row--past" %>">
                            <td><strong><%#: Item.MedicineName %></strong> <%#: Item.Strength %>
                                <div class="text-xs text-muted"><%#: FormLine(Item) %></div>
                            </td>
                            <td><%#: DoseLine(Item) %></td>
                            <td><%#: AppPage.FmtDate(Item.PeriodStart ?? Item.PrescribedDate) %></td>
                            <td><%# UntilHtml(Item) %></td>
                            <td><span class="src-badge <%# Item.IsOutside ? "src-badge--outside" : "src-badge--here" %>"><%# Item.IsOutside ? "Outside" : "Prescribed Here" %></span>
                                <div class="text-xs text-muted"><%#: SourceLine(Item) %></div>
                            </td>
                            <td style="white-space: nowrap;">
                                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Edit" CommandArgument="<%# Item.PrescriptionId %>" CausesValidation="false"
                                    Visible="<%# Item.IsOutside && IsCurrent(Item) %>" ToolTip='<%# "Edit " + Item.MedicineName %>'><i class="ti ti-edit" aria-hidden="true"></i></asp:LinkButton>
                                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Discontinue" CommandArgument="<%# Item.PrescriptionId %>" CausesValidation="false"
                                    Visible="<%# Item.IsOutside && IsCurrent(Item) %>" ToolTip='<%# "Discontinue " + Item.MedicineName %>'
                                    OnClientClick="return confirm('Mark this medicine as discontinued? The record is kept.');"><i class="ti ti-circle-off" aria-hidden="true"></i></asp:LinkButton>
                                <asp:PlaceHolder runat="server" Visible="<%# !Item.IsOutside && IsCurrent(Item) %>"><span class="text-xs text-muted" title="Change or stop it from a consultation">Via Consultation</span></asp:PlaceHolder>
                            </td>
                        </tr>
                    </ItemTemplate>
                </asp:Repeater>
                <asp:PlaceHolder ID="NoMeds" runat="server">
                    <tr class="empty-row">
                        <td colspan="6">
                            <asp:Literal ID="NoMedsText" runat="server" /></td>
                    </tr>
                </asp:PlaceHolder>
            </tbody>
        </table>
        <div class="text-xs text-muted" style="margin-top: 6px;">
            <i class="ti ti-info-circle" aria-hidden="true"></i>
            Prescriptions written in a consultation appear here automatically and drop off once their course ends. Outside medicines stay until discontinued.
        </div>
    </div>

    <div class="split-panel">
        <asp:Panel ID="AllergyAlertPanel" runat="server" CssClass="record-alert" Visible="false">
            <div style="font-weight: 700;"><i class="ti ti-alert-octagon" aria-hidden="true"></i>Drug-Allergy Conflict</div>
            <asp:Repeater ID="AllergyAlertsRepeater" runat="server" ItemType="OpdProject.Models.ClinicalAlert">
                <ItemTemplate>
                    <div class="text-sm" style="margin-top: 4px;"><%#: Item.Message %></div>
                </ItemTemplate>
            </asp:Repeater>
            <div class="text-xs" style="margin-top: 6px;">Record it anyway only if the patient is already taking it without reaction. The override is written to the audit log.</div>
            <div style="display: flex; gap: 8px; margin-top: 8px;">
                <asp:Button ID="RecordAnywayButton" runat="server" Text="Record Anyway" CssClass="btn btn--sm btn--danger" ValidationGroup="OutsideMed" OnClick="RecordAnywayButton_Click" />
                <asp:Button ID="CancelAlertButton" runat="server" Text="Cancel" CssClass="btn btn--sm" CausesValidation="false" OnClick="CancelAlertButton_Click" />
            </div>
        </asp:Panel>

        <asp:Panel ID="FormPanel" runat="server" CssClass="card form-grid" DefaultButton="SaveButton">
            <asp:HiddenField ID="EditingId" runat="server" />
            <div class="form-group form-group--full" style="font-weight: 700;">
                <asp:Literal ID="FormTitle" runat="server" Text="Add Outside Medication" /></div>
            <div class="form-group form-group--full">
                <label class="form-label" for="<%= MedicineInput.ClientID %>">Medicine <span class="required">*</span></label>
                <asp:TextBox ID="MedicineInput" runat="server" CssClass="form-control" MaxLength="200" placeholder="Search Formulary Or Type A Name" list="formularyNames" autocomplete="off" />
                <datalist id="formularyNames">
                    <asp:Literal ID="FormularyOptions" runat="server" /></datalist>
                <asp:RequiredFieldValidator runat="server" ControlToValidate="MedicineInput" ValidationGroup="OutsideMed" Text="Medicine is required" CssClass="field-error" Display="Dynamic" />
            </div>
            <div class="form-group">
                <label class="form-label" for="<%= StrengthInput.ClientID %>">Strength</label>
                <asp:TextBox ID="StrengthInput" runat="server" CssClass="form-control" MaxLength="50" placeholder="e.g. 20 mg" />
            </div>
            <div class="form-group">
                <label class="form-label" for="<%= FormInput.ClientID %>">Form</label>
                <asp:DropDownList ID="FormInput" runat="server" CssClass="form-select">
                    <asp:ListItem>Tablet</asp:ListItem>
                    <asp:ListItem>Capsule</asp:ListItem>
                    <asp:ListItem>Syrup</asp:ListItem>
                    <asp:ListItem>Injection</asp:ListItem>
                    <asp:ListItem>Inhaler</asp:ListItem>
                    <asp:ListItem>Drops</asp:ListItem>
                    <asp:ListItem>Cream / Ointment</asp:ListItem>
                    <asp:ListItem>Other</asp:ListItem>
                </asp:DropDownList>
            </div>
            <div class="form-group">
                <label class="form-label" for="<%= DoseInput.ClientID %>">Dose <span class="required">*</span></label>
                <asp:TextBox ID="DoseInput" runat="server" CssClass="form-control" MaxLength="40" placeholder="e.g. 1 tab" />
                <asp:RequiredFieldValidator runat="server" ControlToValidate="DoseInput" ValidationGroup="OutsideMed" Text="Dose is required" CssClass="field-error" Display="Dynamic" />
            </div>
            <div class="form-group">
                <label class="form-label" for="<%= FrequencyInput.ClientID %>">Frequency <span class="required">*</span></label>
                <asp:DropDownList ID="FrequencyInput" runat="server" CssClass="form-select">
                    <asp:ListItem Value="OD">Once Daily</asp:ListItem>
                    <asp:ListItem Value="BD">Twice Daily</asp:ListItem>
                    <asp:ListItem Value="TDS">Three Times Daily</asp:ListItem>
                    <asp:ListItem Value="QID">Four Times Daily</asp:ListItem>
                    <asp:ListItem Value="HS">At Bedtime</asp:ListItem>
                    <asp:ListItem Value="WEEKLY">Once A Week</asp:ListItem>
                    <asp:ListItem Value="PRN">As Needed</asp:ListItem>
                </asp:DropDownList>
            </div>
            <div class="form-group">
                <label class="form-label" for="<%= SinceInput.ClientID %>">Taking Since</label>
                <asp:TextBox ID="SinceInput" runat="server" CssClass="form-control" TextMode="Month" />
                <asp:CustomValidator runat="server" ControlToValidate="SinceInput" ValidationGroup="OutsideMed" OnServerValidate="Since_ServerValidate" Text="Cannot be in the future" CssClass="field-error" Display="Dynamic" />
            </div>
            <div class="form-group">
                <label class="form-label" for="<%= SourceInput.ClientID %>">Prescribed By / Source</label>
                <asp:TextBox ID="SourceInput" runat="server" CssClass="form-control" MaxLength="120" placeholder="e.g. Cardiologist, self" />
            </div>
            <div class="form-group form-group--full">
                <label class="form-label" for="<%= NotesInput.ClientID %>">Notes</label>
                <asp:TextBox ID="NotesInput" runat="server" CssClass="form-control" MaxLength="250" placeholder="e.g. after lunch" />
            </div>
            <div class="form-group form-group--full" style="display: flex; gap: 8px;">
                <asp:Button ID="SaveButton" runat="server" Text="Add" CssClass="btn btn--success" ValidationGroup="OutsideMed" OnClick="SaveButton_Click" />
                <asp:Button ID="ResetButton" runat="server" Text="Reset" CssClass="btn btn--danger" CausesValidation="false" OnClick="ResetButton_Click" />
            </div>
            <div class="form-group form-group--full text-xs text-muted" style="margin-bottom: 0;">The drug-allergy check runs on save, the same as for prescriptions.</div>
        </asp:Panel>
    </div>
    <asp:Panel ID="FormPanel" runat="server" CssClass="card form-grid" DefaultButton="SaveButton">
      <asp:HiddenField ID="EditingId" runat="server" />
      <div class="form-group form-group--full" style="font-weight:700;"><asp:Literal ID="FormTitle" runat="server" Text="Add Outside Medication" /></div>
      <div class="form-group form-group--full">
        <label class="form-label" for="<%= MedicineInput.ClientID %>">Medicine <span class="required">*</span></label>
        <div class="patient-suggest">
          <asp:TextBox ID="MedicineInput" runat="server" CssClass="form-control" MaxLength="200" placeholder="Type 2+ Letters Of Brand Or Generic Name..." autocomplete="off" data-medicine-suggest="true" />
        </div>
        <asp:RequiredFieldValidator runat="server" ControlToValidate="MedicineInput" ValidationGroup="OutsideMed" Text="Medicine is required" CssClass="field-error" Display="Dynamic" />
      </div>
      <div class="form-group">
        <label class="form-label" for="<%= StrengthInput.ClientID %>">Strength</label>
        <asp:TextBox ID="StrengthInput" runat="server" CssClass="form-control" MaxLength="50" placeholder="e.g. 20 mg" />
      </div>
      <div class="form-group">
        <label class="form-label" for="<%= FormInput.ClientID %>">Form</label>
        <asp:DropDownList ID="FormInput" runat="server" CssClass="form-select">
          <asp:ListItem>Tablet</asp:ListItem><asp:ListItem>Capsule</asp:ListItem><asp:ListItem>Syrup</asp:ListItem>
          <asp:ListItem>Injection</asp:ListItem><asp:ListItem>Inhaler</asp:ListItem><asp:ListItem>Drops</asp:ListItem>
          <asp:ListItem>Cream / Ointment</asp:ListItem><asp:ListItem>Other</asp:ListItem>
        </asp:DropDownList>
      </div>
      <div class="form-group">
        <label class="form-label" for="<%= DoseInput.ClientID %>">Dose</label>
        <asp:TextBox ID="DoseInput" runat="server" CssClass="form-control" MaxLength="40" placeholder="e.g. 1 tab" />
      </div>
      <div class="form-group">
        <label class="form-label" for="<%= FrequencyInput.ClientID %>">Frequency <span class="required">*</span></label>
        <asp:DropDownList ID="FrequencyInput" runat="server" CssClass="form-select">
          <asp:ListItem Value="OD">Once Daily</asp:ListItem>
          <asp:ListItem Value="BD">Twice Daily</asp:ListItem>
          <asp:ListItem Value="TDS">Three Times Daily</asp:ListItem>
          <asp:ListItem Value="QID">Four Times Daily</asp:ListItem>
          <asp:ListItem Value="HS">At Bedtime</asp:ListItem>
          <asp:ListItem Value="WEEKLY">Once A Week</asp:ListItem>
          <asp:ListItem Value="PRN">As Needed</asp:ListItem>
        </asp:DropDownList>
      </div>
      <div class="form-group">
        <label class="form-label" for="<%= SinceInput.ClientID %>">Taking Since</label>
        <asp:TextBox ID="SinceInput" runat="server" CssClass="form-control" TextMode="Month" />
        <asp:CustomValidator runat="server" ControlToValidate="SinceInput" ValidationGroup="OutsideMed" OnServerValidate="Since_ServerValidate" Text="Cannot be in the future" CssClass="field-error" Display="Dynamic" />
      </div>
      <div class="form-group">
        <label class="form-label" for="<%= SourceInput.ClientID %>">Prescribed By / Source</label>
        <asp:TextBox ID="SourceInput" runat="server" CssClass="form-control" MaxLength="120" placeholder="e.g. Cardiologist, self" />
      </div>
      <div class="form-group form-group--full">
        <label class="form-label" for="<%= NotesInput.ClientID %>">Notes</label>
        <asp:TextBox ID="NotesInput" runat="server" CssClass="form-control" MaxLength="250" placeholder="e.g. after lunch" />
      </div>
      <div class="form-group form-group--full" style="display:flex; gap:8px;">
        <asp:Button ID="SaveButton" runat="server" Text="Add" CssClass="btn btn--success" ValidationGroup="OutsideMed" OnClick="SaveButton_Click" />
        <asp:Button ID="ResetButton" runat="server" Text="Reset" CssClass="btn btn--danger" CausesValidation="false" OnClick="ResetButton_Click" />
      </div>
      <div class="form-group form-group--full text-xs text-muted" style="margin-bottom:0;">The drug-allergy check runs on save, the same as for prescriptions.</div>
    </asp:Panel>
  </div>
</div>
<script>
  /* Medicine typeahead (same behaviour as the patient typeahead): formulary suggestions appear after 2 characters
     (brand or generic name); choosing one fills the name and the form. Arrow keys move, Enter picks, Esc closes.
     A name that is not in the formulary can still be typed and saved. */
  (function () {
    var MIN_CHARS = 2;
    var input = document.querySelector('input[data-medicine-suggest]');
    if (!input) return;
    var formSelect = document.getElementById('<%= FormInput.ClientID %>');
    function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return '&#' + c.charCodeAt(0) + ';'; }); }
    function mark(text, term) {
      text = String(text == null ? '' : text);
      var i = text.toLowerCase().indexOf(term.toLowerCase());
      if (i < 0 || !term) return esc(text);
      return esc(text.slice(0, i)) + '<mark>' + esc(text.slice(i, i + term.length)) + '</mark>' + esc(text.slice(i + term.length));
    }

    var wrap = input.parentNode;
    var list = document.createElement('div');
    list.className = 'patient-suggest-list';
    list.setAttribute('role', 'listbox');
    list.hidden = true;
    wrap.appendChild(list);
    var items = [], active = -1, timer = null, seq = 0;

    function close() { list.hidden = true; active = -1; }
    function highlight(i) {
      active = i;
      Array.prototype.forEach.call(list.children, function (el, n) { el.classList.toggle('is-active', n === i); });
      if (list.children[i] && list.children[i].scrollIntoView) list.children[i].scrollIntoView({ block: 'nearest' });
    }
    function selectForm(form) {
      if (!formSelect || !form) return;
      var f = String(form).toLowerCase();
      for (var n = 0; n < formSelect.options.length; n++)
        if (formSelect.options[n].text.toLowerCase().indexOf(f) >= 0) { formSelect.selectedIndex = n; return; }
    }
    function choose(i) {
      var d = items[i];
      if (!d) return;
      input.value = d.name;
      input.classList.add('is-picked');
      selectForm(d.form);
      close();
    }
    function render(term) {
      if (items.length === 0) {
        list.innerHTML = '<div class="patient-suggest-empty">No matching medicines — keep typing to add it by name</div>';
      } else {
        list.innerHTML = items.map(function (d, i) {
          var meta = [d.generic ? mark(d.generic, term) : '', esc(d.composition), esc(d.form)].filter(function (s) { return s; }).join(' · ');
          return '<div class="patient-suggest-item" role="option" data-i="' + i + '">' +
            '<div class="patient-suggest-name">' + mark(d.name, term) + '</div>' +
            (meta ? '<div class="patient-suggest-meta">' + meta + '</div>' : '') + '</div>';
        }).join('');
      }
      list.hidden = false;
      active = -1;
    }
    function search() {
      var term = input.value.trim();
      if (term.length < MIN_CHARS) { close(); return; }
      var mine = ++seq;
      list.hidden = false;
      list.innerHTML = '<div class="patient-suggest-empty">Searching…</div>';
      fetch(window.location.pathname + '?medicineSuggest=' + encodeURIComponent(term), { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
        .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
        .then(function (data) { if (mine !== seq) return; items = data || []; render(term); })
        .catch(function () { if (mine !== seq) return; items = []; list.innerHTML = '<div class="patient-suggest-empty">Search failed — try again</div>'; });
    }

    input.addEventListener('input', function () {
      input.classList.remove('is-picked');
      clearTimeout(timer);
      timer = setTimeout(search, 200);
    });
    input.addEventListener('focus', function () { if (!input.classList.contains('is-picked') && input.value.trim().length >= MIN_CHARS) search(); });
    input.addEventListener('keydown', function (e) {
      if (list.hidden) return;
      if (e.key === 'ArrowDown') { e.preventDefault(); if (items.length) highlight((active + 1) % items.length); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); if (items.length) highlight((active - 1 + items.length) % items.length); }
      /* Enter picks only a highlighted row, so a free-typed name is never swapped for a suggestion */
      else if (e.key === 'Enter' && active >= 0) { e.preventDefault(); e.stopPropagation(); choose(active); }
      else if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close(); }
    });
    /* mousedown (not click) so the choice lands before the input loses focus */
    list.addEventListener('mousedown', function (e) {
      var row = e.target.closest('.patient-suggest-item');
      e.preventDefault();
      if (row) choose(+row.getAttribute('data-i'));
    });
    input.addEventListener('blur', function () { setTimeout(close, 100); });
  })();
</script>
