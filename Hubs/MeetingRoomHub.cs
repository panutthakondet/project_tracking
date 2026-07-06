using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.AspNetCore.SignalR;

namespace ProjectTracking.Hubs
{
    public class MeetingRoomHub : Hub
    {
        public const string RoomGroup = "meeting-room";
        private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, byte>> UserConnections = new();
        private static readonly ConcurrentDictionary<string, int> ConnectionUsers = new();
        private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, byte>> MeetingRoomPageConnections = new();
        private static readonly ConcurrentDictionary<string, int> MeetingRoomPageConnectionUsers = new();

        public static bool IsUserInMeetingRoomPage(int userId)
        {
            return userId > 0 &&
                MeetingRoomPageConnections.TryGetValue(userId, out var connections) &&
                !connections.IsEmpty;
        }

        public static HashSet<int> ActiveMeetingRoomPageUserIds()
        {
            var userIds = new HashSet<int>();
            foreach (var pair in MeetingRoomPageConnections)
            {
                if (!pair.Value.IsEmpty)
                    userIds.Add(pair.Key);
            }

            return userIds;
        }

        public override async Task OnConnectedAsync()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup);

            var userId = CurrentUserId();
            if (userId.HasValue && userId.Value > 0)
            {
                ConnectionUsers[Context.ConnectionId] = userId.Value;
                var connections = UserConnections.GetOrAdd(userId.Value, _ => new ConcurrentDictionary<string, byte>());
                connections[Context.ConnectionId] = 0;

                if (IsMeetingRoomPageConnection())
                {
                    MeetingRoomPageConnectionUsers[Context.ConnectionId] = userId.Value;
                    var pageConnections = MeetingRoomPageConnections.GetOrAdd(userId.Value, _ => new ConcurrentDictionary<string, byte>());
                    pageConnections[Context.ConnectionId] = 0;

                    await Clients.Group(RoomGroup).SendAsync("MeetingRoomPresenceChanged", new
                    {
                        userId = userId.Value,
                        isInRoom = true,
                        updatedAt = DateTimeOffset.UtcNow
                    });
                }
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomGroup);

            if (ConnectionUsers.TryRemove(Context.ConnectionId, out var userId) &&
                UserConnections.TryGetValue(userId, out var connections))
            {
                connections.TryRemove(Context.ConnectionId, out _);
                if (connections.IsEmpty)
                    UserConnections.TryRemove(userId, out _);
            }

            if (MeetingRoomPageConnectionUsers.TryRemove(Context.ConnectionId, out var pageUserId) &&
                MeetingRoomPageConnections.TryGetValue(pageUserId, out var pageConnections))
            {
                pageConnections.TryRemove(Context.ConnectionId, out _);
                if (pageConnections.IsEmpty)
                {
                    MeetingRoomPageConnections.TryRemove(pageUserId, out _);
                    await Clients.Group(RoomGroup).SendAsync("PersonLeftRoom", new
                    {
                        userId = pageUserId,
                        isInRoom = false,
                        updatedAt = DateTimeOffset.UtcNow
                    });
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinVoiceArea(string areaKey)
        {
            var userId = CurrentUserId();
            if (!userId.HasValue || userId.Value <= 0)
                return;

            areaKey = (areaKey ?? "").Trim();
            await Clients.Group(RoomGroup).SendAsync("VoiceAreaChanged", new
            {
                userId = userId.Value,
                areaKey
            });
        }

        public async Task UpdateMediaState(string areaKey, bool micOn, bool cameraOn, bool screenOn, bool isSpeaking)
        {
            var userId = CurrentUserId();
            if (!userId.HasValue || userId.Value <= 0)
                return;

            areaKey = (areaKey ?? "").Trim();
            await Clients.Group(RoomGroup).SendAsync("MeetingMediaStateChanged", new
            {
                userId = userId.Value,
                areaKey,
                micOn,
                cameraOn,
                screenOn,
                isSpeaking = micOn && isSpeaking,
                updatedAt = DateTimeOffset.UtcNow
            });
        }

        public Task SendRtcOffer(int targetUserId, object offer, string? areaKey = null)
            => SendRtcSignalAsync(targetUserId, "RtcOffer", new { offer, areaKey });

        public Task SendRtcAnswer(int targetUserId, object answer, string? areaKey = null)
            => SendRtcSignalAsync(targetUserId, "RtcAnswer", new { answer, areaKey });

        public Task SendRtcIceCandidate(int targetUserId, object candidate)
            => SendRtcSignalAsync(targetUserId, "RtcIceCandidate", new { candidate });

        private async Task SendRtcSignalAsync(int targetUserId, string eventName, object payload)
        {
            var fromUserId = CurrentUserId();
            if (!fromUserId.HasValue || fromUserId.Value <= 0 || targetUserId <= 0)
                return;

            if (!UserConnections.TryGetValue(targetUserId, out var connections) || connections.IsEmpty)
                return;

            foreach (var connectionId in connections.Keys)
            {
                await Clients.Client(connectionId).SendAsync(eventName, new
                {
                    fromUserId = fromUserId.Value,
                    payload
                });
            }
        }

        private int? CurrentUserId()
        {
            var sessionUserId = Context.GetHttpContext()?.Session.GetInt32("UserId");
            if (sessionUserId.HasValue)
                return sessionUserId.Value;

            var rawUserId = Context.GetHttpContext()?.Request.Query["userId"].ToString();
            return int.TryParse(rawUserId, out var userId) ? userId : null;
        }

        private bool IsMeetingRoomPageConnection()
        {
            var request = Context.GetHttpContext()?.Request;
            var client = request?.Query["client"].ToString();
            var scope = request?.Query["scope"].ToString();

            return string.Equals(client, "meeting-room", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(scope, "meeting-room", StringComparison.OrdinalIgnoreCase);
        }
    }
}
