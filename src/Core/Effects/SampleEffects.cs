// このファイルについて:
//   サンプル用の戦術効果の実体。新しい効果の書き方の見本でもある。
//   ★ 効果の増やし方: ここにメソッドを1つ書いて、RegisterAll に1行足すだけ。
namespace Niakuro.Tcg.Core
{
    public static class SampleEffectIds
    {
        public const string Draw = "draw";
        public const string PushBack = "push_back";
        public const string FetchUnit = "fetch_random_unit";
        public const string FetchBigUnit = "fetch_random_unit_min_cost";
        public const string FetchSmallUnit = "fetch_random_unit_max_cost";
        public const string DamageAll = "damage_all_opponents";
        public const string DrawPick = "draw_pick";
        public const string FetchTactics = "fetch_tactics";
        public const string DamageAllUnits = "damage_all_units";
        public const string DestroyRandom = "destroy_random_opponent";
        public const string Ramp = "increase_base_points";
        public const string TempPoints = "gain_points_this_turn";
    }

    public static class SampleEffects
    {
        private static bool _registered;

        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;

            // 「Value 枚ドロー」
            EffectRegistry.Register(SampleEffectIds.Draw, ctx =>
            {
                ctx.Game.DrawCardsForEffect(ctx.PlayerIndex, ctx.Value);
            });

            // 「ゲージを Value だけ自分の側へ押し戻す」
            EffectRegistry.Register(SampleEffectIds.PushBack, ctx =>
            {
                ctx.Game.PushBackGaugeForEffect(ctx.PlayerIndex, ctx.Value);
            });

            // 「デッキからユニットをランダムに1枚、手札に加える」
            EffectRegistry.Register(SampleEffectIds.FetchUnit, ctx =>
            {
                ctx.Game.FetchRandomUnitForEffect(ctx.PlayerIndex);
            });

            // 「デッキからコスト Value 以上のユニットをランダムに1枚、手札に加える」（もしも～し）
            EffectRegistry.Register(SampleEffectIds.FetchBigUnit, ctx =>
            {
                ctx.Game.FetchRandomBigUnitForEffect(ctx.PlayerIndex, ctx.Value);
            });

            // 「デッキからコスト Value 以下のユニットをランダムに1枚、手札に加える」（猫好きミルキー）
            EffectRegistry.Register(SampleEffectIds.FetchSmallUnit, ctx =>
            {
                ctx.Game.FetchRandomSmallUnitForEffect(ctx.PlayerIndex, ctx.Value);
            });

            // 「相手のユニット全体に Value ダメージ」（一斉射撃）
            EffectRegistry.Register(SampleEffectIds.DamageAll, ctx =>
            {
                ctx.Game.DamageAllOpponentUnitsForEffect(ctx.PlayerIndex, ctx.Value);
            });

            // 「Value 枚引いて、その中から2枚を手札に加え、残りを墓地に置く」（三兎追えば二兎得る）
            EffectRegistry.Register(SampleEffectIds.DrawPick, ctx =>
            {
                ctx.Game.DrawPickForEffect(ctx.PlayerIndex, ctx.Value, 2);
            });

            // 「デッキから戦術を Value 枚、手札に加える」（ゲームの準備）
            EffectRegistry.Register(SampleEffectIds.FetchTactics, ctx =>
            {
                ctx.Game.FetchRandomTacticsForEffect(ctx.PlayerIndex, ctx.Value);
            });

            // 「お互いのユニット全てに Value ダメージ」（ちゃぶ台返し！）
            EffectRegistry.Register(SampleEffectIds.DamageAllUnits, ctx =>
            {
                ctx.Game.DamageAllUnitsForEffect(ctx.Value);
            });

            // 「相手のユニットをランダムに Value 体破壊する」（苦し紛れの一撃）
            EffectRegistry.Register(SampleEffectIds.DestroyRandom, ctx =>
            {
                ctx.Game.DestroyRandomOpponentUnitsForEffect(ctx.PlayerIndex, ctx.Value);
            });

            // 「基本ポイントを Value 増やす（次の自分のターンから）」
            EffectRegistry.Register(SampleEffectIds.Ramp, ctx =>
            {
                ctx.Game.IncreaseBasePointsForEffect(ctx.PlayerIndex, ctx.Value);
            });

            // 「このターンに使えるポイントを Value 増やす」
            EffectRegistry.Register(SampleEffectIds.TempPoints, ctx =>
            {
                ctx.Game.GainPointsThisTurnForEffect(ctx.PlayerIndex, ctx.Value);
            });
        }
    }
}
