using System.Collections.Generic;
using System.Linq;
using System;
using OpdProject.Helpers;
using OpdProject.Models;
using OpdProject.Services.Api;
using OpdProject.Web;

namespace OpdProject.Services
{
    /// <summary>A doctor offered on the public booking page.</summary>
    public class PortalDoctor
    {
        public string DoctorId { get; set; }
        public string Label { get; set; }
    }

    /// <summary>A booking made on the public page.</summary>
    public class PortalBooking
    {
        public string AppointmentId { get; set; }
        public string DoctorName { get; set; }
    }

    /// <summary>
    /// The anonymous patient portal (self-registration, self-booking). API mode uses only the portal operations
    /// (GetPortalBookingOptions, ListPortalAvailableSlots, GetPortalPatient, CreatePortalPatient, CreatePortalAppointment),
    /// which take the hospital from the service's own configuration; the in-memory mode uses the regular facades.
    /// </summary>
    public static class PortalService
    {
        private static readonly CommonHelper cnHelperObj = new CommonHelper();

        private static bool Api { get { return DataSource.IsApi("Scheduling"); } }

        /// <summary>The portal hospital: ?h= in memory mode (else the default), the service's portal hospital in API mode.</summary>
        public static Hospital GetHospital(string requestedHospitalId, Hospital fallback)
        {
            if (Api) return HospitalService.PortalHospital() ?? fallback ?? new Hospital { Name = "Consult360" };
            var h = requestedHospitalId != null ? HospitalService.GetHospital(requestedHospitalId) : null;
            return h ?? fallback;
        }

        public static List<PortalDoctor> ListDoctors(string hospitalId)
        {
            if (Api)
                return (SchedulingService.PortalOptions().Doctors ?? new List<SCH_PortalDoctorInfo>()).Select(d => new PortalDoctor
                {
                    DoctorId = d.DoctorId,
                    Label = d.DisplayName + (string.IsNullOrEmpty(d.Specialties) ? "" : " · " + d.Specialties) + " · " + SchedulingService.DaysSummary(d.WorkingDays ?? new List<string>()),
                }).ToList();
            return UserService.ListActiveDoctors(hospitalId).Select(d =>
            {
                var sched = SchedulingService.GetDoctorSchedule(d.UserId, hospitalId, TimeZoneService.Today(hospitalId));
                return new PortalDoctor
                {
                    DoctorId = d.UserId,
                    Label = d.FullName + (string.IsNullOrEmpty(d.Qualifications) ? "" : " — " + d.Qualifications)
                        + " · " + string.Join(", ", SchedulingService.DoctorSpecialtyIds(d.UserId).Select(HospitalService.SpecialtyName))
                        + " · " + SchedulingService.DaysSummary(sched.WorkingDays),
                };
            }).ToList();
        }

        public static List<AppointmentType> ListAppointmentTypes(string hospitalId)
        {
            if (Api) return (SchedulingService.PortalOptions().AppointmentTypes ?? new List<SCH_AppointmentTypeInfo>()).Where(t => t.IsActive).Select(SchedulingService.ToModel).OrderBy(t => t.SortOrder).ToList();
            return SchedulingService.ListAppointmentTypes(hospitalId, false);
        }

        public static List<IntakeQuestion> ListIntakeQuestions(string hospitalId)
        {
            if (Api)
                return (SchedulingService.PortalOptions().IntakeQuestions ?? new List<SCH_IntakeQuestionInfo>())
                    .Select(q => new IntakeQuestion { QuestionId = q.QuestionId, Label = q.Label, QuestionType = q.QuestionType, Options = q.Options ?? "", SortOrder = q.SortOrder, IsActive = true })
                    .OrderBy(q => q.SortOrder).ToList();
            return SchedulingService.ListIntakeQuestions(hospitalId);
        }

        /// <summary>Free, future slots of the doctor on the date (ListPortalAvailableSlots removes booked and past ones).</summary>
        public static List<TimeSlot> ListFreeSlots(string doctorId, string hospitalId, DateTime date, int? durationMinutes)
        {
            if (Api)
            {
                var dur = durationMinutes.HasValue && durationMinutes.Value > 0 ? durationMinutes.Value.ToString() : null;
                var day = DocNyle.Date(date);
                return (cnHelperObj.ListPortalAvailableSlots(doctorId, day, dur, CommonHelper.GetSHA265(doctorId + day)).EnsureSuccess().Slots ?? new List<SCH_AvailableSlotInfo>())
                    .Where(s => !s.IsBooked).Select(s => new TimeSlot { SlotStart = DocNyle.LocalOrMin(s.SlotStart), SlotEnd = DocNyle.LocalOrMin(s.SlotEnd) })
                    .Where(s => s.SlotStart != DateTime.MinValue).OrderBy(s => s.SlotStart).ToList();
            }
            var now = TimeZoneService.Now(hospitalId);
            return SchedulingService.ListAvailableSlots(doctorId, hospitalId, date, durationMinutes).Where(s => !s.IsBooked && s.SlotStart > now).ToList();
        }

        /// <summary>Why there are no free slots (the anonymous API has no schedule details, so only a general reason there).</summary>
        public static string NoSlotReason(string doctorId, string hospitalId, DateTime date)
        {
            if (Api) return "The doctor has no free times on this date";
            return SchedulingService.NoSlotReason(SchedulingService.GetDoctorSchedule(doctorId, hospitalId, date));
        }

