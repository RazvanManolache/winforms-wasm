// Stub of Windows.UI.Notifications + Windows.Data.Xml.Dom.
// Member shapes mirror the real contracts (add_/remove_/put_/get_
// names included) so desktop IL links; behavior is inert: Show is a
// no-op, Setting reports Enabled, Update reports Succeeded.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Data.Xml.Dom;
using Windows.Foundation;

namespace Windows.UI.Notifications
{
    public enum NotificationSetting
    {
        Enabled = 0,
        DisabledForApplication = 1,
        DisabledForUser = 2,
        DisabledByGroupPolicy = 3,
        DisabledByManifest = 4,
    }

    public enum NotificationUpdateResult
    {
        Succeeded = 0,
        Failed = 1,
        NotificationNotFound = 2,
    }

    public sealed class ToastDismissedEventArgs
    {
        public ToastDismissedEventArgs() => Reason = ToastDismissalReason.UserCanceled;
        public ToastDismissalReason Reason { get; }
    }

    public enum ToastDismissalReason
    {
        UserCanceled = 0,
        ApplicationHidden = 1,
        TimedOut = 2,
    }

    public sealed class ToastActivatedEventArgs
    {
        public string Arguments => "";
    }

    public sealed class NotificationData
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        public NotificationData() { }
        public NotificationData(IEnumerable<KeyValuePair<string, string>> initialValues)
        {
            if (initialValues is null) return;
            foreach (var kv in initialValues) _values[kv.Key] = kv.Value;
        }

        public IDictionary<string, string> Values => _values;
        private uint _sequenceNumber;
        public uint get_SequenceNumber() => _sequenceNumber;
        public void put_SequenceNumber(uint value) => _sequenceNumber = value;
    }

    public sealed class ToastNotification
    {
        private readonly Dictionary<EventRegistrationToken, object> _dismissed = new Dictionary<EventRegistrationToken, object>();
        private readonly Dictionary<EventRegistrationToken, object> _activated = new Dictionary<EventRegistrationToken, object>();
        private long _token;

        public ToastNotification(XmlDocument content) => Content = content;
        public XmlDocument Content { get; }

        private string _tag = "";
        private string _group = "";
        private bool _suppressPopup;
        private NotificationData _data;
        private DateTimeOffset? _expirationTime;
        public string get_Tag() => _tag;
        public void put_Tag(string value) => _tag = value ?? "";
        public string get_Group() => _group;
        public void put_Group(string value) => _group = value ?? "";
        public bool get_SuppressPopup() => _suppressPopup;
        public void put_SuppressPopup(bool value) => _suppressPopup = value;
        public NotificationData get_Data() => _data;
        public void put_Data(NotificationData value) => _data = value;
        public DateTimeOffset? get_ExpirationTime() => _expirationTime;
        public void put_ExpirationTime(DateTimeOffset? value) => _expirationTime = value;

        // WinRT-shaped event accessors (C# 'event' cannot express the
        // token-returning add_ the real contract exposes).
        public EventRegistrationToken add_Dismissed(TypedEventHandler<ToastNotification, ToastDismissedEventArgs> handler)
        {
            var token = new EventRegistrationToken { Value = ++_token };
            _dismissed[token] = handler;
            return token;
        }

        public void remove_Dismissed(EventRegistrationToken token) => _dismissed.Remove(token);

        public EventRegistrationToken add_Activated(TypedEventHandler<ToastNotification, object> handler)
        {
            var token = new EventRegistrationToken { Value = ++_token };
            _activated[token] = handler;
            return token;
        }

        public void remove_Activated(EventRegistrationToken token) => _activated.Remove(token);
    }

    public sealed class ScheduledToastNotification
    {
        public ScheduledToastNotification(XmlDocument content, DateTimeOffset deliveryTime)
        {
            Content = content;
            DeliveryTime = deliveryTime;
        }

        public XmlDocument Content { get; }
        public DateTimeOffset DeliveryTime { get; }
        private string _tag = "";
        private string _group = "";
        public string get_Tag() => _tag;
        public void put_Tag(string value) => _tag = value ?? "";
        public string get_Group() => _group;
        public void put_Group(string value) => _group = value ?? "";
    }

    public sealed class ToastNotifier
    {
        private readonly List<ScheduledToastNotification> _scheduled = new List<ScheduledToastNotification>();

        public NotificationSetting Setting => NotificationSetting.Enabled;

        public void Show(ToastNotification toast)
        {
            try { Console.WriteLine("[WinRtStub] ToastNotifier.Show (no-op on web), tag=" + toast?.get_Tag()); } catch { }
        }

        public void Hide(ToastNotification toast) { }

        public void AddToSchedule(ScheduledToastNotification scheduledToast)
        {
            if (scheduledToast is not null) _scheduled.Add(scheduledToast);
        }

        public void RemoveFromSchedule(ScheduledToastNotification scheduledToast) => _scheduled.Remove(scheduledToast);

        public IReadOnlyList<ScheduledToastNotification> GetScheduledToastNotifications() => _scheduled.ToArray();

        public NotificationUpdateResult Update(NotificationData data, string tag) => NotificationUpdateResult.Succeeded;

        public NotificationUpdateResult Update(NotificationData data, string tag, string group) => NotificationUpdateResult.Succeeded;
    }

    public sealed class ToastNotificationHistory
    {
        public void Clear() { }
        public void Clear(string applicationId) { }
        public IReadOnlyList<ToastNotification> GetHistory() => Array.Empty<ToastNotification>();
        public IReadOnlyList<ToastNotification> GetHistory(string applicationId) => Array.Empty<ToastNotification>();
        public void Remove(string tag) { }
        public void Remove(string tag, string group) { }
        public void Remove(string tag, string group, string applicationId) { }
        public void RemoveGroup(string group) { }
        public void RemoveGroup(string group, string applicationId) { }
    }

    public static class ToastNotificationManager
    {
        public static ToastNotifier CreateToastNotifier() => new ToastNotifier();
        public static ToastNotifier CreateToastNotifier(string applicationId) => new ToastNotifier();
        public static ToastNotificationHistory History => new ToastNotificationHistory();
    }
}

namespace Windows.Data.Xml.Dom
{
    public sealed class XmlDocument
    {
        private string _xml = "";

        public void LoadXml(string xml) => _xml = xml ?? "";
        public string GetXml() => _xml;
        public override string ToString() => _xml;
    }
}
