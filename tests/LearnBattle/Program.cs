// このファイルについて:
//   簡易AI同士を対戦させるシミュレータ。同じデッキ同士のミラーマッチで
//   「ゲームが何ターンで終わるか」「背水の陣が発動するか」「詰まらないか」を確認する。
//   AIは単純な方針: ユニットを安い順に出し、連携を効かせ、基本は顔を殴る。
//   有利に倒せる相手（攻撃力3以上を一方的に倒せる）だけ戦闘で処理する。
using System;
using System.Collections.Generic;
using Niakuro.Tcg.Core;

namespace LearnBattle
{
    internal static class Program
    {
        private static GameEngine _g;
        private static readonly List<string> _log = new List<string>();

        // ---------- 学習つき自己対戦ハーネス ----------
        private sealed class Brain
        {
            public readonly Dictionary<string, double> W = new Dictionary<string, double>();
            public double Get(string cardName)
            {
                double v;
                return W.TryGetValue(cardName, out v) ? v : 0.0;
            }
            public void Add(string cardName, double delta)
            {
                double v = Get(cardName) + delta;
                if (v > 1.5) v = 1.5;
                if (v < -1.5) v = -1.5;
                W[cardName] = v;
            }
        }

        private static readonly Brain _brainA = new Brain();
        private static readonly Brain _brainB = new Brain();
        private static bool[] _seatIsA = new[] { true, false };

        private static Brain SeatBrain(int seat) => _seatIsA[seat] ? _brainA : _brainB;
        private static double BrainW(int seat, string cardName) => SeatBrain(seat).Get(cardName);

        // 統計
        private static readonly Dictionary<string, int>[] _gamePlays =
            new[] { new Dictionary<string, int>(), new Dictionary<string, int>() };
        private static readonly Dictionary<string, int> _playsWin = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _playsLoss = new Dictionary<string, int>();
        private static int _gameHaisui;

        private static void CountPlay(int seat, string cardName)
        {
            int n;
            _gamePlays[seat].TryGetValue(cardName, out n);
            _gamePlays[seat][cardName] = n + 1;
        }

        private static void Bump(Dictionary<string, int> dict, string key, int delta)
        {
            int n;
            dict.TryGetValue(key, out n);
            dict[key] = n + delta;
        }

