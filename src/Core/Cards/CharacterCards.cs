// このファイルについて:
//   メンバー本人がモデルのキャラクターカード置き場（1人4体ずつ、計24体）。
//   役割分担（2026-11-19 に本人が決めた感覚）:
//     にあくろ = 邪魔する型 / ニトグリ = 連携型 / マスカット = バランス型
//     ココナッツ = パワー・フィニッシャー型 / やーや = トリッキー / ミルキー = ドロー・手数型
//   数値の目安は「攻撃力+体力 ≒ コスト×2」、効果持ちはその分下げてある。
//   効果のうち対象を取らないもの（ドロー、手札破壊、デッキ破壊）はエンジンが自動解決する。
//   対象を取るもの（バウンス、味方強化、ダメージ移し替え）は AbilityText のみで、
//   対象選択の仕組みを作るときに Effects/ へ実体を足す（未実装と明記してあるもの）。
namespace Niakuro.Tcg.Core
{
    public static class CharacterCards
    {
        // ---------- にあくろ（邪魔する型） ----------
        public static readonly UnitCardDefinition Niakuro = new UnitCardDefinition(
            "char_niakuro", "にあくろ", 3, 2, 2,
            abilityText: "登場時、相手の手札を1枚選んで捨てる。（裏向きのカードは、中身を見ずに選ぶ）",
            onPlayEffectId: CharacterEffectIds.DiscardChosenFaceDown, onPlayEffectValue: 1).WithTraits(CardTraits.Kosan, CardTraits.Akuyuu);

        public static readonly UnitCardDefinition NiakuroSmall = new UnitCardDefinition(
            "char_niakuro_small", "みにあくろ", 1, 1, 1,
            abilityText: "登場時、相手の手札をランダムに1枚表向きにする。（表向きにしたカードは、手札を離れるまで表向きのまま）",
            onPlayEffectId: CharacterEffectIds.RevealHand, onPlayEffectValue: 1).WithTraits(CardTraits.Kosan, CardTraits.Akuyuu);

        public static readonly UnitCardDefinition NiakuroGamer = new UnitCardDefinition(
            "char_niakuro_gamer", "ゲーマーにあくろ", 5, 3, 5,
            abilityText: "このユニットが場にいる間、相手のユニットが攻撃する時、その攻撃力を-1する。",
            attackDebuffAura: 1).WithTraits(CardTraits.Kosan, CardTraits.Akuyuu);

        public static readonly UnitCardDefinition NiakuroSuper = new UnitCardDefinition(
            "char_niakuro_super", "超絶自己中にあくろ", 9, 7, 7,
            abilityText: "登場時、お互いの手札を全て表向きにし、自分の手札を1枚と相手の手札を1枚選んで捨てる。このユニットが場にいる間、お互いの手札は表向きになる（新しく手札に加わるカードも表向きになる）。【起動】1ターンに1度、次のうち1つを選ぶ。①カードを1枚引く。その後、自分の手札を1枚と相手の手札を1枚選んで捨てる。②相手のユニット1体の体力の最大値を-1する。③戦線ゲージを相手側に1進める。このユニットは全ての特徴を持つ。",
            revealsAllHands: true, selfishDiscardOnPlay: true,
            activatedAbilityId: CharacterEffectIds.SelfishModal,
            allTraits: true).WithTraits(CardTraits.Kosan, CardTraits.Akuyuu);

        // 猫被りにあくろ: 善友側の顔を持つにあくろ。回復役。
        public static readonly UnitCardDefinition NiakuroCat = new UnitCardDefinition(
            "char_niakuro_cat", "猫被りにあくろ", 4, 2, 3,
            abilityText: "登場時と破壊された時、自分のユニット1体を選び、体力を2回復する。",
            healOnPlay: 2, healOnDeath: 2).WithTraits(CardTraits.Kosan, CardTraits.Zennyuu);

        // ---------- ニトグリ（連携型） ----------
        public static readonly UnitCardDefinition Nitoguri = new UnitCardDefinition(
            "char_nitoguri", "ニトグリ", 3, 2, 3,
            abilityText: "連携2：古参（自分の場に自分以外の古参がいる間、攻撃力+2）",
            linkageTrait: CardTraits.Kosan, linkageAttackBonus: 2).WithTraits(CardTraits.Kosan, CardTraits.Akuyuu);

