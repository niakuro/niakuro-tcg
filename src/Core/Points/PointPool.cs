// このファイルについて:
//   1プレイヤー分のポイント計算。ルールはこの3つだけ:
//   1) 基本ポイントは最初のターン 2、自分のターンごとに +1（上限なし）
//   2) 使い残しの半分（切り捨て）が次のターンに繰り越される
//   3) 背水の陣や後攻補正などの一時ボーナスは、次の自分のターンに加算されて消える
namespace Niakuro.Tcg.Core
{
    public sealed class PointPool
    {
        public int BasePoints { get; private set; } = GameConstants.StartingBasePoints;
        public int Carryover { get; private set; }   // 前ターンから繰り越された分
        public int Available { get; private set; }   // このターン使える残り
        public int PendingBonus { get; private set; } // 次の自分のターンに加わる一時ボーナス

        private bool _isFirstTurn = true;

        // 自分のターン開始時に呼ぶ。使えるポイントが確定し、その量を返す。
        public int BeginTurn()
        {
            if (!_isFirstTurn) BasePoints += 1;
            _isFirstTurn = false;

            Available = BasePoints + Carryover + PendingBonus;
            PendingBonus = 0;
            return Available;
        }

        // 基本ポイントそのものを恒久的に増やす（ランプ効果「最前線攻略tv⁉︎」用）。
        // このターンの残りには影響せず、次の自分のターンから効く。
        public void IncreaseBasePoints(int amount) => BasePoints += amount;

        // このターンだけ使えるポイントを増やす（みにやーや用）。
        // 余った分は通常の繰り越し計算にそのまま混ざる。
        public void AddPointsThisTurn(int amount) => Available += amount;

        public bool CanSpend(int cost) => cost <= Available;

        public bool TrySpend(int cost)
        {
            if (!CanSpend(cost)) return false;
            Available -= cost;
            return true;
        }

        // 自分のターン終了時に呼ぶ。残りの半分（切り捨て）を繰り越しにして返す。
        public int EndTurn()
        {
            Carryover = Available / 2;
            Available = 0;
            return Carryover;
        }

        // 背水の陣「次のターン +3」や後攻補正などを積む場所。
        public void AddPendingBonus(int amount)
        {
            PendingBonus += amount;
        }
    }
}