        private static int Main(string[] args)
        {
            SampleEffects.RegisterAll();
            CharacterEffects.RegisterAll();

            // 試合数とデッキは引数で指定（既定: 30戦、A=デッキ1、B=デッキ2）。
            // デッキ番号: 1=にあくろ×ニトグリ / 2=マスカット×ミルキー / 3=ココナッツ×やーや。
            int games = 30;
            if (args.Length > 0) int.TryParse(args[0], out games);
            if (games <= 0) games = 30;
            _deckA = 1; _deckB = 2;
            if (args.Length > 1) int.TryParse(args[1], out _deckA);
            if (args.Length > 2) int.TryParse(args[2], out _deckB);
            if (_deckA < 1 || _deckA > 3) _deckA = 1;
            if (_deckB < 1 || _deckB > 3) _deckB = 2;
            int winsA = 0, winsB = 0, firstPlayerWins = 0, deckOuts = 0, totalHaisui = 0, stuck = 0;
            var turnsList = new List<int>();

            for (int gi = 0; gi < games; gi++)
            {
                bool aIsP0 = gi % 2 == 0;
                var r = RunGame(1000 + gi * 37, aIsP0);
                turnsList.Add(r.Turns);
                totalHaisui += r.Haisui;
                if (r.DeckOut) deckOuts++;
                if (r.Stuck) stuck++;
                if (r.WinnerSeat == 0) firstPlayerWins++;
                if (r.WinnerIsA) winsA++; else winsB++;

                // 学習: 勝った脳は使ったカードの評価を上げ、負けた脳は下げる。
                var winnerBrain = r.WinnerIsA ? _brainA : _brainB;
                var loserBrain = r.WinnerIsA ? _brainB : _brainA;
                int winnerSeat = r.WinnerSeat;
                int loserSeat = 1 - r.WinnerSeat;
                foreach (var kv in _gamePlays[winnerSeat])
                {
                    winnerBrain.Add(kv.Key, 0.05 * Math.Min(kv.Value, 2));
                    Bump(_playsWin, kv.Key, kv.Value);
                }
                foreach (var kv in _gamePlays[loserSeat])
                {
                    loserBrain.Add(kv.Key, -0.035 * Math.Min(kv.Value, 2));
                    Bump(_playsLoss, kv.Key, kv.Value);
                }

                Console.WriteLine($"Game {gi + 1,2}: 勝者 {(r.WinnerIsA ? "A" : "B")}(P{winnerSeat}) / {r.Turns}ターン / {(r.DeckOut ? "デッキ切れ" : "ゲージ")} / 背水 {r.Haisui}{(r.Stuck ? " / 詰まり!" : "")}");
            }

            Console.WriteLine();
            Console.WriteLine($"=== {games}戦サマリ ===");
            Console.WriteLine($"脳A（{DeckName(_deckA)}）の勝ち: {winsA} / 脳B（{DeckName(_deckB)}）の勝ち: {winsB} / 先手(P0)の勝ち: {firstPlayerWins}");
            Console.WriteLine($"デッキ切れ決着: {deckOuts} / 背水の陣の総発動: {totalHaisui} / 詰まり: {stuck}");
            int window = Math.Min(10, games);
            double avgFirst = 0, avgLast = 0;
            for (int i = 0; i < window; i++) { avgFirst += turnsList[i]; avgLast += turnsList[games - window + i]; }
            Console.WriteLine($"平均ターン数: 最初の{window}戦 {avgFirst / window:F1} / 最後の{window}戦 {avgLast / window:F1}（ラウンド換算は約半分）");

            Console.WriteLine();
            Console.WriteLine("=== カード別: 使用回数 / 勝ち試合での使用 / 負け試合での使用 / 最終評価 A / B ===");
            var names = new List<string>();
            foreach (var def in DeckOf(_deckA))
            {
                if (!names.Contains(def.Name)) names.Add(def.Name);
            }
            foreach (var def in DeckOf(_deckB))
            {
                if (!names.Contains(def.Name)) names.Add(def.Name);
            }
            var rows = new List<string>();
            foreach (var name in names)
            {
                int w, l;
                _playsWin.TryGetValue(name, out w);
                _playsLoss.TryGetValue(name, out l);
                rows.Add($"{w + l,3}回 | 勝ち時 {w,3} / 負け時 {l,3} | A {_brainA.Get(name):+0.00;-0.00} / B {_brainB.Get(name):+0.00;-0.00} | {name}");
            }
            rows.Sort((a, b) => int.Parse(b.Substring(0, 3)).CompareTo(int.Parse(a.Substring(0, 3))));
            foreach (var row in rows) Console.WriteLine(row);
            return 0;
        }

        private static int _deckA = 1, _deckB = 2;

        private static List<CardDefinition> DeckOf(int id)
        {
            switch (id)
            {
                case 2: return PreconstructedDecks.MuscatKohaku();
                case 3: return PreconstructedDecks.CoconutYaya();
                default: return PreconstructedDecks.NiakuroNitoguri();
            }
        }

        private static string DeckName(int id)
        {
            switch (id)
            {
                case 2: return "マスカット×ミルキー";
                case 3: return "ココナッツ×やーや";
                default: return "にあくろ×ニトグリ";
            }
        }

        private struct GameResult
        {
            public int WinnerSeat;
            public bool WinnerIsA;
            public int Turns;
            public bool DeckOut;
            public int Haisui;
            public bool Stuck;
        }