        public static readonly UnitCardDefinition NitoguriMini = new UnitCardDefinition(
            "char_nitoguri_mini", "ミニトグリ", 2, 2, 1,
            abilityText: "登場時、相手のユニット1体を選び、2ダメージを与える。",
            damageOnPlay: 2).WithTraits(CardTraits.Kosan, CardTraits.Akuyuu);

        public static readonly UnitCardDefinition NitoguriGamer = new UnitCardDefinition(
            "char_nitoguri_gamer", "ゲーマーニトグリ", 5, 5, 4,
            abilityText: "登場時、自分のユニットを2体まで選び、攻撃力を+1する。（同じユニットは1回だけ）",
            buffAttackOnPlay: 1, buffTargetsOnPlay: 2).WithTraits(CardTraits.Kosan, CardTraits.Akuyuu);

        public static readonly UnitCardDefinition NitoguriBakusoku = new UnitCardDefinition(
            "char_nitoguri_bakusoku", "爆速退社ニトグリ", 3, 2, 3,
            abilityText: "登場時、自分の場に自分以外のユニットがいれば、速攻を得る。（速攻：出たターンでも攻撃できる）このユニットは攻撃した後、手札に戻る。",
            conditionalHaste: true, returnsToHandAfterAttack: true).WithTraits(CardTraits.Kosan, CardTraits.Akuyuu);

        public static readonly UnitCardDefinition NitoguriOrderless = new UnitCardDefinition(
            "char_nitoguri_orderless", "秩序なき連携ニトグリ", 8, 8, 7,
            abilityText: "このユニットが場にいる間、自分のユニット全ては連携1：古参を得る（もともと持っている連携と重なる）。自分の場に自分以外の悪友がいれば、その悪友全てとこのユニットは攻撃力を+1し、体力の最大値を-1する。自分の場に善友がいれば、その善友全てとこのユニットは体力の最大値を+1する。",
            grantsLinkageTrait: CardTraits.Kosan, grantsLinkageBonus: 1,
            orderlessAura: true).WithTraits(CardTraits.Kosan, CardTraits.Akuyuu);

        // ---------- マスカット（バランス型） ----------
        public static readonly UnitCardDefinition Muscat = new UnitCardDefinition(
            "char_muscat", "マスカット", 3, 3, 3,
            abilityText: "守護",
            guard: true).WithTraits(CardTraits.Kosan, CardTraits.Zennyuu);

        public static readonly UnitCardDefinition MuscatGamer = new UnitCardDefinition(
            "char_muscat_gamer", "ゲーマーマスカット", 5, 2, 6,
            abilityText: "守護。登場時、自分のユニット1体を選んで、守護を付与する。",
            guard: true, grantsGuardOnPlay: true).WithTraits(CardTraits.Kosan, CardTraits.Zennyuu);

        public static readonly UnitCardDefinition MuscatYoung = new UnitCardDefinition(
            "char_muscat_young", "ミニマスカット", 1, 0, 3,
            abilityText: "守護",
            guard: true).WithTraits(CardTraits.Kosan, CardTraits.Zennyuu);

        public static readonly UnitCardDefinition MuscatRipe = new UnitCardDefinition(
            "char_muscat_ripe", "成熟マスカット", 4, 3, 4,
            abilityText: "このユニットが場にいる間、守護を持つ自分のユニットの体力の最大値を+1する。",
            guardHealthAura: 1).WithTraits(CardTraits.Kosan, CardTraits.Zennyuu);

        public static readonly UnitCardDefinition MuscatBig = new UnitCardDefinition(
            "char_muscat_big", "シャインマスカット", 8, 4, 8,
            abilityText: "守護。このユニットが場にいる間、守護を持つ自分のユニットの攻撃力と体力の最大値を+1する。破壊された時、自分のデッキからコスト5以下で守護を持つユニットを1枚選んで、場に出す。",
            guard: true, guardStatAura: 1, deployGuardOnDeathMaxCost: 5).WithTraits(CardTraits.Kosan, CardTraits.Zennyuu);

