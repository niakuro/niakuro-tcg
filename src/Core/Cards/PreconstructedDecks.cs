// このファイルについて:
//   構築済みデッキ3種のレシピ。どれも「デッキ40枚・同名3枚まで」を満たすよう組んである
//   （EngineCheck で自動検証している）。
//   組み方の方針: メンバー2人分ずつのユニットを軸に、戦術で色を足す。
//   猫被りにあくろ（善友）は、どのデッキにも入る便利な回復役として共通で入れている。
using System.Collections.Generic;

namespace Niakuro.Tcg.Core
{
    public static class PreconstructedDecks
    {
        private static void Add(List<CardDefinition> deck, CardDefinition card, int count)
        {
            for (int i = 0; i < count; i++) deck.Add(card);
        }

        // にあくろ（妨害）× ニトグリ（連携）: 相手の手札を削りながら連携で押し、
        // 爆速退社ニトグリの出入りと酒！酒！酒！のバーストでゲージを詰める。
        // 大型への回答として苦し紛れの一撃、膠着の起爆剤としてちゃぶ台返し！を入れてある。
        public static List<CardDefinition> NiakuroNitoguri()
        {
            var deck = new List<CardDefinition>();
            Add(deck, CharacterCards.Niakuro, 3);
            Add(deck, CharacterCards.NiakuroSmall, 3);
            Add(deck, CharacterCards.NiakuroGamer, 2);
            Add(deck, CharacterCards.NiakuroSuper, 2);
            Add(deck, CharacterCards.NiakuroCat, 3);
            Add(deck, CharacterCards.Nitoguri, 3);
            Add(deck, CharacterCards.NitoguriMini, 3);
            Add(deck, CharacterCards.NitoguriGamer, 2);
            Add(deck, CharacterCards.NitoguriOrderless, 2);
            Add(deck, CharacterCards.NitoguriBakusoku, 3);
            Add(deck, SampleCards.Supply, 3);
            Add(deck, CharacterCards.Sabotage, 3);
            Add(deck, SampleCards.Moshimo, 1);
            Add(deck, SampleCards.Sake, 2);
            Add(deck, SampleCards.Bombardment, 2);
            Add(deck, SampleCards.Kurushimagire, 1);
            Add(deck, SampleCards.Saizensen, 2);
            return deck; // 26 + 14 = 40
        }

        // マスカット（守護）× ミルキー（ドロー・手数）: 守護の壁で守りながらドローで回し続け、
        // 終盤はシャインマスカットと無数の選択肢ミルキーで締める。
        public static List<CardDefinition> MuscatKohaku()
        {
            var deck = new List<CardDefinition>();
            Add(deck, CharacterCards.MuscatYoung, 3);   // ミニマスカット
            Add(deck, CharacterCards.KohakuSmall, 3);   // ミニミルキー
            Add(deck, CharacterCards.KohakuCat, 2);     // 猫好きミルキー
            Add(deck, CharacterCards.Muscat, 2);        // マスカット
            Add(deck, CharacterCards.Kohaku, 3);        // ミルキー
            Add(deck, CharacterCards.MuscatRipe, 3);    // 成熟マスカット
            Add(deck, CharacterCards.NiakuroCat, 2);    // 猫被りにあくろ
            Add(deck, CharacterCards.MuscatGamer, 3);   // ゲーマーマスカット
            Add(deck, CharacterCards.KohakuShine, 3);   // ゲーマーミルキー
            Add(deck, CharacterCards.MuscatBig, 2);     // シャインマスカット
            Add(deck, CharacterCards.KohakuBig, 2);     // 無数の選択肢ミルキー
            Add(deck, SampleCards.Supply, 3);           // 紳士の嗜み
            Add(deck, SampleCards.ThreeRabbits, 1);     // 三兎追えば二兎得る
            Add(deck, SampleCards.Moshimo, 2);          // もしも～し
            Add(deck, SampleCards.Saizensen, 2);        // 最前線攻略tv⁉︎
            Add(deck, SampleCards.Bombardment, 3);      // 不可視の爆撃
            Add(deck, CharacterCards.Sabotage, 1);      // にあくろの遊び心
            return deck; // 28 + 12 = 40
        }

        // ココナッツ（パワー）× やーや（トリッキー）: ランプで加速し、先手必勝と大型で押し切る。
        public static List<CardDefinition> CoconutYaya()
        {
            var deck = new List<CardDefinition>();
            Add(deck, CharacterCards.YayaSmall, 3);      // みにやーや
            Add(deck, CharacterCards.CoconutSmall, 3);   // ミニココナッツ
            Add(deck, CharacterCards.Yaya, 3);           // やーや
            Add(deck, CharacterCards.Coconut, 3);        // ココナッツ
            Add(deck, CharacterCards.YayaRich, 3);       // 器用富豪やーや
            Add(deck, CharacterCards.NiakuroCat, 2);     // 猫被りにあくろ
            Add(deck, CharacterCards.CoconutGamer, 3);   // ゲーマーココナッツ
            Add(deck, CharacterCards.YayaGamer, 2);      // ゲーマーやーや
            Add(deck, CharacterCards.CoconutSolo, 2);    // 孤軍奮闘ココナッツ
            Add(deck, CharacterCards.YayaMaster, 2);     // 手練手管やーや
            Add(deck, CharacterCards.CoconutMarksman, 1);// 百発百中ココナッツ
            Add(deck, SampleCards.Supply, 3);            // 紳士の嗜み
            Add(deck, SampleCards.Saizensen, 3);         // 最前線攻略tv⁉︎
            Add(deck, SampleCards.Moshimo, 2);           // もしも～し
            Add(deck, SampleCards.Bombardment, 3);       // 不可視の爆撃
            Add(deck, SampleCards.Volley, 1);            // 一斉射撃
            Add(deck, SampleCards.Counter, 1);           // 管理者権限
            return deck; // 27 + 13 = 40
        }
    }
}
