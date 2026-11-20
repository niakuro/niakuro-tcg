// このファイルについて:
//   簡易AI同士を対戦させるシミュレータ。同じデッキ同士のミラーマッチで
//   「ゲームが何ターンで終わるか」「背水の陣が発動するか」「詰まらないか」を確認する。
//   AIは単純な方針: ユニットを安い順に出し、連携を効かせ、基本は顔を殴る。
//   有利に倒せる相手（攻撃力3以上を一方的に倒せる）だけ戦闘で処理する。
using System;
using System.Collections.Generic;
using Niakuro.Tcg.Core;

namespace SimBattle
{
    internal static class Program
    {
        private static GameEngine _g;
        private static readonly List<string> _log = new List<string>();

        private static int Main()
        {
            SampleEffects.RegisterAll();
            CharacterEffects.RegisterAll();

            foreach (var seed in new[] { 7, 42, 123 })
            {
                RunGame(seed);
            }
            return 0;
        }

        private static void RunGame(int seed)
        {
            _log.Clear();
            _g = new GameEngine(PreconstructedDecks.NiakuroNitoguri(), PreconstructedDecks.NiakuroNitoguri(), seed);
            Console.WriteLine($"===== seed {seed} =====");

            // マリガン: コスト3以下のユニットが1枚もなければ引き直す。
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

            foreach (var line in _log) Console.WriteLine(line);
            if (_g.IsGameOver)
            {
                Console.WriteLine($"結果: P{_g.WinnerIndex} の勝ち / 合計{_g.GlobalTurn}ターン / 最終ゲージ {_g.Gauge.Position} / " +
                    $"残りデッキ P0:{_g.Players[0].Deck.Count} P1:{_g.Players[1].Deck.Count}");
            }
            else
            {
                Console.WriteLine($"結果: {_g.GlobalTurn}ターンで終わらず（詰まりの可能性）/ ゲージ {_g.Gauge.Position}");
            }
            Console.WriteLine();
        }

        // ---------- スコア: そのプレイヤーが勝ちにどれだけ近いか（+15で勝ち） ----------
        private static int Score(int p) => p == 0 ? -_g.Gauge.Position : _g.Gauge.Position;

        private static bool CanAttackNow(UnitState u)
            => !u.HasAttackedThisTurn && (u.SummonedTurn < _g.GlobalTurn || u.HasHaste || u.AuraHaste);

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
                        if ((_g.PendingDiscardSeesHand || _g.IsHandCardRevealed(1 - by, c))
                            && (pick == null || c.Definition.Cost > pick.Definition.Cost)) pick = c;
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

                // ユニット: 安い順に出して場を埋める（連携・速攻の土台）。
                CardInstance unit = null;
                foreach (var c in player.Hand)
                {
                    if (c.Definition is UnitCardDefinition && c.Definition.Cost <= player.Points.Available
                        && player.Field.Count < GameConstants.FieldLimit)
                    {
                        if (unit == null || c.Definition.Cost < unit.Definition.Cost) unit = c;
                    }
                }
                if (unit != null)
                {
                    if (_g.PlayUnit(unit.InstanceId))
                    {
                        _log.Add($"T{_g.GlobalTurn} P{me}: {unit.Definition.Name} を出す（ゲージ {_g.Gauge.Position}）");
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
                        played = true;
                        ResolvePendings();
                        break;
                    }
                }
            }
        }

        private static bool ShouldPlayTactic(int me, TacticCardDefinition tactic)
        {
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
            foreach (var u in new List<UnitState>(_g.Players[me].Field))
            {
                if (_g.IsGameOver) return;
                if (!_g.Players[me].Field.Contains(u) || !CanAttackNow(u)) continue;
                int eff = _g.EffectiveAttackWhenAttacking(u);
                var opponent = _g.Players[1 - me];

                UnitState target = null;
                foreach (var e in opponent.Field)
                {
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
                else
                {
                    _g.AttackFace(u);
                    _log.Add($"T{_g.GlobalTurn} P{me}: {u.Definition.Name} が顔を攻撃 +{eff}（ゲージ {_g.Gauge.Position}）");
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
