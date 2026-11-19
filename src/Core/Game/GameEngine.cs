// このファイルについて:
//   対戦全体の進行を統括する中心クラス。プレイヤー同士が絡む処理は全部ここ:
//   ターン進行、カードを出す、攻撃（顔/ユニット）、戦闘後のゲージ移動、背水の陣の発動、勝敗判定。
//   個別の計算（ゲージ、ポイント、戦闘ダメージ）は各専門クラスに任せ、ここは「順番と流れ」だけを見る。
using System;
using System.Collections.Generic;

namespace Niakuro.Tcg.Core
{
    public sealed class GameEngine
    {
        public PlayerState[] Players { get; }
        public FrontlineGauge Gauge { get; } = new FrontlineGauge();
        public HaisuiSystem Haisui { get; } = new HaisuiSystem();

        public int ActivePlayerIndex { get; private set; }
        public int GlobalTurn { get; private set; } // 両プレイヤー合計のターン数（召喚ターン判定用）
        public int WinnerIndex { get; private set; } = -1;
        public bool IsGameOver => WinnerIndex >= 0;

        // 背水の陣の発動待ちプレイヤー。いる間は他の行動ができない（必須発動）。
        public int? PendingHaisuiPlayer { get; private set; }

        // 「裏向きの手札を選んで捨てる」効果の選択待ち。
        // PendingDiscardByPlayer が選ぶ側、PendingDiscardTargetPlayer が捨てられる側。
        public int? PendingDiscardByPlayer { get; private set; }
        public int? PendingDiscardTargetPlayer { get; private set; }

        // 超絶自己中にあくろの「自分の手札1枚＋相手の手札1枚を選んで捨てる」選択待ち。
        public int? PendingSelfishByPlayer { get; private set; }
        private bool _selfishNeedOwn;
        private bool _selfishNeedOpponent;

        // ターン終了時に手札上限を超えていて、捨てるのを待っている状態。
        public bool WaitingDiscard { get; private set; }

        private bool _turnStarted;
        private readonly Random _random;

        public GameEngine(IReadOnlyList<CardDefinition> deckPlayer0, IReadOnlyList<CardDefinition> deckPlayer1, int seed = 0)
        {
            SampleEffects.RegisterAll();
            CharacterEffects.RegisterAll();

            var random = new Random(seed); // 同じ seed なら同じシャッフル（テスト用）
            _random = random;
            Players = new[]
            {
                new PlayerState(0, deckPlayer0, random),
                new PlayerState(1, deckPlayer1, random),
            };

            // 初期手札。後攻補正: 後攻は追加で1枚引き、最初のターンにポイント+1。
            Players[0].DrawCards(GameConstants.StartingHand);
            Players[1].DrawCards(GameConstants.StartingHand + GameConstants.SecondPlayerExtraDraw);
            Players[1].Points.AddPendingBonus(GameConstants.SecondPlayerPointBonus);

            ActivePlayerIndex = 0;
        }

        // ---------- 対戦準備 ----------

        // マリガンは最初のターンが始まる前だけ、各プレイヤー1回まで。
        public bool Mulligan(int playerIndex)
        {
            if (_turnStarted) return false;
            return Players[playerIndex].Mulligan();
        }

        // ---------- ターン進行 ----------

        public void StartTurn()
        {
            if (IsGameOver || PendingHaisuiPlayer != null) return;

            _turnStarted = true;
            GlobalTurn++;

            var player = Players[ActivePlayerIndex];
            player.Points.BeginTurn();
            foreach (var unit in player.Field)
            {
                unit.HasAttackedThisTurn = false;
                unit.AbilityUsedThisTurn = false;
            }

            // 毎ターン1ドロー。引けなければデッキ切れ負け。
            if (!player.DrawCards(1))
            {
                WinnerIndex = 1 - ActivePlayerIndex;
                return;
            }

            // ターン開始時ドロー（ゲーマーミルキー）: 自分の場にいる間だけ、自分のターン開始時に引く。
            foreach (var unit in new System.Collections.Generic.List<UnitState>(player.Field))
            {
                int turnDraw = 0;
                foreach (var d in DefsOf(unit)) turnDraw += d.DrawAtTurnStart;
                if (turnDraw > 0)
                {
                    DrawCardsForEffect(ActivePlayerIndex, turnDraw);
                    if (IsGameOver) return;
                }
            }
        }

        // 手札が上限を超えていたら false を返して捨て待ちになる。
        // DiscardFromHand で上限以下にしてから、もう一度呼ぶ。
        public bool EndTurn()
        {
            if (IsGameOver || PendingHaisuiPlayer != null || PendingDiscardByPlayer != null
                || PendingSelfishByPlayer != null || PendingHealByPlayer != null || PendingDamageByPlayer != null
                || PendingBuffByPlayer != null || PendingDrawPickByPlayer != null
                || PendingOwnBuffByPlayer != null || PendingDeployByPlayer != null
                || PendingGuardGrantByPlayer != null || PendingDeckDeployByPlayer != null
                || PendingRichChoiceByPlayer != null
                || PendingMasterChoiceByPlayer != null || PendingMasterCopyByPlayer != null) return false;

            var player = Players[ActivePlayerIndex];
            if (player.Hand.Count > GameConstants.HandLimit)
            {
                WaitingDiscard = true;
                return false;
            }
            WaitingDiscard = false;

            player.Points.EndTurn(); // 残りの半分が繰り越しになる
            ActivePlayerIndex = 1 - ActivePlayerIndex;
            return true;
        }

        public bool DiscardFromHand(Guid instanceId)
        {
            var player = Players[ActivePlayerIndex];
            var card = player.FindInHand(instanceId);
            if (card == null) return false;
            player.RemoveFromHand(card);
            player.SendToGraveyard(card);
            if (player.Hand.Count <= GameConstants.HandLimit) WaitingDiscard = false;
            return true;
        }

        // ---------- カードを出す ----------

        public bool PlayUnit(Guid instanceId)
        {
            if (!CanAct(ActivePlayerIndex)) return false;
            var player = Players[ActivePlayerIndex];
            var card = player.FindInHand(instanceId);
            if (card == null || !(card.Definition is UnitCardDefinition)) return false;
            if (player.Field.Count >= GameConstants.FieldLimit) return false;
            if (!player.Points.TrySpend(card.Definition.Cost)) return false;

            player.RemoveFromHand(card);
            var playedUnit = new UnitState(card, ActivePlayerIndex, GlobalTurn);
            player.Field.Add(playedUnit);

            // 条件付き速攻（爆速退社ニトグリ）: 出した時点で自分の場に他のユニットがいれば速攻を得る。
            var playedDef = (UnitCardDefinition)card.Definition;
            if (playedDef.ConditionalHaste && player.Field.Count > 1) playedUnit.HasHaste = true;

            // 登場時効果。
            var unitDef = (UnitCardDefinition)card.Definition;
            ResolveOnPlayEffects(ActivePlayerIndex, unitDef);

            // 場が変わったのでオーラを再計算する。
            RefreshAuras();
            return true;
        }

