// Stub of the WinRT FoundationContract (Windows.Foundation.*).
// Only types referenced by desktop apps in our pool are present; see
// AppTransform WinRT notes. Members are inert: enough to load, link,
// and run headless without the Windows Runtime.

using System.Collections;
using System.Collections.Generic;

namespace Windows.Foundation
{
    public delegate void TypedEventHandler<TSender, TResult>(TSender sender, TResult args);

    public enum AsyncStatus
    {
        Started = 0,
        Completed = 1,
        Canceled = 2,
        Error = 3,
    }

    public interface IAsyncAction
    {
        AsyncActionCompletedHandler Completed { get; set; }
        void Cancel();
        void Close();
        void GetResults();
        System.Exception ErrorCode { get; }
        uint Id { get; }
        AsyncStatus Status { get; }
    }

    public delegate void AsyncActionCompletedHandler(IAsyncAction asyncInfo, AsyncStatus asyncStatus);
}

// Lives in mscorlib on desktop .NET Framework but is absent from the
// browser CoreLib; desktop callers are remapped here by the rewriter
// (EventRegistrationToken typeMap rule in AppTransform/Rewrite.Tool).
namespace System.Runtime.InteropServices.WindowsRuntime
{
    public struct EventRegistrationToken
    {
        public long Value;

        public override bool Equals(object obj) =>
            obj is EventRegistrationToken other && Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(EventRegistrationToken left, EventRegistrationToken right) =>
            left.Value == right.Value;
        public static bool operator !=(EventRegistrationToken left, EventRegistrationToken right) =>
            left.Value != right.Value;
    }

    // Functional subset of the mscorlib WinRT interop helper: subscribes
    // through the real add_/remove_ accessors and tracks handler->token
    // so RemoveEventHandler unsubscribes the right registration.
    public static class WindowsRuntimeMarshal
    {
        private static readonly Dictionary<object, EventRegistrationToken> Tokens = new Dictionary<object, EventRegistrationToken>();

        public static void AddEventHandler<T>(Func<T, EventRegistrationToken> addMethod, Action<EventRegistrationToken> removeMethod, T handler)
        {
            if (addMethod is null || handler is null) return;
            EventRegistrationToken token = addMethod(handler);
            lock (Tokens) Tokens[handler] = token;
        }

        public static void RemoveEventHandler<T>(Action<EventRegistrationToken> removeMethod, T handler)
        {
            if (removeMethod is null || handler is null) return;
            EventRegistrationToken token;
            lock (Tokens)
            {
                if (!Tokens.TryGetValue(handler, out token)) return;
                Tokens.Remove(handler);
            }
            removeMethod(token);
        }
    }
}

namespace Windows.Foundation.Collections
{
    public sealed class ValueSet : IDictionary<string, object>
    {
        private readonly Dictionary<string, object> _map = new Dictionary<string, object>();

        public object this[string key] { get => _map[key]; set => _map[key] = value; }
        public ICollection<string> Keys => _map.Keys;
        public ICollection<object> Values => _map.Values;
        public int Count => _map.Count;
        public bool IsReadOnly => false;

        public void Add(string key, object value) => _map.Add(key, value);
        public void Add(KeyValuePair<string, object> item) => _map.Add(item.Key, item.Value);
        public void Clear() => _map.Clear();
        public bool Contains(KeyValuePair<string, object> item) => ((ICollection<KeyValuePair<string, object>>)_map).Contains(item);
        public bool ContainsKey(string key) => _map.ContainsKey(key);
        public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) => ((ICollection<KeyValuePair<string, object>>)_map).CopyTo(array, arrayIndex);
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _map.GetEnumerator();
        public bool Remove(string key) => _map.Remove(key);
        public bool Remove(KeyValuePair<string, object> item) => ((ICollection<KeyValuePair<string, object>>)_map).Remove(item);
        public bool TryGetValue(string key, out object value) => _map.TryGetValue(key, out value);
        IEnumerator IEnumerable.GetEnumerator() => _map.GetEnumerator();
    }
}
