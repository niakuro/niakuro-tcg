// このファイルについて:
//   場に出ているユニット1体の「今の状態」。カードの設計図（UnitCardDefinition）とは別に、
//   受けたダメージなど、対戦中に変わる情報だけを持つ。
//   ダメージはターンが終わっても残る（回復しない）のがこのゲームのルール。
namespace Niakuro.Tcg.Core
{
    public sealed class UnitState
    {
        public CardInstance Instance { get; }
        public UnitCardDefinition Definition => (UnitCardDefinition)Instance.Definition;
        public int OwnerIndex { get; }

        public int DamageTaken { get; private set; }
        // このターンに攻撃した回数（通常は最大1回。百発百中ココナッツは登場ターンだけ2回）。
        public int AttacksUsedThisTurn { get; set; }

        // 互換用の見かけ: 1回でも攻撃していれば true。代入は true→最低1回、false→0回として扱う。
        public bool HasAttackedThisTurn
        {
            get => AttacksUsedThisTurn > 0;
            set => AttacksUsedThisTurn = value ? System.Math.Max(AttacksUsedThisTurn, 1) : 0;
        }
        public bool HasHaste { get; set; } // 速攻（出たターンでも攻撃できる）
        public bool DestroyAfterAttack { get; set; } // 攻撃した後に破壊される（酒！酒！酒！）
        public bool AbilityUsedThisTurn { get; set; } // 起動型能力（1ターンに1度）の使用済みフラグ
        public int SummonedTurn { get; } // 出したターンには攻撃できない判定に使う

        // 体力の最大値への恒久的な減少（超絶自己中にあくろの能力など）。場を離れるまで残る。
        public int HealthReduction { get; set; }

        // 攻撃力への恒久的な強化（ゲーマーニトグリの登場時効果など）。場を離れるまで残る。
        public int AttackBuff { get; set; }

        // 効果による体力最大値への恒久的な増加（ゲーマーやーやの自己強化など）。
        public int MaxHealthBuff { get; set; }

        // オーラによる一時的な修正（秩序なき連携ニトグリなど）。GameEngine.RefreshAuras が
        // 場の状況から毎回計算し直す。条件が消えれば修正も消える。
        public int AuraAttackBonus { get; set; }
        public int AuraHealthModifier { get; set; }

        // 効果ドロー連動の攻/体ボーナス（無数の選択肢ミルキー）。GameEngine.RefreshAuras が
        // 持ち主の「この試合で効果で引いた枚数 ÷2（切り捨て）」を計算し直す。
        public int DrawScaleBonus { get; set; }

        // 効果で付与された守護（ゲーマーマスカット）。場を離れるまで残る。
        public bool GrantedGuard { get; set; }

        // 守護を持っているか（素の守護＋付与された守護＋コピー中の守護）。
        public bool HasGuard => Definition.Guard || GrantedGuard
            || (CopiedDefinition != null && CopiedDefinition.Guard);

        // 手練手管やーやの効果4でコピー中のカード定義。場を離れるまでの間、
        // このユニットはコピー元の能力（キーワード・オーラ・起動型能力など）も併せ持つ。
        public UnitCardDefinition CopiedDefinition { get; set; }

        public UnitState(CardInstance instance, int ownerIndex, int summonedTurn)
        {
            Instance = instance;
            OwnerIndex = ownerIndex;
            SummonedTurn = summonedTurn;
        }

        public int Attack => Definition.Attack + AttackBuff + AuraAttackBonus + DrawScaleBonus;
        public int MaxHealth => Definition.Health - HealthReduction + MaxHealthBuff + AuraHealthModifier + DrawScaleBonus;
        public int CurrentHealth => MaxHealth - DamageTaken;
        public bool IsDead => CurrentHealth <= 0;

        // フィニッシャーかどうかはカード定義側の性質（コピー中も含む）。
        public bool PiercesHaisuiProtection => Definition.PiercesHaisuiProtection
            || (CopiedDefinition != null && CopiedDefinition.PiercesHaisuiProtection);

        public void TakeDamage(int amount)
        {
            if (amount > 0) DamageTaken += amount;
        }

        // 回復: 受けたダメージを減らす（0未満にはならない）。
        public void Heal(int amount)
        {
            if (amount > 0) DamageTaken = System.Math.Max(0, DamageTaken - amount);
        }

        // 特徴を持っているか。「全ての特徴を持つ」ユニットは常に true。
        // コピー中の定義の特徴も併せ持つ。
        public bool HasTrait(string trait)
        {
            if (Definition.AllTraits) return true;
            foreach (var t in Definition.Traits)
            {
                if (t == trait) return true;
            }
            if (CopiedDefinition != null)
            {
                if (CopiedDefinition.AllTraits) return true;
                foreach (var t in CopiedDefinition.Traits)
                {
                    if (t == trait) return true;
                }
            }
            return false;
        }

    }
}