        // 登場時効果の解決（通常のプレイと、無数の選択肢ミルキーの無料配置で共用）。
        // 基本は対象を取らないものを自動解決し、対象を取るものは選択待ちにする。
        private void ResolveOnPlayEffects(int playerIndex, UnitCardDefinition unitDef, UnitState sourceOverride = null)
        {
            if (unitDef.OnPlayEffectId == CharacterEffectIds.DiscardChosenFaceDown)
            {
                int opponent = 1 - playerIndex;
                if (Players[opponent].Hand.Count > 0)
                {
                    PendingDiscardByPlayer = playerIndex;
                    PendingDiscardTargetPlayer = opponent;
                }
            }
            else if (unitDef.OnPlayEffectId != null && EffectRegistry.IsRegistered(unitDef.OnPlayEffectId)
                && Players[playerIndex].Points.BasePoints >= unitDef.OnPlayMinBasePoints)
            {
                EffectRegistry.TryResolve(unitDef.OnPlayEffectId,
                    new EffectContext(this, playerIndex, unitDef.OnPlayEffectValue));
            }

            // 超絶自己中にあくろの登場時: 自分1枚＋相手1枚の選択捨て。
            if (unitDef.SelfishDiscardOnPlay) BeginSelfishDiscard(playerIndex);

            // 猫被りにあくろの登場時回復（ダメージを受けた味方がいる時だけ選択待ちになる）。
            if (unitDef.HealOnPlay > 0) BeginHealChoice(playerIndex, unitDef.HealOnPlay);

            // ミニトグリの登場時ダメージ（相手の場にユニットがいる時だけ選択待ちになる）。
            if (unitDef.DamageOnPlay > 0) BeginDamageChoice(playerIndex, unitDef.DamageOnPlay);

            // ゲーマーニトグリの登場時強化（自分のユニットを最大2体まで選ぶ）。
            if (unitDef.BuffAttackOnPlay > 0) BeginBuffChoice(playerIndex, unitDef.BuffAttackOnPlay, unitDef.BuffTargetsOnPlay);

            // 無数の選択肢ミルキーの無料配置（出せるユニットが手札にある時だけ選択待ちになる）。
            if (unitDef.FreeDeployOnPlay) BeginDeployChoice(playerIndex);

            // ゲーマーマスカットの守護付与（自分のユニット1体を選ぶ）。
            if (unitDef.GrantsGuardOnPlay) BeginGuardGrantChoice(playerIndex);

            // 器用富豪やーやの登場時モード選択。
            if (unitDef.RichChoiceDamage > 0)
            {
                PendingRichChoiceByPlayer = playerIndex;
                _richDamageAmount = unitDef.RichChoiceDamage;
            }

            // 手練手管やーやの登場時モード選択。
            if (unitDef.MasterChoiceDamage > 0 && sourceOverride == null)
            {
                PendingMasterChoiceByPlayer = playerIndex;
                _masterDamageAmount = unitDef.MasterChoiceDamage;
                _masterPicksRemaining = MasterPickCount(playerIndex, unitDef);
                _masterSource = FindEnteredUnit(playerIndex, unitDef);
                _masterAttacker = null;
                _masterDefender = null;
            }

            // ゲーマーやーやの条件付き自己強化＋ポイント回復（基本ポイントが条件以上なら発動）。
            if ((unitDef.SelfBuffOnPlayAttack != 0 || unitDef.SelfBuffOnPlayHealth != 0 || unitDef.PointsGainOnPlay != 0)
                && Players[playerIndex].Points.BasePoints >= unitDef.SelfBuffMinBasePoints)
            {
                UnitState self = sourceOverride;
                if (self == null)
                {
                    var field = Players[playerIndex].Field;
                    for (int i = field.Count - 1; i >= 0; i--)
                    {
                        if (ReferenceEquals(field[i].Definition, unitDef) && field[i].SummonedTurn >= GlobalTurn) { self = field[i]; break; }
                    }
                }
                if (self != null)
                {
                    self.AttackBuff += unitDef.SelfBuffOnPlayAttack;
                    self.MaxHealthBuff += unitDef.SelfBuffOnPlayHealth;
                    if (unitDef.PointsGainOnPlay != 0)
                        Players[playerIndex].Points.AddPointsThisTurn(unitDef.PointsGainOnPlay);
                }
            }
        }

        // 守護付与の選択待ち（ゲーマーマスカット）。
        public int? PendingGuardGrantByPlayer { get; private set; }

        private void BeginGuardGrantChoice(int playerIndex)
        {
            if (Players[playerIndex].Field.Count == 0) return;
            PendingGuardGrantByPlayer = playerIndex;
        }

        // 守護付与の解決: 選んだ自分のユニットが守護を得る（場を離れるまで残る）。
        public bool ChooseGuardTarget(UnitState target)
        {
            if (PendingGuardGrantByPlayer == null) return false;
            if (target == null || target.OwnerIndex != PendingGuardGrantByPlayer.Value) return false;
            if (!Players[target.OwnerIndex].Field.Contains(target)) return false;
            target.GrantedGuard = true;
            PendingGuardGrantByPlayer = null;
            return true;
        }

        // デッキから選んで場に出す選択待ち（シャインマスカットの破壊時効果）。
        public int? PendingDeckDeployByPlayer { get; private set; }
        private int _pendingDeckDeployMaxCost;

        // 選択肢になるデッキのカード（UI表示用）。
        public System.Collections.Generic.IReadOnlyList<CardInstance> PendingDeckDeployCandidates
        {
            get
            {
                var list = new System.Collections.Generic.List<CardInstance>();
                if (PendingDeckDeployByPlayer == null) return list;
                foreach (var c in Players[PendingDeckDeployByPlayer.Value].Deck)
                {
                    if (IsDeckDeployCandidate(c)) list.Add(c);
                }
                return list;
            }
        }

        private bool IsDeckDeployCandidate(CardInstance c)
        {
            return c.Definition is UnitCardDefinition def && def.Guard && def.Cost <= _pendingDeckDeployMaxCost;
        }

        private void BeginDeckDeployChoice(int playerIndex, int maxCost)
        {
            var player = Players[playerIndex];
            if (player.Field.Count >= GameConstants.FieldLimit) return;
            _pendingDeckDeployMaxCost = maxCost;
            bool any = false;
            foreach (var c in player.Deck)
            {
                if (IsDeckDeployCandidate(c)) { any = true; break; }
            }
            if (!any) return;
            PendingDeckDeployByPlayer = playerIndex;
        }

        // デッキからの配置の解決: 選んだユニットをコストを支払わずに場に出す。
        // 出したユニットの登場時効果も通常どおり解決する。
        public bool ChooseDeckDeploy(Guid instanceId)
        {
            if (PendingDeckDeployByPlayer == null) return false;
            int playerIndex = PendingDeckDeployByPlayer.Value;
            var player = Players[playerIndex];
            CardInstance card = null;
            foreach (var c in player.Deck)
            {
                if (c.InstanceId == instanceId) { card = c; break; }
            }
            if (card == null || !IsDeckDeployCandidate(card)) return false;
            if (player.Field.Count >= GameConstants.FieldLimit) return false;

            PendingDeckDeployByPlayer = null;
            player.Deck.Remove(card);
            var unit = new UnitState(card, playerIndex, GlobalTurn);
            player.Field.Add(unit);
            var def = (UnitCardDefinition)card.Definition;
            if (def.ConditionalHaste && player.Field.Count > 1) unit.HasHaste = true;
            ResolveOnPlayEffects(playerIndex, def);
            RefreshAuras();
            return true;
        }

        // 無料配置の選択待ち（無数の選択肢ミルキー）。
        // _pendingDeployCostLimit は「選択待ちになった瞬間の手札枚数」で、
        // 出せるのはそれよりコストが低いユニット。
        public int? PendingDeployByPlayer { get; private set; }
        private int _pendingDeployCostLimit;

        private void BeginDeployChoice(int playerIndex)
        {
            var player = Players[playerIndex];
            if (player.Field.Count >= GameConstants.FieldLimit) return;
            int limit = player.Hand.Count;
            bool any = false;
            foreach (var c in player.Hand)
            {
                if (c.Definition is UnitCardDefinition && c.Definition.Cost < limit) { any = true; break; }
            }
            if (!any) return;
            PendingDeployByPlayer = playerIndex;
            _pendingDeployCostLimit = limit;
        }

        // 無料配置の解決: 手札から選んだユニットをコストを支払わずに場に出す。
        // 出したユニットの登場時効果も通常どおり解決する。
        public bool ChooseFreeDeploy(Guid instanceId)
        {
            if (PendingDeployByPlayer == null) return false;
            int playerIndex = PendingDeployByPlayer.Value;
            var player = Players[playerIndex];
            var card = player.FindInHand(instanceId);
            if (card == null || !(card.Definition is UnitCardDefinition)) return false;
            if (card.Definition.Cost >= _pendingDeployCostLimit) return false;
            if (player.Field.Count >= GameConstants.FieldLimit) return false;

            PendingDeployByPlayer = null;
            player.RemoveFromHand(card);
            var unit = new UnitState(card, playerIndex, GlobalTurn);
            player.Field.Add(unit);
            var def = (UnitCardDefinition)card.Definition;
            if (def.ConditionalHaste && player.Field.Count > 1) unit.HasHaste = true;
            ResolveOnPlayEffects(playerIndex, def);
            RefreshAuras();
            return true;
        }

        // 「自分1枚＋相手1枚」選択待ちを開始する。手札が空の側はスキップする。
        private void BeginSelfishDiscard(int byPlayer)
        {
            _selfishNeedOwn = Players[byPlayer].Hand.Count > 0;
            _selfishNeedOpponent = Players[1 - byPlayer].Hand.Count > 0;
            if (_selfishNeedOwn || _selfishNeedOpponent) PendingSelfishByPlayer = byPlayer;
        }

