// このファイルについて:
//   戦線ゲージそのもの。位置の管理と「どれだけ押せるか」の計算だけを担当する純粋なクラス。
//   UnityEngine には依存しないので、単体でテストできる。
//   座標の約束: Position が + なら「プレイヤー1側がプレイヤー0側を押している」状態。
//   つまり Position = +15 でプレイヤー0の負け、-15 でプレイヤー1の負け。
using System;

namespace Niakuro.Tcg.Core
{
    public sealed class FrontlineGauge
    {
        public int Position { get; private set; }

        public bool IsFinished => Math.Abs(Position) >= GameConstants.GaugeMax;

        // 指定プレイヤーから見た「負けまでの残り」。0で敗北。
        public int RemainingFor(int playerIndex)
        {
            // プレイヤー0は + 方向に押されると負け、プレイヤー1は - 方向で負け。
            int pushed = playerIndex == 0 ? Position : -Position;
            return GameConstants.GaugeMax - pushed;
        }

        // 背水の陣の発動圏内かどうか（残り HaisuiRemaining 以下）。
        public bool IsInHaisuiZone(int playerIndex)
            => RemainingFor(playerIndex) <= GameConstants.HaisuiRemaining;

        /// <summary>
        /// ゲージを押す。amount は常に正の量、pushedPlayer は押される側。
        /// 背水の陣が未使用のプレイヤーは、端まで押し切られても「残り1」で止まる
        /// （フィニッシャーの貫通攻撃 piercesProtection: true の場合を除く）。
        /// 実際に動いた量を返す。
        /// </summary>
        public int Push(int amount, int pushedPlayer, bool targetHaisuiUnused, bool piercesProtection)
        {
            if (amount <= 0) return 0;
            int sign = pushedPlayer == 0 ? +1 : -1; // 押される側へ進む向き
            int before = Position;

            int limit = GameConstants.GaugeMax;
            if (targetHaisuiUnused && !piercesProtection)
            {
                // 残り1ストップ: 端の1つ手前までしか進めない。
                limit = GameConstants.GaugeMax - 1;
            }

            int target = Position + sign * amount;
            int clamped = Math.Max(-limit, Math.Min(limit, target));
            // 既に端を超えた位置からは後戻り方向の移動を妨げないよう、
            // 「押す方向への前進」だけを limit で止める。
            Position = sign > 0 ? Math.Max(before, clamped) : Math.Min(before, clamped);
            if (IsFinished) Position = Math.Max(-GameConstants.GaugeMax, Math.Min(GameConstants.GaugeMax, Position));
            return Math.Abs(Position - before);
        }

        // 押し戻し専用: 自分側へ戻す動き。残り1ストップの対象にはならない。
        public int PushBack(int amount, int playerIndex)
        {
            if (amount <= 0) return 0;
            int sign = playerIndex == 0 ? -1 : +1; // 自分の陣地から離れる向き
            int before = Position;
            Position = Math.Max(-GameConstants.GaugeMax, Math.Min(GameConstants.GaugeMax, Position + sign * amount));
            return Math.Abs(Position - before);
        }
    }
}
