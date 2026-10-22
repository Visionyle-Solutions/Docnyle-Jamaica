using System.Collections.Generic;
using System.Linq;
using System;
using OpdProject.Helpers;
using OpdProject.Models;
using OpdProject.Services.Api;
using OpdProject.Web;

namespace OpdProject.Services
{
    /// <summary>
    /// In-app notifications for clinic staff (the bell in the top bar), e.g. "lab result ready" for the ordering doctor.
    /// Patient-facing SMS / e-mail stays in <see cref="NotificationService"/>.
    /// </summary>
    public static class UserNotificationService
    {
        private static readonly CommonHelper cnHelperObj = new CommonHelper();

        private static bool Api { get { return DataSource.IsApi("UserNotifications"); } }

        private static UserNotification ToModel(MSG_UserNotificationInfo n, string userId)
        {
            if (n == null) return null;
            return new UserNotification
            {
                NotificationId = n.NotificationId, UserId = userId, Kind = n.Kind, Title = n.Title, Message = n.Message, Url = n.Url,
                IsCritical = n.IsCritical, EntityId = n.EntityId, CreatedAt = DocNyle.InstantOrMin(n.CreatedAt), ReadAt = DocNyle.ParseInstant(n.ReadAt),
            };
        }

        /// <summary>In-memory only: in API mode SubmitLabResult creates the doctor's notification on the service.</summary>
        public static UserNotification Notify(string hospitalId, string userId, string kind, string title, string message, string url, bool isCritical, string entityId)
        {
            if (string.IsNullOrEmpty(userId) || Api) return null;
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
            if (Api)
                return (cnHelperObj.ListUserNotifications(AppSession.ApiHospitalId, AppSession.ApiUserId, take.ToString(System.Globalization.CultureInfo.InvariantCulture), AppSession.ApiHashValue).EnsureSuccess().Notifications ?? new List<MSG_UserNotificationInfo>())
                    .Select(n => ToModel(n, userId)).OrderByDescending(n => n.IsCritical && n.ReadAt == null).ThenByDescending(n => n.CreatedAt).Take(take).ToList();
            lock (DataStore.Sync)
            {
                var mine = DataStore.UserNotifications.Where(n => n.UserId == userId);
                return mine.OrderByDescending(n => n.IsCritical && n.ReadAt == null).ThenByDescending(n => n.CreatedAt).Take(take).ToList();
            }
        }

        /// <summary>API mode: GetUserNotificationUnreadCount, cached 15 seconds per user (the top bar asks on every page).</summary>
        public static int UnreadCount(string userId)
        {
            if (Api)
            {
                var key = "notif-unread:" + userId;
                var hit = System.Runtime.Caching.MemoryCache.Default.Get(key);
                if (hit is int) return (int)hit;
                var n = cnHelperObj.GetUserNotificationUnreadCount(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Count;
                System.Runtime.Caching.MemoryCache.Default.Set(key, n, DateTimeOffset.UtcNow.AddSeconds(15));
                return n;
            }
            lock (DataStore.Sync) return DataStore.UserNotifications.Count(n => n.UserId == userId && n.ReadAt == null);
        }

        /// <summary>Marks the user's own notification read. Returns null if it does not exist or belongs to someone else.</summary>
        public static UserNotification MarkRead(string notificationId, string userId)
        {
            if (Api)
            {
                System.Runtime.Caching.MemoryCache.Default.Remove("notif-unread:" + userId);
                return ToModel(DocNyle.OrNull(() => cnHelperObj.UpdateUserNotificationRead(new MSG_UpdateUserNotificationReadReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, NotificationId = notificationId }).EnsureSuccess())?.Notification, userId);
            }
            lock (DataStore.Sync)
            {
                var n = DataStore.UserNotifications.FirstOrDefault(x => x.NotificationId == notificationId && x.UserId == userId);
                if (n != null && n.ReadAt == null) n.ReadAt = DateTime.UtcNow;
                return n;
            }
        }

        public static void MarkAllRead(string userId)
        {
            if (Api)
            {
                cnHelperObj.UpdateUserNotificationsReadAll(new MSG_UpdateUserNotificationsReadAllReq() { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue }).EnsureSuccess();
                System.Runtime.Caching.MemoryCache.Default.Remove("notif-unread:" + userId);
                return;
            }
            lock (DataStore.Sync)
                foreach (var n in DataStore.UserNotifications.Where(x => x.UserId == userId && x.ReadAt == null)) n.ReadAt = DateTime.UtcNow;
        }

        /// <summary>Clears notifications about something that has been dealt with (e.g. a lab result marked reviewed).</summary>
        public static void MarkReadForEntity(string entityId)
        {
            if (Api) return;   // UpdateLabResultStatus does it on the service
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