        private static GameResult RunGame(int seed, bool aIsP0)
        {
            _log.Clear();
            _gamePlays[0].Clear();
            _gamePlays[1].Clear();
            _gameHaisui = 0;
            _seatIsA = aIsP0 ? new[] { true, false } : new[] { false, true };
            var deckP0 = aIsP0 ? DeckOf(_deckA) : DeckOf(_deckB);
            var deckP1 = aIsP0 ? DeckOf(_deckB) : DeckOf(_deckA);
            _g = new GameEngine(deckP0, deckP1, seed);

            for (int p = 0; p < 2; p++)
            {
                bool hasCheapUnit = false;
                foreach (var c in _g.Players[p].Hand)
                {
                    if (c.Definition is UnitCardDefinition && c.Definition.Cost <= 3) hasCheapUnit = true;
                }
                if (!hasCheapUnit) _g.Mulligan(p);
            }

            int guard = 0;
            while (!_g.IsGameOver && guard++ < 400)
            {
                int me = _g.ActivePlayerIndex;
                _g.StartTurn();
                if (_g.IsGameOver) break;
                ResolvePendings();
                PlayPhase(me);
                AbilityPhase(me);
                AttackPhase(me);
                ResolvePendings();
                EndPhase(me);
            }

            bool stuck = false;
            foreach (var line in _log) if (line.Contains("詰まった")) stuck = true;
            if (stuck)
            {
                Console.WriteLine($"  [診断] 詰まり時の待ち状態: 背水={_g.PendingHaisuiPlayer} 捨て札={_g.PendingDiscardByPlayer} 自己中={_g.PendingSelfishByPlayer} 回復={_g.PendingHealByPlayer} ダメージ={_g.PendingDamageByPlayer} 強化={_g.PendingBuffByPlayer} 自分強化={_g.PendingOwnBuffByPlayer} ドロー選択={_g.PendingDrawPickByPlayer} 無料配置={_g.PendingDeployByPlayer} 守護付与={_g.PendingGuardGrantByPlayer} デッキ配置={_g.PendingDeckDeployByPlayer} 富豪選択={_g.PendingRichChoiceByPlayer} 手練手管={_g.PendingMasterChoiceByPlayer} コピー対象={_g.PendingMasterCopyByPlayer} 手札破棄待ち={_g.WaitingDiscard}");
                Console.WriteLine($"  [診断] P0 手札{_g.Players[0].Hand.Count} 場{_g.Players[0].Field.Count} / P1 手札{_g.Players[1].Hand.Count} 場{_g.Players[1].Field.Count} / ゲージ {_g.Gauge.Position}");
            }
            int winnerSeat = _g.WinnerIndex >= 0 ? _g.WinnerIndex : 0;
            return new GameResult
            {
                WinnerSeat = winnerSeat,
                WinnerIsA = _seatIsA[winnerSeat],
                Turns = _g.GlobalTurn,
                DeckOut = _g.IsGameOver && !_g.Gauge.IsFinished,
                Haisui = _gameHaisui,
                Stuck = stuck,
            };
        }

        // ---------- スコア: そのプレイヤーが勝ちにどれだけ近いか（+15で勝ち） ----------
        private static int Score(int p) => p == 0 ? -_g.Gauge.Position : _g.Gauge.Position;

        private static bool CanAttackNow(UnitState u)
            => !u.HasAttackedThisTurn && (u.SummonedTurn < _g.GlobalTurn || u.HasHaste
                || u.Definition.CanAttackUnitsWhenPlayed
                || (u.CopiedDefinition != null && u.CopiedDefinition.CanAttackUnitsWhenPlayed));