        // 選択待ちの解決: 自分の手札からでも相手の手札からでも、まだ必要な側の1枚を選んで捨てる。
        public bool ChooseSelfishDiscard(Guid instanceId)
        {
            if (PendingSelfishByPlayer == null) return false;
            int by = PendingSelfishByPlayer.Value;

            if (_selfishNeedOwn)
            {
                var own = Players[by].FindInHand(instanceId);
                if (own != null)
                {
                    Players[by].RemoveFromHand(own);
                    Players[by].SendToGraveyard(own);
                    _selfishNeedOwn = false;
                    if (!_selfishNeedOpponent) PendingSelfishByPlayer = null;
                    return true;
                }
            }
            if (_selfishNeedOpponent)
            {
                var opp = Players[1 - by].FindInHand(instanceId);
                if (opp != null)
                {
                    Players[1 - by].RemoveFromHand(opp);
                    Players[1 - by].SendToGraveyard(opp);
                    _selfishNeedOpponent = false;
                    if (!_selfishNeedOwn) PendingSelfishByPlayer = null;
                    return true;
                }
            }
            return false;
        }

        // 選択待ちの解決: 相手の手札を1枚選んで捨てさせる。
        // 表向きのカードは中身が分かった上で狙える。裏向きのカードは中身が分からないまま選ぶ。
        public bool ChooseCardToDiscard(Guid instanceId)
        {
            if (PendingDiscardByPlayer == null || PendingDiscardTargetPlayer == null) return false;
            var target = Players[PendingDiscardTargetPlayer.Value];
            var card = target.FindInHand(instanceId);
            if (card == null) return false;

            target.RemoveFromHand(card);
            target.SendToGraveyard(card);
            PendingDiscardByPlayer = null;
            PendingDiscardTargetPlayer = null;
            return true;
        }

        // ---------- 「引いた中から選ぶ」選択（三兎追えば二兎得る） ----------

        public int? PendingDrawPickByPlayer { get; private set; }
        private readonly System.Collections.Generic.List<CardInstance> _pendingDrawPool
            = new System.Collections.Generic.List<CardInstance>();
        private int _pendingDrawPicksLeft;

        // 選択中プール（引いたカードの一時置き場）。UIはこの中身を見せて選ばせる。
        public System.Collections.Generic.IReadOnlyList<CardInstance> PendingDrawPool => _pendingDrawPool;

        // ドロー系効果用（三兎追えば二兎得る）: デッキの上から drawCount 枚をプールに入れ、
        // その中から keepCount 枚を選んで手札に加え、残りを墓地に置く。
        // デッキが drawCount 枚未満なら引けず、デッキ切れ負けになる（通常ドローと同じ）。
        public void DrawPickForEffect(int playerIndex, int drawCount, int keepCount)
        {
            if (IsGameOver) return;
            var player = Players[playerIndex];
            if (player.Deck.Count < drawCount)
            {
                WinnerIndex = 1 - playerIndex;
                return;
            }
            _pendingDrawPool.Clear();
            for (int i = 0; i < drawCount; i++)
            {
                var top = player.Deck[player.Deck.Count - 1];
                player.Deck.RemoveAt(player.Deck.Count - 1);
                _pendingDrawPool.Add(top);
            }
            // プールに引いた分も「効果で引いたカード」として数える（無数の選択肢ミルキー用）。
            player.EffectDrawsThisGame += drawCount;
            PendingDrawPickByPlayer = playerIndex;
            _pendingDrawPicksLeft = keepCount;
            if (_pendingDrawPicksLeft <= 0) FinishDrawPick();
        }

        // 選択待ちの解決: プールから1枚選んで手札に加える（必要枚数ぶん繰り返し呼ばれる）。
        public bool ChooseDrawPick(Guid instanceId)
        {
            if (PendingDrawPickByPlayer == null) return false;
            CardInstance picked = null;
            foreach (var c in _pendingDrawPool)
            {
                if (c.InstanceId == instanceId) { picked = c; break; }
            }
            if (picked == null) return false;

            _pendingDrawPool.Remove(picked);
            Players[PendingDrawPickByPlayer.Value].Hand.Add(picked);
            _pendingDrawPicksLeft--;
            if (_pendingDrawPicksLeft <= 0) FinishDrawPick();
            return true;
        }

        private void FinishDrawPick()
        {
            var player = Players[PendingDrawPickByPlayer.Value];
            foreach (var rest in _pendingDrawPool) player.SendToGraveyard(rest);
            _pendingDrawPool.Clear();
            PendingDrawPickByPlayer = null;
        }

        public bool PlayTactic(Guid instanceId)
        {
            if (!CanAct(ActivePlayerIndex)) return false;
            var player = Players[ActivePlayerIndex];
            var card = player.FindInHand(instanceId);
            if (card == null || !(card.Definition is TacticCardDefinition tactic)) return false;
            bool targetsOwnUnit = tactic.BuffOwnAttack != 0 || tactic.ReduceOwnMaxHealth != 0 || tactic.OwnDestroyAfterAttack;
            if (tactic.TargetDamage <= 0 && !targetsOwnUnit && !EffectRegistry.IsRegistered(tactic.EffectId)) return false;
            if (!player.Points.TrySpend(tactic.Cost)) return false;

            player.RemoveFromHand(card);
            if (tactic.TargetDamage > 0)
            {
                // 対象を取るダメージ戦術（不可視の爆撃）: 相手ユニット1体を選ぶ。
                BeginDamageChoice(ActivePlayerIndex, tactic.TargetDamage);
            }
            else if (targetsOwnUnit)
            {
                // 自分のユニット1体を対象に取る戦術（酒！酒！酒！）。
                BeginOwnBuffChoice(ActivePlayerIndex, tactic.BuffOwnAttack, tactic.ReduceOwnMaxHealth, tactic.OwnDestroyAfterAttack);
            }
            else
            {
                EffectRegistry.TryResolve(tactic.EffectId, new EffectContext(this, ActivePlayerIndex, tactic.EffectValue));
            }
            player.SendToGraveyard(card);
            return true;
        }

        // ---------- 攻撃 ----------

        // ユニットの実効定義: 本体の定義＋コピー中の定義（手練手管やーや）。
        private static System.Collections.Generic.IEnumerable<UnitCardDefinition> DefsOf(UnitState unit)
        {
            yield return unit.Definition;
            if (unit.CopiedDefinition != null) yield return unit.CopiedDefinition;
        }

        private static bool AnyDef(UnitState unit, System.Func<UnitCardDefinition, bool> pred)
        {
            foreach (var d in DefsOf(unit)) if (pred(d)) return true;
            return false;
        }

        // 器用富豪モードの実効ダメージ量（コピーで得た場合も含む）。
        private static int EffectiveRichDamage(UnitState unit)
        {
            int dmg = 0;
            foreach (var d in DefsOf(unit)) dmg = System.Math.Max(dmg, d.RichChoiceDamage);
            return dmg;
        }


        // 相手の場にある「攻撃時弱体」オーラの合計（ゲーマーにあくろ等）。複数いれば重なる。
        public int AttackDebuffAgainst(int playerIndex)
        {
            int total = 0;
            foreach (var unit in Players[1 - playerIndex].Field)
            {
                foreach (var d in DefsOf(unit)) total += d.AttackDebuffAura;
            }
            return total;
        }

        // キーワード能力「連携：特徴」のボーナス。自分の場に「自分以外の」その特徴のユニットが
        // 1体でもいればボーナスが付く（人数では重ならない）。
        // 付与された連携（秩序なき連携ニトグリ）は、もともと持っている連携と重なる。
        public int LinkageBonus(UnitState unit)
        {
            var field = Players[unit.OwnerIndex].Field;
            int bonus = 0;

            foreach (var def in DefsOf(unit))
            {
                if (def.LinkageTrait != null && def.LinkageAttackBonus > 0
                    && HasOtherWithTrait(field, unit, def.LinkageTrait))
                {
                    bonus += def.LinkageAttackBonus;
                }
            }

            foreach (var source in field)
            {
                foreach (var grantDef in DefsOf(source))
                {
                    var grantTrait = grantDef.GrantsLinkageTrait;
                    if (grantTrait == null || grantDef.GrantsLinkageBonus <= 0) continue;
                    if (HasOtherWithTrait(field, unit, grantTrait))
                    {
                        bonus += grantDef.GrantsLinkageBonus;
                    }
                }
            }
            return bonus;
        }

        private static bool HasOtherWithTrait(System.Collections.Generic.IReadOnlyList<UnitState> field,
            UnitState unit, string trait)
        {
            foreach (var other in field)
            {
                if (!ReferenceEquals(other, unit) && other.HasTrait(trait)) return true;
            }
            return false;
        }

        // ---------- オーラの再計算（秩序なき連携ニトグリ） ----------

        private bool _refreshingAuras;

