// このファイルについて:
//   「カードそのもののデータ」の定義。カードはコードではなくデータとして持つので、
//   新しいカードを増やすときは、この形のデータを追加するだけでよい（SampleCards.cs 参照）。
//   場に出たあとの状態は Units/UnitState.cs が担当し、ここは不変の設計図だけを持つ。
using System;

namespace Niakuro.Tcg.Core
{
    public enum CardType
    {
        Unit,     // ユニット: 場に出て攻撃する
        Tactic,   // 戦術: 使い切りの効果
    }

    // 特徴（種族や所属のようなタグ）の名前置き場。
    // 文字列で持つので、新しい特徴はここに1行足すだけで増やせる。
    // 効果側は「特徴○○を持つユニット」のように参照できる。
    public static class CardTraits
    {
        public const string Kosan = "古参";   // 初期メンバー全員
        public const string Akuyuu = "悪友";  // にあくろ・ニトグリ・ココナッツ
        public const string Zennyuu = "善友"; // マスカット・やーや・ミルキー
    }

    public abstract class CardDefinition
    {
        public string Id { get; }        // 同名判定に使う一意なID
        public string Name { get; }      // 表示名
        public int Cost { get; }         // 使うのに必要なポイント
        public abstract CardType Type { get; }

        protected CardDefinition(string id, string name, int cost)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("カードIDが必要です", nameof(id));
            if (cost < 0) throw new ArgumentOutOfRangeException(nameof(cost), "コストは0以上");
            Id = id;
            Name = name;
            Cost = cost;
        }
    }

    public sealed class UnitCardDefinition : CardDefinition
    {
        public int Attack { get; }
        public int Health { get; }

        // フィニッシャー: 背水の陣の「残り1ストップ」保護を貫通して一撃で押し切れる。
        public bool PiercesHaisuiProtection { get; }

        // カードに書かれる効果の説明文（表示用）。効果の有無にかかわらずここに書く。
        public string AbilityText { get; }

        // 登場時効果（対象を取らないものだけエンジンが自動で解決する）。
        // 戦術と同じく EffectId で引き、実体は Effects/ に登録する。
        // 対象を取る効果（バウンスなど）は AbilityText のみで、解決は今後実装する。
        public string OnPlayEffectId { get; }
        public int OnPlayEffectValue { get; }

        // 特徴タグ（古参、悪友など）。複数持てる。効果の対象指定に使う。
        public System.Collections.Generic.IReadOnlyList<string> Traits { get; }

        // 常時効果: このユニットが場にいる限り、相手のユニットの「攻撃時」の攻撃力をこの数だけ下げる。
        // 攻撃時だけの一時的な弱体で、ユニット自体の攻撃力は変わらない（ゲーマーにあくろ用）。
        public int AttackDebuffAura { get; }

        // 常時効果: このユニットが場にいる限り、お互いの手札は全て表向きになる（超絶自己中にあくろ用）。
        public bool RevealsAllHands { get; }

        // 登場時効果: 自分の手札1枚と相手の手札1枚を選んで捨てる（超絶自己中にあくろ用）。
        // 選択が必要なので、エンジンが選択待ちとして処理する。
        public bool SelfishDiscardOnPlay { get; }

        // 起動型能力のID（1ターンに1度、選んで使う能力）。実体はエンジン側が持つ。
        public string ActivatedAbilityId { get; }

        // 全ての特徴を持つ（超絶自己中にあくろ用）。特徴の判定は UnitState.HasTrait を使う。
        public bool AllTraits { get; }

        // 登場時／破壊された時に、自分のユニット1体の体力を回復する量（0なら回復しない。猫被りにあくろ用）。
        public int HealOnPlay { get; }
        public int HealOnDeath { get; }

        // キーワード能力「連携：特徴」: 自分の場に「自分以外の」その特徴を持つユニットがいる間、
        // 攻撃力が LinkageAttackBonus 上がる（条件を満たせば1回だけ。人数では重ならない）。
        public string LinkageTrait { get; }
        public int LinkageAttackBonus { get; }

        // 登場時: 相手のユニット1体に与えるダメージ（0なら与えない。ミニトグリ用）。
        // 対象選択が必要なので、エンジンが選択待ちとして処理する。
        public int DamageOnPlay { get; }

        // 登場時: 自分のユニット最大 BuffTargetsOnPlay 体の攻撃力を BuffAttackOnPlay 上げる（ゲーマーニトグリ用）。
        // 恒久強化。対象選択が必要なので、エンジンが選択待ちとして処理する。
        public int BuffAttackOnPlay { get; }
        public int BuffTargetsOnPlay { get; }

        // 常時効果: このユニットが場にいる限り、自分のユニット全員に「連携○：特徴」を付与する
        // （秩序なき連携ニトグリ用）。もともと持っている連携とは重なる。
        public string GrantsLinkageTrait { get; }
        public int GrantsLinkageBonus { get; }

        // 登場時条件: 自分の場に他のユニットがいれば「速攻」を得る（爆速退社ニトグリ用）。
        // 速攻 = 出たターンに攻撃できる。
        public bool ConditionalHaste { get; }

        // 攻撃した後、生き残っていれば持ち主の手札に戻る（爆速退社ニトグリ用）。
        public bool ReturnsToHandAfterAttack { get; }

        // 破壊された時に持ち主が引くカードの枚数（ミルキー用）。
        public int DrawOnDeath { get; }

        // 場にいる間、持ち主のターン開始時に引くカードの枚数（ゲーマーミルキー用）。
        public int DrawAtTurnStart { get; }

        // 攻撃力と体力が「この試合で自分が効果で引いた枚数の半分（切り捨て）」になる（無数の選択肢ミルキー用）。
        // 定義上の攻/体は 0 で置き、エンジンが DrawScaleBonus として加算する。
        public bool ScalesWithEffectDraws { get; }

        // 登場時、手札の枚数より低コストのユニットを1枚選んで無料で場に出す（無数の選択肢ミルキー用）。
        public bool FreeDeployOnPlay { get; }

        // キーワード能力「守護」: 相手は守護を持つユニットしか攻撃できない（マスカット用）。
        public bool Guard { get; }

        // 登場時、自分のユニット1体を選んで守護を付与する（ゲーマーマスカット用）。
        public bool GrantsGuardOnPlay { get; }

        // 場にいる間、守護を持つ自分のユニットの体力の最大値へのオーラ（成熟マスカット用）。
        public int GuardHealthAura { get; }

        // 場にいる間、守護を持つ自分のユニットの攻撃力と体力の最大値へのオーラ（シャインマスカット用）。
        public int GuardStatAura { get; }

        // 破壊された時、デッキから「守護を持つコスト○以下」のユニットを1枚選んで場に出す（シャインマスカット用）。0なら無効。
        public int DeployGuardOnDeathMaxCost { get; }

        // キーワード能力「先手必勝」: 出たターンでも、相手のユニットを攻撃できる（プレイヤーは攻撃できない）。
        public bool CanAttackUnitsWhenPlayed { get; }

        // 登場したターン、戦闘によるダメージを受けない（ゲーマーココナッツ用）。
        public bool BattleDamageImmuneOnPlayTurn { get; }

        // 戦闘で受けるダメージの上限（孤軍奮闘ココナッツ用）。0なら上限なし。戦闘のたびに適用される。
        public int BattleDamageTakenCap { get; }

        // 相手のユニットしか攻撃できない（プレイヤーへの攻撃は常に不可。孤軍奮闘ココナッツ用）。
        public bool CanOnlyAttackUnits { get; }

        // 登場したターンだけ、2回まで攻撃できる（百発百中ココナッツ用）。
        public bool AttacksTwiceOnPlayTurn { get; }

        // 自分が攻撃して相手ユニットを破壊した時、自分の体力を○回復する（百発百中ココナッツ用）。0なら無効。
        public int SelfHealOnBattleKill { get; }

        // 登場時、自分の基本ポイントが SelfBuffMinBasePoints 以上なら、自分を +攻撃/+体力 し、
        // このターンのポイントを PointsGainOnPlay だけ増やす（ゲーマーやーや用）。
        public int SelfBuffOnPlayAttack { get; }
        public int SelfBuffOnPlayHealth { get; }
        public int SelfBuffMinBasePoints { get; }
        public int PointsGainOnPlay { get; }

        // 登場時と攻撃時（攻撃の解決前）に「ドロー1 / 相手ユニット1体に○ダメージ / このターンのポイント+1」から
        // 1つ選んで使う性質（器用富豪やーや用）。○がダメージ量で、0ならこの性質なし。
        public int RichChoiceDamage { get; }

        // 登場時と攻撃時（攻撃の解決前）に「ドロー2 / 相手全体に○ダメージ / このターンのポイント+2 /
        // 自分の場・墓地・手札のユニット1枚を除外して、その登場時効果を使い能力をコピーする」から選んで使う性質
        // （手練手管やーや用）。○が全体ダメージ量で、0ならこの性質なし。
        public int MasterChoiceDamage { get; }

        // 自分の基本ポイントがこの値以上なら、手練手管のモード選択を2回行える（0なら常に1回）。
        public int MasterChoiceExtraPickMinBasePoints { get; }

        // 登場時効果（OnPlayEffectId）が発動するための基本ポイントの下限（0なら無条件）。
        public int OnPlayMinBasePoints { get; }

        // 常時効果（秩序なき連携ニトグリ専用の複合オーラ）:
        //   自分の場に自分以外の悪友がいれば、その悪友全員とこのユニットは攻撃力+1・体力の最大値-1。
        //   自分の場に善友がいれば、その善友全員とこのユニットは体力の最大値+1。
        //   実体は GameEngine.RefreshAuras が計算する。
        public bool OrderlessAura { get; }

        public override CardType Type => CardType.Unit;

        public UnitCardDefinition(string id, string name, int cost, int attack, int health,
            bool piercesHaisuiProtection = false,
            string abilityText = null,
            string onPlayEffectId = null,
            int onPlayEffectValue = 0,
            System.Collections.Generic.IReadOnlyList<string> traits = null,
            int attackDebuffAura = 0,
            bool revealsAllHands = false,
            bool selfishDiscardOnPlay = false,
            string activatedAbilityId = null,
            bool allTraits = false,
            int healOnPlay = 0,
            int healOnDeath = 0,
            string linkageTrait = null,
            int linkageAttackBonus = 0,
            int damageOnPlay = 0,
            int buffAttackOnPlay = 0,
            int buffTargetsOnPlay = 0,
            string grantsLinkageTrait = null,
            int grantsLinkageBonus = 0,
            bool orderlessAura = false,
            bool conditionalHaste = false,
            bool returnsToHandAfterAttack = false,
            int drawOnDeath = 0,
            int drawAtTurnStart = 0,
            bool scalesWithEffectDraws = false,
            bool freeDeployOnPlay = false,
            bool guard = false,
            bool grantsGuardOnPlay = false,
            int guardHealthAura = 0,
            int guardStatAura = 0,
            int deployGuardOnDeathMaxCost = 0,
            bool canAttackUnitsWhenPlayed = false,
            bool battleDamageImmuneOnPlayTurn = false,
            int battleDamageTakenCap = 0,
            bool canOnlyAttackUnits = false,
            bool attacksTwiceOnPlayTurn = false,
            int selfHealOnBattleKill = 0,
            int selfBuffOnPlayAttack = 0,
            int selfBuffOnPlayHealth = 0,
            int selfBuffMinBasePoints = 0,
            int pointsGainOnPlay = 0,
            int richChoiceDamage = 0,
            int masterChoiceDamage = 0,
            int masterChoiceExtraPickMinBasePoints = 0,
            int onPlayMinBasePoints = 0)
            : base(id, name, cost)
        {
            Attack = attack;
            Health = health;
            PiercesHaisuiProtection = piercesHaisuiProtection;
            AbilityText = abilityText;
            OnPlayEffectId = onPlayEffectId;
            OnPlayEffectValue = onPlayEffectValue;
            Traits = traits ?? new string[0];
            AttackDebuffAura = attackDebuffAura;
            RevealsAllHands = revealsAllHands;
            SelfishDiscardOnPlay = selfishDiscardOnPlay;
            ActivatedAbilityId = activatedAbilityId;
            AllTraits = allTraits;
            HealOnPlay = healOnPlay;
            HealOnDeath = healOnDeath;
            LinkageTrait = linkageTrait;
            LinkageAttackBonus = linkageAttackBonus;
            DamageOnPlay = damageOnPlay;
            BuffAttackOnPlay = buffAttackOnPlay;
            BuffTargetsOnPlay = buffTargetsOnPlay;
            GrantsLinkageTrait = grantsLinkageTrait;
            GrantsLinkageBonus = grantsLinkageBonus;
            OrderlessAura = orderlessAura;
            ConditionalHaste = conditionalHaste;
            ReturnsToHandAfterAttack = returnsToHandAfterAttack;
            DrawOnDeath = drawOnDeath;
            DrawAtTurnStart = drawAtTurnStart;
            ScalesWithEffectDraws = scalesWithEffectDraws;
            FreeDeployOnPlay = freeDeployOnPlay;
            Guard = guard;
            GrantsGuardOnPlay = grantsGuardOnPlay;
            GuardHealthAura = guardHealthAura;
            GuardStatAura = guardStatAura;
            DeployGuardOnDeathMaxCost = deployGuardOnDeathMaxCost;
            CanAttackUnitsWhenPlayed = canAttackUnitsWhenPlayed;
            BattleDamageImmuneOnPlayTurn = battleDamageImmuneOnPlayTurn;
            BattleDamageTakenCap = battleDamageTakenCap;
            CanOnlyAttackUnits = canOnlyAttackUnits;
            AttacksTwiceOnPlayTurn = attacksTwiceOnPlayTurn;
            SelfHealOnBattleKill = selfHealOnBattleKill;
            SelfBuffOnPlayAttack = selfBuffOnPlayAttack;
            SelfBuffOnPlayHealth = selfBuffOnPlayHealth;
            SelfBuffMinBasePoints = selfBuffMinBasePoints;
            PointsGainOnPlay = pointsGainOnPlay;
            RichChoiceDamage = richChoiceDamage;
            MasterChoiceDamage = masterChoiceDamage;
            MasterChoiceExtraPickMinBasePoints = masterChoiceExtraPickMinBasePoints;
            OnPlayMinBasePoints = onPlayMinBasePoints;
        }

        public bool HasTrait(string trait)
        {
            foreach (var t in Traits) if (t == trait) return true;
            return false;
        }

        // 特徴だけを付け替えた複製を返す（定義の他の項目はそのまま）。
        // カード置き場で `.WithTraits(...)` と書けるようにするための補助。
        public UnitCardDefinition WithTraits(params string[] traits)
        {
            return new UnitCardDefinition(Id, Name, Cost, Attack, Health,
                PiercesHaisuiProtection, AbilityText, OnPlayEffectId, OnPlayEffectValue, traits, AttackDebuffAura,
                RevealsAllHands, SelfishDiscardOnPlay, ActivatedAbilityId, AllTraits, HealOnPlay, HealOnDeath,
                LinkageTrait, LinkageAttackBonus, DamageOnPlay, BuffAttackOnPlay, BuffTargetsOnPlay,
                GrantsLinkageTrait, GrantsLinkageBonus, OrderlessAura, ConditionalHaste, ReturnsToHandAfterAttack, DrawOnDeath, DrawAtTurnStart, ScalesWithEffectDraws, FreeDeployOnPlay, Guard, GrantsGuardOnPlay, GuardHealthAura, GuardStatAura, DeployGuardOnDeathMaxCost, CanAttackUnitsWhenPlayed, BattleDamageImmuneOnPlayTurn, BattleDamageTakenCap, CanOnlyAttackUnits, AttacksTwiceOnPlayTurn, SelfHealOnBattleKill, SelfBuffOnPlayAttack, SelfBuffOnPlayHealth, SelfBuffMinBasePoints, PointsGainOnPlay, RichChoiceDamage, MasterChoiceDamage, MasterChoiceExtraPickMinBasePoints, OnPlayMinBasePoints);
        }
    }

    public sealed class TacticCardDefinition : CardDefinition
    {
        // 効果はID文字列で引き、実体は Effects/ フォルダのレジストリに登録する。
        // 新しい戦術を増やすときは「データを追加」+「効果を1つ登録」で済む。
        public string EffectId { get; }
        public int EffectValue { get; } // 効果の大きさ（ドロー枚数、押し戻し量など）

        // 対象を取るダメージ戦術（不可視の爆撃など）。0より大きい場合、EffectId は使わず、
        // エンジンが対象選択待ち（相手ユニット1体）として処理する。
        public int TargetDamage { get; }

        // 自分のユニット1体を対象に取る戦術（酒！酒！酒！など）。
        // いずれかが設定されていれば、EffectId は使わず、エンジンが対象選択待ちとして処理する。
        public int BuffOwnAttack { get; }          // 攻撃力への恒久強化
        public int ReduceOwnMaxHealth { get; }     // 体力の最大値への恒久減少
        public bool OwnDestroyAfterAttack { get; } // そのユニットは攻撃後に破壊される

        public override CardType Type => CardType.Tactic;

        public TacticCardDefinition(string id, string name, int cost, string effectId, int effectValue,
            int targetDamage = 0,
            int buffOwnAttack = 0,
            int reduceOwnMaxHealth = 0,
            bool ownDestroyAfterAttack = false)
            : base(id, name, cost)
        {
            EffectId = effectId;
            EffectValue = effectValue;
            TargetDamage = targetDamage;
            BuffOwnAttack = buffOwnAttack;
            ReduceOwnMaxHealth = reduceOwnMaxHealth;
            OwnDestroyAfterAttack = ownDestroyAfterAttack;
        }
    }

    // 手札やデッキの中にある「物理的な1枚」。同じ定義でも1枚ごとに区別できるようIDを持つ。
    public sealed class CardInstance
    {
        public Guid InstanceId { get; } = Guid.NewGuid();
        public CardDefinition Definition { get; }

        public CardInstance(CardDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }
    }
}
