// このファイルについて:
//   ゲーム全体の数値ルール（ゲージ幅、場の上限など）を一か所に集めた定数集。
//   バランス調整で数字を変えたくなったら、まずここだけを見ればよい。
namespace Niakuro.Tcg.Core
{
    public static class GameConstants
    {
        // 戦線ゲージ: -GaugeMax ～ +GaugeMax。端まで押し切ったら勝ち。
        public const int GaugeMax = 15;

        // 背水の陣: ゲージの残りがこの数以下になった瞬間に必須発動。
        public const int HaisuiRemaining = 5;

        // 背水の陣の効果量。
        public const int HaisuiPushBack = 5;
        public const int HaisuiDraw = 4;
        public const int HaisuiPointBonus = 3;

        // ポイント: 最初のターンの基本ポイント。毎ターン +1、上限なし。
        public const int StartingBasePoints = 2;

        // 場に出せるユニットの上限。
        public const int FieldLimit = 5;

        // 手札の上限（自分のターン終了時に超えていたら捨てる）。
        public const int HandLimit = 10;

        // 初期手札とデッキ構築ルール。
        public const int StartingHand = 5;
        public const int DeckSize = 40;
        public const int MaxCopiesPerCard = 3;

        // 後攻補正: 最初のターンだけポイント +1 と追加ドロー 1 枚。
        public const int SecondPlayerPointBonus = 1;
        public const int SecondPlayerExtraDraw = 1;
    }
}
