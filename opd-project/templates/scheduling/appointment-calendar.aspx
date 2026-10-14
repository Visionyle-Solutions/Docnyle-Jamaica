<%@ Page Title="Appointment Calendar" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="appointment-calendar.aspx.cs" Inherits="OpdProject.templates.scheduling.appointment_calendar" MaintainScrollPositionOnPostback="true" %>
<%-- Daily schedule for one doctor + booking. Free slots come from the doctor's weekly timetable,
     leave, slot length and buffer (Schedule Settings). Route: /scheduling/appointments --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <div class="card mb-3">
    <div style="display:flex; gap:12px; align-items:flex-end; flex-wrap:wrap;">
      <div class="form-group" style="margin:0;">
        <label class="form-label" for="<%= DoctorFilter.ClientID %>">Doctor</label>
        <asp:DropDownList ID="DoctorFilter" runat="server" CssClass="form-select" AutoPostBack="true" OnSelectedIndexChanged="Filter_Changed" />
      </div>
      <div class="form-group" style="margin:0;">
        <label class="form-label" for="<%= DateFilter.ClientID %>">Date</label>
        <asp:TextBox ID="DateFilter" runat="server" CssClass="form-control" TextMode="Date" AutoPostBack="true" OnTextChanged="Filter_Changed" />
      </div>
      <asp:LinkButton ID="BookButton" runat="server" CssClass="btn btn--sm btn--primary" OnClick="BookButton_Click" CausesValidation="false"><i class="ti ti-plus" aria-hidden="true"></i> Book Appointment</asp:LinkButton>
      <div style="flex:1;"></div>
      <div class="text-sm text-secondary"><asp:Literal ID="SlotSummary" runat="server" /></div>
    </div>
  </div>

  <div class="card mb-3" style="display:flex; gap:8px; justify-content:flex-end; padding:8px 16px; flex-wrap:wrap;">
    <asp:LinkButton ID="RecurringButton" runat="server" CssClass="btn btn--sm btn--ghost" OnClick="RecurringButton_Click" CausesValidation="false"><i class="ti ti-repeat" aria-hidden="true"></i> Book Recurring Series</asp:LinkButton>
    <asp:LinkButton ID="RemindersButton" runat="server" CssClass="btn btn--sm" style="background:#25D366; color:#fff; border:none;" OnClick="RemindersButton_Click" CausesValidation="false"><i class="ti ti-brand-whatsapp" aria-hidden="true"></i> Send Reminders</asp:LinkButton>
    <asp:LinkButton ID="NoShowButton" runat="server" CssClass="btn btn--sm btn--warning" OnClick="NoShowButton_Click" CausesValidation="false"
      OnClientClick="return confirm('Mark every still-booked appointment on this date as NO_SHOW and send the no-show message?');"><i class="ti ti-alert-circle" aria-hidden="true"></i> Send No-Show Messages</asp:LinkButton>
  </div>

  <div style="display:flex; gap:16px; align-items:flex-start;">
    <div style="flex:1; min-width:0;">
      <div class="card">
        <div class="text-sm text-secondary mb-2" style="font-weight:600;">Schedule</div>
        <asp:Repeater ID="ScheduleRepeater" runat="server" ItemType="OpdProject.Models.AppointmentView" OnItemCommand="ScheduleRepeater_ItemCommand">
          <ItemTemplate>
            <div style="display:flex; align-items:center; gap:10px; padding:10px 8px; border-bottom:1px solid var(--border-default);">
              <div style="width:4px; height:40px; border-radius:2px; background:<%# StatusColor(Item.Status) %>;"></div>
              <div style="min-width:70px;">
                <div class="text-sm" style="font-weight:600;"><%#: FmtTime(Item.SlotStart) %></div>
                <div class="text-xs text-muted"><%#: FmtTime(Item.SlotEnd) %></div>
              </div>
              <div style="flex:1;">
                <div class="text-sm" style="font-weight:600;"><%#: Item.PatientName %></div>
                <div class="text-xs text-muted"><%#: Item.PatientMrn %> · <span style="display:inline-block; width:8px; height:8px; border-radius:2px; background:<%#: TypeColor(Item.Type) %>;"></span> <%#: AppointmentTypeName(Item.Type) %><%#: string.IsNullOrEmpty(Item.Appointment.Notes) ? "" : " · " + Item.Appointment.Notes %></div>
              </div>
              <span class="badge <%#: StatusBadge(Item.Status) %>" style="font-size:10px;"><%#: OpdProject.Web.BasePage.TitleCase(Item.Status) %></span>
              <div style="display:flex; gap:2px;">
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="CheckIn" CommandArgument="<%# Item.AppointmentId %>" Visible='<%# Item.Status == "BOOKED" %>' ToolTip="Check in">✓</asp:LinkButton>
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Reschedule" CommandArgument="<%# Item.AppointmentId %>" Visible='<%# Item.Status == "BOOKED" %>' ToolTip="Reschedule" style="color:var(--color-primary);">↻</asp:LinkButton>
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Cancel" CommandArgument="<%# Item.AppointmentId %>" Visible='<%# Item.Status == "BOOKED" || Item.Status == "CHECKED_IN" %>' ToolTip="Cancel" style="color:var(--color-danger);"
                  OnClientClick="return confirm('Cancel this appointment?');">✗</asp:LinkButton>
                <asp:HyperLink runat="server" CssClass="btn btn--xs btn--primary" NavigateUrl="<%# Urls.Consultation(Item.PatientId) %>" Visible="<%# CanConsult(Item) %>" ToolTip="Open consultation"><i class="ti ti-stethoscope"></i></asp:HyperLink>
              </div>
            </div>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="NoAppointments" runat="server"><div class="zone-a-empty" style="padding:20px; text-align:center;">No appointments for this date.</div></asp:PlaceHolder>
      </div>
    </div>
    <div style="width:230px; flex-shrink:0;">
      <div class="card">
        <div class="text-sm text-secondary mb-2" style="font-weight:600;">Available Slots</div>
        <div class="text-xs text-muted mb-2"><asp:Literal ID="ScheduleInfo" runat="server" /></div>
        <asp:Repeater ID="SlotsRepeater" runat="server" ItemType="OpdProject.Models.TimeSlot" OnItemCommand="SlotsRepeater_ItemCommand">
          <ItemTemplate>
            <asp:LinkButton runat="server" CssClass="text-sm" CommandName="QuickBook" CommandArgument="<%# Item.Value %>" CausesValidation="false"
              style="display:block; padding:4px 6px; margin-bottom:2px; border-radius:var(--radius-md); background:var(--color-success-bg); color:inherit; text-decoration:none;"><%#: FmtTime(Item.SlotStart) %> — <%#: FmtTime(Item.SlotEnd) %></asp:LinkButton>
          </ItemTemplate>
        </asp:Repeater>
        <asp:Literal ID="NoSlots" runat="server" />
      </div>
    </div>
  </div>