        // 場の状況からオーラ修正を計算し直し、体力の最大値が尽きたユニットを墓地に送る。
        // 場が変わるたび（ユニットを出す・倒れる）に呼ぶ。オーラで倒れた場合は効果死で、ゲージは動かない。
        public void RefreshAuras()
        {
            if (_refreshingAuras) return;
            _refreshingAuras = true;
            try
            {
                while (true)
                {
                    RecomputeAuras();
                    UnitState dead = null;
                    foreach (var p in Players)
                    {
                        foreach (var u in p.Field)
                        {
                            if (u.IsDead) { dead = u; break; }
                        }
                        if (dead != null) break;
                    }
                    if (dead == null) break;
                    RemoveUnitFromField(dead);
                }
            }
            finally
            {
                _refreshingAuras = false;
            }
        }

        private void RecomputeAuras()
        {
            foreach (var p in Players)
            {
                foreach (var u in p.Field)
                {
                    u.AuraAttackBonus = 0;
                    u.AuraHealthModifier = 0;
                    // 効果ドロー連動（無数の選択肢ミルキー）: X = 効果で引いた枚数 ÷2（切り捨て）。
                    u.DrawScaleBonus = AnyDef(u, d => d.ScalesWithEffectDraws)
                        ? Players[u.OwnerIndex].EffectDrawsThisGame / 2
                        : 0;
                }
            }

            foreach (var p in Players)
            {
                foreach (var source in p.Field)
                {
                    if (!AnyDef(source, d => d.OrderlessAura)) continue;

                    bool anyAkuyuu = false;
                    foreach (var u in p.Field)
                    {
                        if (ReferenceEquals(u, source) || !u.HasTrait(CardTraits.Akuyuu)) continue;
                        anyAkuyuu = true;
                        u.AuraAttackBonus += 1;
                        u.AuraHealthModifier -= 1;
                    }
                    if (anyAkuyuu)
                    {
                        source.AuraAttackBonus += 1;
                        source.AuraHealthModifier -= 1;
                    }

                    bool anyZennyuu = false;
                    foreach (var u in p.Field)
                    {
                        if (ReferenceEquals(u, source) || !u.HasTrait(CardTraits.Zennyuu)) continue;
                        anyZennyuu = true;
                        u.AuraHealthModifier += 1;
                    }
                    if (anyZennyuu)
                    {
                        source.AuraHealthModifier += 1;
                    }
                }
            }

            // 守護シナジーのオーラ（成熟マスカット）: 守護を持つ味方の体力の最大値を上げる。
            // 付与された守護も数える。本人に守護がなければ本人には効かない。
            foreach (var p in Players)
            {
                foreach (var source in p.Field)
                {
                    foreach (var sourceDef in DefsOf(source))
                    {
                        if (sourceDef.GuardHealthAura <= 0) continue;
                        foreach (var u in p.Field)
                        {
                            if (u.HasGuard) u.AuraHealthModifier += sourceDef.GuardHealthAura;
                        }
                    }
                }
            }

            // 守護シナジーの攻/体オーラ（シャインマスカット）: 守護を持つ味方の攻撃力と体力の最大値を上げる。
            foreach (var p in Players)
            {
                foreach (var source in p.Field)
                {
                    foreach (var sourceDef in DefsOf(source))
                    {
                        if (sourceDef.GuardStatAura <= 0) continue;
                        foreach (var u in p.Field)
                        {
                            if (u.HasGuard)
                            {
                                u.AuraAttackBonus += sourceDef.GuardStatAura;
                                u.AuraHealthModifier += sourceDef.GuardStatAura;
                            }
                        }
                    }
                }
            }
        }

        // 攻撃時に実際に使う攻撃力（連携ボーナスを足し、弱体を引いて、0未満にはならない）。
        public int EffectiveAttackWhenAttacking(UnitState attacker)
        {
            return Math.Max(0, attacker.Attack + LinkageBonus(attacker) - AttackDebuffAgainst(attacker.OwnerIndex));
        }

        // 反撃時に実際に使う攻撃力（連携ボーナスは付くが、「攻撃時」ではないので弱体は受けない）。
        public int EffectiveCounterAttack(UnitState defender)
        {
            return defender.Attack + LinkageBonus(defender);
        }

        // プレイヤーへの直接攻撃: 攻撃力の分だけゲージが動く（このゲームで一番効率のいい押し方）。
        public bool AttackFace(UnitState attacker)
        {
            if (attacker != null && AnyDef(attacker, d => d.CanOnlyAttackUnits)) return false; // ユニットしか攻撃できない
            if (!CanAttackWith(attacker, faceAttack: true)) return false;
            int opponent = 1 - ActivePlayerIndex;
            // 守護: 相手の場に守護ユニットがいる間は、顔を攻撃できない。
            if (HasGuardUnit(opponent)) return false;
            // 手練手管・器用富豪: 攻撃の前にモード選択を解決してから攻撃する。
            if (attacker.Definition.MasterChoiceDamage > 0)
            {
                BeginMasterAttackChoice(attacker, isFace: true, defender: null);
                return true;
            }
            int richDmg = EffectiveRichDamage(attacker);
            if (richDmg > 0)
            {
                BeginRichChoice(attacker, isFace: true, defender: null, richDmg);
                return true;
            }
            ResolveFaceAttack(attacker);
            return true;
        }

        // 顔攻撃の解決本体（器用富豪の選択が終わった後の再開からも呼ばれる）。
        private void ResolveFaceAttack(UnitState attacker)
        {
            int opponent = 1 - ActivePlayerIndex;
            attacker.AttacksUsedThisTurn++;

            Gauge.Push(EffectiveAttackWhenAttacking(attacker), opponent,
                targetHaisuiUnused: Haisui.CanUse(opponent),
                piercesProtection: attacker.PiercesHaisuiProtection);
            AfterGaugePush(opponent);
            DestroyAfterAttackIfNeeded(attacker);
            return;
        }

        // 攻撃後の処理: 戦闘を生き残っていれば、「攻撃後破壊」（酒！酒！酒！）が優先され、
        // 次に「攻撃後に手札に戻る」（爆速退社ニトグリ）が処理される。どちらもゲージは動かない。
        private void DestroyAfterAttackIfNeeded(UnitState attacker)
        {
            if (!Players[attacker.OwnerIndex].Field.Contains(attacker)) return;
            if (attacker.DestroyAfterAttack)
            {
                RemoveUnitFromField(attacker);
            }
            else if (attacker.Definition.ReturnsToHandAfterAttack)
            {
                ReturnUnitToHand(attacker);
            }
        }

        // ユニットを持ち主の手札に戻す。破壊ではないので「破壊された時」の効果は発動しない。
        // ダメージや強化は場を離れる時にリセットされる（新しい UnitState として出し直す）。
        private void ReturnUnitToHand(UnitState unit)
        {
            var owner = Players[unit.OwnerIndex];
            if (!owner.Field.Remove(unit)) return;
            owner.Hand.Add(unit.Instance);
            RefreshAuras();
        }

        // ユニットへの攻撃: 食らい合い。倒したら戦闘撃破としてゲージが動く（差分方式）。
        public bool AttackUnit(UnitState attacker, UnitState defender)
        {
            if (!CanAttackWith(attacker, faceAttack: false)) return false;
            if (defender == null || defender.OwnerIndex == ActivePlayerIndex) return false;
            // 守護: 相手の場に守護ユニットがいる間は、守護ユニットしか攻撃できない。
            if (!defender.HasGuard && HasGuardUnit(defender.OwnerIndex)) return false;
            // 手練手管・器用富豪: 攻撃の前にモード選択を解決してから戦闘する。
            if (attacker.Definition.MasterChoiceDamage > 0)
            {
                BeginMasterAttackChoice(attacker, isFace: false, defender: defender);
                return true;
            }
            int richDmg = EffectiveRichDamage(attacker);
            if (richDmg > 0)
            {
                BeginRichChoice(attacker, isFace: false, defender: defender, richDmg);
                return true;
            }
            ResolveUnitAttack(attacker, defender);
            return true;
        }

