<%@ Page Title="Consultation" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="consultation-360.aspx.cs" Inherits="OpdProject.templates.consultation.consultation_360" %>
<%@ Register TagPrefix="uc" TagName="ErrorState" Src="~/templates/shared/error-state.ascx" %>
<%@ Register TagPrefix="uc" TagName="Tier0" Src="~/templates/consultation/tier0-safety-strip.ascx" %>
<%@ Register TagPrefix="uc" TagName="Tier1" Src="~/templates/consultation/tier1-context-panel.ascx" %>
<%@ Register TagPrefix="uc" TagName="Vitals" Src="~/templates/consultation/consultation-vitals.ascx" %>
<%@ Register TagPrefix="uc" TagName="Symptoms" Src="~/templates/consultation/consultation-symptoms.ascx" %>
<%@ Register TagPrefix="uc" TagName="Diagnosis" Src="~/templates/consultation/consultation-diagnosis-card.ascx" %>
<%@ Register TagPrefix="uc" TagName="Soap" Src="~/templates/consultation/consultation-soap.ascx" %>
<%@ Register TagPrefix="uc" TagName="Labs" Src="~/templates/consultation/consultation-labs.ascx" %>
<%@ Register TagPrefix="uc" TagName="Cpoe" Src="~/templates/consultation/consultation-cpoe-card.ascx" %>
<%@ Register TagPrefix="uc" TagName="Referral" Src="~/templates/consultation/consultation-referral.ascx" %>
<%@ Register TagPrefix="uc" TagName="Followup" Src="~/templates/consultation/consultation-followup.ascx" %>
<%@ Register TagPrefix="uc" TagName="Notes" Src="~/templates/consultation/consultation-confidential-notes.ascx" %>
<%@ Register TagPrefix="c360" Namespace="OpdProject.Web" Assembly="OpdProject" %>
<%-- Consultation 360 — topbar (Rx / sign-off), today's worklist, patient bar, then the workspace:
     section stepper · module cards (all open, the stepper scrolls to them) · Clinical Intelligence panel
     (safety alerts, contradictions, branched-flow suggestions). Which cards, fields and options show, and the
     card order, come from the published consultation configuration (/settings/consultation).
     Route: /consultation/patient/{patientId} --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
  <asp:PlaceHolder ID="NotFound" runat="server" Visible="false"><uc:ErrorState ID="NotFoundMessage" runat="server" /></asp:PlaceHolder>
  <asp:PlaceHolder ID="Workspace" runat="server">
  <div class="c360 titlecase-headings" id="c360" data-scroll-to="<%: ScrollTarget %>">
    <div class="c360-rainbow"></div>

    <%-- Topbar --%>
    <div class="c360-topbar">
      <a class="c360-tb-btn" href="<%: OpdProject.Web.Urls.TodayPatients %>" data-action="go-back" title="Back"><i class="ti ti-arrow-left" aria-hidden="true"></i> Back</a>
      <span class="c360-tb-title">Consultation</span>
      <span class="c360-tb-btn c360-tb-btn--static">Acting as: <strong><%: ActingAs %></strong></span>
      <%= VisitBadgeHtml %>
      <div class="c360-tb-spacer"></div>
      
      <asp:PlaceHolder ID="SignOffHolder" runat="server">
        <asp:LinkButton ID="OpenSignOffButton" runat="server" CssClass="c360-tb-btn c360-tb-btn--teal" OnClick="OpenSignOffButton_Click" CausesValidation="false">
          <i class="ti ti-signature" aria-hidden="true"></i> Sign &amp; Complete
        </asp:LinkButton>
      </asp:PlaceHolder>
      <asp:PlaceHolder ID="LockedBadgeHolder" runat="server" Visible="false">
        <a class="c360-badge c360-badge--routine" href="#card-signoff" data-step="signoff"><i class="ti ti-lock" aria-hidden="true"></i> Signed &amp; Locked</a>
      </asp:PlaceHolder>
    </div>

    <div class="c360-app">
      <%-- Today's patients --%>
      <aside class="c360-worklist" id="c360Worklist" aria-label="Today's patients">
        <div class="c360-wl-head">
          <span class="c360-wl-heading">Today's Patients <small><asp:Literal ID="WorklistCount" runat="server" /></small></span>
          <button class="c360-tb-btn c360-wl-collapse" id="c360WlToggle" type="button" title="Hide patients sidebar" aria-label="Hide patients sidebar" aria-expanded="true">⟨</button>
        </div>
        <div class="c360-wl-scroll">
          <asp:Repeater ID="WorklistRepeater" runat="server" ItemType="OpdProject.Web.WorklistEntry">
            <ItemTemplate>
              <a class="c360-wl-card<%# Item.IsActive ? " active" : "" %>" href="<%#: Item.Url %>">
                <span class="c360-wl-card-name"><%#: Item.Name %></span>
                <span class="c360-wl-card-meta"><span><%#: Item.Mrn %> · <%#: Item.AgeSex %></span><span class="c360-wl-time"><%#: Item.Time %></span></span>
                <span class="c360-wl-status c360-wl-status--<%#: Item.StatusTone %>"><%#: Item.StatusLabel %></span>
              </a>
            </ItemTemplate>
          </asp:Repeater>
          <asp:PlaceHolder ID="WorklistEmpty" runat="server" Visible="false"><div class="c360-wl-empty">No appointments today.</div></asp:PlaceHolder>
        </div>
        <div class="c360-wl-rail" aria-label="Patient shortcuts">
          <asp:Repeater ID="WorklistRailRepeater" runat="server" ItemType="OpdProject.Web.WorklistEntry">
            <ItemTemplate><a class="c360-wl-rail-item<%# Item.IsActive ? " active" : "" %>" href="<%#: Item.Url %>" title="<%#: Item.Name %> · <%#: Item.Time %>" aria-label="Open <%#: Item.Name %>"><%#: Item.Initials %></a></ItemTemplate>
          </asp:Repeater>
        </div>
      </aside>

      <div class="c360-consult">
        <%-- Patient bar --%>
        <div class="c360-pbar">
          <div class="c360-pbar-row">
            <span>Name: <strong class="c360-pbar-name"><%: Patient.FullName %></strong></span>
            <span class="c360-pbar-sep">|</span>
            <span><%: PatientIdLabel %>: <strong class="c360-pbar-id"><%: Patient.Mrn %></strong></span>
            <span class="c360-pbar-sep">·</span>
            <span>Age/Sex: <strong><%: AgeSex %></strong></span>
            <span class="c360-pbar-sep">·</span>
            <span>Visit: <strong>Outpatient</strong></span>
          </div>
          <div class="c360-pbar-row c360-pbar-row--r2">
            <span>Attending Physician: <strong><%: ActingAs %></strong> <%: PhysicianSub %></span>
            <span>DOB: <%: FmtDate(Patient.Dob) %> · <%: HealthIdLabel %>: <%: string.IsNullOrEmpty(Patient.NationalHealthId) ? "Not recorded" : Patient.NationalHealthId %> · ☎ <%: Patient.Phone %></span>
          </div>
        </div>

        <div class="c360-workspace">
          <%-- Section stepper --%>
          <nav class="c360-stepper" id="c360Stepper" aria-label="Consultation sections">
            <asp:Repeater ID="StepsRepeater" runat="server" ItemType="OpdProject.Web.WorkflowStep">
              <ItemTemplate>
                <a href="#card-<%#: Item.Key %>" class="c360-step c360-step--<%#: Item.State %>" data-step="<%#: Item.Key %>" title="<%#: Item.Label %>">
                  <span class="c360-step-dot" aria-hidden="true"></span>
                  <span class="c360-step-label"><%#: Item.Label %></span>
                  <%# Item.Mandatory ? "<span class=\"c360-step-req\" title=\"Required\">*</span>" : "" %>
                </a>
              </ItemTemplate>
            </asp:Repeater>
          </nav>

          <%-- Module cards --%>
          <div class="c360-input-col" id="c360InputCol">
            <asp:HiddenField ID="ActiveStepField" runat="server" Value="vitals" />
            <input type="hidden" id="c360SaveCustom" name="c360SaveCustom" value="" />
            <%-- Cards render in the configured order (no code blocks inside the list) --%>
            <c360:ConsultCardList ID="CardList" runat="server">
              <c360:ConsultCard Step="context" runat="server"><uc:Tier1 ID="Tier1Section" runat="server" /></c360:ConsultCard>
              <c360:ConsultCard Step="vitals" runat="server"><uc:Vitals ID="VitalsSection" runat="server" /></c360:ConsultCard>
              <c360:ConsultCard Step="complaints" runat="server"><uc:Symptoms ID="SymptomsSection" runat="server" /></c360:ConsultCard>
              <c360:ConsultCard Step="diagnosis" runat="server"><uc:Diagnosis ID="DiagnosisSection" runat="server" /></c360:ConsultCard>
              <c360:ConsultCard Step="soap" runat="server"><uc:Soap ID="SoapSection" runat="server" /></c360:ConsultCard>
              <c360:ConsultCard Step="labs" runat="server"><uc:Labs ID="LabsSection" runat="server" /></c360:ConsultCard>
              <c360:ConsultCard Step="cpoe" runat="server"><uc:Cpoe ID="CpoeSection" runat="server" /></c360:ConsultCard>
              <c360:ConsultCard Step="referral" runat="server"><uc:Referral ID="ReferralSection" runat="server" /></c360:ConsultCard>
              <c360:ConsultCard Step="followup" runat="server"><uc:Followup ID="FollowupSection" runat="server" /></c360:ConsultCard>
              <c360:ConsultCard Step="notes" runat="server"><uc:Notes ID="NotesSection" runat="server" /></c360:ConsultCard>
            </c360:ConsultCardList>

            <%-- Signed / locked state --%>
            <asp:PlaceHolder ID="LockedHolder" runat="server" Visible="false">
              <div class="c360-card c360-card--completed" id="card-signoff" data-step="signoff">
                <div class="c360-card-head"><span class="c360-card-title"><i class="ti ti-lock" aria-hidden="true"></i> Signed &amp; Locked</span><span class="c360-badge c360-badge--routine">Complete</span></div>
                <div class="c360-card-body">
                  <asp:HyperLink ID="BillingLink" runat="server" CssClass="btn btn--primary" style="width:100%; justify-content:center;"><i class="ti ti-file-invoice" aria-hidden="true"></i> Proceed To Billing</asp:HyperLink>
                  <asp:PlaceHolder ID="PrintHolder" runat="server">
                    <div class="c360-print-grid">
                      <a class="btn btn--sm" target="_blank" href="<%: Urls.Print("prescription", ConsultationIdValue) %>"><i class="ti ti-printer" aria-hidden="true"></i> Print Rx</a>
                      <a class="btn btn--sm" target="_blank" href="<%: Urls.Print("summary", ConsultationIdValue) %>"><i class="ti ti-file-text" aria-hidden="true"></i> Print Summary</a>
                      <a class="btn btn--sm" target="_blank" href="<%: Urls.Print("lab-requisition", ConsultationIdValue) %>"><i class="ti ti-microscope" aria-hidden="true"></i> Print Lab Order</a>
                      <a class="btn btn--sm" target="_blank" href="<%: Urls.Print("referral", ConsultationIdValue) %>"><i class="ti ti-mail-forward" aria-hidden="true"></i> Print Referral</a>
                    </div>
                  </asp:PlaceHolder>
                  <asp:Panel ID="AddendumPanel" runat="server" CssClass="c360-addendum" DefaultButton="SaveAddendumButton">
                    <div class="c360-subhead"><i class="ti ti-pencil-plus" aria-hidden="true"></i> Post-Lock Addendum</div>
                    <div class="text-xs text-muted mb-2">Add corrections or supplementary notes. These are append-only and timestamped.</div>
                    <asp:Repeater ID="AddendaRepeater" runat="server" ItemType="OpdProject.Models.Addendum">
                      <ItemTemplate>
                        <div class="c360-addendum-item">
                          <span class="badge <%# Item.Category == "RX_CHANGE" ? "badge--warning" : Item.Category == "NEW_LAB_DATA" ? "badge--success" : "badge--info" %>" style="font-size:9px;"><%#: AddendumCategoryLabel(Item.Category) %></span>
                          <span class="text-xs text-muted"><%#: FmtDateTime(Item.CreatedAt) %></span>
                          <div style="margin-top:2px;"><%#: Item.AddendumText %></div>
                        </div>
                      </ItemTemplate>
                    </asp:Repeater>
                    <div class="form-group">
                      <label class="form-label" for="<%= AddendumCategory.ClientID %>">Category</label>
                      <asp:DropDownList ID="AddendumCategory" runat="server" CssClass="form-select" style="max-width:260px;">
                        <asp:ListItem Value="CLARIFICATION">Typo / Clarification</asp:ListItem>
                        <asp:ListItem Value="RX_CHANGE">Medication Adjustment</asp:ListItem>
                        <asp:ListItem Value="NEW_LAB_DATA">New Lab Results</asp:ListItem>
                        <asp:ListItem Value="CLINICAL_NOTE">Additional Clinical Note</asp:ListItem>
                      </asp:DropDownList>
                    </div>
                    <asp:TextBox ID="AddendumText" runat="server" CssClass="form-control" TextMode="MultiLine" style="min-height:60px;" placeholder="Type Correction Or Additional Note..." />
                    <asp:RequiredFieldValidator runat="server" ControlToValidate="AddendumText" ValidationGroup="Addendum" Text="Enter addendum text" CssClass="field-error" Display="Dynamic" />
                    <asp:LinkButton ID="SaveAddendumButton" runat="server" CssClass="btn btn--sm btn--warning mt-2" ValidationGroup="Addendum" OnClick="SaveAddendumButton_Click"><i class="ti ti-plus" aria-hidden="true"></i> Add Addendum</asp:LinkButton>
                  </asp:Panel>
                </div>
              </div>
            </asp:PlaceHolder>
          </div>

          <%-- Clinical Intelligence --%>
          <aside class="c360-intel" aria-label="Clinical intelligence">
            <div class="c360-intel-title"><i class="ti ti-brain" aria-hidden="true"></i> Clinical Intelligence</div>
            <uc:Tier0 ID="Tier0Section" runat="server" />

            <div class="c360-intel-sub">Clinical Alerts</div>
            <asp:Repeater ID="IntelAlertsRepeater" runat="server" ItemType="OpdProject.Models.ClinicalAlert">
              <ItemTemplate>
                <div class="c360-alert c360-alert--<%# Item.Severity == "CRITICAL" ? "danger" : "warning" %>">
                  <i class="ti ti-alert-octagon c360-alert-icon" aria-hidden="true"></i>
                  <div><strong><%#: (Item.Type ?? "").Replace("_", " ") %></strong><div class="c360-alert-text"><%#: Item.Message %></div></div>
                </div>
              </ItemTemplate>
            </asp:Repeater>
            <asp:PlaceHolder ID="NoAlertsHolder" runat="server"><div class="c360-intel-empty">🟢 No active clinical alerts for the data entered so far.</div></asp:PlaceHolder>

            <asp:Literal ID="BranchedFlowBanner" runat="server" />

            <div class="c360-intel-sub">Progress</div>
            <div class="c360-progress">
              <div class="c360-progress-label"><asp:Literal ID="ProgressText" runat="server" /></div>
              <div class="c360-progress-track"><div class="c360-progress-fill" style="width:<%: ProgressPercent %>%;"></div></div>
              <asp:PlaceHolder ID="RequiredMissingHolder" runat="server"><div class="c360-progress-note">Required Before Sign-Off: <asp:Literal ID="RequiredMissingText" runat="server" /></div></asp:PlaceHolder>
            </div>
          </aside>
        </div>
      </div>
    </div>

    <%-- Sign-off dialog (server-side modal) --%>
    <asp:Panel ID="SignOffModal" runat="server" CssClass="modal-overlay" Visible="false">
      <div class="modal-frame modal-frame--wide" role="dialog" aria-modal="true" aria-label="Sign and complete consultation">
        <div class="modal-header">
          <strong>✓ Sign &amp; Complete Consultation</strong>
          <asp:LinkButton ID="CloseSignOffButton" runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseSignOffButton_Click" CausesValidation="false" ToolTip="Close dialog"><i class="ti ti-x" aria-hidden="true"></i></asp:LinkButton>
        </div>
        <div class="modal-body">
          <div class="c360-sign-summary"><strong>Note Summary Preview:</strong> <asp:Literal ID="SignSummary" runat="server" /></div>
          <asp:PlaceHolder ID="ContradictionsHolder" runat="server">
            <div class="text-xs" style="font-weight:700; color:var(--color-danger); margin-bottom:6px;">⚠ The Contradiction Engine found <asp:Literal ID="ContradictionCount" runat="server" />:</div>
            <asp:Repeater ID="ContradictionsRepeater" runat="server" ItemType="OpdProject.Models.ClinicalAlert">
              <ItemTemplate>
                <div class="c360-alert c360-alert--<%# Item.Severity == "CRITICAL" ? "danger" : "warning" %>">
                  <div><strong><%#: Item.Severity.Replace("_", " ") %></strong> · <em><%#: Item.Type.Replace("_", " ") %></em><div class="c360-alert-text"><%#: Item.Message %></div></div>
                </div>
              </ItemTemplate>
            </asp:Repeater>
            <div class="text-xs text-muted" style="margin:4px 0 12px;">You may still sign off after reviewing. These will be logged in the audit trail.</div>
          </asp:PlaceHolder>
          <asp:PlaceHolder ID="MandatoryHolder" runat="server">
            <div class="c360-alert c360-alert--danger">
              <div><strong>Required Sections Incomplete:</strong> <asp:Literal ID="MandatoryMissing" runat="server" />
                <div class="c360-alert-text">Vitals, complaints, at least one diagnosis and any field marked * are required before sign-off.</div></div>
            </div>
          </asp:PlaceHolder>
          <asp:PlaceHolder ID="PersonalHolder" runat="server">
            <div class="c360-alert c360-alert--warning">
              <div><i class="ti ti-pencil" aria-hidden="true"></i> <strong>Personal Observations Missing</strong> for: <asp:Literal ID="PersonalMissing" runat="server" />
                <div class="c360-alert-text">These "your own words" fields help document what's specific to this patient.</div>
                <asp:CheckBox ID="AttestSkipPersonal" runat="server" Text=" I Choose To Sign Off Without Filling These" CssClass="text-xs" /></div>
            </div>
          </asp:PlaceHolder>
          <p class="text-sm text-secondary mb-3">Confirm you have reviewed each section before signing and locking.</p>
          <div class="form-group"><asp:CheckBox ID="AttestS" runat="server" Text=" Subjective Reviewed" /></div>
          <div class="form-group"><asp:CheckBox ID="AttestO" runat="server" Text=" Objective Reviewed" /></div>
          <div class="form-group"><asp:CheckBox ID="AttestA" runat="server" Text=" Assessment / Diagnosis Reviewed" /></div>
          <div class="form-group"><asp:CheckBox ID="AttestP" runat="server" Text=" Plan / Prescriptions Reviewed" /></div>
          <asp:CustomValidator ID="AttestValidator" runat="server" ValidationGroup="SignOff" OnServerValidate="AttestValidator_ServerValidate" CssClass="field-error" Display="Dynamic" />
        </div>
        <div class="modal-footer">
          <asp:LinkButton ID="CancelSignOffButton" runat="server" CssClass="btn" OnClick="CloseSignOffButton_Click" CausesValidation="false">Cancel</asp:LinkButton>
          <asp:LinkButton ID="ConfirmSignOffButton" runat="server" CssClass="btn c360-btn-teal" ValidationGroup="SignOff" OnClick="ConfirmSignOffButton_Click">Sign &amp; Lock Consultation</asp:LinkButton>
        </div>
      </div>
    </asp:Panel>
  </div>
  </asp:PlaceHolder>