        // ---------- ココナッツ（パワー・フィニッシャー型） ----------
        public static readonly UnitCardDefinition Coconut = new UnitCardDefinition(
            "char_coconut", "ココナッツ", 4, 4, 3,
            abilityText: "登場時、相手のユニット1体を選んで、3ダメージを与える。",
            damageOnPlay: 3).WithTraits(CardTraits.Kosan, CardTraits.Akuyuu);

        public static readonly UnitCardDefinition CoconutGamer = new UnitCardDefinition(
            "char_coconut_gamer", "ゲーマーココナッツ", 5, 5, 3,
            abilityText: "先手必勝（出たターンに、相手のユニットを攻撃できる）。登場したターン、戦闘によるダメージを受けない。",
            canAttackUnitsWhenPlayed: true,
            battleDamageImmuneOnPlayTurn: true).WithTraits(CardTraits.Kosan, CardTraits.Akuyuu);

        public static readonly UnitCardDefinition CoconutSmall = new UnitCardDefinition(
            "char_coconut_small", "ミニココナッツ", 2, 3, 1,
            abilityText: "先手必勝（出たターンに、相手のユニットを攻撃できる）",
            canAttackUnitsWhenPlayed: true).WithTraits(CardTraits.Kosan, CardTraits.Akuyuu);

        public static readonly UnitCardDefinition CoconutSolo = new UnitCardDefinition(
            "char_coconut_solo", "孤軍奮闘ココナッツ", 6, 5, 6,
            abilityText: "先手必勝（出たターンに、相手のユニットを攻撃できる）。このユニットが戦闘で受けるダメージは、最大で2になる。このユニットは、相手のユニットしか攻撃できない。",
            canAttackUnitsWhenPlayed: true,
            battleDamageTakenCap: 2,
            canOnlyAttackUnits: true).WithTraits(CardTraits.Kosan, CardTraits.Akuyuu);

        public static readonly UnitCardDefinition CoconutMarksman = new UnitCardDefinition(
            "char_coconut_marksman", "百発百中ココナッツ", 8, 8, 6,
            abilityText: "先手必勝（出たターンに、相手のユニットを攻撃できる）。登場したターン、2回まで攻撃できる。このユニットが戦闘で受けるダメージは、最大で2になる。このユニットが攻撃して相手のユニットを破壊した時、このユニットの体力を1回復する。",
            canAttackUnitsWhenPlayed: true,
            battleDamageTakenCap: 2,
            attacksTwiceOnPlayTurn: true,
            selfHealOnBattleKill: 1).WithTraits(CardTraits.Kosan, CardTraits.Akuyuu);

        // ---------- やーや（トリッキー） ----------
        public static readonly UnitCardDefinition Yaya = new UnitCardDefinition(
            "char_yaya", "やーや", 3, 2, 2,
            abilityText: "登場時、自分の基本ポイントを+1する。",
            onPlayEffectId: SampleEffectIds.Ramp, onPlayEffectValue: 1).WithTraits(CardTraits.Kosan, CardTraits.Zennyuu);

        public static readonly UnitCardDefinition YayaSmall = new UnitCardDefinition(
            "char_yaya_small", "みにやーや", 1, 1, 1,
            abilityText: "登場時、自分の基本ポイントが5以上なら、このターンに使えるポイントを+1する。",
            onPlayEffectId: SampleEffectIds.TempPoints, onPlayEffectValue: 1,
            onPlayMinBasePoints: 5).WithTraits(CardTraits.Kosan, CardTraits.Zennyuu);

        public static readonly UnitCardDefinition YayaGamer = new UnitCardDefinition(
            "char_yaya_gamer", "ゲーマーやーや", 5, 4, 4,
            abilityText: "登場時、自分の基本ポイントが8以上なら、このユニットの攻撃力を+2し、体力の最大値を+2する。このターンに使えるポイントを+2する。",
            selfBuffOnPlayAttack: 2,
            selfBuffOnPlayHealth: 2,
            selfBuffMinBasePoints: 8,
            pointsGainOnPlay: 2).WithTraits(CardTraits.Kosan, CardTraits.Zennyuu);

