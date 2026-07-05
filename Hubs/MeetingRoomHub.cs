using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace ProjectTracking.Hubs
{
    public class MeetingRoomHub : Hub
    {
        public const string RoomGroup = "meeting-room";
        private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, byte>> UserConnections = new();
        private static readonly ConcurrentDictionary<string, int> ConnectionUsers = new();

        public override async Task OnConnectedAsync()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup);

            var userId = CurrentUserId();
            if (userId.HasValue && userId.Value > 0)
            {
                ConnectionUsers[Context.ConnectionId] = userId.Value;
                var connections = UserConnections.GetOrAdd(userId.Value, _ => new ConcurrentDictionary<string, byte>());
                connections[Context.ConnectionId] = 0;
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
    }
}