</div>

<%-- Booking dialog (also used for quick-book of a slot) --%>
<asp:Panel ID="BookModal" runat="server" CssClass="modal-overlay" Visible="false" DefaultButton="ConfirmBookButton">
  <div class="modal-frame" role="dialog" aria-modal="true">
    <div class="modal-header"><strong><asp:Literal ID="BookTitle" runat="server" /></strong>
      <asp:LinkButton runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseModals_Click" CausesValidation="false"><i class="ti ti-x" aria-hidden="true"></i></asp:LinkButton></div>
    <div class="modal-body">
      <asp:ValidationSummary runat="server" ValidationGroup="Book" CssClass="validation-summary" />
      <div class="form-group">
        <label class="form-label" for="<%= PatientSearch.ClientID %>">Patient *</label>
        <div class="patient-suggest">
          <asp:TextBox ID="PatientSearch" runat="server" CssClass="form-control" placeholder="Type 2+ Letters Of Name, MRN Or Phone..." autocomplete="off" data-patient-suggest="true" />
          <asp:HiddenField ID="PatientPick" runat="server" />
        </div>
        <asp:CustomValidator runat="server" ValidationGroup="Book" ErrorMessage="Select a patient" Text="Select a patient from the suggestions" CssClass="field-error" Display="Dynamic"
          ClientValidationFunction="validatePatientPick" OnServerValidate="PatientPick_ServerValidate" />
      </div>
      <asp:PlaceHolder ID="BookDoctorHolder" runat="server">
        <div class="form-group">
          <label class="form-label" for="<%= BookDoctor.ClientID %>">Doctor *</label>
          <asp:DropDownList ID="BookDoctor" runat="server" CssClass="form-select" AutoPostBack="true" OnSelectedIndexChanged="BookOptions_Changed" />
        </div>
      </asp:PlaceHolder>
      <div class="form-group">
        <label class="form-label" for="<%= BookType.ClientID %>">Appointment Type</label>
        <asp:DropDownList ID="BookType" runat="server" CssClass="form-select" AutoPostBack="true" OnSelectedIndexChanged="BookOptions_Changed" />
      </div>
      <asp:PlaceHolder ID="BookSlotHolder" runat="server">
        <div class="form-group">
          <label class="form-label" for="<%= BookSlot.ClientID %>">Time Slot *</label>
          <asp:DropDownList ID="BookSlot" runat="server" CssClass="form-select" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="BookSlot" ValidationGroup="Book" ErrorMessage="No free slot for that doctor and appointment type on this date" Text="No free slot" CssClass="field-error" Display="Dynamic" />
        </div>
      </asp:PlaceHolder>
      <asp:HiddenField ID="QuickSlot" runat="server" />
      <div class="form-group">
        <label class="form-label" for="<%= BookNotes.ClientID %>">Notes</label>
        <asp:TextBox ID="BookNotes" runat="server" CssClass="form-control" placeholder="Optional Booking Notes" MaxLength="200" />
      </div>
      <p class="text-xs text-muted">Weekly timetables and leave are set in Scheduling → Schedule Settings → Doctors.</p>
    </div>
    <div class="modal-footer">
      <asp:LinkButton runat="server" CssClass="btn btn--ghost" OnClick="CloseModals_Click" CausesValidation="false">Cancel</asp:LinkButton>
      <asp:LinkButton ID="ConfirmBookButton" runat="server" CssClass="btn btn--success" ValidationGroup="Book" OnClick="ConfirmBookButton_Click">Book</asp:LinkButton>
    </div>
  </div>
