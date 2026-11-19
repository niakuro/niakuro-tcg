// このファイルについて:
//   サンプルカード置き場。エンジンの動作確認用であり、正式なカードデザインではない。
//   ★ カードの増やし方: ここに1行追加するだけ。能力値の目安は「攻撃力+体力 ≒ コスト×2」。
//     強い効果を持つカードは、その分だけ能力値を下げる。
using System.Collections.Generic;

namespace Niakuro.Tcg.Core
{
    public static class SampleCards
    {
        // --- ユニット（コスト順） ---
        public static readonly UnitCardDefinition Scout       = new UnitCardDefinition("unit_scout",       "偵察兵",     1, 1, 1);
        public static readonly UnitCardDefinition Spearman    = new UnitCardDefinition("unit_spearman",    "槍兵",       2, 2, 2);
        public static readonly UnitCardDefinition Shieldman   = new UnitCardDefinition("unit_shieldman",   "盾兵",       2, 1, 3);
        public static readonly UnitCardDefinition Swordsman   = new UnitCardDefinition("unit_swordsman",   "剣兵",       3, 3, 3);
        public static readonly UnitCardDefinition Archer      = new UnitCardDefinition("unit_archer",      "弓兵",       3, 4, 2);
        public static readonly UnitCardDefinition Knight      = new UnitCardDefinition("unit_knight",      "騎士",       4, 4, 4);
        public static readonly UnitCardDefinition HeavyKnight = new UnitCardDefinition("unit_heavy",       "重騎士",     5, 5, 5);
        public static readonly UnitCardDefinition Golem       = new UnitCardDefinition("unit_golem",       "ゴーレム",   6, 4, 8);
        public static readonly UnitCardDefinition Dragon      = new UnitCardDefinition("unit_dragon",      "ドラゴン",   8, 8, 8);
        public static readonly UnitCardDefinition Finisher    = new UnitCardDefinition("unit_finisher",    "フィニッシャー", 10, 10, 6, piercesHaisuiProtection: true, abilityText: "フィニッシャー（背水の陣の保護を貫通する）");

        // --- 戦術（EffectId は Effects/SampleEffects.cs に実体がある） ---
        public static readonly TacticCardDefinition Supply    = new TacticCardDefinition("tactic_supply",   "紳士の嗜み",     1, SampleEffectIds.Draw, 2);
        public static readonly TacticCardDefinition Counter   = new TacticCardDefinition("tactic_counter",  "管理者権限", 5, SampleEffectIds.PushBack, 3);
        public static readonly TacticCardDefinition Moshimo   = new TacticCardDefinition("tactic_moshimo",  "もしも～し", 2, SampleEffectIds.FetchBigUnit, 5);
        public static readonly TacticCardDefinition Bombardment = new TacticCardDefinition("tactic_bombardment", "不可視の爆撃", 3, null, 0, targetDamage: 3);
        public static readonly TacticCardDefinition Volley   = new TacticCardDefinition("tactic_volley",   "一斉射撃", 4, SampleEffectIds.DamageAll, 1);
        public static readonly TacticCardDefinition ThreeRabbits = new TacticCardDefinition("tactic_usagi", "三兎追えば二兎得る", 3, SampleEffectIds.DrawPick, 3);
        public static readonly TacticCardDefinition Junbi    = new TacticCardDefinition("tactic_junbi",    "ゲームの準備", 4, SampleEffectIds.FetchTactics, 2);
        public static readonly TacticCardDefinition Sake     = new TacticCardDefinition("tactic_sake",     "酒！酒！酒！", 3, null, 0, buffOwnAttack: 2, reduceOwnMaxHealth: 2, ownDestroyAfterAttack: true);
        public static readonly TacticCardDefinition Chabudai = new TacticCardDefinition("tactic_chabudai", "ちゃぶ台返し！", 6, SampleEffectIds.DamageAllUnits, 3);
        public static readonly TacticCardDefinition Kurushimagire = new TacticCardDefinition("tactic_kurushimagire", "苦し紛れの一撃", 4, SampleEffectIds.DestroyRandom, 1);
        public static readonly TacticCardDefinition Saizensen = new TacticCardDefinition("tactic_saizensen", "最前線攻略tv⁉︎", 3, SampleEffectIds.Ramp, 1);

        // デッキ構築ルールを満たすサンプルデッキ（40枚・同名3枚まで）。
        // 動作確認用なので、中身のバランスは未調整。
        public static List<CardDefinition> BuildSampleDeck()
        {
            var deck = new List<CardDefinition>();
            var core = new CardDefinition[]
            {
                Scout, Spearman, Shieldman, Swordsman, Archer,
                Knight, HeavyKnight, Golem, Supply,
            };
            foreach (var card in core)
            {
                for (int i = 0; i < GameConstants.MaxCopiesPerCard; i++) deck.Add(card);
            }
            // 9種 × 3枚 = 27枚。残り13枚を足して40枚にする。
            for (int i = 0; i < 3; i++) deck.Add(Dragon);
            for (int i = 0; i < 3; i++) deck.Add(Counter);
            for (int i = 0; i < 3; i++) deck.Add(Moshimo);
            for (int i = 0; i < 3; i++) deck.Add(Junbi);
            deck.Add(Finisher);
            return deck; // 27 + 3 + 3 + 3 + 3 + 1 = 40枚
        }
    }
}