        // ---------- 選択待ちの解決 ----------
        private static void ResolvePendings()
        {
            for (int i = 0; i < 40; i++)
            {
                if (_g.IsGameOver) return;

                if (_g.PendingHaisuiPlayer != null)
                {
                    int p = _g.PendingHaisuiPlayer.Value;
                    _log.Add($"  ★ 背水の陣（P{p}）ゲージ {_g.Gauge.Position}");
                    _gameHaisui++;
                    if (Score(p) <= -10) { if (_g.ChooseHaisui(HaisuiAction.PushBack)) continue; }
                    if (_g.Players[p].Hand.Count <= 4) { if (_g.ChooseHaisui(HaisuiAction.DrawCards)) continue; }
                    if (_g.ChooseHaisui(HaisuiAction.NextTurnPoints)) continue;
                    if (_g.ChooseHaisui(HaisuiAction.DrawCards)) continue;
                    if (_g.ChooseHaisui(HaisuiAction.PushBack)) continue;
                    return;
                }

                if (_g.PendingDiscardByPlayer != null)
                {
                    int by = _g.PendingDiscardByPlayer.Value;
                    var targetHand = _g.Players[1 - by].Hand;
                    CardInstance pick = null;
                    foreach (var c in targetHand) // 表向きは高い順に狙う
                    {
                        if (_g.IsHandCardRevealed(1 - by, c) && (pick == null || c.Definition.Cost > pick.Definition.Cost)) pick = c;
                    }
                    if (pick == null) foreach (var c in targetHand) { if (pick == null || c.Definition.Cost > pick.Definition.Cost) pick = c; }
                    if (pick != null) { _g.ChooseCardToDiscard(pick.InstanceId); continue; }
                    return;
                }

                if (_g.PendingSelfishByPlayer != null)
                {
                    int by = _g.PendingSelfishByPlayer.Value;
                    bool done = false;
                    // 自分の手札フェーズ: 一番重いカードを捨てる
                    CardInstance own = null;
                    foreach (var c in _g.Players[by].Hand) { if (own == null || c.Definition.Cost > own.Definition.Cost) own = c; }
                    if (own != null) done = _g.ChooseSelfishDiscard(own.InstanceId);
                    if (!done)
                    {
                        CardInstance opp = null;
                        foreach (var c in _g.Players[1 - by].Hand) { if (opp == null || c.Definition.Cost > opp.Definition.Cost) opp = c; }
                        if (opp != null) done = _g.ChooseSelfishDiscard(opp.InstanceId);
                    }
                    if (done) continue;
                    return;
                }

                if (_g.PendingHealByPlayer != null)
                {
                    int by = _g.PendingHealByPlayer.Value;
                    UnitState best = null;
                    foreach (var u in _g.Players[by].Field)
                    {
                        if (u.DamageTaken > 0 && (best == null || u.DamageTaken > best.DamageTaken)) best = u;
                    }
                    if (best != null) { _g.ChooseHealTarget(best.Instance.InstanceId); continue; }
                    return;
                }

                if (_g.PendingDamageByPlayer != null)
                {
                    int by = _g.PendingDamageByPlayer.Value;
                    UnitState best = null;
                    foreach (var u in _g.Players[1 - by].Field)
                    {
                        if (best == null) { best = u; continue; }
                        bool bestDies = best.CurrentHealth <= 2;
                        bool uDies = u.CurrentHealth <= 2;
                        if (uDies && !bestDies) best = u;
                        else if (uDies == bestDies && u.Attack > best.Attack) best = u;
                    }
                    if (best != null) { _g.ChooseDamageTarget(best.Instance.InstanceId); continue; }
                    return;
                }

                if (_g.PendingBuffByPlayer != null)
                {
                    int by = _g.PendingBuffByPlayer.Value;
                    // 攻撃力の高い順に試し、受け付けられたユニットを選ぶ
                    // （同じユニットは1回だけなので、強化済みはエンジンが弾く）。
                    var candidates = new System.Collections.Generic.List<UnitState>(_g.Players[by].Field);
                    candidates.Sort((a, b) => b.Attack.CompareTo(a.Attack));
                    bool progressed = false;
                    foreach (var u in candidates)
                    {
                        if (_g.ChooseBuffTarget(u.Instance.InstanceId)) { progressed = true; break; }
                    }
                    if (progressed) continue;
                    return;
                }

                if (_g.PendingOwnBuffByPlayer != null)
                {
                    int by = _g.PendingOwnBuffByPlayer.Value;
                    UnitState best = null;
                    foreach (var u in _g.Players[by].Field)
                    {
                        if (CanAttackNow(u) && (best == null || _g.EffectiveAttackWhenAttacking(u) > _g.EffectiveAttackWhenAttacking(best))) best = u;
                    }
                    if (best == null)
                    {
                        foreach (var u in _g.Players[by].Field)
                        {
                            if (best == null || u.Attack > best.Attack) best = u;
                        }
                    }
                    if (best != null) { _g.ChooseOwnBuffTarget(best.Instance.InstanceId); continue; }
                    return;
                }

                if (_g.PendingDrawPickByPlayer != null)
                {
                    CardInstance pick = null;
                    foreach (var c in _g.PendingDrawPool)
                    {
                        if (pick == null) { pick = c; continue; }
                        bool pickIsUnit = pick.Definition is UnitCardDefinition;
                        bool cIsUnit = c.Definition is UnitCardDefinition;
                        if (cIsUnit && !pickIsUnit) pick = c;
                        else if (cIsUnit == pickIsUnit && c.Definition.Cost > pick.Definition.Cost) pick = c;
                    }
                    if (pick != null) { _g.ChooseDrawPick(pick.InstanceId); continue; }
                    return;
                }

                if (_g.PendingDeployByPlayer != null)
                {
                    // 無数の選択肢ミルキー: 手札から一番重いユニットを無料で出す。
                    int by = _g.PendingDeployByPlayer.Value;
                    var candidates = new List<CardInstance>();
                    foreach (var c in _g.Players[by].Hand)
                    {
                        if (c.Definition is UnitCardDefinition) candidates.Add(c);
                    }
                    candidates.Sort((a, b) => b.Definition.Cost.CompareTo(a.Definition.Cost));
                    bool progressed = false;
                    foreach (var c in candidates)
                    {
                        if (_g.ChooseFreeDeploy(c.InstanceId)) { progressed = true; break; }
                    }
                    if (progressed) continue;
                    return;
                }

                if (_g.PendingGuardGrantByPlayer != null)
                {
                    // ゲーマーマスカット: 守護をまだ持たない一番強い味方に付与する。
                    int by = _g.PendingGuardGrantByPlayer.Value;
                    UnitState best = null;
                    foreach (var u in _g.Players[by].Field)
                    {
                        if (u.HasGuard) continue;
                        if (best == null || u.Attack > best.Attack) best = u;
                    }
                    if (best == null && _g.Players[by].Field.Count > 0) best = _g.Players[by].Field[0];
                    if (best != null) { _g.ChooseGuardTarget(best); continue; }
                    return;
                }

                if (_g.PendingDeckDeployByPlayer != null)
                {
                    // シャインマスカット: デッキ候補から一番重い守護を出す。
                    CardInstance pick = null;
                    foreach (var c in _g.PendingDeckDeployCandidates)
                    {
                        if (pick == null || c.Definition.Cost > pick.Definition.Cost) pick = c;
                    }
                    if (pick != null) { _g.ChooseDeckDeploy(pick.InstanceId); continue; }
                    return;
                }

                if (_g.PendingMasterCopyByPlayer != null)
                {
                    // 手練手管のコピー対象: 手札→墓地→場の順に、一番コストの高いユニットを選ぶ。
                    int by = _g.PendingMasterCopyByPlayer.Value;
                    var meP = _g.Players[by];
                    CardInstance bestHand = null;
                    foreach (var c in meP.Hand)
                    {
                        if (c.Definition is UnitCardDefinition && !ReferenceEquals(c.Definition, CharacterCards.YayaMaster)
                            && (bestHand == null || c.Definition.Cost > bestHand.Definition.Cost)) bestHand = c;
                    }
                    if (bestHand != null && _g.ChooseMasterCopyTarget(MasterCopyZone.Hand, bestHand.InstanceId)) continue;
                    CardInstance bestGrave = null;
                    foreach (var c in meP.Graveyard)
                    {
                        if (c.Definition is UnitCardDefinition && !ReferenceEquals(c.Definition, CharacterCards.YayaMaster)
                            && (bestGrave == null || c.Definition.Cost > bestGrave.Definition.Cost)) bestGrave = c;
                    }
                    if (bestGrave != null && _g.ChooseMasterCopyTarget(MasterCopyZone.Graveyard, bestGrave.InstanceId)) continue;
                    UnitState bestField = null;
                    foreach (var u in meP.Field)
                    {
                        if (ReferenceEquals(u.Definition, CharacterCards.YayaMaster)) continue;
                        if (bestField == null || u.Definition.Cost > bestField.Definition.Cost) bestField = u;
                    }
                    if (bestField != null && _g.ChooseMasterCopyTarget(MasterCopyZone.Field, bestField.Instance.InstanceId)) continue;
                    return;
                }

                if (_g.PendingMasterChoiceByPlayer != null)
                {
                    // 手練手管: 全体3ダメで2体以上倒せる→モード2 / 未コピーで良い対象があれば→モード4 /
                    // 手札が少なければ→モード1 / それ以外は→モード3。
                    int by = _g.PendingMasterChoiceByPlayer.Value;
                    var meP = _g.Players[by];
                    var oppP = _g.Players[1 - by];
                    int killable = 0;
                    foreach (var u in oppP.Field) if (u.CurrentHealth <= 3) killable++;
                    if (killable >= 2 && _g.ChooseMasterMode(2)) continue;
                    bool copiedAlready = true;
                    foreach (var u in meP.Field)
                    {
                        if (ReferenceEquals(u.Definition, CharacterCards.YayaMaster) && u.CopiedDefinition == null) copiedAlready = false;
                    }
                    bool hasCopyTarget = false;
                    foreach (var c in meP.Hand) if (c.Definition is UnitCardDefinition && c.Definition.Cost >= 4 && !ReferenceEquals(c.Definition, CharacterCards.YayaMaster)) hasCopyTarget = true;
                    foreach (var c in meP.Graveyard) if (c.Definition is UnitCardDefinition && c.Definition.Cost >= 4 && !ReferenceEquals(c.Definition, CharacterCards.YayaMaster)) hasCopyTarget = true;
                    if (!copiedAlready && hasCopyTarget && _g.ChooseMasterMode(4)) continue;
                    if (killable >= 1 && oppP.Field.Count >= 2 && _g.ChooseMasterMode(2)) continue;
                    if (meP.Hand.Count <= 5 && _g.ChooseMasterMode(1)) continue;
                    if (_g.ChooseMasterMode(3)) continue;
                    if (_g.ChooseMasterMode(1)) continue;
                    return;
                }

                if (_g.PendingRichChoiceByPlayer != null)
                {
                    // 器用富豪やーや: 3ダメージで倒せる相手がいればモード2、なければドロー、それもダメならポイント。
                    int by = _g.PendingRichChoiceByPlayer.Value;
                    bool killable = false;
                    foreach (var u in _g.Players[1 - by].Field)
                    {
                        if (u.CurrentHealth <= 3) killable = true;
                    }
                    if (killable && _g.ChooseRichMode(2)) continue;
                    if (_g.ChooseRichMode(1)) continue;
                    if (_g.ChooseRichMode(3)) continue;
                    return;
                }

                return; // 解決待ちなし
            }
        }

