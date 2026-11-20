// このファイルについて:
//   ルールエンジンの動作確認プログラム。決めたルールがコード通り動くかを機械的にチェックする。
//   実行: dotnet run --project tests/EngineCheck
//   新しいルールを足したら、ここにも確認ケースを1つ足すのがおすすめ。
using System;
using System.Collections.Generic;
using Niakuro.Tcg.Core;

namespace EngineCheck
{
    internal static class Program
    {
        private static int _failures;

        private static void Check(bool condition, string label)
        {
            Console.WriteLine((condition ? "PASS " : "FAIL ") + label);
            if (!condition) _failures++;
        }

        private static UnitState MakeUnit(UnitCardDefinition def, int owner)
            => new UnitState(new CardInstance(def), owner, summonedTurn: 0);

        private static int Main()
        {
            // --- ポイント: 繰り越し（切り捨て） ---
            var pool = new PointPool();
            Check(pool.BeginTurn() == 2, "ポイント: 1ターン目は2");
            Check(pool.TurnMaximum == 2, "ポイント表示: ターン上限は2");
            Check(pool.EndTurn() == 1, "ポイント: 残り2の半分=1を繰り越し");
            Check(pool.BeginTurn() == 4, "ポイント: 2ターン目は基本3+繰り越し1=4");
            pool.AddPointsThisTurn(2);
            Check(pool.TurnMaximum == 6, "ポイント表示: ターン中の追加分も上限に含める");
            Check(pool.TrySpend(3) && pool.Available == 3, "ポイント: 3使うと残り3");
            Check(pool.EndTurn() == 1, "ポイント: 残り3の半分は切り捨てで1");

            // --- ゲージ: 背水の残り1ストップとフィニッシャー貫通 ---
            var gauge = new FrontlineGauge();
            gauge.Push(12, pushedPlayer: 0, targetHaisuiUnused: true, piercesProtection: false);
            Check(gauge.Position == 12, "ゲージ: 12押せる");
            gauge.Push(8, pushedPlayer: 0, targetHaisuiUnused: true, piercesProtection: false);
            Check(gauge.Position == 14, "ゲージ: 背水未使用なら残り1(14)で止まる");
            Check(gauge.RemainingFor(0) == 1 && gauge.IsInHaisuiZone(0), "ゲージ: 残り1は背水圏内");
            gauge.Push(8, pushedPlayer: 0, targetHaisuiUnused: true, piercesProtection: true);
            Check(gauge.Position == 15 && gauge.IsFinished, "ゲージ: フィニッシャーは貫通して押し切れる");

            // --- 戦闘: 一方だけ倒れる場合 ---
            var attacker = MakeUnit(SampleCards.Swordsman, owner: 0); // 3/3 コスト3
            var defender = MakeUnit(SampleCards.Spearman, owner: 1);  // 2/2 コスト2
            var r1 = CombatResolver.ResolveBattle(attacker, defender);
            Check(r1.DefenderDied && !r1.AttackerDied, "戦闘: 3/3が2/2を倒して生き残る");
            Check(attacker.CurrentHealth == 1, "戦闘: ダメージは残る（3-2=1）");
            Check(r1.GaugeDelta == -1, "戦闘: 撃破ゲージはコスト2の半分=1、守備側(P1)へ");

            // --- 戦闘: 相打ちは差分 ---
            var a2 = MakeUnit(new UnitCardDefinition("t_a", "相打ちA", 4, 2, 2), owner: 0);
            var d2 = MakeUnit(new UnitCardDefinition("t_d", "相打ちD", 6, 3, 2), owner: 1);
            var r2 = CombatResolver.ResolveBattle(a2, d2);
            Check(r2.AttackerDied && r2.DefenderDied, "戦闘: 相打ちで両方倒れる");
            Check(r2.GaugeDelta == -3 + 2, "戦闘: 相打ちは差分（-3+2=-1）");

            // --- 背水の陣: 封じ合いと1回だけ ---
            var haisui = new HaisuiSystem();
            Check(haisui.TryActivate(0, HaisuiAction.PushBack), "背水: 先に押し戻しを選べる");
            Check(!haisui.TryActivate(1, HaisuiAction.PushBack), "背水: 相手は同じ行動を選べない");
            Check(haisui.TryActivate(1, HaisuiAction.DrawCards), "背水: 相手は別の行動なら選べる");
            Check(!haisui.TryActivate(0, HaisuiAction.DrawCards), "背水: 1人1回まで");

            // --- デッキ構築ルール ---
            Check(PlayerState.ValidateDeck(SampleCards.BuildSampleDeck()).Count == 0, "デッキ: サンプル40枚は適法");
            var tooMany = SampleCards.BuildSampleDeck();
            tooMany.Add(SampleCards.Scout);
            Check(PlayerState.ValidateDeck(tooMany).Count > 0, "デッキ: 41枚は違反");
            var fiveCopies = new List<CardDefinition>(SampleCards.BuildSampleDeck());
            fiveCopies[0] = SampleCards.Spearman; // Spearman が5枚になる構成
            fiveCopies[1] = SampleCards.Spearman;
            Check(PlayerState.ValidateDeck(fiveCopies).Count > 0, "デッキ: 同名5枚は違反");

            // --- マリガン: 全交換・1回だけ ---
            var player = new PlayerState(0, SampleCards.BuildSampleDeck(), new Random(7));
            player.DrawCards(5);
            Check(player.Mulligan() && player.Hand.Count == 5, "マリガン: 全交換できる");
            Check(!player.Mulligan(), "マリガン: 2回目はできない");

            // --- サンプルユニットの能力値の目安（攻撃+体力 ≦ コスト×2） ---
            var units = new[]
            {
                SampleCards.Scout, SampleCards.Spearman, SampleCards.Shieldman, SampleCards.Swordsman,
                SampleCards.Archer, SampleCards.Knight, SampleCards.HeavyKnight, SampleCards.Golem,
                SampleCards.Dragon, SampleCards.Finisher,
            };
            bool budgetOk = true;
            foreach (var u in units) if (u.Attack + u.Health > u.Cost * 2) budgetOk = false;
            Check(budgetOk, "能力値: サンプルは全てコスト×2以内");

            // --- エンジン: 先攻後攻の開始状態 ---
            var engine = new GameEngine(SampleCards.BuildSampleDeck(), SampleCards.BuildSampleDeck(), seed: 42);
            Check(engine.Players[0].Hand.Count == 5, "開始: 先攻の初期手札5枚");
            Check(engine.Players[1].Hand.Count == 6, "開始: 後攻は追加ドローで6枚");
            engine.StartTurn();
            Check(engine.Players[0].Points.Available == 2, "開始: 先攻の初ターンは2ポイント");
            Check(engine.EndTurn(), "開始: 先攻のターン終了");
            engine.StartTurn();
            Check(engine.Players[1].Points.Available == 3, "開始: 後攻の初ターンは補正込み3ポイント");

            // --- 押し戻し効果で決着したときの勝者 ---
            // ゲージ -14（プレイヤー1の残り1）から、プレイヤー0が3押し戻すと -15 に届く。
            // -15 はプレイヤー1の負けなので、勝者は押し戻したプレイヤー0。
            var engine2 = new GameEngine(SampleCards.BuildSampleDeck(), SampleCards.BuildSampleDeck(), seed: 7);
            engine2.Gauge.Push(14, pushedPlayer: 1, targetHaisuiUnused: false, piercesProtection: false);
            Check(engine2.Gauge.Position == -14, "押し戻し決着: 準備でゲージ-14");
            engine2.PushBackGaugeForEffect(0, 3);
            Check(engine2.Gauge.Position == -15 && engine2.WinnerIndex == 0, "押し戻し決着: 押し戻した側が勝つ");

            // --- 妨害効果: 相手の手札が1枚減って墓地に置かれる ---
            var engine3 = new GameEngine(PreconstructedDecks.NiakuroNitoguri(), PreconstructedDecks.MuscatKohaku(), seed: 3);
            int oppHandBefore = engine3.Players[1].Hand.Count;
            int oppGraveBefore = engine3.Players[1].Graveyard.Count;
            engine3.DiscardRandomForEffect(0, 1);
            Check(engine3.Players[1].Hand.Count == oppHandBefore - 1
               && engine3.Players[1].Graveyard.Count == oppGraveBefore + 1,
                "妨害効果: 相手の手札をランダムに1枚捨てさせる");

            // --- 構築済みデッキ: 全て40枚・同名4枚までを満たす ---
            var precons = new[]
            {
                PreconstructedDecks.NiakuroNitoguri(),
                PreconstructedDecks.MuscatKohaku(),
                PreconstructedDecks.CoconutYaya(),
            };
            bool preconOk = true;
            foreach (var d in precons)
            {
                if (d.Count != 40) preconOk = false;
                if (PlayerState.ValidateDeck(d).Count != 0) preconOk = false;
            }
            Check(preconOk, "構築済み: 3デッキ全て適法（40枚・同名3枚まで）");
            UnitCardDefinition[][] allowedUnits =
            {
                new[] { CharacterCards.Niakuro, CharacterCards.NiakuroSmall, CharacterCards.NiakuroGamer,
                    CharacterCards.NiakuroSuper, CharacterCards.NiakuroCat, CharacterCards.Nitoguri,
                    CharacterCards.NitoguriMini, CharacterCards.NitoguriGamer, CharacterCards.NitoguriOrderless,
                    CharacterCards.NitoguriBakusoku },
                new[] { CharacterCards.Muscat, CharacterCards.MuscatYoung, CharacterCards.MuscatGamer,
                    CharacterCards.MuscatRipe, CharacterCards.MuscatBig, CharacterCards.Kohaku,
                    CharacterCards.KohakuSmall, CharacterCards.KohakuCat, CharacterCards.KohakuShine,
                    CharacterCards.KohakuBig },
                new[] { CharacterCards.Coconut, CharacterCards.CoconutGamer, CharacterCards.CoconutSmall,
                    CharacterCards.CoconutSolo, CharacterCards.CoconutMarksman, CharacterCards.Yaya,
                    CharacterCards.YayaSmall, CharacterCards.YayaGamer, CharacterCards.YayaRich,
                    CharacterCards.YayaMaster },
            };
            bool familiesOk = true;
            for (int i = 0; i < precons.Length; i++)
            {
                foreach (var card in precons[i])
                {
                    if (!(card is UnitCardDefinition unit)) continue;
                    bool allowed = false;
                    foreach (var familyUnit in allowedUnits[i])
                        if (ReferenceEquals(unit, familyUnit)) allowed = true;
                    if (!allowed) familiesOk = false;
                }
            }
            Check(familiesOk, "構築済み: 各デッキのユニットは指定された2家系のみ");

            // --- キャラカードの能力値の目安（攻撃+体力 ≦ コスト×2） ---
            var charUnits = new[]
            {
                CharacterCards.Niakuro, CharacterCards.NiakuroSmall, CharacterCards.NiakuroGamer, CharacterCards.NiakuroSuper,
                CharacterCards.NiakuroCat, CharacterCards.Nitoguri, CharacterCards.NitoguriMini, CharacterCards.NitoguriGamer,
                CharacterCards.NitoguriOrderless, CharacterCards.NitoguriBakusoku, CharacterCards.Muscat, CharacterCards.MuscatYoung,
                CharacterCards.MuscatGamer, CharacterCards.MuscatRipe, CharacterCards.MuscatBig, CharacterCards.Coconut,
                CharacterCards.CoconutGamer, CharacterCards.CoconutSmall, CharacterCards.CoconutSolo, CharacterCards.CoconutMarksman,
                CharacterCards.Yaya, CharacterCards.YayaSmall, CharacterCards.YayaGamer, CharacterCards.YayaRich,
                CharacterCards.YayaMaster, CharacterCards.Kohaku, CharacterCards.KohakuSmall, CharacterCards.KohakuCat,
                CharacterCards.KohakuShine, CharacterCards.KohakuBig,
            };
            var charBudgetViolations = new List<string>();
            // ミニトグリは低コスト除去、ミニマスカットは壁型の例外として扱う。
            foreach (var u in charUnits)
            {
                int guardAllowance = u.Guard ? 1 : 0;
                if (ReferenceEquals(u, CharacterCards.MuscatYoung)) guardAllowance = 2;
                if (ReferenceEquals(u, CharacterCards.NitoguriMini)) guardAllowance = 1;
                if (u.Attack + u.Health > u.Cost * 2 + guardAllowance) charBudgetViolations.Add(u.Name);
            }
            Check(charBudgetViolations.Count == 0,
                "キャラカード: コスト×2の目安内（効果・守護・壁型の例外込み）"
                + (charBudgetViolations.Count == 0 ? "" : " / " + string.Join(", ", charBudgetViolations)));

            // --- 特徴タグ: 全員が古参、にあくろ/ニトグリ/ココナッツは悪友も持つ ---
            bool traitCheckOk = true;
            foreach (var u in charUnits) if (!u.HasTrait(CardTraits.Kosan)) traitCheckOk = false;
            var akuyuuUnits = new[]
            {
                CharacterCards.Niakuro, CharacterCards.NiakuroSmall, CharacterCards.NiakuroGamer, CharacterCards.NiakuroSuper,
                CharacterCards.Nitoguri, CharacterCards.NitoguriMini, CharacterCards.NitoguriGamer, CharacterCards.NitoguriOrderless,
                CharacterCards.NitoguriBakusoku, CharacterCards.Coconut, CharacterCards.CoconutGamer, CharacterCards.CoconutSmall,
                CharacterCards.CoconutSolo, CharacterCards.CoconutMarksman,
            };
            foreach (var u in akuyuuUnits) if (!u.HasTrait(CardTraits.Akuyuu)) traitCheckOk = false;
            if (CharacterCards.Muscat.HasTrait(CardTraits.Akuyuu)) traitCheckOk = false;
            if (CharacterCards.Yaya.HasTrait(CardTraits.Akuyuu)) traitCheckOk = false;
            if (CharacterCards.Kohaku.HasTrait(CardTraits.Akuyuu)) traitCheckOk = false;
            var zennyuuUnits = new[]
            {
                CharacterCards.NiakuroCat, CharacterCards.Muscat, CharacterCards.MuscatYoung, CharacterCards.MuscatGamer,
                CharacterCards.MuscatRipe, CharacterCards.MuscatBig, CharacterCards.Yaya, CharacterCards.YayaSmall,
                CharacterCards.YayaGamer, CharacterCards.YayaRich, CharacterCards.YayaMaster, CharacterCards.Kohaku,
                CharacterCards.KohakuSmall, CharacterCards.KohakuCat, CharacterCards.KohakuShine, CharacterCards.KohakuBig,
            };
            foreach (var u in zennyuuUnits) if (!u.HasTrait(CardTraits.Zennyuu)) traitCheckOk = false;
            foreach (var u in akuyuuUnits)
                if (!u.AllTraits && u.HasTrait(CardTraits.Zennyuu)) traitCheckOk = false;
            Check(traitCheckOk, "特徴: 全員が古参、悪友と善友はそれぞれ3人だけ");

            // --- 表向きと「裏向きを選んで捨てる」の流れ ---
            var deckAllNiakuro = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckAllNiakuro.Add(CharacterCards.Niakuro);
            var engine4 = new GameEngine(deckAllNiakuro, SampleCards.BuildSampleDeck(), seed: 11);
            engine4.StartTurn(); // P0 ターン1（2ポイント）
            engine4.EndTurn();
            engine4.StartTurn(); // P1 ターン1
            engine4.EndTurn();
            engine4.StartTurn(); // P0 ターン2（基本3+繰り越し1=4ポイント）
            CardInstance niakuroCard = null;
            foreach (var c in engine4.Players[0].Hand) { niakuroCard = c; break; }
            Check(engine4.PlayUnit(niakuroCard.InstanceId), "にあくろ: コスト2で出せる");
            Check(engine4.PendingDiscardByPlayer == 0 && engine4.PendingDiscardTargetPlayer == 1,
                "にあくろ: 登場時に「選んで捨てる」待ちになる");
            engine4.RevealRandomForEffect(0, 1);
            Check(engine4.Players[1].RevealedCount == 1, "表向き: 相手の手札が1枚表向きになる");
            CardInstance revealedCard = null, faceDownCard = null;
            foreach (var c in engine4.Players[1].Hand)
            {
                if (engine4.Players[1].IsRevealed(c)) revealedCard = c; else faceDownCard = c;
            }
            int graveBefore = engine4.Players[1].Graveyard.Count;
            Check(engine4.ChooseCardToDiscard(revealedCard.InstanceId)
               && engine4.Players[1].Graveyard.Count == graveBefore + 1
               && engine4.PendingDiscardByPlayer == null,
                "選んで捨てる: 表向きのカードは中身が分かった上で狙って捨てられる");

            // --- みにあくろ: 登場時に相手の手札が1枚表向きになる ---
            var deckAllMini = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckAllMini.Add(CharacterCards.NiakuroSmall);
            var engine5 = new GameEngine(deckAllMini, SampleCards.BuildSampleDeck(), seed: 5);
            engine5.StartTurn();
            CardInstance miniCard = null;
            foreach (var c in engine5.Players[0].Hand) { miniCard = c; break; }
            Check(engine5.PlayUnit(miniCard.InstanceId) && engine5.Players[1].RevealedCount == 1,
                "みにあくろ: 登場時に相手の手札を1枚表向きにする");

            // --- ゲーマーにあくろ: 相手の攻撃時の攻撃力が1下がる（顔攻撃で確認） ---
            var deckAllMuscat = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckAllMuscat.Add(CharacterCards.Muscat);
            var deckAllGamer = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckAllGamer.Add(CharacterCards.NiakuroGamer);
            var engine6 = new GameEngine(deckAllMuscat, deckAllGamer, seed: 9);
            engine6.Players[1].Field.Add(MakeUnit(CharacterCards.Niakuro, owner: 1));
            // P0: ターン1・2は温存、ターン3（GT5）にマスカットを出す
            // P1: ターン3（GT6）にゲーマーにあくろを出す
            for (int round = 0; round < 2; round++)
            {
                engine6.StartTurn(); engine6.EndTurn();
                engine6.StartTurn(); engine6.EndTurn();
            }
            engine6.StartTurn(); // P0 ターン3
            CardInstance muscatCard = null;
            foreach (var c in engine6.Players[0].Hand) { muscatCard = c; break; }
            Check(engine6.PlayUnit(muscatCard.InstanceId), "ゲーマー検証: マスカットを出せる");
            engine6.EndTurn();
            engine6.StartTurn(); // P1 ターン3
            CardInstance gamerCard = null;
            foreach (var c in engine6.Players[1].Hand) { gamerCard = c; break; }
            Check(engine6.PlayUnit(gamerCard.InstanceId), "ゲーマー検証: ゲーマーにあくろを出せる");
            Check(engine6.PendingDiscardByPlayer == 1 && engine6.PendingDiscardSeesHand
               && engine6.Players[0].RevealedCount == 0,
                "ゲーマー: 他の悪友がいれば相手の手札を公開せずに覗き見選択待ち");
            CardInstance peekedCard = null;
            foreach (var c in engine6.Players[0].Hand) { peekedCard = c; break; }
            Check(peekedCard != null && engine6.ChooseCardToDiscard(peekedCard.InstanceId)
               && engine6.Players[0].RevealedCount == 0,
                "ゲーマー: 覗いたカードを選んで捨てても公開状態にならない");
            engine6.EndTurn();
            engine6.StartTurn(); // P0 ターン4: マスカットで顔攻撃
            var muscatUnit = engine6.Players[0].Field[0];
            Check(engine6.AttackFace(muscatUnit) && engine6.Gauge.Position == -1,
                "ゲーマー: 顔攻撃の攻撃力が2→1に下がる（攻撃時のみ弱体）");
            Check(muscatUnit.Attack == 2, "ゲーマー: 攻撃が終われば攻撃力は元の2に戻る");

            // --- 超絶自己中にあくろ: 全表向き・登場時の自分1枚+相手1枚・起動型能力 ---
            var deckAllSuper = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckAllSuper.Add(CharacterCards.NiakuroSuper);
            var engine7 = new GameEngine(deckAllSuper, deckAllMuscat, seed: 13);
            engine7.StartTurn(); engine7.EndTurn(); // P0 T1
            engine7.StartTurn(); engine7.EndTurn(); // P1 T1
            engine7.StartTurn(); engine7.EndTurn(); // P0 T2
            engine7.StartTurn();                     // P1 T2（4ポイント）
            CardInstance muscatCard2 = null;
            foreach (var c in engine7.Players[1].Hand) { muscatCard2 = c; break; }
            Check(engine7.PlayUnit(muscatCard2.InstanceId), "超絶自己中検証: 先にマスカットを出せる");
            engine7.EndTurn();
            engine7.StartTurn(); engine7.EndTurn(); // P0 T3（温存）
            engine7.StartTurn(); engine7.EndTurn(); // P1 T3
            engine7.StartTurn(); engine7.EndTurn(); // P0 T4（8ポイントでも温存）
            engine7.StartTurn(); engine7.EndTurn(); // P1 T4
            engine7.StartTurn();                     // P0 T5（10ポイント）
            CardInstance superCard = null;
            foreach (var c in engine7.Players[0].Hand) { superCard = c; break; }
            Check(engine7.PlayUnit(superCard.InstanceId), "超絶自己中検証: コスト8で出せる");
            var superUnit = engine7.Players[0].Field[0];
            var muscatUnit2 = engine7.Players[1].Field[0];
            Check(engine7.PendingSelfishByPlayer == 0
               && engine7.PendingSelfishOpponentCardsRemaining == 2,
                "超絶自己中: 登場時に自分1枚+相手2枚の選択待ちになる");
            CardInstance anyP1Card = null;
            foreach (var c in engine7.Players[1].Hand) { anyP1Card = c; break; }
            Check(engine7.IsHandCardRevealed(1, anyP1Card), "超絶自己中: 場にいる限り相手の手札は全て表向き");
            CardInstance ownPick = null;
            foreach (var c in engine7.Players[0].Hand) { ownPick = c; break; }
            Check(engine7.ChooseSelfishDiscard(ownPick.InstanceId), "超絶自己中: 自分の手札を1枚捨てる");
            Check(engine7.ChooseSelfishDiscard(anyP1Card.InstanceId)
               && engine7.PendingSelfishOpponentCardsRemaining == 1,
                "超絶自己中: 相手の手札の1枚目を捨てる");
            CardInstance anyP1Card2 = null;
            foreach (var c in engine7.Players[1].Hand) { anyP1Card2 = c; break; }
            Check(engine7.ChooseSelfishDiscard(anyP1Card2.InstanceId)
               && engine7.PendingSelfishByPlayer == null,
                "超絶自己中: 相手の手札を2枚捨てて選択待ちが終わる");
            Check(engine7.UseSelfishAbility(superUnit, 3) && engine7.Gauge.Position == -1,
                "超絶自己中能力3: ゲージを相手側に1進める");
            Check(!engine7.UseSelfishAbility(superUnit, 3), "超絶自己中: 能力は1ターンに1度だけ");
            Check(superUnit.HasTrait(CardTraits.Zennyuu) && superUnit.HasTrait(CardTraits.Akuyuu),
                "超絶自己中: 全ての特徴を持つ");
            engine7.EndTurn();
            engine7.StartTurn(); engine7.EndTurn(); // P1 T5
            engine7.StartTurn();                     // P0 T6（能力リセット）
            Check(engine7.UseSelfishAbility(superUnit, 2, muscatUnit2) && muscatUnit2.MaxHealth == 3,
                "超絶自己中能力2: 相手ユニットの体力の最大値を1下げる");

            // --- 猫被りにあくろ: 相互ミルの後、コスト3以下のユニットを墓地から蘇生 ---
            var deckAllCat = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckAllCat.Add(CharacterCards.NiakuroCat);
            var deckAllSpear = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckAllSpear.Add(SampleCards.Spearman);
            var engine8 = new GameEngine(deckAllCat, deckAllSpear, seed: 17);
            engine8.StartTurn(); engine8.EndTurn();
            engine8.StartTurn(); engine8.EndTurn();
            engine8.StartTurn();
            CardInstance catCard = null;
            foreach (var c in engine8.Players[0].Hand) { catCard = c; break; }
            int p0DeckBeforeCat = engine8.Players[0].Deck.Count;
            int p1DeckBeforeCat = engine8.Players[1].Deck.Count;
            engine8.Players[0].SendToGraveyard(new CardInstance(CharacterCards.Niakuro));
            Check(engine8.PlayUnit(catCard.InstanceId), "猫被り: 登場できる");
            bool revivedNiakuro = false;
            foreach (var u in engine8.Players[0].Field)
                if (ReferenceEquals(u.Definition, CharacterCards.Niakuro)) revivedNiakuro = true;
            Check(revivedNiakuro && engine8.PendingDiscardByPlayer == 0,
                "猫被り: 墓地から復活したユニットの登場時効果も解決する");
            Check(engine8.Players[0].Deck.Count == p0DeckBeforeCat - 1
               && engine8.Players[1].Deck.Count == p1DeckBeforeCat - 1
               && engine8.Players[0].Graveyard.Count == 1,
                "猫被り: お互いのデッキ上を1枚墓地に置いてから復活する");

            // --- マスカット: 破壊時にミニマスカットを場に出す ---
            var engineMuscatDeath = new GameEngine(deckAllMuscat, deckAllSpear, seed: 18);
            engineMuscatDeath.StartTurn(); engineMuscatDeath.EndTurn();
            engineMuscatDeath.StartTurn(); engineMuscatDeath.EndTurn();
            engineMuscatDeath.StartTurn();
            CardInstance muscatForDeath = null;
            foreach (var c in engineMuscatDeath.Players[0].Hand) { muscatForDeath = c; break; }
            engineMuscatDeath.PlayUnit(muscatForDeath.InstanceId);
            var muscatToDestroy = engineMuscatDeath.Players[0].Field[0];
            engineMuscatDeath.RemoveUnitByEffect(muscatToDestroy);
            Check(engineMuscatDeath.Players[0].Field.Count == 1
               && engineMuscatDeath.Players[0].Field[0].Definition == CharacterCards.MuscatYoung,
                "マスカット: 破壊されるとゲーム外からミニマスカットを召喚");

            // --- 連携：古参（ニトグリ）: 自分以外の古参がいる間だけ攻撃力+2 ---
            var deckAllNito = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckAllNito.Add(CharacterCards.Nitoguri);
            var engine9 = new GameEngine(deckAllNito, SampleCards.BuildSampleDeck(), seed: 19);
            engine9.StartTurn(); engine9.EndTurn(); // P0 T1
            engine9.StartTurn(); engine9.EndTurn(); // P1 T1
            engine9.StartTurn();                     // P0 T2: ニトグリ1体目
            CardInstance nitoCard1 = null;
            foreach (var c in engine9.Players[0].Hand) { nitoCard1 = c; break; }
            engine9.PlayUnit(nitoCard1.InstanceId);
            var nito1 = engine9.Players[0].Field[0];
            engine9.EndTurn();
            engine9.StartTurn(); engine9.EndTurn(); // P1 T2
            engine9.StartTurn();                     // P0 T3: 1人だけの間は連携なし（顔攻撃2）
            Check(engine9.LinkageBonus(nito1) == 0, "連携: 1人だけではボーナスなし");
            engine9.AttackFace(nito1);
            Check(engine9.Gauge.Position == -2, "連携: 1人だけの顔攻撃は攻撃力2のまま");
            CardInstance nitoCard2 = null;
            foreach (var c in engine9.Players[0].Hand) { nitoCard2 = c; break; }
            engine9.PlayUnit(nitoCard2.InstanceId);  // ニトグリ2体目（お互い古参）
            Check(engine9.LinkageBonus(nito1) == 2, "連携: 他の古参がいれば+2");
            engine9.EndTurn();
            engine9.StartTurn(); engine9.EndTurn(); // P1 T3
            engine9.StartTurn();                     // P0 T4: 連携ありの顔攻撃は4
            engine9.AttackFace(nito1);
            Check(engine9.Gauge.Position == -6, "連携: 2人いれば顔攻撃は攻撃力4");

            // --- ミニトグリ: 登場時に相手ユニットへ2ダメージ（効果で倒してもゲージは動かない） ---
            var deckAllMini2 = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckAllMini2.Add(CharacterCards.NitoguriMini);
            var engine10 = new GameEngine(deckAllMini2, deckAllSpear, seed: 23);
            engine10.StartTurn(); engine10.EndTurn(); // P0 T1
            engine10.StartTurn();                     // P1 T1: 槍兵を出す
            CardInstance spearCard5 = null;
            foreach (var c in engine10.Players[1].Hand) { spearCard5 = c; break; }
            engine10.PlayUnit(spearCard5.InstanceId);
            var spear5 = engine10.Players[1].Field[0];
            engine10.EndTurn();
            engine10.StartTurn();                     // P0 T2: ミニトグリを出す
            CardInstance miniToguriCard = null;
            foreach (var c in engine10.Players[0].Hand) { miniToguriCard = c; break; }
            engine10.PlayUnit(miniToguriCard.InstanceId);
            Check(engine10.PendingDamageByPlayer == 0, "ミニトグリ: 登場時にダメージの対象選択待ちになる");
            Check(engine10.ChooseDamageTarget(spear5.Instance.InstanceId)
               && engine10.Players[1].Field.Count == 0, "ミニトグリ: 2ダメージで槍兵を倒せる");
            Check(engine10.Gauge.Position == 0, "ミニトグリ: 効果で倒してもゲージは動かない");

            // --- ゲーマーニトグリ: 登場時に自分のユニット2体まで攻撃力+1 ---
            var deckAllNitoGamer = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckAllNitoGamer.Add(CharacterCards.NitoguriGamer);
            var engine11 = new GameEngine(deckAllNitoGamer, SampleCards.BuildSampleDeck(), seed: 29);
            engine11.StartTurn(); engine11.EndTurn(); // P0 T1
            engine11.StartTurn(); engine11.EndTurn(); // P1 T1
            engine11.StartTurn(); engine11.EndTurn(); // P0 T2
            engine11.StartTurn(); engine11.EndTurn(); // P1 T2
            engine11.StartTurn();                     // P0 T3（6ポイント）: 1体目を出す
            CardInstance ngCard1 = null;
            foreach (var c in engine11.Players[0].Hand) { ngCard1 = c; break; }
            engine11.PlayUnit(ngCard1.InstanceId);
            var ng1 = engine11.Players[0].Field[0];
            Check(engine11.PendingBuffByPlayer == 0, "ゲーマーニトグリ: 登場時に強化の選択待ちになる");
            Check(engine11.ChooseBuffTarget(ng1.Instance.InstanceId) && ng1.Attack == 6
               && engine11.PendingBuffByPlayer == null,
                "ゲーマーニトグリ: 場に1体だけなら、その1体だけ+1して終わる");
            engine11.EndTurn();
            engine11.StartTurn(); engine11.EndTurn(); // P1 T3
            engine11.StartTurn(); engine11.EndTurn(); // P0 T4
            engine11.StartTurn(); engine11.EndTurn(); // P1 T4
            engine11.StartTurn();                     // P0 T5: 2体目を出す
            CardInstance ngCard2 = null;
            foreach (var c in engine11.Players[0].Hand) { ngCard2 = c; break; }
            engine11.PlayUnit(ngCard2.InstanceId);
            var ng2 = engine11.Players[0].Field[1];
            Check(engine11.ChooseBuffTarget(ng1.Instance.InstanceId) && engine11.PendingBuffByPlayer == 0,
                "ゲーマーニトグリ: 2体選ぶ時は1体目では終わらない");
            Check(!engine11.ChooseBuffTarget(ng1.Instance.InstanceId),
                "ゲーマーニトグリ: 同じユニットは2回選べない");
            Check(engine11.ChooseBuffTarget(ng2.Instance.InstanceId)
               && ng1.Attack == 7 && ng2.Attack == 6 && engine11.PendingBuffByPlayer == null,
                "ゲーマーニトグリ: 2体に+1ずつ入る");

            // --- 秩序なき連携ニトグリ: 連携付与（重なる）＋悪友/善友オーラ ---
            var deckOrder = new List<CardDefinition>();
            for (int i = 0; i < 14; i++) deckOrder.Add(CharacterCards.NitoguriOrderless);
            for (int i = 0; i < 13; i++) deckOrder.Add(CharacterCards.Nitoguri);
            for (int i = 0; i < 13; i++) deckOrder.Add(CharacterCards.Muscat);
            var engine12 = new GameEngine(deckOrder, SampleCards.BuildSampleDeck(), seed: 31);
            UnitState nito12 = null, cat12 = null, order12 = null;
            for (int turn = 0; turn < 8 && (nito12 == null || cat12 == null || order12 == null); turn++)
            {
                engine12.StartTurn(); // P0 のターン
                var wanted = new[] { CharacterCards.Nitoguri, CharacterCards.Muscat, CharacterCards.NitoguriOrderless };
                var have = new[] { nito12 != null, cat12 != null, order12 != null };
                for (int w = 0; w < wanted.Length; w++)
                {
                    if (have[w]) continue;
                    CardInstance found = null;
                    foreach (var c in engine12.Players[0].Hand)
                    {
                        if (ReferenceEquals(c.Definition, wanted[w])) { found = c; break; }
                    }
                    if (found != null && engine12.Players[0].Points.Available >= wanted[w].Cost)
                    {
                        engine12.PlayUnit(found.InstanceId);
                    }
                }
                foreach (var u in engine12.Players[0].Field)
                {
                    if (ReferenceEquals(u.Definition, CharacterCards.Nitoguri)) nito12 = u;
                    if (ReferenceEquals(u.Definition, CharacterCards.Muscat)) cat12 = u;
                    if (ReferenceEquals(u.Definition, CharacterCards.NitoguriOrderless)) order12 = u;
                }
                engine12.EndTurn();
                engine12.StartTurn(); engine12.EndTurn(); // P1 は何もしない
            }
            Check(nito12 != null && cat12 != null && order12 != null, "秩序検証: 3体とも場に出せる");
            if (nito12 != null && cat12 != null && order12 != null)
            {
                Check(nito12.Attack == 3 && nito12.MaxHealth == 1,
                    "秩序: 悪友は攻撃力+1・体力の最大値-1");
                Check(cat12.MaxHealth == 6 && cat12.Attack == 2,
                    "秩序: 善友は体力の最大値+2（攻撃力はそのまま）");
                Check(order12.Attack == 7 && order12.MaxHealth == 7,
                    "秩序: 本人は悪友分と善友分が両方かかる（攻+1、最大体力-1+2）");
                Check(engine12.LinkageBonus(nito12) == 3,
                    "秩序: 付与された連携1は、もとの連携2と重なって連携3になる");
                Check(engine12.LinkageBonus(cat12) == 1,
                    "秩序: 連携を持たないユニットにも連携1が付く");
                Check(engine12.EffectiveAttackWhenAttacking(nito12) == 6,
                    "秩序: ニトグリの実効攻撃力は 3（オーラ込み）+ 連携3 = 6");
                Check(nito12.AuraHaste && cat12.AuraHaste && order12.AuraHaste,
                    "秩序: 悪友と善友がそろうと自軍全員が速攻を得る");
            }

            // --- 爆速退社ニトグリ: 条件付き速攻 ---
            var deckBaku = new List<CardDefinition>();
            for (int i = 0; i < 20; i++) deckBaku.Add(CharacterCards.NitoguriBakusoku);
            for (int i = 0; i < 20; i++) deckBaku.Add(CharacterCards.Nitoguri);
            var engine13 = new GameEngine(deckBaku, SampleCards.BuildSampleDeck(), seed: 41);
            CardInstance Find13(UnitCardDefinition def)
            {
                foreach (var c in engine13.Players[0].Hand)
                {
                    if (ReferenceEquals(c.Definition, def)) return c;
                }
                return null;
            }
            engine13.StartTurn(); engine13.EndTurn(); // GT1: ポイント不足
            engine13.StartTurn(); engine13.EndTurn(); // P1
            engine13.StartTurn(); // GT3 (P0): 使えるポイント4
            var bakuFirst = Find13(CharacterCards.NitoguriBakusoku);
            Check(bakuFirst != null && engine13.PlayUnit(bakuFirst.InstanceId),
                "爆速: 1体目を出せる");
            var bakuUnit1 = engine13.Players[0].Field[0];
            Check(!bakuUnit1.HasHaste, "爆速: 自分の場に他のユニットがいなければ速攻は付かない");
            Check(!engine13.AttackFace(bakuUnit1), "爆速: 速攻なしでは出たターンに攻撃できない");
            engine13.EndTurn();
            engine13.StartTurn(); engine13.EndTurn(); // P1
            engine13.StartTurn(); // GT5 (P0)
            var nito13 = Find13(CharacterCards.Nitoguri);
            Check(nito13 != null && engine13.PlayUnit(nito13.InstanceId), "爆速検証: ニトグリを出せる");
            engine13.EndTurn();
            engine13.StartTurn(); engine13.EndTurn(); // P1
            engine13.StartTurn(); // GT7 (P0)
            var bakuSecond = Find13(CharacterCards.NitoguriBakusoku);
            Check(bakuSecond != null && engine13.PlayUnit(bakuSecond.InstanceId),
                "爆速: 他のユニットがいる状態で2体目を出せる");
            UnitState bakuUnit2 = null;
            foreach (var u in engine13.Players[0].Field)
            {
                if (ReferenceEquals(u.Definition, CharacterCards.NitoguriBakusoku) && u.HasHaste) bakuUnit2 = u;
            }
            Check(bakuUnit2 != null, "爆速: 自分の場に他のユニットがいれば速攻を得る");
            Check(bakuUnit2 != null && engine13.AttackFace(bakuUnit2) && engine13.Gauge.Position == -2,
                "爆速: 速攻で出たターンに攻撃でき、ゲージが2進む");
            Check(bakuUnit2 != null && !engine13.Players[0].Field.Contains(bakuUnit2)
                && engine13.Players[0].Hand.Contains(bakuUnit2.Instance),
                "爆速: 攻撃した後、手札に戻る");

            // --- もしも～し: デッキからコスト5以上のユニットをランダムに手札へ ---
            var deckMoshimo = new List<CardDefinition>();
            for (int i = 0; i < 10; i++) deckMoshimo.Add(SampleCards.Moshimo);
            for (int i = 0; i < 15; i++) deckMoshimo.Add(CharacterCards.NiakuroGamer); // コスト5
            for (int i = 0; i < 15; i++) deckMoshimo.Add(SampleCards.Knight);          // コスト4（対象外）
            var engine14 = new GameEngine(deckMoshimo, SampleCards.BuildSampleDeck(), seed: 51);
            engine14.StartTurn(); // GT1 (P0): ポイント2
            CardInstance moshimoCard = null;
            int bigBefore = 0;
            foreach (var c in engine14.Players[0].Hand)
            {
                if (ReferenceEquals(c.Definition, SampleCards.Moshimo)) moshimoCard = c;
                if (ReferenceEquals(c.Definition, CharacterCards.NiakuroGamer)) bigBefore++;
            }
            int deckBefore14 = engine14.Players[0].Deck.Count;
            Check(moshimoCard != null && engine14.PlayTactic(moshimoCard.InstanceId),
                "もしも～し: 使える");
            int bigAfter = 0;
            foreach (var c in engine14.Players[0].Hand)
            {
                if (ReferenceEquals(c.Definition, CharacterCards.NiakuroGamer)) bigAfter++;
            }
            Check(bigAfter == bigBefore + 1 && engine14.Players[0].Deck.Count == deckBefore14 - 1,
                "もしも～し: デッキからコスト5以上のユニットが1枚手札に加わる");

            // --- 不可視の爆撃: 相手ユニット1体に3ダメージ（対象選択） ---
            var deckBomb = new List<CardDefinition>();
            for (int i = 0; i < 10; i++) deckBomb.Add(SampleCards.Bombardment);
            for (int i = 0; i < 30; i++) deckBomb.Add(SampleCards.Knight);
            var deckKnights = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckKnights.Add(SampleCards.Knight);
            var engine15 = new GameEngine(deckBomb, deckKnights, seed: 61);
            bool bombResolved = false;
            for (int round = 0; round < 10 && !bombResolved; round++)
            {
                engine15.StartTurn(); // P0
                if (engine15.Players[1].Field.Count > 0)
                {
                    CardInstance bomb = null;
                    foreach (var c in engine15.Players[0].Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Bombardment)) { bomb = c; break; }
                    }
                    if (bomb != null && engine15.PlayTactic(bomb.InstanceId))
                    {
                        Check(engine15.PendingDamageByPlayer == 0, "爆撃: 対象選択待ちになる");
                        var targetKnight = engine15.Players[1].Field[0];
                        Check(engine15.ChooseDamageTarget(targetKnight.Instance.InstanceId),
                            "爆撃: 対象を選べる");
                        Check(targetKnight.DamageTaken == 3 && engine15.Players[1].Field.Count == 1,
                            "爆撃: 相手ユニットに3ダメージ（倒れはしない）");
                        Check(engine15.Gauge.Position == 0, "爆撃: 効果ダメージではゲージは動かない");
                        bombResolved = true;
                    }
                }
                engine15.EndTurn();
                engine15.StartTurn(); // P1: 騎士を出す
                if (engine15.Players[1].Field.Count == 0)
                {
                    foreach (var c in engine15.Players[1].Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Knight)
                            && engine15.Players[1].Points.Available >= SampleCards.Knight.Cost)
                        {
                            engine15.PlayUnit(c.InstanceId);
                            break;
                        }
                    }
                }
                engine15.EndTurn();
            }
            Check(bombResolved, "爆撃: 一連の流れが完了する");

            // --- 一斉射撃: 相手のユニット全体に1ダメージ ---
            var deckVolley = new List<CardDefinition>();
            for (int i = 0; i < 10; i++) deckVolley.Add(SampleCards.Volley);
            for (int i = 0; i < 30; i++) deckVolley.Add(SampleCards.Knight);
            var engine16 = new GameEngine(deckVolley, deckKnights, seed: 71);
            bool volleyResolved = false;
            for (int round = 0; round < 12 && !volleyResolved; round++)
            {
                engine16.StartTurn(); // P0
                if (engine16.Players[1].Field.Count >= 2)
                {
                    CardInstance volley = null;
                    foreach (var c in engine16.Players[0].Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Volley)) { volley = c; break; }
                    }
                    if (volley != null && engine16.PlayTactic(volley.InstanceId))
                    {
                        Check(engine16.Players[1].Field.Count == 2
                            && engine16.Players[1].Field[0].DamageTaken == 1
                            && engine16.Players[1].Field[1].DamageTaken == 1,
                            "一斉射撃: 相手のユニット全体に1ダメージ");
                        Check(engine16.Gauge.Position == 0, "一斉射撃: 効果ダメージではゲージは動かない");
                        volleyResolved = true;
                    }
                }
                engine16.EndTurn();
                engine16.StartTurn(); // P1: 騎士を2体まで出す
                while (engine16.Players[1].Field.Count < 2)
                {
                    CardInstance knightCard = null;
                    foreach (var c in engine16.Players[1].Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Knight)) { knightCard = c; break; }
                    }
                    if (knightCard == null || !engine16.PlayUnit(knightCard.InstanceId)) break;
                }
                engine16.EndTurn();
            }
            Check(volleyResolved, "一斉射撃: 一連の流れが完了する");

            // --- 三兎追えば二兎得る: 3枚引いて、2枚を手札に、残りを墓地に ---
            var deckUsagi = new List<CardDefinition>();
            for (int i = 0; i < 10; i++) deckUsagi.Add(SampleCards.ThreeRabbits);
            for (int i = 0; i < 30; i++) deckUsagi.Add(SampleCards.Knight);
            var engine17 = new GameEngine(deckUsagi, SampleCards.BuildSampleDeck(), seed: 83);
            engine17.StartTurn(); engine17.EndTurn(); // GT1 (P0)
            engine17.StartTurn(); engine17.EndTurn(); // P1
            engine17.StartTurn(); // GT3 (P0): 使えるポイント4
            CardInstance usagiCard = null;
            foreach (var c in engine17.Players[0].Hand)
            {
                if (ReferenceEquals(c.Definition, SampleCards.ThreeRabbits)) { usagiCard = c; break; }
            }
            int handBefore17 = engine17.Players[0].Hand.Count;
            int deckBefore17 = engine17.Players[0].Deck.Count;
            Check(usagiCard != null && engine17.PlayTactic(usagiCard.InstanceId),
                "三兎: 使える");
            Check(engine17.PendingDrawPickByPlayer == 0 && engine17.PendingDrawPool.Count == 3,
                "三兎: 引いた3枚からの選択待ちになる");
            Check(engine17.Players[0].Hand.Count == handBefore17 - 1
                && engine17.Players[0].Deck.Count == deckBefore17 - 3,
                "三兎: この時点では手札は増えず、デッキが3枚減る");
            var pick1 = engine17.PendingDrawPool[0];
            var pick2 = engine17.PendingDrawPool[1];
            Check(engine17.ChooseDrawPick(pick1.InstanceId) && engine17.ChooseDrawPick(pick2.InstanceId)
                && engine17.PendingDrawPickByPlayer == null,
                "三兎: 2枚選んで手札に加えられる");
            Check(engine17.Players[0].Hand.Count == handBefore17 + 1
                && engine17.Players[0].Graveyard.Count == 2,
                "三兎: 最終的に手札は+1、墓地には使ったカードと残りの1枚");

            // --- ゲームの準備: デッキから戦術を2枚手札に加える ---
            var deckJunbi = new List<CardDefinition>();
            for (int i = 0; i < 12; i++) deckJunbi.Add(SampleCards.Junbi);
            for (int i = 0; i < 28; i++) deckJunbi.Add(SampleCards.Knight);
            var engine18 = new GameEngine(deckJunbi, SampleCards.BuildSampleDeck(), seed: 97);
            engine18.StartTurn(); engine18.EndTurn(); // GT1 (P0)
            engine18.StartTurn(); engine18.EndTurn(); // P1
            engine18.StartTurn(); engine18.EndTurn(); // GT3 (P0)
            engine18.StartTurn(); engine18.EndTurn(); // P1
            engine18.StartTurn(); // GT5 (P0): ポイントは十分
            CardInstance junbiCard = null;
            int junbiInHandBefore = 0;
            foreach (var c in engine18.Players[0].Hand)
            {
                if (ReferenceEquals(c.Definition, SampleCards.Junbi))
                {
                    junbiInHandBefore++;
                    if (junbiCard == null) junbiCard = c;
                }
            }
            int deckBefore18 = engine18.Players[0].Deck.Count;
            Check(junbiCard != null && engine18.PlayTactic(junbiCard.InstanceId),
                "ゲームの準備: 使える");
            int junbiInHandAfter = 0;
            foreach (var c in engine18.Players[0].Hand)
            {
                if (ReferenceEquals(c.Definition, SampleCards.Junbi)) junbiInHandAfter++;
            }
            Check(junbiInHandAfter == junbiInHandBefore + 1 && engine18.Players[0].Deck.Count == deckBefore18 - 2,
                "ゲームの準備: デッキから戦術が2枚手札に加わる（使った1枚と合わせて戦術は+1）");

            // --- 酒！酒！酒！: 自分のユニット1体が攻撃力+2・体力の最大値-2、攻撃後破壊 ---
            var deckSake = new List<CardDefinition>();
            for (int i = 0; i < 10; i++) deckSake.Add(SampleCards.Sake);
            for (int i = 0; i < 30; i++) deckSake.Add(SampleCards.Knight);
            var engine19 = new GameEngine(deckSake, SampleCards.BuildSampleDeck(), seed: 101);
            engine19.StartTurn(); engine19.EndTurn(); // GT1 (P0)
            engine19.StartTurn(); engine19.EndTurn(); // P1
            engine19.StartTurn(); // GT3 (P0): 騎士を出す
            CardInstance knightCard19 = null, sakeCard19 = null;
            foreach (var c in engine19.Players[0].Hand)
            {
                if (ReferenceEquals(c.Definition, SampleCards.Knight)) knightCard19 = c;
                if (ReferenceEquals(c.Definition, SampleCards.Sake)) sakeCard19 = c;
            }
            Check(knightCard19 != null && engine19.PlayUnit(knightCard19.InstanceId),
                "酒検証: 騎士を出せる");
            engine19.EndTurn();
            engine19.StartTurn(); engine19.EndTurn(); // P1
            engine19.StartTurn(); // GT5 (P0): 酒を使う
            if (sakeCard19 == null)
            {
                foreach (var c in engine19.Players[0].Hand)
                {
                    if (ReferenceEquals(c.Definition, SampleCards.Sake)) { sakeCard19 = c; break; }
                }
            }
            var knight19 = engine19.Players[0].Field[0];
            Check(sakeCard19 != null && engine19.PlayTactic(sakeCard19.InstanceId),
                "酒！酒！酒！: 使える");
            Check(engine19.PendingOwnBuffByPlayer == 0, "酒: 自分のユニットの選択待ちになる");
            Check(engine19.ChooseOwnBuffTarget(knight19.Instance.InstanceId),
                "酒: 対象を選べる");
            Check(knight19.Attack == 6 && knight19.MaxHealth == 2 && knight19.DestroyAfterAttack,
                "酒: 攻撃力+2（4→6）、体力の最大値-2（4→2）、攻撃後破壊が付く");
            Check(engine19.AttackFace(knight19) && engine19.Gauge.Position == -6,
                "酒: 強化された攻撃力6で顔を殴り、ゲージが6進む");
            Check(engine19.Players[0].Field.Count == 0,
                "酒: 攻撃後にそのユニットは破壊される（自分の撃破ではゲージは動かない）");

            // --- ちゃぶ台返し！: お互いのユニット全てに3ダメージ ---
            var deckChabu = new List<CardDefinition>();
            for (int i = 0; i < 10; i++) deckChabu.Add(SampleCards.Chabudai);
            for (int i = 0; i < 15; i++) deckChabu.Add(SampleCards.Knight);
            for (int i = 0; i < 15; i++) deckChabu.Add(SampleCards.Scout);
            var deckFoe = new List<CardDefinition>();
            for (int i = 0; i < 20; i++) deckFoe.Add(SampleCards.Knight);
            for (int i = 0; i < 20; i++) deckFoe.Add(SampleCards.Shieldman);
            var engine20 = new GameEngine(deckChabu, deckFoe, seed: 111);
            bool chabuResolved = false;
            for (int round = 0; round < 14 && !chabuResolved; round++)
            {
                engine20.StartTurn(); // P0: 偵察兵→騎士の順に出しておく
                foreach (var want in new[] { SampleCards.Scout, SampleCards.Knight })
                {
                    bool already = false;
                    foreach (var u in engine20.Players[0].Field) if (ReferenceEquals(u.Definition, want)) already = true;
                    if (already) continue;
                    foreach (var c in engine20.Players[0].Hand)
                    {
                        if (ReferenceEquals(c.Definition, want) && engine20.Players[0].Points.Available >= want.Cost)
                        {
                            engine20.PlayUnit(c.InstanceId);
                            break;
                        }
                    }
                }
                bool p0Ready = engine20.Players[0].Field.Count == 2;
                bool p1Ready = engine20.Players[1].Field.Count == 2;
                foreach (var u in engine20.Players[1].Field)
                {
                    if (!ReferenceEquals(u.Definition, SampleCards.Knight) && !ReferenceEquals(u.Definition, SampleCards.Shieldman)) p1Ready = false;
                }
                if (p0Ready && p1Ready)
                {
                    CardInstance chabu = null;
                    foreach (var c in engine20.Players[0].Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Chabudai)) { chabu = c; break; }
                    }
                    if (chabu != null && engine20.PlayTactic(chabu.InstanceId))
                    {
                        int p0Alive = engine20.Players[0].Field.Count;
                        int p1Alive = engine20.Players[1].Field.Count;
                        bool p0KnightHurt = false, p1KnightHurt = false;
                        foreach (var u in engine20.Players[0].Field) if (u.DamageTaken == 3) p0KnightHurt = true;
                        foreach (var u in engine20.Players[1].Field) if (u.DamageTaken == 3) p1KnightHurt = true;
                        Check(p0Alive == 1 && p1Alive == 1 && p0KnightHurt && p1KnightHurt,
                            "ちゃぶ台返し: お互いの場の小さいユニットが倒れ、騎士は3ダメージを受けて残る");
                        Check(engine20.Gauge.Position == 0, "ちゃぶ台返し: 効果ダメージではゲージは動かない");
                        chabuResolved = true;
                    }
                }
                engine20.EndTurn();
                engine20.StartTurn(); // P1: 盾兵→騎士の順に出しておく
                foreach (var want in new[] { SampleCards.Shieldman, SampleCards.Knight })
                {
                    bool already = false;
                    foreach (var u in engine20.Players[1].Field) if (ReferenceEquals(u.Definition, want)) already = true;
                    if (already) continue;
                    foreach (var c in engine20.Players[1].Hand)
                    {
                        if (ReferenceEquals(c.Definition, want) && engine20.Players[1].Points.Available >= want.Cost)
                        {
                            engine20.PlayUnit(c.InstanceId);
                            break;
                        }
                    }
                }
                engine20.EndTurn();
            }
            Check(chabuResolved, "ちゃぶ台返し: 一連の流れが完了する");

            // --- 苦し紛れの一撃: 相手のユニットをランダムに1体破壊 ---
            var deckKurushi = new List<CardDefinition>();
            for (int i = 0; i < 12; i++) deckKurushi.Add(SampleCards.Kurushimagire);
            for (int i = 0; i < 28; i++) deckKurushi.Add(SampleCards.Knight);
            var engine21 = new GameEngine(deckKurushi, deckFoe, seed: 121);
            bool kurushiResolved = false;
            for (int round = 0; round < 12 && !kurushiResolved; round++)
            {
                engine21.StartTurn(); // P0
                if (engine21.Players[1].Field.Count >= 2)
                {
                    CardInstance kurushi = null;
                    foreach (var c in engine21.Players[0].Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Kurushimagire)) { kurushi = c; break; }
                    }
                    if (kurushi != null && engine21.PlayTactic(kurushi.InstanceId))
                    {
                        Check(engine21.Players[1].Field.Count == 1 && engine21.Players[1].Graveyard.Count == 1,
                            "苦し紛れの一撃: 相手のユニットがランダムに1体破壊される");
                        Check(engine21.Gauge.Position == 0, "苦し紛れの一撃: 効果による破壊ではゲージは動かない");
                        kurushiResolved = true;
                    }
                }
                engine21.EndTurn();
                engine21.StartTurn(); // P1: 盾兵→騎士の順に出しておく
                foreach (var want in new[] { SampleCards.Shieldman, SampleCards.Knight })
                {
                    bool already = false;
                    foreach (var u in engine21.Players[1].Field) if (ReferenceEquals(u.Definition, want)) already = true;
                    if (already) continue;
                    foreach (var c in engine21.Players[1].Hand)
                    {
                        if (ReferenceEquals(c.Definition, want) && engine21.Players[1].Points.Available >= want.Cost)
                        {
                            engine21.PlayUnit(c.InstanceId);
                            break;
                        }
                    }
                }
                engine21.EndTurn();
            }
            Check(kurushiResolved, "苦し紛れの一撃: 一連の流れが完了する");

            // --- 回復の選択待ちが対象消滅で残らないこと（ちゃぶ台返し！で猫と対象が一緒に倒れるケース） ---
            var deckP0 = new List<CardDefinition>();
            for (int i = 0; i < 10; i++) deckP0.Add(CharacterCards.NitoguriMini);
            for (int i = 0; i < 10; i++) deckP0.Add(SampleCards.Chabudai);
            for (int i = 0; i < 20; i++) deckP0.Add(SampleCards.Knight);
            var deckP1 = new List<CardDefinition>();
            for (int i = 0; i < 10; i++) deckP1.Add(CharacterCards.NiakuroCat);
            for (int i = 0; i < 30; i++) deckP1.Add(SampleCards.Knight);
            var engine22 = new GameEngine(deckP0, deckP1, seed: 131);
            bool orphanResolved = false;
            for (int round = 0; round < 14 && !orphanResolved; round++)
            {
                engine22.StartTurn(); // P0: ミニトグリで相手の騎士に2ダメージ → 後でちゃぶ台
                UnitState foeKnight = null;
                foreach (var u in engine22.Players[1].Field)
                {
                    if (ReferenceEquals(u.Definition, SampleCards.Knight)) foeKnight = u;
                }
                if (foeKnight != null && foeKnight.DamageTaken == 0)
                {
                    foreach (var c in engine22.Players[0].Hand)
                    {
                        if (ReferenceEquals(c.Definition, CharacterCards.NitoguriMini)
                            && engine22.Players[0].Points.Available >= c.Definition.Cost
                            && engine22.PlayUnit(c.InstanceId))
                        {
                            if (engine22.PendingDamageByPlayer != null)
                                engine22.ChooseDamageTarget(foeKnight.Instance.InstanceId);
                            break;
                        }
                    }
                }
                bool foeReady = engine22.Players[1].Field.Count == 2 && foeKnight != null && foeKnight.DamageTaken == 2;
                if (foeReady)
                {
                    foreach (var c in engine22.Players[0].Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Chabudai) && engine22.PlayTactic(c.InstanceId))
                        {
                            Check(engine22.Players[1].Field.Count == 0, "孤児ペンディング: ちゃぶ台で相手の場が全滅する");
                            Check(engine22.PendingHealByPlayer == null,
                                "孤児ペンディング: 回復対象が消えたら回復の選択待ちは不発で解消される");
                            Check(engine22.EndTurn(), "孤児ペンディング: そのままターン終了できる");
                            orphanResolved = true;
                            break;
                        }
                    }
                }
                if (!orphanResolved) engine22.EndTurn();
                if (orphanResolved) break;
                engine22.StartTurn(); // P1: 猫→騎士の順に出す（猫が先に場にいる状態を作る）
                foreach (var wantDef in new CardDefinition[] { CharacterCards.NiakuroCat, SampleCards.Knight })
                {
                    bool already = false;
                    foreach (var u in engine22.Players[1].Field) if (ReferenceEquals(u.Definition, wantDef)) already = true;
                    if (already) continue;
                    foreach (var c in engine22.Players[1].Hand)
                    {
                        if (ReferenceEquals(c.Definition, wantDef) && engine22.Players[1].Points.Available >= wantDef.Cost)
                        {
                            engine22.PlayUnit(c.InstanceId);
                            break;
                        }
                    }
                }
                engine22.EndTurn();
            }
            Check(orphanResolved, "孤児ペンディング: 一連の流れが完了する");

            // --- ミルキー: 登場時2ドロー＋破壊時2ドロー / ミニミルキー: 登場時1ドロー ---
            var deckMilky = new List<CardDefinition>();
            for (int i = 0; i < 10; i++) deckMilky.Add(CharacterCards.Kohaku);
            for (int i = 0; i < 30; i++) deckMilky.Add(SampleCards.Knight);
            var deckDestroy = new List<CardDefinition>();
            for (int i = 0; i < 10; i++) deckDestroy.Add(SampleCards.Kurushimagire);
            for (int i = 0; i < 30; i++) deckDestroy.Add(SampleCards.Knight);
            var engine23 = new GameEngine(deckMilky, deckDestroy, seed: 141);
            bool milkyPlayed = false, milkyDestroyed = false;
            int deckBeforePlay = -1, deckAfterPlay = -1;
            for (int round = 0; round < 12 && !milkyDestroyed; round++)
            {
                engine23.StartTurn(); // P0
                if (!milkyPlayed)
                {
                    foreach (var c in engine23.Players[0].Hand)
                    {
                        if (ReferenceEquals(c.Definition, CharacterCards.Kohaku)
                            && engine23.Players[0].Points.Available >= c.Definition.Cost)
                        {
                            deckBeforePlay = engine23.Players[0].Deck.Count;
                            if (engine23.PlayUnit(c.InstanceId))
                            {
                                milkyPlayed = true;
                                deckAfterPlay = engine23.Players[0].Deck.Count;
                            }
                            break;
                        }
                    }
                }
                engine23.EndTurn();
                engine23.StartTurn(); // P1: 苦し紛れでミルキーを破壊する
                if (milkyPlayed && !milkyDestroyed)
                {
                    foreach (var c in engine23.Players[1].Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Kurushimagire)
                            && engine23.Players[1].Points.Available >= c.Definition.Cost
                            && engine23.PlayTactic(c.InstanceId))
                        {
                            milkyDestroyed = true;
                            break;
                        }
                    }
                }
                engine23.EndTurn();
            }
            Check(milkyPlayed && deckAfterPlay == deckBeforePlay - 2, "ミルキー: 登場時に2枚引く");
            Check(milkyDestroyed && engine23.Players[0].Deck.Count == deckAfterPlay - 2,
                "ミルキー: 破壊された時に2枚引く");

            var deckMini = new List<CardDefinition>();
            for (int i = 0; i < 10; i++) deckMini.Add(CharacterCards.KohakuSmall);
            for (int i = 0; i < 30; i++) deckMini.Add(SampleCards.Knight);
            var engine24 = new GameEngine(deckMini, SampleCards.BuildSampleDeck(), seed: 142);
            engine24.StartTurn(); // GT1: ポイント2
            int deckBefore24 = engine24.Players[0].Deck.Count;
            bool miniPlayed = false;
            foreach (var c in engine24.Players[0].Hand)
            {
                if (ReferenceEquals(c.Definition, CharacterCards.KohakuSmall) && engine24.PlayUnit(c.InstanceId))
                {
                    miniPlayed = true;
                    break;
                }
            }
            Check(miniPlayed && engine24.Players[0].Deck.Count == deckBefore24 - 1,
                "ミニミルキー: 登場時に1枚引く");

            // --- ゲーマーミルキー: 登場時2ドロー＋自分のターン開始時に1ドロー ---
            var deckGamerMilky = new List<CardDefinition>();
            for (int i = 0; i < 10; i++) deckGamerMilky.Add(CharacterCards.KohakuShine);
            for (int i = 0; i < 30; i++) deckGamerMilky.Add(SampleCards.Knight);
            var engine25 = new GameEngine(deckGamerMilky, SampleCards.BuildSampleDeck(), seed: 151);
            bool gmPlayed = false;
            int deckAfterPlay25 = -1, deckAtP1Start = -1, deckAtP0NextStart = -1;
            for (int round = 0; round < 10 && deckAtP0NextStart < 0; round++)
            {
                engine25.StartTurn(); // P0
                if (gmPlayed && deckAtP0NextStart < 0 && round > 0)
                {
                    deckAtP0NextStart = engine25.Players[0].Deck.Count;
                }
                if (!gmPlayed)
                {
                    int before = engine25.Players[0].Deck.Count;
                    foreach (var c in engine25.Players[0].Hand)
                    {
                        if (ReferenceEquals(c.Definition, CharacterCards.KohakuShine)
                            && engine25.Players[0].Points.Available >= c.Definition.Cost
                            && engine25.PlayUnit(c.InstanceId))
                        {
                            gmPlayed = true;
                            deckAfterPlay25 = engine25.Players[0].Deck.Count;
                            Check(deckAfterPlay25 == before - 2, "ゲーマーミルキー: 登場時に2枚引く");
                            break;
                        }
                    }
                }
                engine25.EndTurn();
                engine25.StartTurn(); // P1: 相手のターン開始時には引かない
                if (gmPlayed && deckAtP1Start < 0) deckAtP1Start = engine25.Players[0].Deck.Count;
                engine25.EndTurn();
            }
            Check(gmPlayed && deckAtP1Start == deckAfterPlay25,
                "ゲーマーミルキー: 相手のターン開始時には引かない");
            Check(deckAtP0NextStart == deckAtP1Start - 2,
                "ゲーマーミルキー: 自分のターン開始時は通常ドロー＋1枚引く");

            // --- 猫好きミルキー: 登場時、デッキからコスト4以下のユニットをランダムに1枚手札へ ---
            var deckCat = new List<CardDefinition>();
            for (int i = 0; i < 10; i++) deckCat.Add(CharacterCards.KohakuCat);
            for (int i = 0; i < 30; i++) deckCat.Add(SampleCards.Knight); // コスト4（対象内）
            var engine26 = new GameEngine(deckCat, SampleCards.BuildSampleDeck(), seed: 161);
            engine26.StartTurn(); // GT1: ポイント2
            int deckBefore26 = engine26.Players[0].Deck.Count;
            int handBefore26 = engine26.Players[0].Hand.Count;
            bool catPlayed = false;
            foreach (var c in engine26.Players[0].Hand)
            {
                if (ReferenceEquals(c.Definition, CharacterCards.KohakuCat) && engine26.PlayUnit(c.InstanceId))
                {
                    catPlayed = true;
                    break;
                }
            }
            Check(catPlayed && engine26.Players[0].Deck.Count == deckBefore26 - 1
                && engine26.Players[0].Hand.Count == handBefore26,
                "猫好きミルキー: 登場時にコスト4以下のユニットが1枚手札に加わる");

            // --- 無数の選択肢ミルキー: X/X（効果ドロー連動）＋登場時2ドロー＋無料配置 ---
            var deckMusuu = new List<CardDefinition>();
            for (int i = 0; i < 6; i++) deckMusuu.Add(CharacterCards.KohakuBig);
            for (int i = 0; i < 6; i++) deckMusuu.Add(SampleCards.Supply); // 紳士の嗜み
            for (int i = 0; i < 28; i++) deckMusuu.Add(SampleCards.Knight);
            var engine27 = new GameEngine(deckMusuu, SampleCards.BuildSampleDeck(), seed: 171);
            bool gentPlayed = false, musuuPlayed = false, deployed = false, grew = false;
            UnitState musuuUnit = null;
            for (int round = 0; round < 25 && !grew; round++)
            {
                engine27.StartTurn(); // P0
                var p0 = engine27.Players[0];
                if (!gentPlayed)
                {
                    foreach (var c in p0.Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Supply) && engine27.PlayTactic(c.InstanceId))
                        {
                            gentPlayed = true;
                            break;
                        }
                    }
                }
                if (gentPlayed && !musuuPlayed)
                {
                    foreach (var c in p0.Hand)
                    {
                        if (ReferenceEquals(c.Definition, CharacterCards.KohakuBig)
                            && p0.Points.Available >= c.Definition.Cost && engine27.PlayUnit(c.InstanceId))
                        {
                            musuuPlayed = true;
                            foreach (var u in p0.Field)
                            {
                                if (ReferenceEquals(u.Definition, CharacterCards.KohakuBig)) musuuUnit = u;
                            }
                            break;
                        }
                    }
                    if (musuuPlayed)
                    {
                        Check(p0.EffectDrawsThisGame == 4, "無数: 効果ドロー累計は紳士2＋登場時2で4");
                        Check(musuuUnit != null && musuuUnit.Attack == 2 && musuuUnit.MaxHealth == 2,
                            "無数: X = 4÷2 で 2/2 になる");
                        Check(engine27.PendingDeployByPlayer == 0, "無数: 無料配置の選択待ちになる");
                        int pointsBefore = p0.Points.Available;
                        foreach (var c in p0.Hand)
                        {
                            if (ReferenceEquals(c.Definition, SampleCards.Knight) && engine27.ChooseFreeDeploy(c.InstanceId))
                            {
                                deployed = true;
                                break;
                            }
                        }
                        Check(deployed && p0.Field.Count == 2 && p0.Points.Available == pointsBefore,
                            "無数: 手札のユニットをコストを支払わずに場に出せる");
                    }
                }
                else if (musuuPlayed && deployed && !grew)
                {
                    foreach (var c in p0.Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Supply) && engine27.PlayTactic(c.InstanceId))
                        {
                            grew = musuuUnit != null && musuuUnit.Attack == 3 && musuuUnit.MaxHealth == 3;
                            break;
                        }
                    }
                }
                while (!engine27.EndTurn())
                {
                    // 手札上限で捨て待ちになったら、余分な騎士を捨てる。
                    CardInstance extra = null;
                    foreach (var c in p0.Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Knight)) { extra = c; break; }
                    }
                    if (extra == null || !engine27.DiscardFromHand(extra.InstanceId)) break;
                }
                engine27.StartTurn(); // P1（何もしない）
                var p1 = engine27.Players[1];
                while (!engine27.EndTurn())
                {
                    if (p1.Hand.Count == 0 || !engine27.DiscardFromHand(p1.Hand[0].InstanceId)) break;
                }
            }
            Check(musuuPlayed && grew, "無数: さらに効果で引くと X が 3 に伸びる");

            // --- 守護（マスカット）: 守護がいる間は顔も他のユニットも攻撃できない ---
            var deckGuard = new List<CardDefinition>();
            for (int i = 0; i < 10; i++) deckGuard.Add(CharacterCards.Muscat);
            for (int i = 0; i < 30; i++) deckGuard.Add(SampleCards.Knight);
            var deckAttacker = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckAttacker.Add(SampleCards.Knight);
            var engine28 = new GameEngine(deckGuard, deckAttacker, seed: 181);
            UnitState guardMuscat = null, guardP0Knight = null, guardP1Knight = null;
            bool guardChecked = false;
            for (int round = 0; round < 15 && !guardChecked; round++)
            {
                engine28.StartTurn(); // P0
                var p0 = engine28.Players[0];
                foreach (var c in p0.Hand)
                {
                    if (guardMuscat == null && ReferenceEquals(c.Definition, CharacterCards.Muscat)
                        && engine28.PlayUnit(c.InstanceId))
                    {
                        foreach (var u in p0.Field) if (ReferenceEquals(u.Definition, CharacterCards.Muscat)) guardMuscat = u;
                        break;
                    }
                }
                foreach (var c in p0.Hand)
                {
                    if (guardP0Knight == null && ReferenceEquals(c.Definition, SampleCards.Knight)
                        && engine28.PlayUnit(c.InstanceId))
                    {
                        foreach (var u in p0.Field) if (ReferenceEquals(u.Definition, SampleCards.Knight)) guardP0Knight = u;
                        break;
                    }
                }
                engine28.EndTurn();
                engine28.StartTurn(); // P1
                var p1 = engine28.Players[1];
                if (guardP1Knight == null)
                {
                    foreach (var c in p1.Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Knight) && engine28.PlayUnit(c.InstanceId))
                        {
                            foreach (var u in p1.Field) if (ReferenceEquals(u.Definition, SampleCards.Knight)) guardP1Knight = u;
                            break;
                        }
                    }
                }
                else if (guardMuscat != null && guardP0Knight != null)
                {
                    Check(engine28.Gauge.Position == 0, "守護テスト: 攻撃前のゲージは0");
                    Check(!engine28.AttackFace(guardP1Knight), "守護: 顔を攻撃できない");
                    Check(!engine28.AttackUnit(guardP1Knight, guardP0Knight), "守護: 守護以外のユニットを攻撃できない");
                    Check(engine28.AttackUnit(guardP1Knight, guardMuscat), "守護: 守護ユニットは攻撃できる");
                    Check(engine28.Gauge.Position == 1, "守護: 戦闘で倒すとコスト3の半分=1だけゲージが動く");
                    guardChecked = true;
                }
                while (!engine28.EndTurn())
                {
                    if (p1.Hand.Count == 0 || !engine28.DiscardFromHand(p1.Hand[0].InstanceId)) break;
                }
            }
            Check(guardChecked, "守護: 一連の攻撃制限を検証できた");

            // --- ゲーマーマスカット: 登場時に味方1体へ守護を付与（付与は本体が落ちても残る） ---
            var deckGamerMuscat = new List<CardDefinition>();
            for (int i = 0; i < 8; i++) deckGamerMuscat.Add(CharacterCards.MuscatGamer);
            for (int i = 0; i < 32; i++) deckGamerMuscat.Add(SampleCards.Knight);
            var deckBomb29 = new List<CardDefinition>();
            for (int i = 0; i < 15; i++) deckBomb29.Add(SampleCards.Bombardment);
            for (int i = 0; i < 25; i++) deckBomb29.Add(SampleCards.Knight);
            var engine29 = new GameEngine(deckGamerMuscat, deckBomb29, seed: 191);
            UnitState knight29 = null, gamer29 = null, p1Knight29 = null;
            bool granted = false, grantGuardChecked = false;
            for (int round = 0; round < 20 && !grantGuardChecked; round++)
            {
                engine29.StartTurn(); // P0
                var p0 = engine29.Players[0];
                if (knight29 == null)
                {
                    foreach (var c in p0.Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Knight) && engine29.PlayUnit(c.InstanceId))
                        {
                            foreach (var u in p0.Field) if (ReferenceEquals(u.Definition, SampleCards.Knight)) knight29 = u;
                            break;
                        }
                    }
                }
                else if (gamer29 == null)
                {
                    foreach (var c in p0.Hand)
                    {
                        if (ReferenceEquals(c.Definition, CharacterCards.MuscatGamer) && engine29.PlayUnit(c.InstanceId))
                        {
                            foreach (var u in p0.Field) if (ReferenceEquals(u.Definition, CharacterCards.MuscatGamer)) gamer29 = u;
                            break;
                        }
                    }
                    if (gamer29 != null && engine29.PendingGuardGrantByPlayer == 0)
                    {
                        granted = engine29.ChooseGuardTarget(knight29);
                    }
                }
                while (!engine29.EndTurn())
                {
                    CardInstance extra = null;
                    foreach (var c in p0.Hand)
                    {
                        if (gamer29 == null && ReferenceEquals(c.Definition, CharacterCards.MuscatGamer)) continue;
                        extra = c;
                        break;
                    }
                    if (extra == null || !engine29.DiscardFromHand(extra.InstanceId)) break;
                }
                engine29.StartTurn(); // P1
                var p1 = engine29.Players[1];
                bool gamerAlive = gamer29 != null && p0.Field.Contains(gamer29);
                if (granted && gamerAlive)
                {
                    foreach (var c in p1.Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Bombardment) && engine29.PlayTactic(c.InstanceId))
                        {
                            if (engine29.PendingDamageByPlayer == 1) engine29.ChooseDamageTarget(gamer29.Instance.InstanceId);
                            break;
                        }
                    }
                }
                else if (granted && !gamerAlive && p1Knight29 == null)
                {
                    foreach (var c in p1.Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Knight) && engine29.PlayUnit(c.InstanceId))
                        {
                            foreach (var u in p1.Field) if (ReferenceEquals(u.Definition, SampleCards.Knight)) p1Knight29 = u;
                            break;
                        }
                    }
                }
                else if (granted && !gamerAlive && p1Knight29 != null)
                {
                    Check(knight29 != null && knight29.GrantedGuard, "守護付与: 選んだ味方が守護を得る");
                    Check(!engine29.AttackFace(p1Knight29), "守護付与: 付与された守護でも顔を守れる");
                    Check(engine29.AttackUnit(p1Knight29, knight29), "守護付与: 付与された守護は攻撃対象にできる");
                    grantGuardChecked = true;
                }
                while (!engine29.EndTurn())
                {
                    if (p1.Hand.Count == 0 || !engine29.DiscardFromHand(p1.Hand[0].InstanceId)) break;
                }
            }
            Check(granted && grantGuardChecked, "ゲーマーマスカット: 守護付与の一連の流れを検証できた");

            // --- 成熟マスカット: 守護を持つ味方の体力最大値+1オーラ（本人は守護なし） ---
            var deckRipe = new List<CardDefinition>();
            for (int i = 0; i < 8; i++) deckRipe.Add(CharacterCards.Muscat);
            for (int i = 0; i < 8; i++) deckRipe.Add(CharacterCards.MuscatRipe);
            for (int i = 0; i < 24; i++) deckRipe.Add(SampleCards.Knight);
            var engine30 = new GameEngine(deckRipe, SampleCards.BuildSampleDeck(), seed: 201);
            UnitState guardA = null, ripeUnit = null, guardB = null, plainKnight = null;
            bool auraChecked = false;
            for (int round = 0; round < 15 && !auraChecked; round++)
            {
                engine30.StartTurn(); // P0
                var p0 = engine30.Players[0];
                foreach (var c in p0.Hand)
                {
                    if (guardA == null && ReferenceEquals(c.Definition, CharacterCards.Muscat) && engine30.PlayUnit(c.InstanceId))
                    {
                        foreach (var u in p0.Field) if (ReferenceEquals(u.Definition, CharacterCards.Muscat)) guardA = u;
                        break;
                    }
                    if (guardA != null && ripeUnit == null && ReferenceEquals(c.Definition, CharacterCards.MuscatRipe) && engine30.PlayUnit(c.InstanceId))
                    {
                        foreach (var u in p0.Field) if (ReferenceEquals(u.Definition, CharacterCards.MuscatRipe)) ripeUnit = u;
                        break;
                    }
                    if (ripeUnit != null && guardB == null && ReferenceEquals(c.Definition, CharacterCards.Muscat) && engine30.PlayUnit(c.InstanceId))
                    {
                        foreach (var u in p0.Field)
                        {
                            if (ReferenceEquals(u.Definition, CharacterCards.Muscat) && !ReferenceEquals(u, guardA)) guardB = u;
                        }
                        break;
                    }
                    if (ripeUnit != null && plainKnight == null && ReferenceEquals(c.Definition, SampleCards.Knight) && engine30.PlayUnit(c.InstanceId))
                    {
                        foreach (var u in p0.Field) if (ReferenceEquals(u.Definition, SampleCards.Knight)) plainKnight = u;
                        break;
                    }
                }
                if (guardA != null && ripeUnit != null && guardB != null && plainKnight != null)
                {
                    Check(guardA.MaxHealth == 5, "成熟マスカット: 先に出ていた守護の体力最大値が+1される");
                    Check(guardB.MaxHealth == 5, "成熟マスカット: 後から出た守護にもオーラが効く");
                    Check(ripeUnit.MaxHealth == 4 && !ripeUnit.HasGuard, "成熟マスカット: 本人は守護を持たず、自分のオーラも効かない");
                    Check(plainKnight.MaxHealth == 4, "成熟マスカット: 守護でないユニットには効かない");
                    auraChecked = true;
                }
                while (!engine30.EndTurn())
                {
                    if (p0.Hand.Count == 0 || !engine30.DiscardFromHand(p0.Hand[0].InstanceId)) break;
                }
                engine30.StartTurn(); // P1（何もしない）
                var p1 = engine30.Players[1];
                while (!engine30.EndTurn())
                {
                    if (p1.Hand.Count == 0 || !engine30.DiscardFromHand(p1.Hand[0].InstanceId)) break;
                }
            }
            Check(auraChecked, "成熟マスカット: オーラの一連の効き方を検証できた");

            // --- シャインマスカット: 守護+1/+1オーラ＋破壊時にデッキから守護を選んで出す ---
            var deckShine = new List<CardDefinition>();
            for (int i = 0; i < 6; i++) deckShine.Add(CharacterCards.MuscatBig);
            for (int i = 0; i < 6; i++) deckShine.Add(CharacterCards.MuscatGamer);
            for (int i = 0; i < 10; i++) deckShine.Add(CharacterCards.Muscat);
            for (int i = 0; i < 18; i++) deckShine.Add(SampleCards.Knight);
            var deckBomb31 = new List<CardDefinition>();
            for (int i = 0; i < 15; i++) deckBomb31.Add(SampleCards.Bombardment);
            for (int i = 0; i < 25; i++) deckBomb31.Add(SampleCards.Knight);
            var engine31 = new GameEngine(deckShine, deckBomb31, seed: 211);
            UnitState shineUnit = null, muscat31 = null;
            bool aura31 = false, deploy31 = false;
            for (int round = 0; round < 25 && !deploy31; round++)
            {
                engine31.StartTurn(); // P0
                var p0 = engine31.Players[0];
                if (muscat31 == null)
                {
                    foreach (var c in p0.Hand)
                    {
                        if (ReferenceEquals(c.Definition, CharacterCards.Muscat) && engine31.PlayUnit(c.InstanceId))
                        {
                            foreach (var u in p0.Field) if (ReferenceEquals(u.Definition, CharacterCards.Muscat)) muscat31 = u;
                            break;
                        }
                    }
                }
                else if (shineUnit == null)
                {
                    foreach (var c in p0.Hand)
                    {
                        if (ReferenceEquals(c.Definition, CharacterCards.MuscatBig) && engine31.PlayUnit(c.InstanceId))
                        {
                            foreach (var u in p0.Field) if (ReferenceEquals(u.Definition, CharacterCards.MuscatBig)) shineUnit = u;
                            break;
                        }
                    }
                    if (shineUnit != null && !aura31)
                    {
                        aura31 = muscat31.Attack == 3 && muscat31.MaxHealth == 5
                            && shineUnit.Attack == 5 && shineUnit.MaxHealth == 8;
                    }
                }
                while (!engine31.EndTurn())
                {
                    if (p0.Hand.Count == 0 || !engine31.DiscardFromHand(p0.Hand[0].InstanceId)) break;
                }
                engine31.StartTurn(); // P1: 爆撃でシャインを落とす
                var p1 = engine31.Players[1];
                bool shineAlive = shineUnit != null && p0.Field.Contains(shineUnit);
                if (shineAlive)
                {
                    foreach (var c in p1.Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Bombardment) && engine31.PlayTactic(c.InstanceId))
                        {
                            if (engine31.PendingDamageByPlayer == 1)
                                engine31.ChooseDamageTarget(shineUnit.Instance.InstanceId);
                            break;
                        }
                    }
                }
                while (!engine31.EndTurn())
                {
                    if (p1.Hand.Count == 0 || !engine31.DiscardFromHand(p1.Hand[0].InstanceId)) break;
                }
                // シャインが落ちたら、P0のデッキ選択待ちを解決する（相手ターン中の選択も可）。
                if (shineUnit != null && !p0.Field.Contains(shineUnit) && engine31.PendingDeckDeployByPlayer == 0)
                {
                    CardInstance pick = null;
                    foreach (var cand in engine31.PendingDeckDeployCandidates)
                    {
                        if (ReferenceEquals(cand.Definition, CharacterCards.MuscatGamer)) { pick = cand; break; }
                    }
                    if (pick != null && engine31.ChooseDeckDeploy(pick.InstanceId))
                    {
                        // ゲーマーマスカットが出たので、その守護付与も解決する。
                        if (engine31.PendingGuardGrantByPlayer == 0)
                            deploy31 = engine31.ChooseGuardTarget(muscat31) && muscat31.GrantedGuard;
                    }
                }
            }
            Check(aura31, "シャイン: 守護ユニット（本人含む）が+1/+1される");
            Check(deploy31, "シャイン: 破壊時にデッキから守護を選んで場に出し、登場時効果も発動する");
            Check(muscat31 != null && muscat31.Attack == 2 && muscat31.MaxHealth == 4,
                "シャイン: 場を離れるとオーラが消える");

            // --- 先手必勝（ミニココナッツ）: 出たターンにユニットは攻撃できるが顔は攻撃できない ---
            var deckWall = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckWall.Add(SampleCards.Knight);
            var deckMiniCoco = new List<CardDefinition>();
            for (int i = 0; i < 12; i++) deckMiniCoco.Add(CharacterCards.CoconutSmall);
            for (int i = 0; i < 28; i++) deckMiniCoco.Add(SampleCards.Knight);
            var engine32 = new GameEngine(deckWall, deckMiniCoco, seed: 221);
            UnitState wallKnight = null;
            bool unitAttackOk = false, faceBlocked = false;
            for (int round = 0; round < 12 && !(unitAttackOk && faceBlocked); round++)
            {
                engine32.StartTurn(); // P0: 壁になる騎士を出す
                var p0 = engine32.Players[0];
                if (wallKnight == null)
                {
                    foreach (var c in p0.Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Knight) && engine32.PlayUnit(c.InstanceId))
                        {
                            foreach (var u in p0.Field) if (ReferenceEquals(u.Definition, SampleCards.Knight)) wallKnight = u;
                            break;
                        }
                    }
                }
                engine32.EndTurn();
                engine32.StartTurn(); // P1: ミニココナッツを出して即テスト
                var p1 = engine32.Players[1];
                if (wallKnight != null && !unitAttackOk)
                {
                    foreach (var c in p1.Hand)
                    {
                        if (ReferenceEquals(c.Definition, CharacterCards.CoconutSmall) && engine32.PlayUnit(c.InstanceId))
                        {
                            UnitState mini = null;
                            foreach (var u in p1.Field) if (ReferenceEquals(u.Definition, CharacterCards.CoconutSmall)) mini = u;
                            if (mini != null)
                            {
                                faceBlocked = !engine32.AttackFace(mini);
                                unitAttackOk = engine32.AttackUnit(mini, wallKnight);
                            }
                            break;
                        }
                    }
                }
                while (!engine32.EndTurn())
                {
                    if (p1.Hand.Count == 0 || !engine32.DiscardFromHand(p1.Hand[0].InstanceId)) break;
                }
            }
            Check(faceBlocked, "先手必勝: 出たターンに顔は攻撃できない");
            Check(unitAttackOk, "先手必勝: 出たターンに相手ユニットを攻撃できる");

            // --- ゲーマーココナッツ: 登場ターンは戦闘ダメージを受けない（次のターンからは受ける） ---
            var deckWall33 = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckWall33.Add(SampleCards.Knight);
            var deckGamerCoco = new List<CardDefinition>();
            for (int i = 0; i < 12; i++) deckGamerCoco.Add(CharacterCards.CoconutGamer);
            for (int i = 0; i < 28; i++) deckGamerCoco.Add(SampleCards.Knight);
            var engine33 = new GameEngine(deckWall33, deckGamerCoco, seed: 231);
            UnitState gamerUnit = null;
            bool immuneOnPlayTurn = false, vulnerableNextTurn = false;
            int knightsPlayed = 0;
            for (int round = 0; round < 12 && !vulnerableNextTurn; round++)
            {
                engine33.StartTurn(); // P0: 騎士を壁として出す
                var q0 = engine33.Players[0];
                bool needKnight = gamerUnit == null ? knightsPlayed < 1 : knightsPlayed < 2;
                if (needKnight)
                {
                    foreach (var c in q0.Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Knight) && engine33.PlayUnit(c.InstanceId)) { knightsPlayed++; break; }
                    }
                }
                while (!engine33.EndTurn())
                {
                    if (q0.Hand.Count == 0 || !engine33.DiscardFromHand(q0.Hand[0].InstanceId)) break;
                }
                engine33.StartTurn(); // P1: ゲーマーココナッツで即攻撃→次のターンにもう一度攻撃
                var q1 = engine33.Players[1];
                if (gamerUnit == null && knightsPlayed >= 1)
                {
                    foreach (var c in q1.Hand)
                    {
                        if (ReferenceEquals(c.Definition, CharacterCards.CoconutGamer) && engine33.PlayUnit(c.InstanceId))
                        {
                            foreach (var u in q1.Field) if (ReferenceEquals(u.Definition, CharacterCards.CoconutGamer)) gamerUnit = u;
                            break;
                        }
                    }
                    if (gamerUnit != null)
                    {
                        UnitState target = null;
                        foreach (var u in q0.Field) target = u;
                        if (target != null && engine33.AttackUnit(gamerUnit, target))
                            immuneOnPlayTurn = gamerUnit.DamageTaken == 0 && q1.Field.Contains(gamerUnit);
                    }
                }
                else if (gamerUnit != null && immuneOnPlayTurn && knightsPlayed >= 2 && q1.Field.Contains(gamerUnit))
                {
                    UnitState target = null;
                    foreach (var u in q0.Field) target = u;
                    if (target != null && engine33.AttackUnit(gamerUnit, target))
                        vulnerableNextTurn = !q1.Field.Contains(gamerUnit) || gamerUnit.DamageTaken > 0;
                }
                while (!engine33.EndTurn())
                {
                    if (q1.Hand.Count == 0 || !engine33.DiscardFromHand(q1.Hand[0].InstanceId)) break;
                }
            }
            Check(immuneOnPlayTurn, "ゲーマーココナッツ: 登場ターンは戦闘ダメージを受けない");
            Check(vulnerableNextTurn, "ゲーマーココナッツ: 次のターンからは戦闘ダメージを受ける");

            // --- 孤軍奮闘ココナッツ: 戦闘ダメージは最大2まで、顔は攻撃できない ---
            var deckWall34 = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckWall34.Add(SampleCards.Knight);
            var deckSolo = new List<CardDefinition>();
            for (int i = 0; i < 12; i++) deckSolo.Add(CharacterCards.CoconutSolo);
            for (int i = 0; i < 28; i++) deckSolo.Add(SampleCards.Knight);
            var engine34 = new GameEngine(deckWall34, deckSolo, seed: 241);
            UnitState soloUnit = null;
            bool capAsAttacker = false, faceAlwaysBlocked = false, capAsDefender = false;
            int knights34 = 0;
            for (int round = 0; round < 14 && !capAsDefender; round++)
            {
                engine34.StartTurn(); // P0
                var s0 = engine34.Players[0];
                if (knights34 < 2)
                {
                    foreach (var c in s0.Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Knight) && engine34.PlayUnit(c.InstanceId)) { knights34++; break; }
                    }
                }
                if (capAsAttacker && soloUnit != null && knights34 >= 2)
                {
                    // 守備側としての上限テスト: 騎士2号から孤軍を攻撃させる
                    UnitState k2 = null;
                    foreach (var u in s0.Field) k2 = u;
                    int before = soloUnit.DamageTaken;
                    if (k2 != null && engine34.AttackUnit(k2, soloUnit))
                        capAsDefender = soloUnit.DamageTaken == before + 2 && s0.Field.Count == 0;
                }
                while (!engine34.EndTurn())
                {
                    if (s0.Hand.Count == 0 || !engine34.DiscardFromHand(s0.Hand[0].InstanceId)) break;
                }
                engine34.StartTurn(); // P1
                var s1 = engine34.Players[1];
                if (soloUnit == null && knights34 >= 1)
                {
                    foreach (var c in s1.Hand)
                    {
                        if (ReferenceEquals(c.Definition, CharacterCards.CoconutSolo) && engine34.PlayUnit(c.InstanceId))
                        {
                            foreach (var u in s1.Field) if (ReferenceEquals(u.Definition, CharacterCards.CoconutSolo)) soloUnit = u;
                            break;
                        }
                    }
                    if (soloUnit != null)
                    {
                        bool faceNow = engine34.AttackFace(soloUnit);
                        UnitState target = null;
                        foreach (var u in s0.Field) target = u;
                        bool atk = target != null && engine34.AttackUnit(soloUnit, target);
                        capAsAttacker = atk && soloUnit.DamageTaken == 2 && s1.Field.Contains(soloUnit);
                        faceAlwaysBlocked = !faceNow;
                    }
                }
                else if (soloUnit != null && capAsAttacker && s1.Field.Contains(soloUnit))
                {
                    if (engine34.AttackFace(soloUnit)) faceAlwaysBlocked = false; // 次のターンでも顔は不可
                }
                while (!engine34.EndTurn())
                {
                    if (s1.Hand.Count == 0 || !engine34.DiscardFromHand(s1.Hand[0].InstanceId)) break;
                }
            }
            Check(capAsAttacker, "孤軍奮闘: 攻撃時の反撃ダメージは2に抑えられる");
            Check(faceAlwaysBlocked, "孤軍奮闘: プレイヤーは攻撃できない（常時）");
            Check(capAsDefender, "孤軍奮闘: 守備時も受けるダメージは2まで");

            // --- 百発百中ココナッツ: 登場ターンだけ2回攻撃、被ダメ上限2、キルで1回復。次のターンは1回 ---
            var deckWall35 = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckWall35.Add(SampleCards.Knight);
            var deckMarks = new List<CardDefinition>();
            for (int i = 0; i < 12; i++) deckMarks.Add(CharacterCards.CoconutMarksman);
            for (int i = 0; i < 28; i++) deckMarks.Add(SampleCards.Knight);
            var engine35 = new GameEngine(deckWall35, deckMarks, seed: 251);
            UnitState marksUnit = null;
            bool capHealOk = false, twoAttacksOk = false, normalNextTurn = false;
            int knights35 = 0;
            for (int round = 0; round < 14 && !normalNextTurn; round++)
            {
                engine35.StartTurn(); // P0: 騎士を順番に出す
                var t0 = engine35.Players[0];
                if (knights35 < 3)
                {
                    foreach (var c in t0.Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Knight) && engine35.PlayUnit(c.InstanceId)) { knights35++; break; }
                    }
                }
                while (!engine35.EndTurn())
                {
                    if (t0.Hand.Count == 0 || !engine35.DiscardFromHand(t0.Hand[0].InstanceId)) break;
                }
                engine35.StartTurn(); // P1
                var t1 = engine35.Players[1];
                if (marksUnit == null && knights35 >= 2)
                {
                    foreach (var c in t1.Hand)
                    {
                        if (ReferenceEquals(c.Definition, CharacterCards.CoconutMarksman) && engine35.PlayUnit(c.InstanceId))
                        {
                            foreach (var u in t1.Field) if (ReferenceEquals(u.Definition, CharacterCards.CoconutMarksman)) marksUnit = u;
                            break;
                        }
                    }
                    if (marksUnit != null)
                    {
                        UnitState tgt1 = null, tgt2 = null;
                        foreach (var u in t0.Field) { if (tgt1 == null) tgt1 = u; else tgt2 = u; }
                        if (tgt1 != null && engine35.AttackUnit(marksUnit, tgt1))
                            capHealOk = marksUnit.DamageTaken == 1; // 反撃4→上限2→キル回復1
                        if (tgt2 != null && engine35.AttackUnit(marksUnit, tgt2))
                            twoAttacksOk = marksUnit.DamageTaken == 2 && t1.Field.Contains(marksUnit);
                    }
                }
                else if (marksUnit != null && twoAttacksOk && knights35 >= 3 && t1.Field.Contains(marksUnit))
                {
                    UnitState tgt = null;
                    foreach (var u in t0.Field) tgt = u;
                    bool first = tgt != null && engine35.AttackUnit(marksUnit, tgt);
                    bool second = engine35.AttackFace(marksUnit);
                    normalNextTurn = first && !second; // 次のターンは1回だけ
                }
                while (!engine35.EndTurn())
                {
                    if (t1.Hand.Count == 0 || !engine35.DiscardFromHand(t1.Hand[0].InstanceId)) break;
                }
            }
            Check(capHealOk, "百発百中: 反撃は上限2、倒したら体力を1回復する");
            Check(twoAttacksOk, "百発百中: 登場ターンに2回攻撃できる");
            Check(normalNextTurn, "百発百中: 次のターンは1回しか攻撃できない");

            // --- 最前線攻略tv⁉︎（コスト3）: 基本ポイントが恒久+1（次の自分のターンから効く） ---
            var deckRamp = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckRamp.Add(SampleCards.Saizensen);
            var engine36 = new GameEngine(deckRamp, deckRamp, seed: 261);
            var rp = engine36.Players[0];
            bool playedRamp = false;
            for (int round = 0; round < 6 && !playedRamp; round++)
            {
                engine36.StartTurn();
                if (engine36.ActivePlayerIndex == 0)
                {
                    int beforeBase = rp.Points.BasePoints;
                    foreach (var c in rp.Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Saizensen) && engine36.PlayTactic(c.InstanceId))
                        {
                            playedRamp = rp.Points.BasePoints == beforeBase + 1;
                            break;
                        }
                    }
                }
                while (!engine36.EndTurn())
                {
                    var ap = engine36.Players[engine36.ActivePlayerIndex];
                    if (ap.Hand.Count == 0 || !engine36.DiscardFromHand(ap.Hand[0].InstanceId)) break;
                }
            }
            Check(playedRamp, "最前線攻略tv: 基本ポイントが+1される");

            // --- やーや: 登場時に自分の基本ポイント+1 ---
            var deckYayaRamp = new List<CardDefinition>();
            for (int i = 0; i < 12; i++) deckYayaRamp.Add(CharacterCards.Yaya);
            for (int i = 0; i < 28; i++) deckYayaRamp.Add(SampleCards.Knight);
            var engine37 = new GameEngine(deckYayaRamp, deckYayaRamp, seed: 271);
            bool yayaRampOk = false;
            for (int round = 0; round < 8 && !yayaRampOk; round++)
            {
                engine37.StartTurn();
                var yp = engine37.Players[engine37.ActivePlayerIndex];
                int beforeBase = yp.Points.BasePoints;
                foreach (var c in yp.Hand)
                {
                    if (ReferenceEquals(c.Definition, CharacterCards.Yaya) && engine37.PlayUnit(c.InstanceId))
                    {
                        yayaRampOk = yp.Points.BasePoints == beforeBase + 1;
                        break;
                    }
                }
                while (!engine37.EndTurn())
                {
                    if (yp.Hand.Count == 0 || !engine37.DiscardFromHand(yp.Hand[0].InstanceId)) break;
                }
            }
            Check(yayaRampOk, "やーや: 登場時に基本ポイントが+1される");

            // --- みにやーや: 基本ポイント5未満では無効、5以上なら登場時にこのターンのポイント+1 ---
            var deckMiniYaya = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckMiniYaya.Add(CharacterCards.YayaSmall);
            var engine38 = new GameEngine(deckMiniYaya, deckMiniYaya, seed: 281);
            bool miniNoBonus = false, miniBonus = false;
            for (int round = 0; round < 10 && !(miniNoBonus && miniBonus); round++)
            {
                engine38.StartTurn();
                var mp = engine38.Players[engine38.ActivePlayerIndex];
                int availBefore38 = mp.Points.Available;
                foreach (var c in mp.Hand)
                {
                    if (ReferenceEquals(c.Definition, CharacterCards.YayaSmall) && engine38.PlayUnit(c.InstanceId))
                    {
                        if (mp.Points.BasePoints < 5 && mp.Points.Available == availBefore38 - 1) miniNoBonus = true;
                        if (mp.Points.BasePoints >= 5 && mp.Points.Available == availBefore38) miniBonus = true;
                        break;
                    }
                }
                while (!engine38.EndTurn())
                {
                    if (mp.Hand.Count == 0 || !engine38.DiscardFromHand(mp.Hand[0].InstanceId)) break;
                }
            }
            Check(miniNoBonus, "みにやーや: 基本ポイント5未満ではポイントは増えない");
            Check(miniBonus, "みにやーや: 基本ポイント5以上なら出したターンのポイントが+1される（コストと相殺）");

            // --- ゲーマーやーや: 基本ポイント8以上なら登場時に+2/+2とポイント+2、未満なら素のまま ---
            var deckGamerYayaOnly = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckGamerYayaOnly.Add(CharacterCards.YayaGamer);
            var engine39 = new GameEngine(deckGamerYayaOnly, deckGamerYayaOnly, seed: 291);
            bool unmetOk = false;
            for (int round = 0; round < 8 && !unmetOk; round++)
            {
                engine39.StartTurn();
                var up = engine39.Players[engine39.ActivePlayerIndex];
                if (up.Points.BasePoints < 8)
                {
                    foreach (var c in up.Hand)
                    {
                        if (ReferenceEquals(c.Definition, CharacterCards.YayaGamer))
                        {
                            int before = up.Points.Available;
                            if (engine39.PlayUnit(c.InstanceId))
                            {
                                UnitState gu = null;
                                foreach (var u in up.Field) if (ReferenceEquals(u.Definition, CharacterCards.YayaGamer)) gu = u;
                                unmetOk = gu != null && gu.Attack == 3 && gu.MaxHealth == 3 && up.Points.Available == before - 5;
                            }
                            break;
                        }
                    }
                }
                while (!engine39.EndTurn())
                {
                    if (up.Hand.Count == 0 || !engine39.DiscardFromHand(up.Hand[0].InstanceId)) break;
                }
            }
            Check(unmetOk, "ゲーマーやーや: 基本ポイント8未満では発動しない");

            var deckRampToGamer = new List<CardDefinition>();
            for (int i = 0; i < 12; i++) deckRampToGamer.Add(CharacterCards.Yaya);
            for (int i = 0; i < 12; i++) deckRampToGamer.Add(CharacterCards.YayaGamer);
            for (int i = 0; i < 16; i++) deckRampToGamer.Add(SampleCards.Knight);
            var engine40 = new GameEngine(deckRampToGamer, deckRampToGamer, seed: 301);
            bool metOk = false;
            for (int round = 0; round < 16 && !metOk; round++)
            {
                engine40.StartTurn();
                var mp2 = engine40.Players[engine40.ActivePlayerIndex];
                foreach (var c in mp2.Hand) // まずやーやでランプ
                {
                    if (ReferenceEquals(c.Definition, CharacterCards.Yaya)) { engine40.PlayUnit(c.InstanceId); break; }
                }
                if (mp2.Points.BasePoints >= 8)
                {
                    foreach (var c in mp2.Hand)
                    {
                        if (ReferenceEquals(c.Definition, CharacterCards.YayaGamer))
                        {
                            int before = mp2.Points.Available;
                            if (engine40.PlayUnit(c.InstanceId))
                            {
                                UnitState gu = null;
                                foreach (var u in mp2.Field) if (ReferenceEquals(u.Definition, CharacterCards.YayaGamer)) gu = u;
                                metOk = gu != null && gu.Attack == 5 && gu.MaxHealth == 5 && mp2.Points.Available == before - 5 + 2;
                            }
                            break;
                        }
                    }
                }
                while (!engine40.EndTurn())
                {
                    if (mp2.Hand.Count == 0 || !engine40.DiscardFromHand(mp2.Hand[0].InstanceId)) break;
                }
            }
            Check(metOk, "ゲーマーやーや: 基本ポイント8以上で+2/+2とポイント+2が発動する");

            // --- 器用富豪やーや: 登場時はモード選択、攻撃時は選択を解決してから戦闘が再開される ---
            var deckWall41 = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckWall41.Add(SampleCards.Knight);
            var deckRich = new List<CardDefinition>();
            for (int i = 0; i < 12; i++) deckRich.Add(CharacterCards.YayaRich);
            for (int i = 0; i < 28; i++) deckRich.Add(SampleCards.Knight);
            var engine41 = new GameEngine(deckWall41, deckRich, seed: 311);
            UnitState richUnit = null;
            bool onPlayChoiceOk = false, attackResumeOk = false;
            for (int round = 0; round < 12 && !attackResumeOk; round++)
            {
                engine41.StartTurn(); // P0: 騎士を出す
                var v0 = engine41.Players[0];
                if (richUnit == null || !attackResumeOk)
                {
                    foreach (var c in v0.Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Knight) && v0.Field.Count == 0 && engine41.PlayUnit(c.InstanceId)) break;
                    }
                }
                while (!engine41.EndTurn())
                {
                    if (v0.Hand.Count == 0 || !engine41.DiscardFromHand(v0.Hand[0].InstanceId)) break;
                }
                engine41.StartTurn(); // P1
                var v1 = engine41.Players[1];
                if (richUnit == null)
                {
                    foreach (var c in v1.Hand)
                    {
                        if (ReferenceEquals(c.Definition, CharacterCards.YayaRich) && engine41.PlayUnit(c.InstanceId))
                        {
                            foreach (var u in v1.Field) if (ReferenceEquals(u.Definition, CharacterCards.YayaRich)) richUnit = u;
                            break;
                        }
                    }
                    if (richUnit != null && engine41.PendingRichChoiceByPlayer == 1)
                    {
                        int before = v1.Points.Available;
                        onPlayChoiceOk = engine41.ChooseRichMode(3) && v1.Points.Available == before + 1
                            && engine41.PendingRichChoiceByPlayer == null;
                    }
                }
                else if (onPlayChoiceOk && v0.Field.Count > 0 && v1.Field.Contains(richUnit))
                {
                    UnitState target = null;
                    foreach (var u in v0.Field) target = u;
                    bool declared = engine41.AttackUnit(richUnit, target);
                    bool held = declared && engine41.PendingRichChoiceByPlayer == 1 && target.DamageTaken == 0 && richUnit.AttacksUsedThisTurn == 0;
                    if (held && engine41.ChooseRichMode(2) && engine41.PendingDamageByPlayer == 1
                        && engine41.ChooseDamageTarget(target.Instance.InstanceId))
                    {
                        // 3ダメージで騎士は残り1→再開した戦闘で倒れる。反撃4で器用富豪も落ちる（相打ち）
                        attackResumeOk = v0.Field.Count == 0 && !v1.Field.Contains(richUnit);
                    }
                }
                while (!engine41.EndTurn())
                {
                    if (v1.Hand.Count == 0 || !engine41.DiscardFromHand(v1.Hand[0].InstanceId)) break;
                }
            }
            Check(onPlayChoiceOk, "器用富豪: 登場時にモードを選べる（ポイント+1）");
            Check(attackResumeOk, "器用富豪: 攻撃前にモードを解決し、その後で戦闘が再開される");

            // --- 器用富豪やーや（追加）: 登場時ドロー/モード2キル、選択中の行動ブロック、
            //     攻撃時モード2で対象を倒すと攻撃は不発（回数を消費しない）、モード1後は顔攻撃が通る ---
            var deckTarget42 = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckTarget42.Add(SampleCards.Spearman); // 槍兵 2/2
            var deckRich42 = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckRich42.Add(CharacterCards.YayaRich);
            var engine42 = new GameEngine(deckTarget42, deckRich42, seed: 412);
            bool richBlockedOk = false, richOnPlayKillOk = false, richOnPlayDrawOk = false;
            bool richFizzleOk = false, richFaceAfterDrawOk = false;
            UnitState richFirst = null;
            int richPhase = 0;
            for (int round = 0; round < 20 && richPhase < 4; round++)
            {
                engine42.StartTurn(); // P0: 槍兵を出す（場が空の時だけ）
                var w0 = engine42.Players[0];
                if (w0.Field.Count == 0)
                {
                    foreach (var c in w0.Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Spearman) && engine42.PlayUnit(c.InstanceId)) break;
                    }
                }
                while (!engine42.EndTurn())
                {
                    if (w0.Hand.Count == 0 || !engine42.DiscardFromHand(w0.Hand[0].InstanceId)) break;
                }

                engine42.StartTurn(); // P1
                var w1 = engine42.Players[1];
                if (richPhase == 0 && w0.Field.Count > 0)
                {
                    foreach (var c in w1.Hand)
                    {
                        if (!ReferenceEquals(c.Definition, CharacterCards.YayaRich)) continue;
                        if (!engine42.PlayUnit(c.InstanceId)) break;
                        foreach (var u in w1.Field) if (ReferenceEquals(u.Definition, CharacterCards.YayaRich)) richFirst = u;
                        if (engine42.PendingRichChoiceByPlayer == 1)
                        {
                            // 選択待ちの間は、他のユニットを出すこともターン終了もできない。
                            Guid other = Guid.Empty;
                            foreach (var oc in w1.Hand) other = oc.InstanceId;
                            richBlockedOk = other != Guid.Empty && !engine42.PlayUnit(other) && !engine42.EndTurn();
                            int gaugeBefore = engine42.Gauge.Position;
                            UnitState target = w0.Field[0];
                            if (engine42.ChooseRichMode(2) && engine42.PendingDamageByPlayer == 1
                                && engine42.ChooseDamageTarget(target.Instance.InstanceId))
                            {
                                // 槍兵（体力2）は効果ダメージで倒れる。効果死なのでゲージは動かない。
                                richOnPlayKillOk = w0.Field.Count == 0 && engine42.Gauge.Position == gaugeBefore;
                            }
                        }
                        break;
                    }
                    if (richOnPlayKillOk) richPhase = 1;
                }
                else if (richPhase == 1)
                {
                    foreach (var c in w1.Hand)
                    {
                        if (!ReferenceEquals(c.Definition, CharacterCards.YayaRich)) continue;
                        int fieldCount = w1.Field.Count;
                        if (!engine42.PlayUnit(c.InstanceId)) break;
                        if (w1.Field.Count > fieldCount && engine42.PendingRichChoiceByPlayer == 1)
                        {
                            int handBefore = w1.Hand.Count;
                            int drawsBefore = w1.EffectDrawsThisGame;
                            richOnPlayDrawOk = engine42.ChooseRichMode(1)
                                && w1.Hand.Count == handBefore + 1
                                && w1.EffectDrawsThisGame == drawsBefore + 1;
                        }
                        break;
                    }
                    if (richOnPlayDrawOk) richPhase = 2;
                }
                else if (richPhase == 2 && richFirst != null && w1.Field.Contains(richFirst) && w0.Field.Count > 0)
                {
                    UnitState target = w0.Field[0];
                    if (engine42.AttackUnit(richFirst, target) && engine42.PendingRichChoiceByPlayer == 1
                        && engine42.ChooseRichMode(2) && engine42.PendingDamageByPlayer == 1
                        && engine42.ChooseDamageTarget(target.Instance.InstanceId))
                    {
                        // 対象が効果で倒れたので攻撃は不発。攻撃回数は消費されず、本人も無傷で残る。
                        richFizzleOk = w0.Field.Count == 0 && w1.Field.Contains(richFirst)
                            && richFirst.AttacksUsedThisTurn == 0 && richFirst.DamageTaken == 0;
                    }
                    if (richFizzleOk)
                    {
                        int gaugeBefore = engine42.Gauge.Position;
                        if (engine42.AttackFace(richFirst) && engine42.PendingRichChoiceByPlayer == 1
                            && engine42.ChooseRichMode(1))
                        {
                            // モード1（ドロー）解決後に顔攻撃が再開され、攻撃力3の分だけゲージが動く。
                            richFaceAfterDrawOk = richFirst.AttacksUsedThisTurn == 1
                                && Math.Abs(engine42.Gauge.Position - gaugeBefore) == 3;
                        }
                    }
                    if (richFizzleOk && richFaceAfterDrawOk) richPhase = 4;
                }
                while (!engine42.EndTurn())
                {
                    if (w1.Hand.Count == 0 || !engine42.DiscardFromHand(w1.Hand[0].InstanceId)) break;
                }
            }
            Check(richBlockedOk, "器用富豪: モード選択待ちの間は他の行動ができない");
            Check(richOnPlayKillOk, "器用富豪: 登場時モード2で体力2のユニットを倒せる（ゲージは動かない）");
            Check(richOnPlayDrawOk, "器用富豪: 登場時モード1で1枚引ける（効果ドローとして数える）");
            Check(richFizzleOk, "器用富豪: 攻撃時モード2で対象を倒すと攻撃は不発になり、回数を消費しない");
            Check(richFaceAfterDrawOk, "器用富豪: 攻撃時モード1の解決後に顔攻撃が通り、ゲージが動く");

            // --- 手練手管やーや: 基本ポイント10以上で2回選べる／モード4で能力をコピー ---
            var deckWall44 = new List<CardDefinition>();
            for (int i = 0; i < 40; i++) deckWall44.Add(SampleCards.Knight);
            var deckMaster44 = new List<CardDefinition>();
            for (int i = 0; i < 12; i++) deckMaster44.Add(CharacterCards.Yaya);
            for (int i = 0; i < 12; i++) deckMaster44.Add(CharacterCards.YayaMaster);
            for (int i = 0; i < 16; i++) deckMaster44.Add(SampleCards.Knight);
            var engine44 = new GameEngine(deckWall44, deckMaster44, seed: 331);
            bool doublePickOk = false;
            for (int round = 0; round < 22 && !doublePickOk; round++)
            {
                engine44.StartTurn();
                var w1 = engine44.Players[engine44.ActivePlayerIndex];
                foreach (var c in w1.Hand) // やーやでランプ
                {
                    if (ReferenceEquals(c.Definition, CharacterCards.Yaya)) { engine44.PlayUnit(c.InstanceId); break; }
                }
                if (w1.Points.BasePoints >= 10)
                {
                    foreach (var c in w1.Hand)
                    {
                        if (ReferenceEquals(c.Definition, CharacterCards.YayaMaster) && engine44.PlayUnit(c.InstanceId))
                        {
                            int availAfterPlay = w1.Points.Available;
                            bool firstPick = engine44.PendingMasterChoiceByPlayer != null
                                && engine44.ChooseMasterMode(3) && w1.Points.Available == availAfterPlay + 2
                                && engine44.PendingMasterChoiceByPlayer != null; // まだ2回目が残る
                            int handBefore = w1.Hand.Count;
                            bool secondPick = firstPick && engine44.ChooseMasterMode(1)
                                && w1.Hand.Count == handBefore + 2 && engine44.PendingMasterChoiceByPlayer == null;
                            doublePickOk = secondPick;
                            break;
                        }
                    }
                }
                while (!engine44.EndTurn())
                {
                    if (w1.Hand.Count == 0 || !engine44.DiscardFromHand(w1.Hand[0].InstanceId)) break;
                }
            }
            Check(doublePickOk, "手練手管: 基本ポイント10以上なら2回選べる（ポイント+2→ドロー2）");

            // コピー: 手札のミニココナッツを除外して先手必勝を得て、出したターンに攻撃できる
            var deckCopy45 = new List<CardDefinition>();
            for (int i = 0; i < 12; i++) deckCopy45.Add(CharacterCards.YayaMaster);
            for (int i = 0; i < 12; i++) deckCopy45.Add(CharacterCards.CoconutSmall);
            for (int i = 0; i < 16; i++) deckCopy45.Add(SampleCards.Knight);
            var engine45 = new GameEngine(deckWall44, deckCopy45, seed: 341);
            UnitState masterUnit = null;
            bool copyOk = false;
            for (int round = 0; round < 14 && !copyOk; round++)
            {
                engine45.StartTurn(); // P0: 騎士を出す
                var x0 = engine45.Players[0];
                if (x0.Field.Count == 0)
                {
                    foreach (var c in x0.Hand)
                    {
                        if (ReferenceEquals(c.Definition, SampleCards.Knight) && engine45.PlayUnit(c.InstanceId)) break;
                    }
                }
                while (!engine45.EndTurn())
                {
                    if (x0.Hand.Count == 0 || !engine45.DiscardFromHand(x0.Hand[0].InstanceId)) break;
                }
                engine45.StartTurn(); // P1
                var x1 = engine45.Players[1];
                if (masterUnit == null)
                {
                    CardInstance miniInHand = null;
                    foreach (var c in x1.Hand) if (ReferenceEquals(c.Definition, CharacterCards.CoconutSmall)) miniInHand = c;
                    foreach (var c in x1.Hand)
                    {
                        if (ReferenceEquals(c.Definition, CharacterCards.YayaMaster) && engine45.PlayUnit(c.InstanceId))
                        {
                            foreach (var u in x1.Field) if (ReferenceEquals(u.Definition, CharacterCards.YayaMaster)) masterUnit = u;
                            break;
                        }
                    }
                    if (masterUnit != null && miniInHand != null && engine45.PendingMasterChoiceByPlayer == 1
                        && engine45.ChooseMasterMode(4) && engine45.PendingMasterCopyByPlayer == 1
                        && engine45.ChooseMasterCopyTarget(MasterCopyZone.Hand, miniInHand.InstanceId))
                    {
                        bool exiled = !x1.Hand.Contains(miniInHand) && !x1.Graveyard.Contains(miniInHand);
                        bool copied = ReferenceEquals(masterUnit.CopiedDefinition, CharacterCards.CoconutSmall);
                        UnitState knight = null;
                        foreach (var u in x0.Field) knight = u;
                        bool declared = knight != null && engine45.AttackUnit(masterUnit, knight);
                        bool resumed = declared && engine45.PendingMasterChoiceByPlayer == 1 && engine45.ChooseMasterMode(3);
                        copyOk = exiled && copied && resumed && x0.Field.Count == 0 && x1.Field.Contains(masterUnit);
                    }
                }
                while (!engine45.EndTurn())
                {
                    if (x1.Hand.Count == 0 || !engine45.DiscardFromHand(x1.Hand[0].InstanceId)) break;
                }
            }
            Check(copyOk, "手練手管: 手札のユニットを除外して能力をコピーし、登場時効果のように攻撃前選択も使える");

            Console.WriteLine(_failures == 0 ? "ALL PASS" : $"{_failures} FAILURES");
            return _failures == 0 ? 0 : 1;
        }
    }
}