        // ユニット戦闘の解決本体（器用富豪の選択が終わった後の再開からも呼ばれる）。
        private void ResolveUnitAttack(UnitState attacker, UnitState defender)
        {
            attacker.AttacksUsedThisTurn++;

            // 戦闘ダメージの調整: 登場ターンの無効（ゲーマーココナッツ）は0に、
            // 受けるダメージの上限（孤軍奮闘ココナッツ）は上限までに切り詰める。
            int damageToDefender = IsBattleDamageImmuneThisTurn(defender) ? 0 : EffectiveAttackWhenAttacking(attacker);
            int capDef = BattleDamageCapOf(defender);
            if (capDef > 0) damageToDefender = Math.Min(damageToDefender, capDef);
            int damageToAttacker = IsBattleDamageImmuneThisTurn(attacker) ? 0 : EffectiveCounterAttack(defender);
            int capAtk = BattleDamageCapOf(attacker);
            if (capAtk > 0) damageToAttacker = Math.Min(damageToAttacker, capAtk);
            var result = CombatResolver.ResolveBattle(attacker, defender,
                attackOverride: damageToDefender,
                counterOverride: damageToAttacker);

            if (result.DefenderDied) RemoveUnitFromField(defender);
            if (result.AttackerDied) RemoveUnitFromField(attacker);

            if (result.GaugeDelta != 0)
            {
                int pushedPlayer = result.GaugeDelta > 0 ? 0 : 1;
                Gauge.Push(Math.Abs(result.GaugeDelta), pushedPlayer,
                    targetHaisuiUnused: Haisui.CanUse(pushedPlayer),
                    piercesProtection: attacker.PiercesHaisuiProtection && result.DefenderDied);
                AfterGaugePush(pushedPlayer);
            }
            // 攻撃して相手ユニットを倒したら自己回復（百発百中ココナッツ）。相打ちで自分も落ちた場合は回復しない。
            int selfHeal = 0;
            foreach (var d in DefsOf(attacker)) selfHeal = Math.Max(selfHeal, d.SelfHealOnBattleKill);
            if (result.DefenderDied && selfHeal > 0
                && Players[attacker.OwnerIndex].Field.Contains(attacker))
                attacker.Heal(selfHeal);
            DestroyAfterAttackIfNeeded(attacker);
            return;
        }

        // 効果による除去: ゲージは動かない（戦闘撃破との差別化）。
        public void RemoveUnitByEffect(UnitState unit)
        {
            RemoveUnitFromField(unit);
        }

        // ---------- 超絶自己中にあくろ ----------

        // 手札のカードが表向きかどうか。個別に表向きにされたカードか、
        // 「全表向き」オーラを持つユニットがどちらかの場にいる間は全部表向き。
        // オーラ側は動的判定なので、オーラのユニットが場を離れれば手札は裏に戻る
        // （個別に表向きにされたカードだけは表向きのまま）。
        public bool IsHandCardRevealed(int playerIndex, CardInstance card)
        {
            if (Players[playerIndex].IsRevealed(card)) return true;
            foreach (var p in Players)
            {
                foreach (var unit in p.Field)
                {
                    if (AnyDef(unit, d => d.RevealsAllHands)) return true;
                }
            }
            return false;
        }

        // 起動型能力（1ターンに1度、3つから1つ）:
        // 1 = 1枚ドローし、自分1枚＋相手1枚を選んで捨てる（選択待ちになる）
        // 2 = 相手のユニット1体の体力の最大値を1下げる（恒久。最大値が尽きれば効果死として墓地へ）
        // 3 = 戦線ゲージを相手側に1進める（能力であって攻撃ではないので、背水の陣は発動しない）
        public bool UseSelfishAbility(UnitState unit, int choice, UnitState target = null)
        {
            if (unit == null || !AnyDef(unit, d => d.ActivatedAbilityId == CharacterEffectIds.SelfishModal)) return false;
            if (!CanAct(unit.OwnerIndex)) return false;
            if (unit.AbilityUsedThisTurn) return false;

            int owner = unit.OwnerIndex;
            int opponent = 1 - owner;

            switch (choice)
            {
                case 1:
                    unit.AbilityUsedThisTurn = true;
                    DrawCardsForEffect(owner, 1);
                    if (!IsGameOver) BeginSelfishDiscard(owner);
                    return true;
                case 2:
                    if (target == null || target.OwnerIndex != opponent) return false;
                    unit.AbilityUsedThisTurn = true;
                    target.HealthReduction += 1;
                    if (target.IsDead) RemoveUnitFromField(target);
                    return true;
                case 3:
                    unit.AbilityUsedThisTurn = true;
                    Gauge.Push(1, opponent, targetHaisuiUnused: false, piercesProtection: true);
                    if (Gauge.IsFinished) WinnerIndex = owner;
                    return true;
                default:
                    return false;
            }
        }

        // ---------- 背水の陣 ----------

        public bool ChooseHaisui(HaisuiAction action)
        {
            if (PendingHaisuiPlayer == null) return false;
            int playerIndex = PendingHaisuiPlayer.Value;
            if (!Haisui.TryActivate(playerIndex, action)) return false;

            switch (action)
            {
                case HaisuiAction.PushBack:
                    Gauge.PushBack(GameConstants.HaisuiPushBack, playerIndex);
                    // 発動条件（残り5以下）からは通常ここで決着しないが、
                    // 万一ゲージが端まで届いた場合の勝者は押し戻した側。
                    if (Gauge.IsFinished) WinnerIndex = playerIndex;
                    break;
                case HaisuiAction.DrawCards:
                    DrawCardsForEffect(playerIndex, GameConstants.HaisuiDraw);
                    break;
                case HaisuiAction.NextTurnPoints:
                    Players[playerIndex].Points.AddPendingBonus(GameConstants.HaisuiPointBonus);
                    break;
            }
            PendingHaisuiPlayer = null;
            return true;
        }

        // ---------- 効果から呼ばれる共通処理 ----------

        // ドロー系効果用。引けなければデッキ切れ負け。
        public bool DrawCardsForEffect(int playerIndex, int count)
        {
            if (IsGameOver) return false;
            int deckBefore = Players[playerIndex].Deck.Count;
            bool ok = Players[playerIndex].DrawCards(count);
            // 実際に引けた枚数だけ「効果ドロー」として数える（無数の選択肢ミルキー用）。
            Players[playerIndex].EffectDrawsThisGame += deckBefore - Players[playerIndex].Deck.Count;
            if (!ok) WinnerIndex = 1 - playerIndex;
            // ドローで X が伸びるユニットがいるかもしれないので、場の数値を計算し直す。
            if (!IsGameOver) RefreshAuras();
            return ok;
        }

        // 全体ダメージ効果用（一斉射撃）: 相手のユニット全員にダメージ。効果ダメージなのでゲージは動かない。
        public void DamageAllOpponentUnitsForEffect(int playerIndex, int amount)
        {
            if (IsGameOver) return;
            var opponent = Players[1 - playerIndex];
            var targets = new System.Collections.Generic.List<UnitState>(opponent.Field);
            foreach (var unit in targets)
            {
                unit.TakeDamage(amount);
                if (unit.IsDead) RemoveUnitFromField(unit);
            }
        }

        // ランダム破壊効果用（苦し紛れの一撃）: 相手のユニットをランダムに count 体破壊する。
        // 効果による破壊なのでゲージは動かない。相手の場が空なら何も起きない。
        public void DestroyRandomOpponentUnitsForEffect(int playerIndex, int count)
        {
            if (IsGameOver) return;
            var opponent = Players[1 - playerIndex];
            for (int i = 0; i < count && opponent.Field.Count > 0; i++)
            {
                var victim = opponent.Field[_random.Next(opponent.Field.Count)];
                RemoveUnitFromField(victim);
            }
        }

        // 全体ダメージ効果用（ちゃぶ台返し！）: お互いのユニット全員に同時にダメージ。
        // 全員にダメージを与えてから、倒れたユニットをまとめて墓地に送る。効果ダメージなのでゲージは動かない。
        public void DamageAllUnitsForEffect(int amount)
        {
            if (IsGameOver) return;
            var targets = new System.Collections.Generic.List<UnitState>();
            foreach (var p in Players) targets.AddRange(p.Field);
            foreach (var unit in targets) unit.TakeDamage(amount);
            foreach (var unit in targets)
            {
                if (unit.IsDead) RemoveUnitFromField(unit);
            }
        }

        // サーチ系効果用（もしも～し）: デッキからコスト minCost 以上のユニットをランダムに1枚選び、手札に加える。
        // デッキに対象がなければ何も起きない（デッキ切れ負けにはならない）。
        public void FetchRandomUnitForEffect(int playerIndex)
        {
            FetchRandomForEffect(playerIndex, CardType.Unit, 1);
        }

        public void FetchRandomBigUnitForEffect(int playerIndex, int minCost)
        {
            FetchRandomForEffect(playerIndex, CardType.Unit, 1, minCost);
        }

        public void FetchRandomSmallUnitForEffect(int playerIndex, int maxCost)
        {
            FetchRandomForEffect(playerIndex, CardType.Unit, 1, 0, maxCost);
        }

        // サーチ系効果用（ゲームの準備）: デッキから戦術をランダムに count 枚選び、手札に加える。
        // デッキの戦術が足りなければ、ある分だけ加える。
        public void FetchRandomTacticsForEffect(int playerIndex, int count)
        {
            FetchRandomForEffect(playerIndex, CardType.Tactic, count);
        }

