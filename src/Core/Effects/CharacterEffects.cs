// このファイルについて:
//   メンバーカード用の登場時効果の実体（対象を取らないもの）。
//   - discard_random: 相手の手札をランダムに Value 枚捨てさせる（にあくろの妨害）
//   - mill: 相手のデッキの上から Value 枚を墓地に置く
//   対象を取る効果（バウンス、味方強化など）は、対象選択の仕組みができてからここに足す。
namespace Niakuro.Tcg.Core
{
    public static class CharacterEffectIds
    {
        public const string DiscardRandom = "discard_random";
        public const string Mill = "mill";
        public const string RevealHand = "reveal_hand";

        // これは自動解決せず、GameEngine の選択待ち（PendingDiscardChoice）で処理する特殊な効果。
        public const string DiscardChosenFaceDown = "discard_chosen_facedown";

        // 超絶自己中にあくろの起動型能力（1ターンに1度、3つから1つ選ぶ）。実体は GameEngine.UseSelfishAbility。
        public const string SelfishModal = "selfish_modal";
    }

    public static class CharacterEffects
    {
        private static bool _registered;

        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;

            EffectRegistry.Register(CharacterEffectIds.DiscardRandom, ctx =>
            {
                ctx.Game.DiscardRandomForEffect(ctx.PlayerIndex, ctx.Value);
            });

            EffectRegistry.Register(CharacterEffectIds.Mill, ctx =>
            {
                ctx.Game.MillForEffect(ctx.PlayerIndex, ctx.Value);
            });

            EffectRegistry.Register(CharacterEffectIds.RevealHand, ctx =>
            {
                ctx.Game.RevealRandomForEffect(ctx.PlayerIndex, ctx.Value);
            });
        }
    }
}
