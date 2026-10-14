<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="consultation-soap.ascx.cs" Inherits="OpdProject.templates.consultation.consultation_soap" %>
<%-- Observations / SOAP. Phrase chips insert text in the browser; "/trigger" words are expanded on the
     server when the note is saved; Auto-Generate drafts the note from the structured visit data. --%>
<div class="consult-section" id="sectionSoap" data-section="soap">
  <div class="consult-section-body">
    <div class="hint-banner" id="soapHintBanner">
      <i class="ti ti-info-circle" aria-hidden="true"></i>
      <span>Tap blue phrase chips to insert common findings quickly. Type a <strong>/trigger</strong> (e.g. <code>/fever</code>, <code>/cvs</code>) anywhere in a note — it is expanded to the phrase when you save. The <em>"Your observation"</em> field below each section is for your own words specific to this patient — <strong>required before sign-off</strong>.</span>
      <span class="zone-a-more" onclick="this.parentNode.style.display='none';" style="margin-left:8px; white-space:nowrap; cursor:pointer;">✕ Hide</span>
    </div>

    <asp:PlaceHolder ID="VoiceHolder" runat="server">
      <div style="display:flex; gap:8px; margin-bottom:8px;">
        <button class="btn btn--sm" id="voiceDictateBtn" type="button" style="background:var(--surface-page);" onclick="Consult360.toggleVoice();">
          <i class="ti ti-microphone" aria-hidden="true"></i> <span id="voiceBtnLabel">Start Dictation</span>
        </button>
        <span id="voiceStatus" class="text-xs text-muted" style="align-self:center;"></span>
      </div>
    </asp:PlaceHolder>

    <asp:PlaceHolder ID="SubjectiveHolder" runat="server">
    <div class="form-group mb-3">
      <label class="form-label" for="soapSubjective">Subjective <span class="text-muted">(Patient's Own Words)</span></label>
      <asp:PlaceHolder ID="ChipsSHolder" runat="server">
      <div class="text-xs text-muted mb-1"><i class="ti ti-sparkles" aria-hidden="true" style="color:var(--color-primary);"></i> Quick Phrases — Tap To Insert:</div>
      <div class="phrase-chip-row"><asp:Literal ID="ChipsS" runat="server" /></div>
      </asp:PlaceHolder>
      <asp:TextBox ID="soapSubjective" runat="server" ClientIDMode="Static" CssClass="form-control" TextMode="MultiLine" style="min-height:50px;" placeholder="What is the patient telling you? Duration, severity, what makes it better/worse... (phrase chips insert here, or type /trigger)" />
      <asp:PlaceHolder ID="SubjectivePersonalHolder" runat="server">
      <div class="text-xs mt-1" style="color:var(--color-warning); font-weight:600;"><i class="ti ti-pencil" aria-hidden="true"></i> Free Text — Your Own Words (Required):</div>
      <asp:TextBox ID="SubjectivePersonal" runat="server" CssClass="form-control mt-1" placeholder="What's different about this patient's complaint?" />
      </asp:PlaceHolder>
    </div>
    </asp:PlaceHolder>

    <asp:PlaceHolder ID="ObjectiveHolder" runat="server">
    <div class="form-group mb-3">
      <label class="form-label" for="soapObjective">Objective <span class="text-muted">(Examination Findings)</span></label>
      <asp:PlaceHolder ID="ChipsOHolder" runat="server">
      <div class="text-xs text-muted mb-1"><i class="ti ti-sparkles" aria-hidden="true" style="color:var(--color-primary);"></i> Quick Phrases — Tap To Insert:</div>
      <div class="phrase-chip-row"><asp:Literal ID="ChipsO" runat="server" /></div>
      </asp:PlaceHolder>
      <asp:TextBox ID="soapObjective" runat="server" ClientIDMode="Static" CssClass="form-control" TextMode="MultiLine" style="min-height:50px;" placeholder="Examination findings, relevant negatives... (phrase chips insert here, or type /trigger)" />
      <asp:PlaceHolder ID="ObjectivePersonalHolder" runat="server">
      <div class="text-xs mt-1" style="color:var(--color-warning); font-weight:600;"><i class="ti ti-pencil" aria-hidden="true"></i> Free Text — Your Own Words (Required):</div>
      <asp:TextBox ID="ObjectivePersonal" runat="server" CssClass="form-control mt-1" placeholder="What stands out on examination for this patient?" />
      </asp:PlaceHolder>
    </div>
    </asp:PlaceHolder>

    <asp:PlaceHolder ID="PlanHolder" runat="server">
    <div class="form-group mb-3">
      <label class="form-label" for="soapPlan">Plan</label>
      <asp:PlaceHolder ID="ChipsPHolder" runat="server">
      <div class="text-xs text-muted mb-1"><i class="ti ti-sparkles" aria-hidden="true" style="color:var(--color-primary);"></i> Quick Phrases — Tap To Insert:</div>
      <div class="phrase-chip-row"><asp:Literal ID="ChipsP" runat="server" /></div>
      </asp:PlaceHolder>
      <asp:TextBox ID="soapPlan" runat="server" ClientIDMode="Static" CssClass="form-control" TextMode="MultiLine" style="min-height:50px;" placeholder="Treatment plan, lifestyle advice, follow-up timeline... (phrase chips insert here, or type /trigger)" />
      <asp:PlaceHolder ID="PlanPersonalHolder" runat="server">
      <div class="text-xs mt-1" style="color:var(--color-warning); font-weight:600;"><i class="ti ti-pencil" aria-hidden="true"></i> Free Text — Your Own Words (Required):</div>
      <asp:TextBox ID="PlanPersonal" runat="server" CssClass="form-control mt-1" placeholder="Why this treatment for this patient?" />
      </asp:PlaceHolder>
    </div>
    </asp:PlaceHolder>

    <asp:PlaceHolder ID="ActionsHolder" runat="server">
      <div style="display:flex; gap:8px; flex-wrap:wrap;">
        <asp:LinkButton ID="SaveSoapButton" runat="server" CssClass="btn btn--sm btn--primary" OnClick="SaveSoapButton_Click">
          <i class="ti ti-device-floppy" aria-hidden="true"></i> Save Observations
        </asp:LinkButton>
        <asp:LinkButton ID="AutoGenerateButton" runat="server" CssClass="btn btn--sm" style="background:var(--color-warning-bg); color:var(--color-warning);" OnClick="AutoGenerateButton_Click">
          <i class="ti ti-sparkles" aria-hidden="true"></i> Auto-Generate SOAP
        </asp:LinkButton>
        <asp:LinkButton ID="ShowSavePhraseButton" runat="server" CssClass="btn btn--sm btn--ghost" OnClick="ShowSavePhraseButton_Click">
          <i class="ti ti-bookmark" aria-hidden="true"></i> Save A Phrase
        </asp:LinkButton>
      </div>
    </asp:PlaceHolder>

    <%-- Auto-generated draft preview: doctor edits, then accepts into the note --%>
    <asp:Panel ID="DraftPanel" runat="server" CssClass="card mt-3" Visible="false" style="border-left:4px solid var(--color-warning);">
      <div class="text-sm" style="font-weight:600; margin-bottom:8px;"><i class="ti ti-sparkles" aria-hidden="true"></i> Auto-Generated SOAP Note (Preview)</div>
      <div class="form-group"><label class="form-label" for="<%= DraftS.ClientID %>">Subjective</label><asp:TextBox ID="DraftS" runat="server" CssClass="form-control" TextMode="MultiLine" style="min-height:60px;" /></div>
      <div class="form-group"><label class="form-label" for="<%= DraftO.ClientID %>">Objective</label><asp:TextBox ID="DraftO" runat="server" CssClass="form-control" TextMode="MultiLine" style="min-height:60px;" /></div>
      <div class="form-group"><label class="form-label" for="<%= DraftA.ClientID %>">Assessment</label><asp:TextBox ID="DraftA" runat="server" CssClass="form-control" TextMode="MultiLine" style="min-height:60px;" /></div>
      <div class="form-group"><label class="form-label" for="<%= DraftP.ClientID %>">Plan</label><asp:TextBox ID="DraftP" runat="server" CssClass="form-control" TextMode="MultiLine" style="min-height:80px;" /></div>
      <p class="text-xs text-muted">Review and edit before accepting. This is a draft — you have full control. The assessment is added to the Objective note.</p>
      <div style="display:flex; gap:8px;">
        <asp:LinkButton ID="AcceptDraftButton" runat="server" CssClass="btn btn--sm btn--success" OnClick="AcceptDraftButton_Click">Accept &amp; Fill</asp:LinkButton>
        <asp:LinkButton ID="CancelDraftButton" runat="server" CssClass="btn btn--sm btn--ghost" OnClick="CancelDraftButton_Click">Cancel</asp:LinkButton>
      </div>
    </asp:Panel>

    <%-- Save a personal phrase (doctor scope) --%>
    <asp:Panel ID="SavePhrasePanel" runat="server" CssClass="card mt-3" Visible="false" DefaultButton="SavePhraseButton">
      <div class="text-sm" style="font-weight:600; margin-bottom:8px;"><i class="ti ti-bookmark" aria-hidden="true"></i> Save To My Phrase Library</div>
      <div class="form-grid">
        <div class="form-group">
          <label class="form-label" for="<%= PhraseSection.ClientID %>">Section</label>
          <asp:DropDownList ID="PhraseSection" runat="server" CssClass="form-select">
            <asp:ListItem Value="S">Subjective</asp:ListItem><asp:ListItem Value="O">Objective</asp:ListItem>
            <asp:ListItem Value="P">Plan</asp:ListItem><asp:ListItem Value="ANY">Any Section</asp:ListItem>
          </asp:DropDownList>
        </div>
        <div class="form-group">
          <label class="form-label" for="<%= PhraseTrigger.ClientID %>">Trigger (Starts With /)</label>
          <asp:TextBox ID="PhraseTrigger" runat="server" CssClass="form-control" placeholder="/keyword" MaxLength="20" />
          <asp:RegularExpressionValidator runat="server" ControlToValidate="PhraseTrigger" ValidationGroup="Phrase" ValidationExpression="^/?[A-Za-z0-9_]{2,19}$" Text="Letters/digits only, 2-19 characters" CssClass="field-error" Display="Dynamic" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="PhraseTrigger" ValidationGroup="Phrase" Text="Required" CssClass="field-error" Display="Dynamic" />
        </div>
        <div class="form-group form-group--full">
          <label class="form-label" for="<%= PhraseText.ClientID %>">Phrase Text</label>
          <asp:TextBox ID="PhraseText" runat="server" CssClass="form-control" TextMode="MultiLine" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="PhraseText" ValidationGroup="Phrase" Text="Required" CssClass="field-error" Display="Dynamic" />
        </div>
      </div>
      <div style="display:flex; gap:8px;">
        <asp:LinkButton ID="SavePhraseButton" runat="server" CssClass="btn btn--sm btn--primary" ValidationGroup="Phrase" OnClick="SavePhraseButton_Click">Save Phrase</asp:LinkButton>
        <asp:LinkButton ID="CancelPhraseButton" runat="server" CssClass="btn btn--sm btn--ghost" CausesValidation="false" OnClick="CancelPhraseButton_Click">Cancel</asp:LinkButton>
      </div>
    </asp:Panel>

    <asp:PlaceHolder ID="SketchesHolder" runat="server">
    <div style="margin-top:16px; border-top:1px solid var(--border-default); padding-top:12px;">
      <div style="display:flex; align-items:center; gap:8px; margin-bottom:8px;">
        <i class="ti ti-writing" aria-hidden="true" style="color:var(--color-primary);"></i>
        <strong class="text-sm">Sketches &amp; Handwritten Notes</strong>
        <asp:PlaceHolder ID="PadToggleHolder" runat="server">
          <button class="btn btn--xs btn--ghost" type="button" onclick="Consult360.toggleDrawingPad();"><i class="ti ti-pencil-plus" aria-hidden="true"></i> Open Drawing Pad</button>
        </asp:PlaceHolder>
      </div>
      <div id="drawingPadContainer"></div>
      <input type="hidden" id="sketchData" name="sketchData" />
      <input type="hidden" id="sketchTitle" name="sketchTitle" />
      <asp:LinkButton ID="SaveSketchButton" runat="server" OnClick="SaveSketchButton_Click" style="display:none;" CausesValidation="false">Save Sketch</asp:LinkButton>
      <div style="display:flex; flex-wrap:wrap; gap:8px; margin-top:8px;">
        <asp:Repeater ID="SketchesRepeater" runat="server" ItemType="OpdProject.Models.ConsultationSketch">
          <ItemTemplate>
            <div style="border:1px solid <%# Item.ConsultationId == ConsultationId ? "var(--color-primary)" : "var(--border-default)" %>; border-radius:6px; padding:6px; max-width:160px;">
              <img src="<%#: Item.ImageData %>" style="width:140px; height:80px; object-fit:contain; border-radius:4px; display:block;" alt="<%#: Item.Title %>" />
              <div class="text-xs" style="font-weight:600; margin-top:4px;"><%#: Item.Title %></div>
              <div class="text-xs text-muted"><%#: AppPage.FmtDate(Item.CreatedAt) %><%#: Item.ConsultationId == ConsultationId ? " · this visit" : "" %></div>
            </div>
          </ItemTemplate>
        </asp:Repeater>
      </div>
    </div>
    </asp:PlaceHolder>
  </div>
</div>
<script>
  window.Consult360 = window.Consult360 || {};
  Consult360.toggleVoice = function () {
    if (!window.VoiceSOAP || !window.VoiceSOAP.isSupported()) { alert('Voice dictation needs the Web Speech API — use Chrome or Edge.'); return; }
    if (!Consult360._voiceReady) {
      window.VoiceSOAP.init({ lang: 'en-IN', onStatusChange: function (s) {
        var btn = document.getElementById('voiceDictateBtn'), label = document.getElementById('voiceBtnLabel'), st = document.getElementById('voiceStatus');
        btn.style.background = s.isListening ? 'var(--color-danger)' : 'var(--surface-page)'; btn.style.color = s.isListening ? '#fff' : '';
        label.textContent = s.isListening ? 'Stop dictation' : 'Start dictation';
        st.textContent = s.isListening ? '🔴 Listening... Say "Subjective", "Objective", or "Plan" to switch sections' : (s.error ? 'Voice error: ' + s.error : '');
      } });
      Consult360._voiceReady = true;
    }
    window.VoiceSOAP.toggle('soapSubjective');
  };
  Consult360.toggleDrawingPad = function () {
    var c = document.getElementById('drawingPadContainer');
    if (Consult360._pad) { Consult360._pad.destroy(); Consult360._pad = null; return; }
    /* Fit the card: up to 560px wide, never wider than the Observations column */
    var w = Math.max(240, Math.min(560, c.clientWidth - 2));
    Consult360._pad = window.DrawingPad.create({ container: c, width: w, height: 250, onSave: function (d) {
      document.getElementById('sketchData').value = d.jpeg;
      document.getElementById('sketchTitle').value = d.title || '';
      <%= Page.ClientScript.GetPostBackEventReference(SaveSketchButton, "") %>;
    } });
  };
</script>