        private void FetchRandomForEffect(int playerIndex, CardType type, int count, int minCost = 0, int maxCost = int.MaxValue)
        {
            if (IsGameOver) return;
            var player = Players[playerIndex];
            var pool = new System.Collections.Generic.List<CardInstance>();
            foreach (var c in player.Deck)
            {
                if (c.Definition.Type == type && c.Definition.Cost >= minCost && c.Definition.Cost <= maxCost) pool.Add(c);
            }
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                var picked = pool[_random.Next(pool.Count)];
                pool.Remove(picked);
                player.Deck.Remove(picked);
                player.Hand.Add(picked);
            }
        }

        // 妨害系効果用: 相手の手札をランダムに count 枚捨てさせる。
        public void DiscardRandomForEffect(int playerIndex, int count)
        {
            var opponent = Players[1 - playerIndex];
            for (int i = 0; i < count && opponent.Hand.Count > 0; i++)
            {
                int pick = _random.Next(opponent.Hand.Count);
                var card = opponent.Hand[pick];
                opponent.RemoveFromHand(card);
                opponent.SendToGraveyard(card);
            }
        }

        // のぞき見系効果用: 相手の裏向きの手札をランダムに count 枚、表向きにする。
        // 表向きになったカードは両プレイヤーから見える状態が続く。
        public void RevealRandomForEffect(int playerIndex, int count)
        {
            var opponent = Players[1 - playerIndex];
            for (int i = 0; i < count; i++)
            {
                var faceDown = new System.Collections.Generic.List<CardInstance>();
                foreach (var c in opponent.Hand)
                {
                    if (!opponent.IsRevealed(c)) faceDown.Add(c);
                }
                if (faceDown.Count == 0) return;
                opponent.Reveal(faceDown[_random.Next(faceDown.Count)]);
            }
        }

        // デッキ破壊系効果用: 相手のデッキの上から count 枚を墓地に置く。
        public void MillForEffect(int playerIndex, int count)
        {
            var opponent = Players[1 - playerIndex];
            for (int i = 0; i < count && opponent.Deck.Count > 0; i++)
            {
                var top = opponent.Deck[opponent.Deck.Count - 1];
                opponent.Deck.RemoveAt(opponent.Deck.Count - 1);
                opponent.SendToGraveyard(top);
            }
        }

        // 押し戻し系効果用。効果による押し戻しでは背水の陣は発動しない（発動は「攻撃で」が条件）。
        // 押し戻しでゲージが相手の端まで届いたら、押し戻した側の勝ち。
        // （+15 はプレイヤー0の負け、-15 はプレイヤー1の負けなので、
        //  プレイヤー0が - 方向へ押し戻して -15 に届けたときの勝者はプレイヤー0）
        public void PushBackGaugeForEffect(int playerIndex, int amount)
        {
            Gauge.PushBack(amount, playerIndex);
            if (Gauge.IsFinished) WinnerIndex = playerIndex;
        }

        // 基本ポイントを恒久的に増やす効果用（最前線攻略tv⁉︎）。次の自分のターンから効く。
        public void IncreaseBasePointsForEffect(int playerIndex, int amount)
        {
            Players[playerIndex].Points.IncreaseBasePoints(amount);
        }

        // このターンだけ使えるポイントを増やす効果用（みにやーや）。
        public void GainPointsThisTurnForEffect(int playerIndex, int amount)
        {
            Players[playerIndex].Points.AddPointsThisTurn(amount);
        }

        // ---------- 内部処理 ----------

        private bool CanAct(int playerIndex)
            => !IsGameOver && PendingHaisuiPlayer == null && PendingDiscardByPlayer == null
               && PendingSelfishByPlayer == null && PendingHealByPlayer == null && PendingDamageByPlayer == null
               && PendingBuffByPlayer == null && PendingDrawPickByPlayer == null
               && PendingOwnBuffByPlayer == null && PendingDeployByPlayer == null
               && PendingGuardGrantByPlayer == null && PendingDeckDeployByPlayer == null
               && PendingRichChoiceByPlayer == null
               && PendingMasterChoiceByPlayer == null && PendingMasterCopyByPlayer == null
               && playerIndex == ActivePlayerIndex && _turnStarted;

        // このターンに攻撃できる回数。通常は1回、登場ターンだけ2回攻撃できる性質があれば2回。
        private int MaxAttacksThisTurn(UnitState unit)
            => AnyDef(unit, d => d.AttacksTwiceOnPlayTurn) && unit.SummonedTurn >= GlobalTurn ? 2 : 1;

        private bool CanAttackWith(UnitState attacker, bool faceAttack)
        {
            if (!CanAct(ActivePlayerIndex)) return false;
            if (attacker == null || attacker.OwnerIndex != ActivePlayerIndex) return false;
            if (attacker.AttacksUsedThisTurn >= MaxAttacksThisTurn(attacker)) return false;
            if (attacker.SummonedTurn >= GlobalTurn && !attacker.HasHaste)
            {
                // 出たターンは攻撃できない。ただし速攻は全て可、先手必勝はユニットへの攻撃だけ可。
                if (faceAttack || !AnyDef(attacker, d => d.CanAttackUnitsWhenPlayed)) return false;
            }
            return true;
        }

        // 登場したこのターン、戦闘によるダメージを受けない性質を持つか。
        private bool IsBattleDamageImmuneThisTurn(UnitState unit)
            => AnyDef(unit, d => d.BattleDamageImmuneOnPlayTurn) && unit.SummonedTurn >= GlobalTurn;

        // 戦闘で受けるダメージの上限（本体＋コピーのうち一番小さい正の値。なければ0）。
        private static int BattleDamageCapOf(UnitState unit)
        {
            int cap = 0;
            foreach (var d in DefsOf(unit))
            {
                if (d.BattleDamageTakenCap > 0 && (cap == 0 || d.BattleDamageTakenCap < cap))
                    cap = d.BattleDamageTakenCap;
            }
            return cap;
        }

        // 指定プレイヤーの場に守護ユニットがいるか。
        private bool HasGuardUnit(int playerIndex)
        {
            foreach (var u in Players[playerIndex].Field)
            {
                if (u.HasGuard) return true;
            }
            return false;
        }

        private void RemoveUnitFromField(UnitState unit)
        {
            var owner = Players[unit.OwnerIndex];
            if (!owner.Field.Remove(unit)) return;
            owner.SendToGraveyard(unit.Instance);

            // 破壊された時の回復（猫被りにあくろ）。場を離れた後に、残った味方から対象を選ぶ。
            if (unit.Definition.HealOnDeath > 0) BeginHealChoice(unit.OwnerIndex, unit.Definition.HealOnDeath);

            // 破壊された時のドロー（ミルキー）。
            if (unit.Definition.DrawOnDeath > 0) DrawCardsForEffect(unit.OwnerIndex, unit.Definition.DrawOnDeath);

            // 破壊された時、デッキから守護ユニットを選んで場に出す（シャインマスカット）。
            if (unit.Definition.DeployGuardOnDeathMaxCost > 0)
                BeginDeckDeployChoice(unit.OwnerIndex, unit.Definition.DeployGuardOnDeathMaxCost);

            // 場が変わったのでオーラを再計算する（RefreshAuras の中から呼ばれた場合は何もしない）。
            RefreshAuras();

            ValidatePendings();
        }

        // 選択待ちの掃除: 場が変わって対象が消えた選択待ちは、効果不発として解消する。
        // （例: ちゃぶ台返し！で回復対象の味方も一緒に倒れた場合に、回復の選択待ちだけが残って
        //   ゲームが進まなくなるのを防ぐ）
        private void ValidatePendings()
        {
            if (PendingHealByPlayer != null)
            {
                bool anyDamaged = false;
                foreach (var u in Players[PendingHealByPlayer.Value].Field)
                {
                    if (u.DamageTaken > 0) anyDamaged = true;
                }
                if (!anyDamaged) PendingHealByPlayer = null;
            }
            if (PendingDamageByPlayer != null && Players[1 - PendingDamageByPlayer.Value].Field.Count == 0)
            {
                PendingDamageByPlayer = null;
            }
            if (PendingRichChoiceByPlayer != null && _richAttacker != null
                && !Players[_richAttacker.OwnerIndex].Field.Contains(_richAttacker))
            {
                // 攻撃保留中の器用富豪が場を離れたら、選択待ちごと取り消す。
                PendingRichChoiceByPlayer = null;
                _richAttacker = null;
                _richDefender = null;
            }
            if (PendingMasterChoiceByPlayer != null && _masterAttacker != null
                && !Players[_masterAttacker.OwnerIndex].Field.Contains(_masterAttacker))
            {
                PendingMasterChoiceByPlayer = null;
                _masterAttacker = null;
                _masterDefender = null;
                _masterSource = null;
            }
            if (PendingBuffByPlayer != null)
            {
                var field = Players[PendingBuffByPlayer.Value].Field;
                if (field.Count == 0 || _buffChosen.Count >= field.Count) PendingBuffByPlayer = null;
            }
            if (PendingOwnBuffByPlayer != null && Players[PendingOwnBuffByPlayer.Value].Field.Count == 0)
            {
                PendingOwnBuffByPlayer = null;
            }
        }