</asp:Content>
<asp:Content ContentPlaceHolderID="ScriptContent" runat="server">
  <script src="<%: ((OpdProject.SiteMaster)Master).Asset("~/js/engines/engine-voice-soap.js") %>"></script>
  <script src="<%: ((OpdProject.SiteMaster)Master).Asset("~/js/components/component-drawing-pad.js") %>"></script>
  <script>
    /* Workspace behaviour: the stepper scrolls to a card and tracks the card in view; the card column's
       scroll position survives postbacks; the patients sidebar collapses to an initials rail. */
    (function () {
      var root = document.getElementById('c360');
      var col = document.getElementById('c360InputCol');
      if (!root || !col) return;
      var field = document.getElementById('<%= ActiveStepField.ClientID %>');
      var scrollKey = 'c360-scroll:' + location.pathname;
      var steps = Array.prototype.slice.call(document.querySelectorAll('.c360-step[data-step]'));
      var cards = Array.prototype.slice.call(col.querySelectorAll('.c360-card[data-step]'));

      function markStep(key) {
        steps.forEach(function (s) { s.classList.toggle('c360-step--current', s.getAttribute('data-step') === key); });
      }
      function scrollToCard(key, smooth) {
        var card = document.getElementById('card-' + key);
        if (!card) return;
        var behavior = smooth ? 'smooth' : 'auto';
        if (col.scrollHeight > col.clientHeight) col.scrollTo({ top: card.offsetTop - 8, behavior: behavior });
        else card.scrollIntoView({ behavior: behavior, block: 'start' });
        markStep(key);
        if (field) field.value = key;
      }
      function spy() {
        var top = col.getBoundingClientRect().top + 40, current = cards.length ? cards[0].getAttribute('data-step') : null;
        cards.forEach(function (c) { if (c.getBoundingClientRect().top <= top) current = c.getAttribute('data-step'); });
        if (col.scrollTop + col.clientHeight >= col.scrollHeight - 2 && cards.length) current = cards[cards.length - 1].getAttribute('data-step');
        markStep(current);
      }

      /* Custom fields: their Save button (or Enter in one of their inputs) posts the section key */
      function saveCustom(key) {
        document.getElementById('c360SaveCustom').value = key;
        __doPostBack('c360SaveCustom', '');
      }
      document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-custom-save]');
        if (!btn) return;
        e.preventDefault();
        saveCustom(btn.getAttribute('data-custom-save'));
      });
      document.addEventListener('keydown', function (e) {
        if (e.key !== 'Enter' || !e.target.matches || !e.target.matches('.c360-custom input')) return;
        e.preventDefault();
        saveCustom(e.target.closest('.c360-custom').getAttribute('data-custom-section'));
      });

      document.addEventListener('click', function (e) {
        var link = e.target.closest('a[data-step]');
        if (!link || !root.contains(link)) return;
        e.preventDefault();
        scrollToCard(link.getAttribute('data-step'), true);
      });
      col.addEventListener('scroll', spy, { passive: true });

      /* Keep the card column where it was across postbacks; after a save that moves the doctor on to
         another section (server called ActivateStep), bring that card into view if it isn't already. */
      window.addEventListener('pagehide', function () { try { sessionStorage.setItem(scrollKey, String(col.scrollTop)); } catch (x) { } });
      var saved = null;
      try { saved = sessionStorage.getItem(scrollKey); } catch (x) { }
      if (saved !== null) col.scrollTop = parseInt(saved, 10) || 0;
      var target = root.getAttribute('data-scroll-to');
      var targetCard = target && document.getElementById('card-' + target);
      if (targetCard) {
        var r = targetCard.getBoundingClientRect(), c = col.getBoundingClientRect();
        if (r.bottom < c.top + 60 || r.top > c.bottom - 60) scrollToCard(target, false);
      }
      spy();

      /* Patients sidebar collapse (remembered per browser) */
      var wl = document.getElementById('c360Worklist'), btn = document.getElementById('c360WlToggle');
      function setCollapsed(collapsed) {
        wl.classList.toggle('collapsed', collapsed);
        btn.textContent = collapsed ? '⟩' : '⟨';
        btn.title = collapsed ? 'Show patients sidebar' : 'Hide patients sidebar';
        btn.setAttribute('aria-label', btn.title);
        btn.setAttribute('aria-expanded', String(!collapsed));
      }
      /* No saved choice: start collapsed on smaller screens so the cards keep a usable width */
      var pref = null;
      try { pref = localStorage.getItem('c360-worklist-collapsed'); } catch (x) { }
      if (pref === '1' || (pref === null && window.innerWidth < 1440 && window.innerWidth > 900)) setCollapsed(true);
      btn.addEventListener('click', function () {
        var collapsed = !wl.classList.contains('collapsed');
        setCollapsed(collapsed);
        try { localStorage.setItem('c360-worklist-collapsed', collapsed ? '1' : '0'); } catch (x) { }
      });
    })();
  </script>
</asp:Content>