</asp:Panel>

<%-- Reschedule dialog --%>
<asp:Panel ID="RescheduleModal" runat="server" CssClass="modal-overlay" Visible="false" DefaultButton="ConfirmRescheduleButton">
  <div class="modal-frame" role="dialog" aria-modal="true">
    <div class="modal-header"><strong>Reschedule Appointment</strong>
      <asp:LinkButton runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseModals_Click" CausesValidation="false"><i class="ti ti-x" aria-hidden="true"></i></asp:LinkButton></div>
    <div class="modal-body">
      <asp:HiddenField ID="RescheduleId" runat="server" />
      <div class="hint-banner mb-3"><i class="ti ti-calendar-event" aria-hidden="true"></i> Currently: <strong><asp:Literal ID="RescheduleCurrent" runat="server" /></strong></div>
      <div class="form-group">
        <label class="form-label" for="<%= RescheduleDate.ClientID %>">New Date *</label>
        <asp:TextBox ID="RescheduleDate" runat="server" CssClass="form-control" TextMode="Date" AutoPostBack="true" OnTextChanged="RescheduleDate_Changed" />
        <asp:RequiredFieldValidator runat="server" ControlToValidate="RescheduleDate" ValidationGroup="Reschedule" Text="Choose a date" CssClass="field-error" Display="Dynamic" />
      </div>
      <div class="form-group">
        <label class="form-label" for="<%= RescheduleSlot.ClientID %>">New Time *</label>
        <asp:DropDownList ID="RescheduleSlot" runat="server" CssClass="form-select" />
        <asp:RequiredFieldValidator runat="server" ControlToValidate="RescheduleSlot" ValidationGroup="Reschedule" Text="No free time on this date — choose another date" CssClass="field-error" Display="Dynamic" />
      </div>
      <p class="text-xs text-muted">Free slots with the same doctor and of the same length are shown. Changing the date reloads the times.</p>
    </div>
    <div class="modal-footer">
      <asp:LinkButton runat="server" CssClass="btn btn--ghost" OnClick="CloseModals_Click" CausesValidation="false">Cancel</asp:LinkButton>
      <asp:LinkButton ID="ConfirmRescheduleButton" runat="server" CssClass="btn btn--primary" ValidationGroup="Reschedule" OnClick="ConfirmRescheduleButton_Click">Reschedule</asp:LinkButton>
    </div>
  </div>