        // ---------- 回復の対象選択（猫被りにあくろ） ----------

        // 回復対象の選択待ち。ダメージを受けている味方がいない時は、選択待ち自体が発生しない。
        public int? PendingHealByPlayer { get; private set; }
        private int _pendingHealAmount;

        private void BeginHealChoice(int ownerIndex, int amount)
        {
            foreach (var unit in Players[ownerIndex].Field)
            {
                if (unit.DamageTaken > 0)
                {
                    PendingHealByPlayer = ownerIndex;
                    _pendingHealAmount = amount;
                    return;
                }
            }
        }

        public bool ChooseHealTarget(Guid instanceId)
        {
            if (PendingHealByPlayer == null) return false;
            var player = Players[PendingHealByPlayer.Value];
            foreach (var unit in player.Field)
            {
                if (unit.Instance.InstanceId == instanceId && unit.DamageTaken > 0)
                {
                    unit.Heal(_pendingHealAmount);
                    PendingHealByPlayer = null;
                    return true;
                }
            }
            return false;
        }

        // ---------- 登場時ダメージの対象選択（ミニトグリ） ----------

        // 相手の場にユニットがいない時は、選択待ち自体が発生しない。
        // 効果ダメージで倒れてもゲージは動かない（戦闘で倒した時だけ動く）。
        public int? PendingDamageByPlayer { get; private set; }
        private int _pendingDamageAmount;

        // ---------- 器用富豪やーや（登場時・攻撃時のモード選択） ----------

        // モード選択待ちのプレイヤー。攻撃時モードの場合は、選択が終わるまで攻撃は保留される。
        public int? PendingRichChoiceByPlayer { get; private set; }
        private UnitState _richAttacker;   // 攻撃保留中の器用富豪（登場時モードなら null）
        private int _richDamageAmount;     // モード2のダメージ量（出したカードの定義から控える）
        private UnitState _richDefender;   // 保留中の攻撃対象（顔攻撃なら null）
        private bool _richIsFace;

        private void BeginRichChoice(UnitState attacker, bool isFace, UnitState defender, int damage)
        {
            PendingRichChoiceByPlayer = ActivePlayerIndex;
            _richAttacker = attacker;
            _richDamageAmount = damage;
            _richDefender = defender;
            _richIsFace = isFace;
        }

        // モードを選ぶ: 1=カードを1枚引く / 2=相手ユニット1体にダメージ（続けて対象選択） / 3=このターンのポイント+1。
        // 攻撃保留中の場合は、モード解決後に攻撃が再開される。対象が場を離れていたら攻撃は不発になる。
        public bool ChooseRichMode(int mode)
        {
            if (PendingRichChoiceByPlayer == null || PendingRichChoiceByPlayer.Value != ActivePlayerIndex) return false;
            int player = ActivePlayerIndex;
            if (mode == 2)
            {
                if (Players[1 - player].Field.Count == 0) return false; // 対象がいないので選べない
                PendingRichChoiceByPlayer = null;
                BeginDamageChoice(player, _richDamageAmount); // 解決後に ResumeRichAttack が呼ばれる
                return true;
            }
            if (mode != 1 && mode != 3) return false;
            PendingRichChoiceByPlayer = null;
            if (mode == 1) DrawCardsForEffect(player, 1);
            else GainPointsThisTurnForEffect(player, 1);
            if (_richAttacker != null) ResumeRichAttack();
            return true;
        }

        // 保留していた攻撃を再開する。攻撃側か対象が場を離れていたら不発（攻撃回数は消費しない）。
        private void ResumeRichAttack()
        {
            var attacker = _richAttacker;
            var defender = _richDefender;
            bool isFace = _richIsFace;
            _richAttacker = null;
            _richDefender = null;
            if (attacker == null) return;
            if (!Players[attacker.OwnerIndex].Field.Contains(attacker)) return;
            if (isFace) ResolveFaceAttack(attacker);
            else if (defender != null && Players[defender.OwnerIndex].Field.Contains(defender))
                ResolveUnitAttack(attacker, defender);
        }

        // ---------- 手練手管やーや（登場時・攻撃時のモード選択＋コピー） ----------

        // モード選択待ちと、モード4の対象（場・墓地・手札）選択待ち。
        public int? PendingMasterChoiceByPlayer { get; private set; }
        public int? PendingMasterCopyByPlayer { get; private set; }
        private int _masterDamageAmount;
        private int _masterPicksRemaining;
        private UnitState _masterSource;   // 選択を使っている手練手管自身
        private UnitState _masterAttacker; // 攻撃保留中の場合の攻撃側（登場時なら null）
        private UnitState _masterDefender;
        private bool _masterIsFace;

        // 基本ポイントが条件以上なら2回、そうでなければ1回選べる。
        private int MasterPickCount(int playerIndex, UnitCardDefinition def)
        {
            if (def.MasterChoiceExtraPickMinBasePoints > 0
                && Players[playerIndex].Points.BasePoints >= def.MasterChoiceExtraPickMinBasePoints)
                return 2;
            return 1;
        }

