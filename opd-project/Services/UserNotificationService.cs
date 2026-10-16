using System;
using System.Collections.Generic;
using System.Linq;
using OpdProject.Models;

namespace OpdProject.Services
{
    /// <summary>
    /// In-app notifications for clinic staff (the bell in the top bar), e.g. "lab result ready" for the ordering doctor.
    /// Patient-facing SMS / e-mail stays in <see cref="NotificationService"/>.
    /// </summary>
    public static class UserNotificationService
    {
        public static UserNotification Notify(string hospitalId, string userId, string kind, string title, string message, string url, bool isCritical, string entityId)
        {
            if (string.IsNullOrEmpty(userId)) return null;
            var n = new UserNotification
            {
                NotificationId = DataStore.NextId("NTF"), HospitalId = hospitalId, UserId = userId, Kind = kind, Title = title,
                Message = message, Url = url, IsCritical = isCritical, EntityId = entityId, CreatedAt = DateTime.UtcNow,
            };
            lock (DataStore.Sync) DataStore.UserNotifications.Add(n);
            return n;
        }

        /// <summary>Newest first; unread critical ones are never pushed out of the list by newer routine ones.</summary>
        public static List<UserNotification> ListRecent(string userId, int take)
        {
            lock (DataStore.Sync)
            {
                var mine = DataStore.UserNotifications.Where(n => n.UserId == userId);
                return mine.OrderByDescending(n => n.IsCritical && n.ReadAt == null).ThenByDescending(n => n.CreatedAt).Take(take).ToList();
            }
        }

        public static int UnreadCount(string userId)
        {
            lock (DataStore.Sync) return DataStore.UserNotifications.Count(n => n.UserId == userId && n.ReadAt == null);
        }

        /// <summary>Marks the user's own notification read. Returns null if it does not exist or belongs to someone else.</summary>
        public static UserNotification MarkRead(string notificationId, string userId)
        {
            lock (DataStore.Sync)
            {
                var n = DataStore.UserNotifications.FirstOrDefault(x => x.NotificationId == notificationId && x.UserId == userId);
                if (n != null && n.ReadAt == null) n.ReadAt = DateTime.UtcNow;
                return n;
            }
        }

        public static void MarkAllRead(string userId)
        {
            lock (DataStore.Sync)
                foreach (var n in DataStore.UserNotifications.Where(x => x.UserId == userId && x.ReadAt == null)) n.ReadAt = DateTime.UtcNow;
        }

        /// <summary>Clears notifications about something that has been dealt with (e.g. a lab result marked reviewed).</summary>
        public static void MarkReadForEntity(string entityId)
        {
            lock (DataStore.Sync)
                foreach (var n in DataStore.UserNotifications.Where(x => x.EntityId == entityId && x.ReadAt == null)) n.ReadAt = DateTime.UtcNow;
        }

        public static string Ago(DateTime at)
        {
            var span = DateTime.UtcNow - at;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalHours < 1) return (int)span.TotalMinutes + " min ago";
            if (span.TotalDays < 1) return (int)span.TotalHours + " h ago";
            return (int)span.TotalDays + " d ago";
        }
    }
}
