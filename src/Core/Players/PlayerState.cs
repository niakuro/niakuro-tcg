// このファイルについて:
//   1プレイヤーの持ち物一式: デッキ・手札・墓地・場・ポイント。
//   ドローやマリガンなど「そのプレイヤーだけに閉じた操作」はここ、
//   相手と絡む処理（攻撃やゲージ）は Game/GameEngine.cs が担当する。
using System;
using System.Collections.Generic;
using System.Linq;

namespace Niakuro.Tcg.Core
{
    public sealed class PlayerState
    {
        public int Index { get; }
        public List<CardInstance> Deck { get; private set; }
        public List<CardInstance> Hand { get; } = new List<CardInstance>();
        public List<CardInstance> Graveyard { get; } = new List<CardInstance>();
        public List<UnitState> Field { get; } = new List<UnitState>();
        public PointPool Points { get; } = new PointPool();

        public bool HasUsedMulligan { get; private set; }
        public bool DeckEmpty => Deck.Count == 0;

        // この試合で「効果で」引いたカードの累計枚数（無数の選択肢ミルキーの X の元）。
        // 通常のターンドローや、デッキから手札に「加える」サーチは数えない。
        // GameEngine の効果ドロー経路だけが増やす。
        public int EffectDrawsThisGame { get; set; }

        // 表向きになっている手札。表向きのカードは両プレイヤーから見える。
        // 手札を離れたら表向き状態は解除される。
        private readonly HashSet<Guid> _revealedInstanceIds = new HashSet<Guid>();
        public bool IsRevealed(CardInstance card) => _revealedInstanceIds.Contains(card.InstanceId);
        public void Reveal(CardInstance card) => _revealedInstanceIds.Add(card.InstanceId);
        public int RevealedCount => _revealedInstanceIds.Count;

        private readonly Random _random;

        public PlayerState(int index, IEnumerable<CardDefinition> deckDefinitions, Random random)
        {
            Index = index;
            _random = random;
            Deck = deckDefinitions.Select(d => new CardInstance(d)).ToList();
            ShuffleDeck();
        }

        public void ShuffleDeck()
        {
            // Fisher-Yates シャッフル。
            for (int i = Deck.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (Deck[i], Deck[j]) = (Deck[j], Deck[i]);
            }
        }

        // 指定枚数ドローする。途中でデッキが尽きたら false（デッキ切れ負けの判定用）。
        public bool DrawCards(int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (Deck.Count == 0) return false;
                var top = Deck[Deck.Count - 1];
                Deck.RemoveAt(Deck.Count - 1);
                Hand.Add(top);
            }
            return true;
        }

        // マリガン: 手札を全部デッキに戻して引き直すか、そのままか。一回だけ。
        public bool Mulligan()
        {
            if (HasUsedMulligan) return false;
            HasUsedMulligan = true;

            int count = Hand.Count;
            Deck.AddRange(Hand);
            Hand.Clear();
            ShuffleDeck();
            DrawCards(count);
            return true;
        }

        public CardInstance FindInHand(Guid instanceId)
            => Hand.FirstOrDefault(c => c.InstanceId == instanceId);

        public bool RemoveFromHand(CardInstance card)
        {
            _revealedInstanceIds.Remove(card.InstanceId);
            return Hand.Remove(card);
        }

        public void SendToGraveyard(CardInstance card) => Graveyard.Add(card);

        // デッキ構築ルールの検証。40枚ちょうど・同名3枚まで。
        public static List<string> ValidateDeck(IEnumerable<CardDefinition> deckDefinitions)
        {
            var errors = new List<string>();
            var list = deckDefinitions.ToList();

            if (list.Count != GameConstants.DeckSize)
                errors.Add($"デッキは{GameConstants.DeckSize}枚ちょうど（現在{list.Count}枚）");

            foreach (var group in list.GroupBy(d => d.Id))
            {
                if (group.Count() > GameConstants.MaxCopiesPerCard)
                    errors.Add($"「{group.First().Name}」は{GameConstants.MaxCopiesPerCard}枚まで（現在{group.Count()}枚）");
            }
            return errors;
        }
    }
}
