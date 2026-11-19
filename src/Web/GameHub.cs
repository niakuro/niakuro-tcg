using Microsoft.AspNetCore.SignalR;

namespace NiakuroTcg.Web;

public sealed class GameHub : Hub
{
    private readonly RoomManager _rooms;

    public GameHub(RoomManager rooms) => _rooms = rooms;

    public async Task<string> CreateRoom(string name, string deckId)
    {
        var result = _rooms.Create(name, deckId, Context.ConnectionId);
        if (!result.Success) throw new HubException(result.Error);
        await Groups.AddToGroupAsync(result.RoomCode, Context.ConnectionId);
        await Broadcast(result.RoomCode);
        return result.RoomCode;
    }

    public async Task<bool> JoinRoom(string roomCode, string name, string deckId)
    {
        var result = _rooms.Join(roomCode.Trim().ToUpperInvariant(), name, deckId, Context.ConnectionId);
        if (!result.Success) throw new HubException(result.Error);
        await Groups.AddToGroupAsync(result.RoomCode, Context.ConnectionId);
        await Broadcast(result.RoomCode);
        return true;
    }

    public async Task StartRoom(string roomCode)
    {
        var result = _rooms.Start(roomCode.Trim().ToUpperInvariant(), Context.ConnectionId);
        if (!result.Success) throw new HubException(result.Error);
        await Broadcast(result.RoomCode);
    }

    public async Task Action(string roomCode, string action, string value)
    {
        var code = roomCode.Trim().ToUpperInvariant();
        var result = _rooms.Action(code, Context.ConnectionId, action, value);
        if (!result.Success) throw new HubException(result.Error);
        await Broadcast(code);
    }

    private async Task Broadcast(string roomCode)
    {
        var states = _rooms.GetViewerStates(roomCode);
        foreach (var state in states)
            await Clients.Client(state.Value.ConnectionId).SendAsync("roomState", state.Value.State);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _rooms.RemoveConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