</asp:Panel>

<%-- Recurring series dialog --%>
<asp:Panel ID="RecurringModal" runat="server" CssClass="modal-overlay" Visible="false" DefaultButton="ConfirmRecurringButton">
  <div class="modal-frame" role="dialog" aria-modal="true">
    <div class="modal-header"><strong>Book Recurring Appointments</strong>
      <asp:LinkButton runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseModals_Click" CausesValidation="false"><i class="ti ti-x" aria-hidden="true"></i></asp:LinkButton></div>
    <div class="modal-body">
      <div class="form-group">
        <label class="form-label" for="<%= RecurPatientSearch.ClientID %>">Patient *</label>
        <div class="patient-suggest">
          <asp:TextBox ID="RecurPatientSearch" runat="server" CssClass="form-control" placeholder="Type 2+ Letters Of Name, MRN Or Phone..." autocomplete="off" data-patient-suggest="true" />
          <asp:HiddenField ID="RecurPatientPick" runat="server" />
        </div>
        <asp:CustomValidator runat="server" ValidationGroup="Recurring" Text="Select a patient from the suggestions" CssClass="field-error" Display="Dynamic"
          ClientValidationFunction="validatePatientPick" OnServerValidate="PatientPick_ServerValidate" />
      </div>
      <div class="form-grid">
        <div class="form-group"><label class="form-label" for="<%= RecurStart.ClientID %>">Start Date</label><asp:TextBox ID="RecurStart" runat="server" CssClass="form-control" TextMode="Date" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="RecurStart" ValidationGroup="Recurring" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= RecurFreq.ClientID %>">Frequency</label>
          <asp:DropDownList ID="RecurFreq" runat="server" CssClass="form-select"><asp:ListItem Value="7">Weekly</asp:ListItem><asp:ListItem Value="14">Bi-Weekly</asp:ListItem><asp:ListItem Value="30">Monthly</asp:ListItem></asp:DropDownList></div>
        <div class="form-group"><label class="form-label" for="<%= RecurCount.ClientID %>">Number Of Appointments</label><asp:TextBox ID="RecurCount" runat="server" CssClass="form-control" TextMode="Number" Text="4" />
          <asp:RangeValidator runat="server" ControlToValidate="RecurCount" ValidationGroup="Recurring" Type="Integer" MinimumValue="2" MaximumValue="12" Text="2–12" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= RecurType.ClientID %>">Type</label><asp:DropDownList ID="RecurType" runat="server" CssClass="form-select" /></div>
      </div>
    </div>
    <div class="modal-footer">
      <asp:LinkButton runat="server" CssClass="btn btn--ghost" OnClick="CloseModals_Click" CausesValidation="false">Cancel</asp:LinkButton>
      <asp:LinkButton ID="ConfirmRecurringButton" runat="server" CssClass="btn btn--primary" ValidationGroup="Recurring" OnClick="ConfirmRecurringButton_Click">Create Series</asp:LinkButton>
    </div>
  </div>
