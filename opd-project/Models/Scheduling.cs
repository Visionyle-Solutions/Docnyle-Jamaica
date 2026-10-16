using System;
using System.Collections.Generic;

namespace OpdProject.Models
{
    /// <summary>Appointment lifecycle: BOOKED → CHECKED_IN → IN_CONSULT → COMPLETED (or CANCELLED / NO_SHOW).</summary>
    public class Appointment
    {
        public string AppointmentId { get; set; }
        public string HospitalId { get; set; }
        public string PatientId { get; set; }
        public string DoctorId { get; set; }
        /// <summary>UTC instants (business event: shown in <see cref="TimeZoneId"/> wherever the viewer is).</summary>
        public DateTime SlotStart { get; set; }
        public DateTime SlotEnd { get; set; }
        /// <summary>The clinic's timezone when booked (IANA, e.g. America/Jamaica).</summary>
        public string TimeZoneId { get; set; }
        /// <summary>Calendar date of SlotStart in TimeZoneId — what the day lists filter on.</summary>
        public DateTime AppointmentDate { get; set; }
        /// <summary>SlotStart / SlotEnd on the clinic's wall clock (not stored).</summary>
        public DateTime SlotStartLocal { get { return OpdProject.Services.TimeZoneService.ToZone(SlotStart, TimeZoneId); } }
        public DateTime SlotEndLocal { get { return OpdProject.Services.TimeZoneService.ToZone(SlotEnd, TimeZoneId); } }
        public string Type { get; set; }
        public string Status { get; set; }
        public string Notes { get; set; }
        public DateTime? CheckedInAt { get; set; }
        public DateTime? ConsultStartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string CheckedInBy { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public string ModifiedBy { get; set; }
    }

    /// <summary>A doctor's weekly availability window (DoctorSlotConfig).</summary>
    public class DoctorSlotConfig
    {
        public string SlotConfigId { get; set; }
        public string DoctorId { get; set; }
        public string HospitalId { get; set; }
        public string DayOfWeek { get; set; }
        public string StartTime { get; set; }
        public string EndTime { get; set; }
        public bool IsActive { get; set; }
    }

    public class DoctorLeave
    {
        public string LeaveId { get; set; }
        public string HospitalId { get; set; }
        public string DoctorId { get; set; }
        public DateTime LeaveDate { get; set; }
        public string Reason { get; set; }
    }

    public class AppointmentType
    {
        public string AppointmentTypeId { get; set; }
        public string HospitalId { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public int? DurationMinutes { get; set; }
        public string Color { get; set; }
        public bool IsChargeable { get; set; }
        public string FeeCategory { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }

    public class DoctorSpecialty
    {
        public string DoctorId { get; set; }
        public string SpecialtyId { get; set; }
    }

    public class IntakeQuestion
    {
        public string QuestionId { get; set; }
        public string HospitalId { get; set; }
        public string Label { get; set; }
        public string QuestionType { get; set; }
        public string Options { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }

    public class IntakeAnswer
    {
        public string QuestionId { get; set; }
        public string Label { get; set; }
        public string Answer { get; set; }
    }

    public class IntakeResponse
    {
        public string ResponseId { get; set; }
        public string HospitalId { get; set; }
        public string AppointmentId { get; set; }
        public string PatientId { get; set; }
        public List<IntakeAnswer> Answers { get; set; }
        public DateTime SubmittedAt { get; set; }
    }

    public class ConsentForm
    {
        public string ConsentFormId { get; set; }
        public string HospitalId { get; set; }
        public string Title { get; set; }
        public string BodyText { get; set; }
        public int Version { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>Append-only consent signature; the form title/text are snapshotted at signing.</summary>
    public class ConsentRecord
    {
        public string ConsentRecordId { get; set; }
        public string ConsentFormId { get; set; }
        public string ConsultationId { get; set; }
        public string AppointmentId { get; set; }
        public string PatientId { get; set; }
        public string SignedByName { get; set; }
        public string Method { get; set; }
        public int FormVersion { get; set; }
        public string TitleSnapshot { get; set; }
        public string BodySnapshot { get; set; }
        public DateTime SignedAt { get; set; }
    }

    /// <summary>A bookable (or already booked) slot computed from the doctor's timetable.</summary>
    /// <summary>A bookable slot on the clinic's wall clock (hospital timezone); CreateAppointment converts it to UTC.</summary>
    public class TimeSlot
    {
        public DateTime SlotStart { get; set; }
        public DateTime SlotEnd { get; set; }
        public bool IsBooked { get; set; }
        public string Value { get { return SlotStart.ToString("yyyy-MM-ddTHH:mm") + "|" + SlotEnd.ToString("yyyy-MM-ddTHH:mm"); } }
    }

    public class TimeWindow
    {
        public string StartTime { get; set; }
        public string EndTime { get; set; }
    }

    /// <summary>What applies to a doctor on one date: windows, slot length, leave.</summary>
    public class DoctorSchedule
    {
        public string DoctorId { get; set; }
        public DateTime Date { get; set; }
        public string DayOfWeek { get; set; }
        public List<TimeWindow> Windows { get; set; }
        public List<string> WorkingDays { get; set; }
        public int SlotMinutes { get; set; }
        public int BufferMinutes { get; set; }
        public bool OnLeave { get; set; }
        public string LeaveReason { get; set; }
    }

    public class DoctorCharge
    {
        public string Model { get; set; }
        public decimal NewFee { get; set; }
        public decimal FollowupFee { get; set; }
        public int UnitMinutes { get; set; }
        public decimal UnitRate { get; set; }
    }

    /// <summary>An appointment joined with its patient and doctor, for list screens.</summary>
    public class AppointmentView
    {
        public Appointment Appointment { get; set; }
        public Patient Patient { get; set; }
        public string DoctorName { get; set; }
        public string DoctorRoom { get; set; }
        public string AppointmentId { get { return Appointment.AppointmentId; } }
        public string PatientId { get { return Appointment.PatientId; } }
        public string DoctorId { get { return Appointment.DoctorId; } }
        public string Status { get { return Appointment.Status; } }
        public string Type { get { return Appointment.Type; } }
        /// <summary>Clinic wall-clock times (Appointment.SlotStartLocal); the UTC instants are on Appointment.</summary>
        public DateTime SlotStart { get { return Appointment.SlotStartLocal; } }
        public DateTime SlotEnd { get { return Appointment.SlotEndLocal; } }
        public string TimeZoneId { get { return Appointment.TimeZoneId; } }
        public DateTime AppointmentDate { get { return Appointment.AppointmentDate; } }
        public string PatientName { get { return Patient != null ? Patient.FullName : "Unknown"; } }
        public string PatientMrn { get { return Patient != null ? Patient.Mrn : ""; } }
    }
}
