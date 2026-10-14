using System;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.consultation
{
    /// <summary>Author-only confidential note: only the writing doctor can read it; content is never audited or printed.</summary>
    public partial class consultation_confidential_notes : ConsultationSection
    {
        protected override string SectionKey { get { return "notes"; } }

        public override void Bind()
        {
            var note = ConsultationService.GetConfidentialNote(ConsultationId, Host.DoctorId);
            ConfidentialNoteText.Text = note != null ? note.NoteText : "";
            SavedAt.Text = note != null ? "Saved " + Server.HtmlEncode(AppPage.FmtDateTime(note.UpdatedAt)) : "";
        }

        protected void SaveNoteButton_Click(object sender, EventArgs e)
        {
            Host.ActivateStep("notes");
            ConsultationService.SaveConfidentialNote(ConsultationId, Host.DoctorId, ConfidentialNoteText.Text);
            /* audit records that a note changed, never its content */
            Audit("UPDATE", "ConfidentialNote");
            AppPage.ToastSuccess("Note saved");
            Bind();
        }
    }
}