</asp:Panel>
</asp:Content>
<asp:Content ContentPlaceHolderID="ScriptContent" runat="server">
  <script>
    /* Patient typeahead: suggestions appear after 2 characters (name, MRN or phone); choosing one fills the
       hidden patient id. Arrow keys move, Enter picks, Esc closes. Editing the text clears the choice. */
    function validatePatientPick(source, args) {
      var hidden = source.parentNode.querySelector('.patient-suggest input[type=hidden]');
      args.IsValid = !!(hidden && hidden.value);
    }
    (function () {
      var MIN_CHARS = 2;
      function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return '&#' + c.charCodeAt(0) + ';'; }); }
      function mark(text, term) {
        var i = String(text).toLowerCase().indexOf(term.toLowerCase());
        if (i < 0 || !term) return esc(text);
        return esc(text.slice(0, i)) + '<mark>' + esc(text.slice(i, i + term.length)) + '</mark>' + esc(text.slice(i + term.length));
      }

      document.querySelectorAll('input[data-patient-suggest]').forEach(function (input) {
        var wrap = input.parentNode;
        var hidden = wrap.querySelector('input[type=hidden]');
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
        function choose(i) {
          var p = items[i];
          if (!p) return;
          hidden.value = p.id;
          input.value = p.name + ' (' + p.mrn + ')';
          input.classList.add('is-picked');
          close();
        }
        function render(term) {
          if (items.length === 0) {
            list.innerHTML = '<div class="patient-suggest-empty">No matching patients</div>';
          } else {
            list.innerHTML = items.map(function (p, i) {
              return '<div class="patient-suggest-item" role="option" data-i="' + i + '">' +
                '<div class="patient-suggest-name">' + mark(p.name, term) + '</div>' +
                '<div class="patient-suggest-meta">' + mark(p.mrn, term) + (p.phone ? ' · ' + mark(p.phone, term) : '') +
                (p.info ? ' · ' + esc(p.info) : '') + '</div></div>';
            }).join('');
          }
          list.hidden = false;
          if (items.length === 1) highlight(0); else active = -1;
        }
        function search() {
          var term = input.value.trim();
          if (term.length < MIN_CHARS) { close(); return; }
          var mine = ++seq;
          list.hidden = false;
          list.innerHTML = '<div class="patient-suggest-empty">Searching…</div>';
          fetch(window.location.pathname + '?patientSuggest=' + encodeURIComponent(term), { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
            .then(function (data) { if (mine !== seq) return; items = data || []; render(term); })
            .catch(function () { if (mine !== seq) return; items = []; list.innerHTML = '<div class="patient-suggest-empty">Search failed — try again</div>'; });
        }

        input.addEventListener('input', function () {
          hidden.value = '';
          input.classList.remove('is-picked');
          clearTimeout(timer);
          timer = setTimeout(search, 200);
        });
        input.addEventListener('focus', function () { if (!hidden.value && input.value.trim().length >= MIN_CHARS) search(); });
        input.addEventListener('keydown', function (e) {
          if (list.hidden) return;
          if (e.key === 'ArrowDown') { e.preventDefault(); if (items.length) highlight((active + 1) % items.length); }
          else if (e.key === 'ArrowUp') { e.preventDefault(); if (items.length) highlight((active - 1 + items.length) % items.length); }
          else if (e.key === 'Enter') { e.preventDefault(); e.stopPropagation(); choose(active >= 0 ? active : 0); }
          else if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close(); }
        });
        /* mousedown (not click) so the choice lands before the input loses focus */
        list.addEventListener('mousedown', function (e) {
          var row = e.target.closest('.patient-suggest-item');
          e.preventDefault();
          if (row) choose(+row.getAttribute('data-i'));
        });
        input.addEventListener('blur', function () { setTimeout(close, 100); });
        if (hidden.value) input.classList.add('is-picked');
        if (input.closest('.modal-overlay') && !hidden.value) input.focus();
      });
    })();
  </script>
</asp:Content>