        // 今このターンに場へ出たばかりの、その定義のユニットを探す（登場時効果の発生源特定用）。
        private UnitState FindEnteredUnit(int playerIndex, UnitCardDefinition def)
        {
            var field = Players[playerIndex].Field;
            for (int i = field.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(field[i].Definition, def) && field[i].SummonedTurn >= GlobalTurn)
                    return field[i];
            }
            return null;
        }

        private void BeginMasterAttackChoice(UnitState attacker, bool isFace, UnitState defender)
        {
            int player = ActivePlayerIndex;
            PendingMasterChoiceByPlayer = player;
            _masterDamageAmount = attacker.Definition.MasterChoiceDamage;
            _masterPicksRemaining = MasterPickCount(player, attacker.Definition);
            _masterSource = attacker;
            _masterAttacker = attacker;
            _masterDefender = defender;
            _masterIsFace = isFace;
        }

        // モードを選ぶ: 1=カードを2枚引く / 2=相手全体にダメージ / 3=このターンのポイント+2 /
        // 4=自分の場・墓地・手札のユニット1枚を除外して登場時効果と能力をコピー（続けて対象選択）。
        public bool ChooseMasterMode(int mode)
        {
            if (PendingMasterChoiceByPlayer == null || PendingMasterChoiceByPlayer.Value != ActivePlayerIndex) return false;
            int player = ActivePlayerIndex;
            switch (mode)
            {
                case 1:
                    DrawCardsForEffect(player, 2);
                    AfterMasterPick();
                    return true;
                case 2:
                    DamageAllOpponentUnitsForEffect(player, _masterDamageAmount);
                    AfterMasterPick();
                    return true;
                case 3:
                    GainPointsThisTurnForEffect(player, 2);
                    AfterMasterPick();
                    return true;
                case 4:
                    if (!HasMasterCopyTarget(player)) return false; // 対象がいないので選べない
                    PendingMasterChoiceByPlayer = null;
                    PendingMasterCopyByPlayer = player;
                    return true;
                default:
                    return false;
            }
        }

        // モード4の対象が1つでもあるか（自分自身と同名カードは対象外）。
        private bool HasMasterCopyTarget(int player)
        {
            var p = Players[player];
            var selfDef = _masterSource != null ? _masterSource.Definition : null;
            foreach (var u in p.Field)
            {
                if (!ReferenceEquals(u, _masterSource) && !ReferenceEquals(u.Definition, selfDef)) return true;
            }
            foreach (var c in p.Graveyard)
            {
                if (c.Definition is UnitCardDefinition && !ReferenceEquals(c.Definition, selfDef)) return true;
            }
            foreach (var c in p.Hand)
            {
                if (c.Definition is UnitCardDefinition && !ReferenceEquals(c.Definition, selfDef)) return true;
            }
            return false;
        }

        // モード4の対象を選ぶ。対象はゲームから除外され、手練手管はその登場時効果を使い、能力をコピーする。
        // 場のユニットを除外しても破壊ではないので、死亡時効果は発動しない。
        public bool ChooseMasterCopyTarget(MasterCopyZone zone, System.Guid instanceId)
        {
            if (PendingMasterCopyByPlayer == null || PendingMasterCopyByPlayer.Value != ActivePlayerIndex) return false;
            int player = ActivePlayerIndex;
            var p = Players[player];
            var source = _masterSource;
            if (source == null || !p.Field.Contains(source)) return false;

            UnitCardDefinition copiedDef = null;
            switch (zone)
            {
                case MasterCopyZone.Field:
                    UnitState targetUnit = null;
                    foreach (var u in p.Field)
                    {
                        if (u.Instance.InstanceId == instanceId) { targetUnit = u; break; }
                    }
                    if (targetUnit == null || ReferenceEquals(targetUnit, source)) return false;
                    if (ReferenceEquals(targetUnit.Definition, source.Definition)) return false;
                    copiedDef = targetUnit.Definition;
                    ExileUnitFromField(targetUnit);
                    break;
                case MasterCopyZone.Graveyard:
                    CardInstance graveCard = null;
                    foreach (var c in p.Graveyard)
                    {
                        if (c.InstanceId == instanceId) { graveCard = c; break; }
                    }
                    if (graveCard == null || !(graveCard.Definition is UnitCardDefinition)) return false;
                    if (ReferenceEquals(graveCard.Definition, source.Definition)) return false;
                    copiedDef = (UnitCardDefinition)graveCard.Definition;
                    p.Graveyard.Remove(graveCard);
                    break;
                case MasterCopyZone.Hand:
                    CardInstance handCard = null;
                    foreach (var c in p.Hand)
                    {
                        if (c.InstanceId == instanceId) { handCard = c; break; }
                    }
                    if (handCard == null || !(handCard.Definition is UnitCardDefinition)) return false;
                    if (ReferenceEquals(handCard.Definition, source.Definition)) return false;
                    copiedDef = (UnitCardDefinition)handCard.Definition;
                    p.Hand.Remove(handCard);
                    break;
            }
            if (copiedDef == null) return false;

            PendingMasterCopyByPlayer = null;
            source.CopiedDefinition = copiedDef;
            // コピーした速攻（条件つき）は、コピーの時点で条件を満たしていれば得る。
            if (copiedDef.ConditionalHaste && p.Field.Count > 1) source.HasHaste = true;
            RefreshAuras();
            // コピーしたユニットの登場時効果を、手練手管を発生源として使う。
            ResolveOnPlayEffects(player, copiedDef, source);
            AfterMasterPick();
            return true;
        }

        // 場のユニットを「破壊ではなく除外」で取り除く（死亡時効果・ゲージは一切なし）。
        private void ExileUnitFromField(UnitState unit)
        {
            var owner = Players[unit.OwnerIndex];
            if (!owner.Field.Remove(unit)) return;
            RefreshAuras();
            ValidatePendings();
        }

        // 1つ選び終えた後の処理: まだ選べるなら選択待ちに戻り、終わりなら保留中の攻撃を再開する。
        private void AfterMasterPick()
        {
            _masterPicksRemaining--;
            if (_masterPicksRemaining > 0)
            {
                PendingMasterChoiceByPlayer = ActivePlayerIndex;
                return;
            }
            PendingMasterChoiceByPlayer = null;
            if (_masterAttacker != null) ResumeMasterAttack();
            else _masterSource = null;
        }

        // 保留していた攻撃を再開する。攻撃側か対象が場を離れていたら不発（攻撃回数は消費しない）。
        private void ResumeMasterAttack()
        {
            var attacker = _masterAttacker;
            var defender = _masterDefender;
            bool isFace = _masterIsFace;
            _masterAttacker = null;
            _masterDefender = null;
            _masterSource = null;
            if (attacker == null) return;
            if (!Players[attacker.OwnerIndex].Field.Contains(attacker)) return;
            if (isFace) ResolveFaceAttack(attacker);
            else if (defender != null && Players[defender.OwnerIndex].Field.Contains(defender))
                ResolveUnitAttack(attacker, defender);
        }

        private void BeginDamageChoice(int byPlayer, int amount)
        {
            if (Players[1 - byPlayer].Field.Count == 0) return;
            PendingDamageByPlayer = byPlayer;
            _pendingDamageAmount = amount;
        }

        public bool ChooseDamageTarget(Guid instanceId)
        {
            if (PendingDamageByPlayer == null) return false;
            var opponent = Players[1 - PendingDamageByPlayer.Value];
            UnitState target = null;
            foreach (var unit in opponent.Field)
            {
                if (unit.Instance.InstanceId == instanceId) { target = unit; break; }
            }
            if (target == null) return false;
            PendingDamageByPlayer = null;
            target.TakeDamage(_pendingDamageAmount);
            if (target.IsDead) RemoveUnitFromField(target);
            if (_richAttacker != null) ResumeRichAttack();
            return true;
        }

        // ---------- 自分のユニット1体への戦術の対象選択（酒！酒！酒！） ----------

        // 自分の場にユニットがいない時は、選択待ち自体が発生しない。
        public int? PendingOwnBuffByPlayer { get; private set; }
        private int _pendingOwnBuffAttack;
        private int _pendingOwnReduceHealth;
        private bool _pendingOwnDestroyAfterAttack;

        private void BeginOwnBuffChoice(int byPlayer, int attackBuff, int reduceMaxHealth, bool destroyAfterAttack)
        {
            if (Players[byPlayer].Field.Count == 0) return;
            PendingOwnBuffByPlayer = byPlayer;
            _pendingOwnBuffAttack = attackBuff;
            _pendingOwnReduceHealth = reduceMaxHealth;
            _pendingOwnDestroyAfterAttack = destroyAfterAttack;
        }

        public bool ChooseOwnBuffTarget(Guid instanceId)
        {
            if (PendingOwnBuffByPlayer == null) return false;
            UnitState target = null;
            foreach (var unit in Players[PendingOwnBuffByPlayer.Value].Field)
            {
                if (unit.Instance.InstanceId == instanceId) { target = unit; break; }
            }
            if (target == null) return false;
            PendingOwnBuffByPlayer = null;
            target.AttackBuff += _pendingOwnBuffAttack;
            target.HealthReduction += _pendingOwnReduceHealth;
            if (_pendingOwnDestroyAfterAttack) target.DestroyAfterAttack = true;
            // 体力の最大値が尽きたら、その場で倒れる（効果死で、ゲージは動かない）。
            if (target.IsDead) RemoveUnitFromField(target);
            return true;
        }

        // ---------- 登場時の攻撃力強化の対象選択（ゲーマーニトグリ） ----------

        // 自分のユニットを最大で指定数まで、1体ずつ選んで攻撃力を上げる。同じユニットは2回選べない。
        // 場のユニットが指定数より少なければ、いる分だけ選んで終わる。
        public int? PendingBuffByPlayer { get; private set; }
        private int _buffAmount;
        private int _buffRemaining;
        private readonly System.Collections.Generic.HashSet<Guid> _buffChosen
            = new System.Collections.Generic.HashSet<Guid>();

        private void BeginBuffChoice(int byPlayer, int amount, int targets)
        {
            int available = Players[byPlayer].Field.Count;
            if (available == 0) return;
            PendingBuffByPlayer = byPlayer;
            _buffAmount = amount;
            _buffRemaining = Math.Min(targets, available);
            _buffChosen.Clear();
        }

        public bool ChooseBuffTarget(Guid instanceId)
        {
            if (PendingBuffByPlayer == null) return false;
            if (_buffChosen.Contains(instanceId)) return false;
            var player = Players[PendingBuffByPlayer.Value];
            foreach (var unit in player.Field)
            {
                if (unit.Instance.InstanceId != instanceId) continue;
                unit.AttackBuff += _buffAmount;
                _buffChosen.Add(instanceId);
                _buffRemaining--;
                if (_buffRemaining <= 0 || _buffChosen.Count >= player.Field.Count)
                {
                    PendingBuffByPlayer = null;
                }
                return true;
            }
            return false;
        }

        // ゲージが動いたあとの勝敗・背水チェック。
        private void AfterGaugePush(int pushedPlayer)
        {
            if (Gauge.IsFinished)
            {
                WinnerIndex = 1 - pushedPlayer;
                return;
            }
            if (Haisui.CanUse(pushedPlayer) && Gauge.IsInHaisuiZone(pushedPlayer))
            {
                PendingHaisuiPlayer = pushedPlayer; // 必須発動: 解決するまで他は止まる
            }
        }
    }

    // 手練手管やーやのモード4で、コピー対象をどの領域から選ぶか。
    public enum MasterCopyZone
    {
        Field,
        Graveyard,
        Hand,
    }
}
