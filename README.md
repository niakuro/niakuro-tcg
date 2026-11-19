# にあくろのオリジナルTCG — ルールエンジン

オンラインで遊べるオリジナルTCGの試作プロジェクト。C#のルールエンジンに加え、
ブラウザ対戦画面とSignalRのルームサーバーを含む。Unityに持っていく場合も、
`src/Core/` は画面や通信から独立して使い回せる。

## 今決まっているルール（2026-11-19 時点・仮置き含む）

- **ポイント**: 基本2から毎ターン+1、上限なし。使い残しの半分（切り捨て）を次ターンへ繰り越し。
- **勝利**: 戦線ゲージ −15〜+15、端まで押し切ったら勝ち。
- **顔への攻撃**: 攻撃力分ゲージが動く（一番効率がいい押し方）。
- **ユニット撃破**: 戦闘で倒すと、倒されたユニットのコストの半分（切り捨て・最低1）ゲージが動く。相打ちは差分。効果（除去）で倒した場合は動かない。
- **戦闘**: 殴り合いで反撃あり、ダメージは残る。出たターンは攻撃できない。ブロックなし、攻撃先は自由。
- **背水の陣**: 攻撃でゲージの残りが5以下になった瞬間に必須発動。各プレイヤー1回だけ。次のターン+3ポイント／4枚ドロー／ゲージ3押し戻し、から1つ。先に選ばれた行動は相手は選べない。未使用なら端まで押し切られても残り1（14）で止まる。超高コストのフィニッシャーだけがこの保護を貫通できる。
- **場・手札・デッキ**: 場は5体まで、手札上限10枚、初期手札5枚、毎ターン1ドロー。デッキ40枚、同名カード3枚まで。マリガンは全交換・1回だけ。後攻は最初のポイント+1と追加ドロー1枚。
- **カードの種類**: ユニット／戦術（使い切り）。
- **能力値の目安**: 攻撃力+体力 ≒ コスト×2。強い効果を持つカードはその分下げる。

## フォルダ構成（何がどこに書いてあるか）

```
src/Core/                          Unityに入れるときはこのフォルダ配下の .cs を
  GameConstants.cs                 数字ルールの一覧（ゲージ幅、場の上限など）。
                                   バランス調整はまずここだけ見る。
  Gauge/FrontlineGauge.cs          戦線ゲージの位置管理と押し／押し戻しの計算だけ。
  Points/PointPool.cs              ポイントの計算（基本+繰り越し+一時ボーナス）。
  Haisui/HaisuiSystem.cs           背水の陣の管理（1回だけ・行動の封じ合い）。
  Combat/CombatResolver.cs         ユニット同士の戦闘の計算（反撃・ダメージ残存・撃破ゲージ）。
  Units/UnitState.cs               場に出ているユニット1体の今の状態（ダメージなど）。
  Players/PlayerState.cs           1プレイヤーの持ち物（デッキ・手札・墓地・場・ポイント）。
  Cards/CardDefinitions.cs         カードの設計図（データ）。新しいカードはデータを追加するだけ。
  Cards/SampleCards.cs             サンプルカード置き場。正式なカードデザインではない。
  Cards/CharacterCards.cs          メンバー本人がモデルのカード26体（にあくろ・ニトグリ各5体、ほか各4体）と専用戦術。
  Cards/PreconstructedDecks.cs     構築済みデッキ3種のレシピ（メンバー2人分ずつの組み合わせ）。
  Effects/EffectRegistry.cs        戦術カードの効果の登録場所。効果はID文字列で引く。
  Effects/SampleEffects.cs         サンプル効果（ドロー／押し戻し）。新しい効果の見本。
  Effects/CharacterEffects.cs      メンバーカード用効果（手札破壊／デッキ破壊）。
  Game/GameEngine.cs               対戦の進行役。ターン・出す・攻撃・背水・勝敗を統括する。
                                   個別の計算は各専門クラスに任せ、ここは順番と流れだけを見る。
tests/EngineCheck/Program.cs       動作確認プログラム。決めたルールがコード通り動くかチェックする。
tests/SimBattle/Program.cs         簡易AI同士の対戦シミュレータ。デッキ同士を戦わせて試合の流れを見る。
                                   新しいルールを足したら、ここにも確認ケースを1つ足す。
```

どのファイルにも冒頭に「このファイルについて」のコメントがある。

## Unityに入れる方法

1. `src/Core/` 配下の `.cs` ファイルを、Unityプロジェクトの `Assets/Scripts/Core/` にコピーする。
   （`tests/` はUnityには入れない、開発時だけの確認用）
2. `GameManager` のような MonoBehaviour を1つ作り、`GameEngine` を呼ぶ。例:

```csharp
using UnityEngine;
using Niakuro.Tcg.Core;

public class GameManager : MonoBehaviour
{
    private GameEngine game;

    void Start()
    {
        game = new GameEngine(
            SampleCards.BuildSampleDeck(),
            SampleCards.BuildSampleDeck(),
            seed: 42);
        game.StartTurn();
        Debug.Log(game.Players[0].Points.Available); // 先攻の初ターンは 2
    }
}
```

3. `GameManager` を空のオブジェクトに付けて再生すれば、コンソールで確認できる。
   `NiakuroTcg.Core` は UnityEngine に依存していないので、そのままコンパイルできる
   （netstandard2.1 / C# 9 に揃えてある）。

## 動作確認（Unityなしでテストする）

```bash
dotnet run --project tests/EngineCheck
```

`ALL PASS` になれば、決めたルールがコード通り動いている。

## ブラウザ対戦

```bash
dotnet run --project src/Web --urls http://localhost:5080
```

ブラウザで `http://localhost:5080` を開く。ルームを作成して表示された6文字の番号を相手に共有し、
相手は同じ番号を入力して参加する。プレイヤー名はブラウザCookieに保存される。

RenderではGitHubリポジトリ `niakuro/niakuro-tcg` を接続し、`render.yaml` のBlueprint設定から
Web Serviceを作成する。ルームと対戦状態はサーバーのメモリ上にあるため、単一インスタンスでの
試遊用。サーバーの再起動・スリープ後にはルームが消える。

## カードの増やし方

- **ユニット**: `Cards/SampleCards.cs` に1行追加するだけ。
  能力値の目安は「攻撃力+体力 ≒ コスト×2」。
- **戦術**: カードのデータを追加したうえで、`Effects/SampleEffects.cs` に
  効果の実体を1つ書いて `RegisterAll` に1行足す。

## ルールの変え方

- 数字だけ変えたいときは `GameConstants.cs` を見る。
- ルールの流れを変えたいときは `Game/GameEngine.cs`、計算を変えたいときは
  その計算を担当するファイル（ゲージなら `Gauge/`、ポイントなら `Points/`）を見る。
- 新しいルールを足したら `tests/EngineCheck/Program.cs` に確認ケースを1つ足す。