        /// <summary>
        /// The patient by MRN, or by phone (digits). API mode: GetPortalPatient returns only the id, MRN and a short
        /// display name ("Ravi K."), which is what <see cref="Patient.FullName"/> holds then.
        /// </summary>
        public static Patient FindPatient(string hospitalId, string mrn, string phone)
        {
            mrn = (mrn ?? "").Trim();
            phone = (phone ?? "").Trim();
            if (Api)
            {
                var digits = new string(phone.Where(char.IsDigit).ToArray());
                string byMrn = mrn.Length > 0 ? mrn : "", byPhone = mrn.Length > 0 ? "" : digits;
                var p = cnHelperObj.GetPortalPatient(byMrn, byPhone, CommonHelper.GetSHA265(byMrn + byPhone)).EnsureSuccess().Patient;
                return p == null ? null : new Patient { PatientId = p.PatientId, Mrn = p.Mrn, FullName = p.DisplayName };
            }
            var all = PatientService.Search(hospitalId, "");
            if (mrn.Length > 0) return all.FirstOrDefault(p => string.Equals(p.Mrn, mrn, StringComparison.OrdinalIgnoreCase));
            if (phone.Length > 0)
            {
                var digits = new string(phone.Where(char.IsDigit).ToArray());
                return all.FirstOrDefault(p => !string.IsNullOrEmpty(p.Phone) && new string(p.Phone.Where(char.IsDigit).ToArray()) == digits);
            }
            return null;
        }

        /// <summary>
        /// Self-registration. Returns null with <paramref name="duplicateField"/> set when the details match a registered
        /// patient (API mode: CreatePortalPatient refuses without saying which field, so the field is "").
        /// </summary>
        public static Patient Register(Patient record, out string duplicateField)
        {
            duplicateField = null;
            if (Api)
            {
                try
                {
                    var created = cnHelperObj.CreatePortalPatient(new SCH_CreatePortalPatientReq
                    {
                        FullName = record.FullName, Gender = record.Gender, Dob = DocNyle.Date(record.Dob), Phone = record.Phone, Email = record.Email,
                        Address = record.Address, NationalHealthId = record.NationalHealthId, InsuranceProvider = record.InsuranceProvider,
                        InsurancePolicyNo = record.InsurancePolicyNo, EmergencyContactName = record.EmergencyContactName, EmergencyContactPhone = record.EmergencyContactPhone,
                        Prefix = record.Prefix, FirstName = record.FirstName, MiddleName = record.MiddleName, LastName = record.LastName, Trn = record.Trn,
                        CountryCode = record.CountryCode, Mobile = record.Mobile, PreferredLanguage = record.PreferredLanguage, AddrLine1 = record.AddrLine1,
                        AddrLine2 = record.AddrLine2, City = record.City, Region = record.Region, Country = record.Country,
                        MaritalStatus = string.IsNullOrEmpty(record.MaritalStatus) ? null : record.MaritalStatus, ReferralSource = record.ReferralSource,
                        PreferredDoctorId = string.IsNullOrEmpty(record.PreferredDoctorId) ? null : record.PreferredDoctorId, BloodGroup = record.BloodGroup,
                        Occupation = record.Occupation,
                        hashValue = CommonHelper.GetSHA265(record.FullName + record.Phone),
                    }).EnsureSuccess().Patient;
                    return new Patient { PatientId = created.PatientId, Mrn = created.Mrn, FullName = created.DisplayName };
                }
                catch (ApiException ex) when ((ex.Message ?? "").ToUpperInvariant().Contains("ALREADY REGISTERED"))
                {
                    duplicateField = "";
                    return null;
                }
            }
            var dup = PatientService.FindDuplicate(record);
            if (dup != null) { duplicateField = dup.Field; return null; }
            var p2 = PatientService.Create(record);
            AuditService.Record(record.HospitalId, null, "CREATE", "Patient", p2.PatientId, "Self-registered via patient portal");
            return p2;
        }

        /// <summary>
        /// Books the slot (re-checked under the doctor-day lock) with the intake answers. Null when the slot was taken
        /// meanwhile (API fault 50214 / 50220).
        /// </summary>
        public static PortalBooking Book(string hospitalId, Patient patient, string doctorId, DateTime start, DateTime end, string type, List<IntakeAnswer> answers)
        {
            if (Api)
            {
                try
                {
                    var a = cnHelperObj.CreatePortalAppointment(new SCH_CreatePortalAppointmentReq
                    {
                        PatientId = patient.PatientId, DoctorId = doctorId, SlotStartLocal = DocNyle.DateTimeLocal(start), SlotEndLocal = DocNyle.DateTimeLocal(end),
                        Type = type, Answers = answers.Select(x => new SCH_IntakeAnswerInfo { QuestionId = x.QuestionId, Answer = x.Answer }).ToList(),
                        hashValue = CommonHelper.GetSHA265(patient.PatientId + doctorId),
                    }).EnsureSuccess().Appointment;
                    return new PortalBooking { AppointmentId = a.AppointmentId, DoctorName = a.DoctorName };
                }
                catch (ApiException ex) when (ex.Kind == ApiFaultKind.Conflict)
                {
                    return null;
                }
            }
            var stillFree = SchedulingService.ListAvailableSlots(doctorId, hospitalId, start.Date, (int)(end - start).TotalMinutes).Any(s => !s.IsBooked && s.SlotStart == start);
            if (!stillFree) return null;
            var appt = SchedulingService.CreateAppointment(hospitalId, patient.PatientId, doctorId, start, end, type, "Booked online");
            if (answers.Count > 0) SchedulingService.SubmitIntake(hospitalId, appt.AppointmentId, patient.PatientId, answers);
            AuditService.Record(hospitalId, null, "CREATE", "Appointment", appt.AppointmentId, "Booked online by patient " + patient.Mrn);
            return new PortalBooking { AppointmentId = appt.AppointmentId, DoctorName = UserService.UserName(appt.DoctorId) };
        }
    }
}
