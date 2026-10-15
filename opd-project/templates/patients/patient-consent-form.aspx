<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="patient-consent-form.aspx.cs" Inherits="OpdProject.templates.patients.patient_consent_form" %>
<%-- Printable "Patient Consent for Sharing Health Information" (Jamaica, Data Protection Act 2020), pre-filled with the
     hospital, the patient's preferred physician and the patient's registration details. Printed from the coverage page,
     signed on paper, then scanned and uploaded back there. Route: /patients/{patientId}/consent-form --%>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
  <meta charset="UTF-8" />
  <title>Consent Form</title>
  <link rel="stylesheet" href="<%: ResolveUrl("~/assets/icons/tabler-icons.min.css") %>" />
  <link rel="stylesheet" href="<%: ResolveUrl("~/css/tokens.css") %>" />
  <link rel="stylesheet" href="<%: ResolveUrl("~/css/components.css") %>" />
  <style>
    body { background: var(--surface-page); margin: 0; padding: 16px; font-family: 'Segoe UI', Arial, sans-serif; color: #111; }
    .print-toolbar { max-width: 820px; margin: 0 auto 12px; display: flex; gap: 8px; align-items: center; }
    .consent-sheet { max-width: 820px; margin: 0 auto; background: #fff; padding: 28px 34px; font-size: 12px; line-height: 1.45; box-shadow: 0 1px 4px rgba(0,0,0,.12); }
    .cf-head { text-align: center; border-bottom: 2px solid #333; padding-bottom: 8px; margin-bottom: 10px; }
    .cf-head img { max-height: 50px; max-width: 150px; margin-bottom: 4px; }
    .cf-head h2 { margin: 0; font-size: 17px; }
    .cf-head .sub { font-size: 11px; color: #444; }
    .cf-title { text-align: center; font-size: 15px; font-weight: 700; letter-spacing: .5px; margin: 10px 0 8px; }
    .cf-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 4px 24px; border: 1px solid #bbb; padding: 8px 12px; margin-bottom: 8px; }
    .cf-grid .full { grid-column: 1 / -1; }
    .cf-field { display: flex; gap: 6px; align-items: baseline; }
    .cf-field b { white-space: nowrap; }
    .cf-field span { flex: 1; border-bottom: 1px dotted #888; min-height: 16px; }
    .cf-section { margin-top: 10px; page-break-inside: avoid; }
    .cf-section h3 { font-size: 12.5px; margin: 0 0 4px; text-transform: uppercase; border-bottom: 1px solid #ccc; padding-bottom: 2px; }
    .cf-section p { margin: 3px 0; }
    .cf-checks { list-style: none; margin: 4px 0; padding: 0; columns: 2; column-gap: 24px; }
    .cf-checks.one { columns: 1; }
    .cf-checks li { margin: 2px 0; break-inside: avoid; }
    .cf-checks .sub-head { font-weight: 700; margin-top: 4px; column-span: all; }
    .box { display: inline-block; width: 11px; height: 11px; border: 1.2px solid #222; vertical-align: -1px; margin-right: 6px; }
    .cf-yesno { display: flex; gap: 40px; font-weight: 700; margin: 6px 0 2px; }
    .cf-bullets { margin: 3px 0 3px 18px; padding: 0; }
    .cf-sign { display: grid; grid-template-columns: 1fr 1fr; gap: 10px 28px; margin-top: 6px; }
    .cf-sign .line { border-bottom: 1px solid #333; height: 22px; }
    .cf-sign label { font-size: 11px; color: #333; }
    .cf-sign .prefilled { border-bottom: 1px solid #333; min-height: 22px; display: flex; align-items: flex-end; padding-bottom: 2px; font-weight: 600; }
    .cf-footer { margin-top: 16px; border-top: 1px solid #ccc; padding-top: 6px; font-size: 10px; color: #555; display: flex; justify-content: space-between; gap: 12px; }
    @media print {
      body { background: #fff; padding: 0; }
      .no-print { display: none !important; }
      .consent-sheet { box-shadow: none; max-width: none; padding: 0; }
      @page { size: A4; margin: 14mm 14mm 12mm; }
    }
  </style>
</head>
<body>
  <div class="print-toolbar no-print">
    <button class="btn btn--primary" type="button" onclick="window.print();"><i class="ti ti-printer" aria-hidden="true"></i> Print</button>
    <button class="btn btn--ghost" type="button" onclick="window.close();">Close</button>
    <span class="text-xs text-muted">Consent form · <%: P.FullName %> — after the patient signs, scan it and upload the signed copy on the Coverage page.</span>
  </div>

  <div class="consent-sheet">
    <div class="cf-head">
      <% if (!string.IsNullOrEmpty(Hospital.LogoBase64)) { %><img src="<%: Hospital.LogoBase64 %>" alt="" /><br /><% } %>
      <h2><%: Hospital.Name %></h2>
      <div class="sub"><%: Hospital.LegalAddress %></div>
      <div class="sub"><%: HospitalContact %></div>
    </div>

    <div class="cf-title">PATIENT CONSENT FOR SHARING HEALTH INFORMATION</div>

    <div class="cf-grid">
      <div class="cf-field full"><b>Hospital/Clinic Name:</b> <span><%: Hospital.Name %></span></div>
      <div class="cf-field"><b>Patient Registration Number:</b> <span><%: P.Mrn %></span></div>
      <div class="cf-field"><b>Date:</b> <span><%: Today %></span></div>
      <div class="cf-field full"><b>Attending Physician:</b> <span><%: DoctorLine %></span></div>
    </div>

    <div class="cf-section">
      <h3>1. Patient Identification</h3>
      <div class="cf-grid" style="margin:0;">
        <div class="cf-field full"><b>Patient Full Name:</b> <span><%: P.FullName %></span></div>
        <div class="cf-field"><b>Date of Birth:</b> <span><%: Dob %></span></div>
        <div class="cf-field"><b>Telephone Number:</b> <span><%: Phone %></span></div>
        <div class="cf-field full"><b>Address:</b> <span><%: P.Address %></span></div>
      </div>
    </div>

    <div class="cf-section">
      <h3>2. Purpose of Consent</h3>
      <p>I understand that during my care and treatment, healthcare professionals may need to share relevant portions of my medical information with other healthcare providers involved in my care.</p>
      <p>I hereby authorize <strong><%: Hospital.Name %></strong> to use and disclose my personal and health information for the purposes described below.</p>
    </div>

    <div class="cf-section">
      <h3>3. Information That May Be Shared</h3>
      <p>Only the minimum information reasonably required for my treatment, diagnosis, referral, investigations, medication management, insurance processing, or healthcare administration may be shared, including:</p>
      <ul class="cf-checks">
        <li><span class="box"></span>Patient identification details</li>
        <li><span class="box"></span>Medical history relevant to my treatment</li>
        <li><span class="box"></span>Current diagnoses</li>
        <li><span class="box"></span>Medications</li>
        <li><span class="box"></span>Allergies</li>
        <li><span class="box"></span>Laboratory results</li>
        <li><span class="box"></span>Radiology and imaging reports</li>
        <li><span class="box"></span>Consultation notes</li>
        <li><span class="box"></span>Referral letters</li>
        <li><span class="box"></span>Surgical and procedure records</li>
        <li><span class="box"></span>Insurance and NHF information (where necessary)</li>
        <li><span class="box"></span>Other (specify): ____________________</li>
      </ul>
    </div>

    <div class="cf-section">
      <h3>4. Persons and Organizations With Whom Information May Be Shared</h3>
      <p>I authorize sharing of my information with:</p>
      <ul class="cf-checks">
        <li class="sub-head">Treatment Purposes</li>
        <li><span class="box"></span>Physicians within the hospital</li>
        <li><span class="box"></span>Specialists and consultants</li>
        <li><span class="box"></span>Orthopedic surgeons</li>
        <li><span class="box"></span>Laboratory providers</li>
        <li><span class="box"></span>Radiology providers</li>
        <li><span class="box"></span>Pharmacists</li>
        <li><span class="box"></span>Nurses and authorized clinical staff</li>
        <li class="sub-head">Administrative Purposes</li>
        <li><span class="box"></span>Health insurance providers</li>
        <li><span class="box"></span>NHF (National Health Fund)</li>
        <li><span class="box"></span>Third-party administrators</li>
        <li><span class="box"></span>Government authorities where required by law</li>
        <li><span class="box"></span>External hospitals and healthcare facilities involved in my care</li>
      </ul>
    </div>

    <div class="cf-section">
      <h3>5. Internal Clinical Referral Consent</h3>
      <p>I understand that if I am referred from one doctor to another within the same hospital or healthcare organization, my relevant medical records may be made available to the receiving healthcare professional for treatment purposes.</p>
      <p><em>Example:</em> If my attending physician<%: DoctorExample %> refers me to an Orthopedic Specialist regarding a swollen ankle or any other condition, my relevant medical records, test results, medication history, allergies, and referral notes may be shared with that specialist.</p>
      <div class="cf-yesno"><span><span class="box"></span>I CONSENT</span><span><span class="box"></span>I DO NOT CONSENT</span></div>
    </div>

    <div class="cf-section">
      <h3>6. Electronic Health Records Consent</h3>
      <p>I understand that my information may be stored and accessed electronically by authorized healthcare professionals involved in my care.</p>
      <div class="cf-yesno"><span><span class="box"></span>I CONSENT</span><span><span class="box"></span>I DO NOT CONSENT</span></div>
    </div>

    <div class="cf-section">
      <h3>7. Insurance and NHF Processing Consent</h3>
      <p>I authorize the facility to disclose information required for:</p>
      <ul class="cf-checks">
        <li><span class="box"></span>Private Insurance claim processing</li>
        <li><span class="box"></span>NHF benefit verification</li>
        <li><span class="box"></span>NHF reimbursement processing</li>
        <li><span class="box"></span>Payment and billing activities</li>
      </ul>
    </div>

    <div class="cf-section">
      <h3>8. Limitations on Disclosure</h3>
      <p>I understand that:</p>
      <ul class="cf-bullets">
        <li>Only the minimum necessary information will be disclosed.</li>
        <li>Information will be shared solely for legitimate healthcare, operational, insurance, or legal purposes.</li>
        <li>Access is restricted to authorized persons.</li>
      </ul>
    </div>

    <div class="cf-section">
      <h3>9. Withdrawal of Consent</h3>
      <p>I understand that I may withdraw this consent at any time by providing written notice to the hospital or clinic, except where information has already been shared or where disclosure is required by law.</p>
    </div>

    <div class="cf-section">
      <h3>10. Patient Declaration</h3>
      <p>I confirm that:</p>
      <ul class="cf-bullets">
        <li>I have read and understood this consent.</li>
        <li>I have had the opportunity to ask questions.</li>
        <li>I voluntarily provide this consent.</li>
      </ul>
      <div class="cf-sign">
        <div><div class="prefilled"><%: P.FullName %></div><label>Patient Name</label></div>
        <div><div class="line"></div><label>Patient Signature</label></div>
        <div><div class="line"></div><label>Date</label></div>
      </div>
    </div>

    <div class="cf-section">
      <h3>11. Representative (if applicable)</h3>
      <div class="cf-sign">
        <div><div class="line"></div><label>Representative Name</label></div>
        <div><div class="line"></div><label>Relationship to Patient</label></div>
        <div><div class="line"></div><label>Signature</label></div>
        <div><div class="line"></div><label>Date</label></div>
      </div>
    </div>

    <div class="cf-section">
      <h3>12. Witness</h3>
      <div class="cf-sign">
        <div><div class="line"></div><label>Witness Name</label></div>
        <div><div class="line"></div><label>Signature</label></div>
        <div><div class="line"></div><label>Date</label></div>
      </div>
    </div>

    <div class="cf-footer">
      <span>Form <%: FormVersion %> · <%: PatientIdLabel %> <%: P.Mrn %></span>
      <span>Printed <%: Printed %> by <%: CurrentUser.FullName %></span>
    </div>
  </div>
</body>
</html>
