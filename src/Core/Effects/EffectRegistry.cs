// このファイルについて:
//   戦術カードの「効果の実体」を登録しておく場所。
//   カードデータは EffectId（文字列）しか持たないので、新しい効果を増やすときは
//   ここに1つ登録するだけで、エンジン本体には手を入れなくてよい。
using System;
using System.Collections.Generic;

namespace Niakuro.Tcg.Core
{
    // 効果が必要とする最小限の文脈。エンジン内部の都合が変わっても効果側は影響を受けにくい。
    public sealed class EffectContext
    {
        public GameEngine Game { get; }
        public int PlayerIndex { get; }   // 効果を使ったプレイヤー
        public int OpponentIndex => 1 - PlayerIndex;
        public int Value { get; }         // カードに書かれた効果量

        public EffectContext(GameEngine game, int playerIndex, int value)
        {
            Game = game;
            PlayerIndex = playerIndex;
            Value = value;
        }
    }

    public static class EffectRegistry
    {
        private static readonly Dictionary<string, Action<EffectContext>> Effects
            = new Dictionary<string, Action<EffectContext>>();

        public static void Register(string effectId, Action<EffectContext> effect)
        {
            Effects[effectId] = effect;
        }

        public static bool TryResolve(string effectId, EffectContext context)
        {
            if (effectId == null) return false;
            if (!Effects.TryGetValue(effectId, out var effect)) return false;
            effect(context);
            return true;
        }

        public static bool IsRegistered(string effectId)
            => effectId != null && Effects.ContainsKey(effectId);
    }
}