        // ---------- カードを出す ----------
        private static void PlayPhase(int me)
        {
            var player = _g.Players[me];
            bool played = true;
            int loopGuard = 0;
            while (played && loopGuard++ < 30 && !_g.IsGameOver)
            {
                played = false;

                // ユニット: 学習した評価値が高いものから出す（同点なら安い順）。
                CardInstance unit = null;
                double unitScore = double.NegativeInfinity;
                foreach (var c in player.Hand)
                {
                    if (c.Definition is UnitCardDefinition && c.Definition.Cost <= player.Points.Available
                        && player.Field.Count < GameConstants.FieldLimit)
                    {
                        double sc = BrainW(me, c.Definition.Name) * 2.0 - c.Definition.Cost * 0.05;
                        if (sc > unitScore) { unitScore = sc; unit = c; }
                    }
                }
                if (unit != null)
                {
                    if (_g.PlayUnit(unit.InstanceId))
                    {
                        _log.Add($"T{_g.GlobalTurn} P{me}: {unit.Definition.Name} を出す（ゲージ {_g.Gauge.Position}）");
                        CountPlay(me, unit.Definition.Name);
                        played = true;
                        ResolvePendings();
                        continue;
                    }
                }

                // 戦術: 状況に合うものから使う。
                foreach (var c in new List<CardInstance>(player.Hand))
                {
                    if (!(c.Definition is TacticCardDefinition tactic)) continue;
                    if (tactic.Cost > player.Points.Available) continue;
                    if (!ShouldPlayTactic(me, tactic)) continue;
                    if (_g.PlayTactic(c.InstanceId))
                    {
                        _log.Add($"T{_g.GlobalTurn} P{me}: {tactic.Name} を使う");
                        CountPlay(me, tactic.Name);
                        played = true;
                        ResolvePendings();
                        break;
                    }
                }
            }
        }

