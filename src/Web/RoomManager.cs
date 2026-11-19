using Niakuro.Tcg.Core;

namespace NiakuroTcg.Web;

public sealed class RoomManager
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Room> _rooms = new();

    public OperationResult Create(string name, string deckId, string connectionId)
    {
        lock (_sync)
        {
            string code;
            do code = GenerateCode(); while (_rooms.ContainsKey(code));
            var room = new Room(code);
            room.Players.Add(new RoomPlayer(name.Trim(), deckId, connectionId));
            _rooms[code] = room;
            return OperationResult.Ok(code);
        }
    }

    public OperationResult Join(string code, string name, string deckId, string connectionId)
    {
        lock (_sync)
        {
            if (!_rooms.TryGetValue(code, out var room)) return OperationResult.Fail("ルームが見つかりません。");
            if (room.Started) return OperationResult.Fail("対戦はすでに開始されています。");
            if (room.Players.Count >= 2) return OperationResult.Fail("ルームは満員です。");
            room.Players.Add(new RoomPlayer(name.Trim(), deckId, connectionId));
            return OperationResult.Ok(code);
        }
    }

    public OperationResult Start(string code, string connectionId)
    {
        lock (_sync)
        {
            if (!_rooms.TryGetValue(code, out var room)) return OperationResult.Fail("ルームが見つかりません。");
            if (room.Players.Count != 2) return OperationResult.Fail("対戦相手を待っています。");
            if (room.Players[0].ConnectionId != connectionId) return OperationResult.Fail("ルーム作成者だけが開始できます。");
            room.Engine = new GameEngine(DeckFor(room.Players[0].DeckId), DeckFor(room.Players[1].DeckId), Random.Shared.Next());
            room.Engine.StartTurn();
            room.Started = true;
            return OperationResult.Ok(code);
        }
    }

    public OperationResult Action(string code, string connectionId, string action, string value)
    {
        lock (_sync)
        {
            if (!_rooms.TryGetValue(code, out var room) || room.Engine == null)
                return OperationResult.Fail("対戦が開始されていません。");
            int index = room.Players.FindIndex(p => p.ConnectionId == connectionId);
            if (index < 0) return OperationResult.Fail("ルームの参加者ではありません。");
            if (room.Engine.PendingHaisuiPlayer != null && action != "haisui")
                return OperationResult.Fail("背水の陣を選択してください。");

            bool success = action switch
            {
                "mulligan" => room.Engine.Mulligan(index),
                "playUnit" => Guid.TryParse(value, out var id) && room.Engine.PlayUnit(id),
                "playTactic" => Guid.TryParse(value, out var id) && room.Engine.PlayTactic(id),
                "attackFace" => Guid.TryParse(value, out var id) && room.Engine.AttackFace(FindUnit(room.Engine, index, id)),
                "attackUnit" => ParseAttack(value, room.Engine, index, out var a, out var d) && room.Engine.AttackUnit(a!, d!),
                "endTurn" => room.Engine.EndTurn(),
                "haisui" => Enum.TryParse<HaisuiAction>(value, out var choice) && room.Engine.ChooseHaisui(choice),
                "discard" => Guid.TryParse(value, out var discardId) && room.Engine.DiscardFromHand(discardId),
                "chooseDiscard" => Guid.TryParse(value, out var chosenDiscardId) && room.Engine.ChooseCardToDiscard(chosenDiscardId),
                "chooseSelfish" => Guid.TryParse(value, out var selfishId) && room.Engine.ChooseSelfishDiscard(selfishId),
                "chooseDamage" => Guid.TryParse(value, out var damageId) && room.Engine.ChooseDamageTarget(damageId),
                "chooseHeal" => Guid.TryParse(value, out var healId) && room.Engine.ChooseHealTarget(healId),
                "chooseBuff" => Guid.TryParse(value, out var buffId) && room.Engine.ChooseBuffTarget(buffId),
                "chooseOwnBuff" => Guid.TryParse(value, out var ownBuffId) && room.Engine.ChooseOwnBuffTarget(ownBuffId),
                "chooseGuard" => Guid.TryParse(value, out var guardId) && room.Engine.ChooseGuardTarget(FindUnit(room.Engine, index, guardId)),
                "chooseDeploy" => Guid.TryParse(value, out var deployId) && room.Engine.ChooseFreeDeploy(deployId),
                "chooseDeckDeploy" => Guid.TryParse(value, out var deckDeployId) && room.Engine.ChooseDeckDeploy(deckDeployId),
                "chooseDrawPick" => Guid.TryParse(value, out var drawPickId) && room.Engine.ChooseDrawPick(drawPickId),
                "richMode" => int.TryParse(value, out var richMode) && room.Engine.ChooseRichMode(richMode),
                "masterMode" => int.TryParse(value, out var masterMode) && room.Engine.ChooseMasterMode(masterMode),
                "masterCopy" => ParseMasterCopy(value, room.Engine),
                _ => false
            };
            if (success && action == "endTurn") room.Engine.StartTurn();
            return success ? OperationResult.Ok(code) : OperationResult.Fail("その行動は今実行できません。");
        }
    }

    public Dictionary<string, ViewerState> GetViewerStates(string code)
    {
        lock (_sync)
        {
            var room = _rooms[code];
            var result = new Dictionary<string, ViewerState>();
            for (int i = 0; i < room.Players.Count; i++)
            {
                object state = room.Started && room.Engine != null
                    ? BuildGameState(room, i)
                    : new { room.Code, started = false, viewerIndex = i, players = room.Players.Select(p => new { p.Name, p.DeckId }).ToArray() };
                result[room.Players[i].ConnectionId] = new ViewerState(room.Players[i].ConnectionId, state);
            }
            return result;
        }
    }

    public void RemoveConnection(string connectionId)
    {
        lock (_sync)
        {
            var emptyCodes = new List<string>();
            foreach (var room in _rooms.Values)
            {
                room.Players.RemoveAll(p => p.ConnectionId == connectionId);
                if (room.Players.Count == 0) emptyCodes.Add(room.Code);
            }
            foreach (var code in emptyCodes) _rooms.Remove(code);
        }
    }

    private static object BuildGameState(Room room, int viewer)
    {
        var engine = room.Engine!;
        var me = engine.Players[viewer];
        var opp = engine.Players[1 - viewer];
        return new
        {
            room.Code,
            started = true,
            viewerIndex = viewer,
            myName = room.Players[viewer].Name,
            opponentName = room.Players[1 - viewer].Name,
            players = room.Players.Select(p => new { p.Name, p.DeckId }).ToArray(),
            isMyTurn = engine.ActivePlayerIndex == viewer,
            gameOver = engine.IsGameOver,
            winnerIndex = engine.WinnerIndex,
            gauge = engine.Gauge.Position,
            activePlayerIndex = engine.ActivePlayerIndex,
            globalTurn = engine.GlobalTurn,
            pendingHaisui = engine.PendingHaisuiPlayer,
            waitingDiscard = engine.WaitingDiscard,
            pendingDiscardBy = engine.PendingDiscardByPlayer,
            pendingDiscardTarget = engine.PendingDiscardTargetPlayer,
            pendingSelfishBy = engine.PendingSelfishByPlayer,
            pendingHealBy = engine.PendingHealByPlayer,
            pendingDamageBy = engine.PendingDamageByPlayer,
            pendingBuffBy = engine.PendingBuffByPlayer,
            pendingOwnBuffBy = engine.PendingOwnBuffByPlayer,
            pendingGuardBy = engine.PendingGuardGrantByPlayer,
            pendingDeployBy = engine.PendingDeployByPlayer,
            pendingDeckDeployBy = engine.PendingDeckDeployByPlayer,
            pendingDrawPickBy = engine.PendingDrawPickByPlayer,
            pendingRichBy = engine.PendingRichChoiceByPlayer,
            pendingMasterBy = engine.PendingMasterChoiceByPlayer,
            pendingDrawPool = engine.PendingDrawPickByPlayer == viewer
                ? engine.PendingDrawPool.Select(c => ViewCard(c)).ToArray()
                : Array.Empty<object>(),
            haisuiUsed = new[] { engine.Haisui.HasUsed(0), engine.Haisui.HasUsed(1) },
            haisuiActionsTaken = new[] {
                engine.Haisui.IsActionTaken(HaisuiAction.PushBack),
                engine.Haisui.IsActionTaken(HaisuiAction.DrawCards),
                engine.Haisui.IsActionTaken(HaisuiAction.NextTurnPoints)
            },
            me = ViewPlayer(me, engine, room.Players[viewer].DeckId),
            opponent = ViewOpponent(opp, engine, room.Players[1 - viewer].DeckId)
        };
    }

    private static object ViewPlayer(PlayerState p, GameEngine engine, string deckId) => new
    {
        deckId,
        hand = p.Hand.Select(c => ViewCard(c)).ToArray(),
        field = p.Field.Select(u => ViewUnit(u, engine)).ToArray(),
        graveyardCount = p.Graveyard.Count,
        deckCount = p.Deck.Count,
        points = p.Points.Available,
        basePoints = p.Points.BasePoints,
        mulliganUsed = p.HasUsedMulligan
    };

    private static object ViewOpponent(PlayerState p, GameEngine engine, string deckId) => new
    {
        deckId,
        handCount = p.Hand.Count,
        hand = p.Hand.Select(c => engine.IsHandCardRevealed(p.Index, c)
            ? ViewCard(c)
            : (object)new { c.InstanceId, name = (string?)null, cost = (int?)null, type = (string?)null, hidden = true }).ToArray(),
        revealedCards = p.Hand.Where(c => engine.IsHandCardRevealed(p.Index, c)).Select(c => ViewCard(c)).ToArray(),
        field = p.Field.Select(u => ViewUnit(u, engine)).ToArray(),
        graveyardCount = p.Graveyard.Count,
        deckCount = p.Deck.Count
    };

    private static object ViewCard(CardInstance c) => new { c.InstanceId, name = c.Definition.Name, cost = c.Definition.Cost, type = c.Definition.Type.ToString() };
    private static object ViewUnit(UnitState u, GameEngine engine) => new
    {
        instanceId = u.Instance.InstanceId,
        name = u.Definition.Name,
        cost = u.Definition.Cost,
        attack = engine.EffectiveAttackWhenAttacking(u),
        currentHealth = u.CurrentHealth,
        maxHealth = u.MaxHealth,
        hasAttacked = u.HasAttackedThisTurn,
        hasHaste = u.HasHaste,
        guard = u.HasGuard
    };

    private static UnitState? FindUnit(GameEngine engine, int index, Guid id)
        => engine.Players[index].Field.FirstOrDefault(u => u.Instance.InstanceId == id);

    private static bool ParseAttack(string value, GameEngine engine, int index, out UnitState? attacker, out UnitState? defender)
    {
        attacker = null; defender = null;
        var parts = value.Split('|');
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var aid) || !Guid.TryParse(parts[1], out var did)) return false;
        attacker = engine.Players[index].Field.FirstOrDefault(u => u.Instance.InstanceId == aid);
        defender = engine.Players[1 - index].Field.FirstOrDefault(u => u.Instance.InstanceId == did);
        return attacker != null && defender != null;
    }

    private static bool ParseMasterCopy(string value, GameEngine engine)
    {
        var parts = value.Split('|');
        return parts.Length == 2
            && Enum.TryParse<MasterCopyZone>(parts[0], out var zone)
            && Guid.TryParse(parts[1], out var id)
            && engine.ChooseMasterCopyTarget(zone, id);
    }

    private static string GenerateCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 6).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
    }

    private static IReadOnlyList<CardDefinition> DeckFor(string deckId) => deckId switch
    {
        "muscat" => PreconstructedDecks.MuscatKohaku(),
        "coconut" => PreconstructedDecks.CoconutYaya(),
        _ => PreconstructedDecks.NiakuroNitoguri()
    };
}

public sealed class Room
{
    public Room(string code) => Code = code;
    public string Code { get; }
    public List<RoomPlayer> Players { get; } = new();
    public GameEngine? Engine { get; set; }
    public bool Started { get; set; }
}

public sealed record RoomPlayer(string Name, string DeckId, string ConnectionId);
public sealed record ViewerState(string ConnectionId, object State);
public sealed record OperationResult(bool Success, string? Error, string RoomCode)
{
    public static OperationResult Ok(string code) => new(true, null, code);
    public static OperationResult Fail(string error) => new(false, error, "");
}
