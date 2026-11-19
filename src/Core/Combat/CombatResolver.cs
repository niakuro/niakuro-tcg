// このファイルについて:
//   ユニット同士の戦闘の計算だけを行う場所。お互いの攻撃力分を食らい合い（反撃あり）、
//   ダメージはユニットに残る。倒れた場合のゲージ移動量もここで計算する:
//   - 戦闘で倒すと、倒したユニットの「コストの半分（切り捨て・最低1）」だけゲージが動く
//   - 相打ちの場合は両方向分を合計した「差分」だけが動く
//   - 効果（除去など）で倒した場合はゲージは動かない → それは GameEngine 側の責務
namespace Niakuro.Tcg.Core
{
    public sealed class BattleResult
    {
        public bool AttackerDied { get; set; }
        public bool DefenderDied { get; set; }

        // + なら攻撃側の陣地が押された（攻撃側が不利）、- なら守備側が押された…ではなく、
        // ゲージ座標への加算量そのもの: FrontlineGauge.Position に足す符号付きの量。
        public int GaugeDelta { get; set; }
    }

    public static class CombatResolver
    {
        // 戦闘撃破で動くゲージ量: コストの半分（切り捨て）、最低1。
        public static int DefeatGaugeAmount(int defeatedUnitCost)
            => System.Math.Max(1, defeatedUnitCost / 2);

        // attackOverride / counterOverride: 攻撃時にだけ効く弱体（ゲーマーにあくろ等）を
        // 反映した実効攻撃力を、エンジンが計算して渡すための口。通常は null でよい。
        // 反撃（守備側のダメージ）は「攻撃時」ではないので、弱体は攻撃側にだけ適用する。
        public static BattleResult ResolveBattle(UnitState attacker, UnitState defender,
            int? attackOverride = null, int? counterOverride = null)
        {
            // 同時にダメージを与え合う。
            defender.TakeDamage(attackOverride ?? attacker.Attack);
            attacker.TakeDamage(counterOverride ?? defender.Attack);

            var result = new BattleResult
            {
                AttackerDied = attacker.IsDead,
                DefenderDied = defender.IsDead,
            };

            // ゲージは「倒された側へ」動く。Position の + 方向はプレイヤー0が押される向き。
            int delta = 0;
            if (result.DefenderDied)
                delta += SignedToward(defender.OwnerIndex, DefeatGaugeAmount(defender.Definition.Cost));
            if (result.AttackerDied)
                delta += SignedToward(attacker.OwnerIndex, DefeatGaugeAmount(attacker.Definition.Cost));
            result.GaugeDelta = delta;
            return result;
        }

        // 指定プレイヤーが押される方向への符号付き量。
        public static int SignedToward(int pushedPlayerIndex, int amount)
            => pushedPlayerIndex == 0 ? +amount : -amount;
    }
}
