// このファイルについて:
//   「背水の陣」の管理。ルール:
//   - 攻撃でゲージの残りが5以下になった瞬間に必須発動（温存できない）
//   - 各プレイヤー、ゲーム中1回だけ
//   - 3つの行動から1つ選ぶ。先に選ばれた行動は、相手は選べない（封じ合い）
//   - 効果の適用そのもの（ドローや押し戻し）は GameEngine が行い、ここは記録と判定だけ。
namespace Niakuro.Tcg.Core
{
    public enum HaisuiAction
    {
        PushBack,     // ゲージを3押し戻す
        DrawCards,    // 4枚ドロー
        NextTurnPoints, // 次の自分のターンのポイント +3
    }

    public sealed class HaisuiSystem
    {
        private readonly bool[] _used = new bool[2];
        private readonly System.Collections.Generic.HashSet<HaisuiAction> _takenActions
            = new System.Collections.Generic.HashSet<HaisuiAction>();

        public bool HasUsed(int playerIndex) => _used[playerIndex];
        public bool IsActionTaken(HaisuiAction action) => _takenActions.Contains(action);
        public bool CanUse(int playerIndex) => !_used[playerIndex];

        // 発動を確定させる。相手が既に選んだ行動は選べない。
        public bool TryActivate(int playerIndex, HaisuiAction action)
        {
            if (_used[playerIndex]) return false;
            if (_takenActions.Contains(action)) return false;
            _used[playerIndex] = true;
            _takenActions.Add(action);
            return true;
        }
    }
}