        public static readonly UnitCardDefinition YayaRich = new UnitCardDefinition(
            "char_yaya_rich", "器用富豪やーや", 4, 4, 4,
            abilityText: "登場時と攻撃時、以下から1つを選んで使う。①カードを1枚引く。②相手のユニット1体を選び、3ダメージを与える。③このターンに使えるポイントを+1する。",
            richChoiceDamage: 3).WithTraits(CardTraits.Kosan, CardTraits.Zennyuu);

        public static readonly UnitCardDefinition YayaMaster = new UnitCardDefinition(
            "char_yaya_master", "手練手管やーや", 7, 6, 6,
            abilityText: "登場時と攻撃時、以下から1つを選んで使う。自分の基本ポイントが10以上なら、2つまで使える（同じ効果を2回選んでもよい）。①カードを2枚引く。②相手のユニット全てに3ダメージを与える。③このターンに使えるポイントを+2する。④自分の場・墓地・手札のユニットを1枚選んで除外する。そのユニットの登場時効果を使い、そのユニットの能力をコピーする。",
            masterChoiceDamage: 3,
            masterChoiceExtraPickMinBasePoints: 10).WithTraits(CardTraits.Kosan, CardTraits.Zennyuu);

        // ---------- ミルキー（ドロー・手数型） ----------
        public static readonly UnitCardDefinition Kohaku = new UnitCardDefinition(
            "char_kohaku", "ミルキー", 4, 3, 4,
            abilityText: "登場時、カードを2枚引く。破壊された時、カードを2枚引く。",
            onPlayEffectId: SampleEffectIds.Draw, onPlayEffectValue: 2, drawOnDeath: 2).WithTraits(CardTraits.Kosan, CardTraits.Zennyuu);

        public static readonly UnitCardDefinition KohakuSmall = new UnitCardDefinition(
            "char_kohaku_small", "ミニミルキー", 1, 1, 1,
            abilityText: "登場時、カードを1枚引く。",
            onPlayEffectId: SampleEffectIds.Draw, onPlayEffectValue: 1).WithTraits(CardTraits.Kosan, CardTraits.Zennyuu);

        public static readonly UnitCardDefinition KohakuShine = new UnitCardDefinition(
            "char_kohaku_shine", "ゲーマーミルキー", 5, 2, 6,
            abilityText: "登場時、カードを2枚引く。このユニットが場にいる間、自分のターン開始時、カードを1枚引く。",
            onPlayEffectId: SampleEffectIds.Draw, onPlayEffectValue: 2, drawAtTurnStart: 1).WithTraits(CardTraits.Kosan, CardTraits.Zennyuu);

        public static readonly UnitCardDefinition KohakuCat = new UnitCardDefinition(
            "char_kohaku_cat", "猫好きミルキー", 2, 1, 2,
            abilityText: "登場時、自分のデッキからコスト4以下のユニットをランダムに1枚、手札に加える。",
            onPlayEffectId: SampleEffectIds.FetchSmallUnit, onPlayEffectValue: 4).WithTraits(CardTraits.Kosan, CardTraits.Zennyuu);

        public static readonly UnitCardDefinition KohakuBig = new UnitCardDefinition(
            "char_kohaku_big", "無数の選択肢ミルキー", 8, 0, 0,
            abilityText: "このユニットの攻撃力と体力は、この試合で自分が効果で引いたカードの枚数の半分（切り捨て）になる。登場時、カードを2枚引く。その後、自分の手札から、自分の手札の枚数よりコストが低いユニットを1枚選んで、コストを支払わずに場に出す。",
            onPlayEffectId: SampleEffectIds.Draw, onPlayEffectValue: 2,
            scalesWithEffectDraws: true, freeDeployOnPlay: true).WithTraits(CardTraits.Kosan, CardTraits.Zennyuu);

        // ---------- メンバーテーマの戦術 ----------
        public static readonly TacticCardDefinition Sabotage = new TacticCardDefinition(
            "tactic_sabotage", "にあくろの遊び心", 3, CharacterEffectIds.DiscardRandom, 1);
    }
}
