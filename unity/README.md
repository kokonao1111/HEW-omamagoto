# unity/（本番の画面）

「リアルおままごと調理システム」の画面を作る Unity のプロジェクト。
横のモニターにキッチン、縦のモニターにお客さんを出す。サーバー（`server/`）から WebSocket でイベントを受け取り、演出を出すだけ（判断はしない）。

くわしい経緯と、これからやることは `docs/Unity_引き継ぎ.md` と `docs/作業記録_Unity.md`。

## 必要なもの

- **Unity 6000.3.28f1**（Unity 6.3 LTS）。チーム全員で同じバージョンにする
  - Unity Hub →「インストール」→「エディターをインストール」→「正式リリース」→ Unity 6.3 LTS
  - モジュールは Windows Build Support (IL2CPP)
- Git と Git LFS（PC ごとに 1 回 `git lfs install`）

## 開き方

1. リポジトリを `git pull` する
2. Unity Hub →「プロジェクト」→「追加」→「ディスクから追加」→ このリポジトリの `unity` フォルダーを選ぶ
3. 開く（初回は `Library/` を作るので数分かかる）

## 設定

- 描画は **URP**（`Assets/Settings/` に PC 用と Mobile 用の設定がある）
- 大きいファイル（画像・3D モデル・音・動画・フォント）は Git LFS に入る（`unity/.gitattributes`）
- `.meta` ファイルは必ず git に入れる
- `Library/` `Temp/` `Logs/` `UserSettings/` などは git に入れない（`unity/.gitignore`）

## 入っている道具

| パッケージ | 何のため |
| --- | --- |
| Newtonsoft Json（`com.unity.nuget.newtonsoft-json`） | サーバーの JSON を読む。「届かなかった項目」と「0」を見分けられる |
| MCP for Unity（`com.coplaydev.unity-mcp` v10.3.0） | Claude Code が Unity のエディターを操作するため。エディターの中だけで動き、ビルドした実行ファイルには入らない。使わない人は気にしなくてよい |

## 動かし方

`Assets/Scenes/SampleScene` を開いて再生する。シーンの `ServerEvents` が、サーバーとのやり取りをまとめて受け持つ。

### サーバーにつなぐ

- `ServerEvents` の **Server Connection** の `Server Url`（はじめは `ws://localhost:9000/screen`）につなぐ
- サーバーが別の PC で動いているときは、その PC の IP にする（例：`ws://192.168.x.x:9000/screen`）
- つながると `{"device":"screen","event":"hello"}` を送る。切れたら自動でつなぎ直す（1 秒〜最大 10 秒おき）
- 画面の左上に、つながりの状態が出る（サーバー接続中 / サーバーを待っています / 再接続中… / デモ再生中）

### サーバーなしで試す（キーとデモ）

サーバーにつながっていないと、3 秒後にデモ（three.js 版と同じ台本）が自動で流れる。キーを押すとデモは止まる。

| キー | イベント |
| --- | --- |
| K | 包丁を振り下ろす（swing） |
| C | 切る（cut。ピースが 1 個増える） |
| A | まな板のピースをフライパンに入れる（add） |
| S / P | 塩 / こしょうを振る（season。0.5 g ずつ） |
| 0〜3 | 火の強さ（fire） |
| Enter | 提供（serve。テスト用に 80 点） |
| R | リセット（サーバーにつながっていれば、サーバーに頼む） |
| D | デモを始める / 止める |
| F1 | 左上の「届いたイベント」の表示を出す / 消す |

届いたイベントを Console にも出したいときは、`ServerEvents` の **Event Router** の `Log To Console` をオンにする（今はオン。包丁の位置が多くて見にくければオフにする）。

### 演出を足すとき

演出のスクリプトは `EventRouter` の `OnCut` などに登録して待つ。サーバーからでも、キーやデモからでも同じように届く。

```csharp
router.OnCut += e => Debug.Log($"ピースが {e.Count} 個になった");
```

## キッチンの画面

- メニュー「**Omamagoto → キッチンを作り直す**」で、シーンの `Kitchen` を作り直す（中は全部消えて作り直される）。作り直したらシーンを保存する
- 配置は現実のキッチン台と同じ（`Assets/Scripts/Kitchen/KitchenLayout.cs`。数字は `tools/kitchen_plan.py` の `P` と同じ。`P` を変えたら、ここも直して作り直す）
- 座標：Unity の 1 = 1 m。台の左手前・天板の上面が原点。x は右、y は上、z は奥
- カメラ・光・空（HDRI）・影の距離も、作り直すときに決まる（`Assets/Scripts/Editor/KitchenSceneBuilder.cs`）
- 包丁とにんじんは、再生したときにスクリプトで形を作る（`KnifeView`・`CarrotView`）。にんじんは最初の `cut` で一気に輪切り（18 枚＋葉の付いた端）になって、ドミノのように倒れる
- 素材（テクスチャ・HDRI）は CC0 だけ。出どころは `Assets/CREDITS.md`

## スクリプトの場所

| ファイル | 役目 |
| --- | --- |
| `Assets/Scripts/Events/ScreenEvent.cs` | 1 件のイベントの形（要件定義書 6.4） |
| `Assets/Scripts/Events/EventRouter.cs` | 種類ごとに振り分ける。同じ `seq` 以下は捨てる |
| `Assets/Scripts/Network/ServerConnection.cs` | WebSocket でサーバーにつなぐ・つなぎ直す・リセットを頼む |
| `Assets/Scripts/Testing/LocalEventSender.cs` | サーバーと同じ形のイベントを作る（キーとデモで使う） |
| `Assets/Scripts/Testing/KeyboardEventSender.cs` | キーでイベントを出す |
| `Assets/Scripts/Testing/DemoPlayer.cs` | サーバーがないときのデモ |
| `Assets/Scripts/Testing/StatusOverlay.cs` | 左上の表示 |
| `Assets/Scripts/Kitchen/KitchenLayout.cs` | 現実のキッチン台の寸法と、まな板の座標 → Unity の位置 |
| `Assets/Scripts/Kitchen/KnifeView.cs` | 包丁（位置・振り下ろし・消える） |
| `Assets/Scripts/Kitchen/CarrotView.cs` | にんじん（位置・切る・戻す） |
| `Assets/Scripts/Kitchen/Shapes/` | 形を作る道具（ろくろの形・板の形・にんじん） |
| `Assets/Scripts/Editor/KitchenSceneBuilder.cs` | メニューの「キッチンを作り直す」 |
| `Assets/Scripts/Editor/KitchenMaterials.cs` | キッチンのマテリアル（色・つや）。色を変えたいときはここ |

## テスト

Unity のメニュー「Window → General → Test Runner」で流せる。

- **EditMode**（21 個）：JSON の読み方、振り分け、`seq` の重複、作るイベントの形、形（面の向き・にんじんの大きさ）、キッチンの寸法の計算
- **PlayMode**（29 個）：キー（Input System でキーを押したことにする）、デモ（20 倍速）、サーバーとの接続（テストの中ににせサーバーを立てる。`Assets/Tests/PlayMode/FakeScreenServer.cs`）、包丁、にんじん（切る・倒れる・戻す）

スクリプトを変えたら、両方流して全部成功するか確かめる。

MCP for Unity からテストを流すと、Project Settings の Enter Play Mode Options が変わることがある。流したあとは `git diff unity/ProjectSettings` で確かめ、変わっていたら戻す。