        private static bool ShouldPlayTactic(int me, TacticCardDefinition tactic)
        {
            if (BrainW(me, tactic.Name) <= -0.6) return false; // 学習で「使わない」になった戦術
            var player = _g.Players[me];
            var opponent = _g.Players[1 - me];
            switch (tactic.Name)
            {
                case "紳士の嗜み":
                case "三兎追えば二兎得る":
                case "ゲームの準備":
                    return player.Hand.Count <= 7;
                case "もしも～し":
                    return player.Field.Count < GameConstants.FieldLimit;
                case "にあくろの遊び心":
                    return opponent.Hand.Count >= 2;
                case "不可視の爆撃":
                    foreach (var u in opponent.Field) if (u.CurrentHealth <= 3) return true;
                    foreach (var u in opponent.Field) if (u.Attack >= 5) return true;
                    return false;
                case "一斉射撃":
                    return opponent.Field.Count >= 3;
                case "苦し紛れの一撃":
                    foreach (var u in opponent.Field) if (u.Definition.Cost >= 5) return true;
                    return opponent.Field.Count >= 3;
                case "ちゃぶ台返し！":
                    // 場負けしている時だけひっくり返す。
                    return opponent.Field.Count - player.Field.Count >= 2
                        || (opponent.Field.Count >= 3 && player.Field.Count <= 1);
                case "管理者権限":
                    return Score(me) <= -8;
                case "最前線攻略tv⁉︎":
                    // ランプ: 基本ポイント10未満なら使う（重いユニットを抱えている時ほど優先される想定で常時可）。
                    return player.Points.BasePoints < 10;
                case "酒！酒！酒！":
                    foreach (var u in player.Field)
                    {
                        if (!CanAttackNow(u)) continue;
                        int boosted = _g.EffectiveAttackWhenAttacking(u) + 2;
                        if (Score(me) + boosted >= 13 || boosted >= 6) return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        // ---------- 超絶自己中にあくろの起動能力 ----------
        private static void AbilityPhase(int me)
        {
            foreach (var u in new List<UnitState>(_g.Players[me].Field))
            {
                if (!ReferenceEquals(u.Definition, CharacterCards.NiakuroSuper) || u.AbilityUsedThisTurn) continue;
                var opponent = _g.Players[1 - me];
                if (Score(me) >= 13)
                {
                    _g.UseSelfishAbility(u, 3);
                }
                else if (_g.Players[me].Hand.Count <= 3)
                {
                    _g.UseSelfishAbility(u, 1);
                    ResolvePendings();
                }
                else if (opponent.Field.Count > 0)
                {
                    UnitState biggest = null;
                    foreach (var e in opponent.Field) { if (biggest == null || e.Attack > biggest.Attack) biggest = e; }
                    _g.UseSelfishAbility(u, 2, biggest);
                }
                else
                {
                    _g.UseSelfishAbility(u, 3);
                }
            }
        }

        // ---------- 攻撃 ----------
        private static void AttackPhase(int me)
        {
            var attackers = new List<UnitState>(_g.Players[me].Field);
            attackers.Sort((a, b) => BrainW(me, b.Definition.Name).CompareTo(BrainW(me, a.Definition.Name)));
            foreach (var u in attackers)
            {
                if (_g.IsGameOver) return;
                if (!_g.Players[me].Field.Contains(u) || !CanAttackNow(u)) continue;
                int eff = _g.EffectiveAttackWhenAttacking(u);
                var opponent = _g.Players[1 - me];

                // 守護がいる間は守護しか攻撃できない。
                bool enemyHasGuard = false;
                foreach (var e in opponent.Field) if (e.HasGuard) enemyHasGuard = true;

                UnitState target = null;
                foreach (var e in opponent.Field)
                {
                    if (enemyHasGuard && !e.HasGuard) continue;
                    // 一方的に倒せて、相手の攻撃力が高いなら戦闘で処理する。
                    if (e.Attack >= 3 && eff >= e.CurrentHealth && _g.EffectiveCounterAttack(e) < u.CurrentHealth)
                    {
                        if (target == null || e.Attack > target.Attack) target = e;
                    }
                }
                if (target == null)
                {
                    // 安いユニットは大型への特攻も辞さない。
                    foreach (var e in opponent.Field)
                    {
                        if (enemyHasGuard && !e.HasGuard) continue;
                        if (e.Attack >= 6 && eff >= e.CurrentHealth && u.Definition.Cost <= 3)
                        {
                            if (target == null || e.Attack > target.Attack) target = e;
                        }
                    }
                }

                if (target != null)
                {
                    _g.AttackUnit(u, target);
                    _log.Add($"T{_g.GlobalTurn} P{me}: {u.Definition.Name} が {target.Definition.Name} を攻撃（ゲージ {_g.Gauge.Position}）");
                }
                else if (_g.AttackFace(u))
                {
                    _log.Add($"T{_g.GlobalTurn} P{me}: {u.Definition.Name} が顔を攻撃 +{eff}（ゲージ {_g.Gauge.Position}）");
                }
                else if (enemyHasGuard)
                {
                    // 顔が守られているので、守護を殴って崩す（倒せる守護優先、なければ一番弱い守護）。
                    UnitState guardTarget = null;
                    foreach (var e in opponent.Field)
                    {
                        if (!e.HasGuard) continue;
                        if (guardTarget == null) { guardTarget = e; continue; }
                        bool eDies = eff >= e.CurrentHealth;
                        bool bestDies = eff >= guardTarget.CurrentHealth;
                        if (eDies && !bestDies) guardTarget = e;
                        else if (eDies == bestDies && e.CurrentHealth < guardTarget.CurrentHealth) guardTarget = e;
                    }
                    if (guardTarget != null)
                    {
                        _g.AttackUnit(u, guardTarget);
                        _log.Add($"T{_g.GlobalTurn} P{me}: {u.Definition.Name} が守護 {guardTarget.Definition.Name} を攻撃（ゲージ {_g.Gauge.Position}）");
                    }
                }
                ResolvePendings();
            }
        }

        // ---------- ターン終了（手札上限の処理つき） ----------
        private static void EndPhase(int me)
        {
            int guard = 0;
            while (guard++ < 20)
            {
                if (_g.EndTurn()) return;
                if (_g.WaitingDiscard)
                {
                    var player = _g.Players[me];
                    CardInstance worst = null;
                    foreach (var c in player.Hand) { if (worst == null || c.Definition.Cost > worst.Definition.Cost) worst = c; }
                    if (worst != null) { _g.DiscardFromHand(worst.InstanceId); continue; }
                }
                ResolvePendings();
                if (_g.IsGameOver) return;
            }
            _log.Add("  !! ターン終了できず詰まった（選択待ちが解消しない）");
        }
    }
}
