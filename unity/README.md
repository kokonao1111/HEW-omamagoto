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

サーバーにつながっていないと、3 秒後にデモ（three.js 版とほぼ同じ台本。包丁で切るのは 1 回だけ）が自動で流れる。キーを押すとデモは止まる。

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
- 雰囲気は「**明るい家のキッチン**」：白い人工大理石の天板、白いタイル、白い引き出し、ステンレス、ガラスの天板のガスコンロ、窓からの昼の光。天板と壁は、遊ぶ範囲より左右に長くのばしてある
- 本物らしさは「形（角の丸み `RoundedBoxMesh`、曲がった棒 `SweepMesh`、ろくろの形 `LatheMesh`）・素材（つや・凹凸・クリアコート・ガラス）・光（HDRI・映り込み・物のすき間の影 SSAO・やわらかい影）」で出している
- ガスコンロ：受け皿・バーナー（炎口 32 個）・温度センサー（ばねつき）・点火プラグ・鋳物の五徳。フライパンは五徳の上（底の高さ 40 mm＝現実のおもちゃと同じ）
- 電子レンジ：スモークガラスの扉（窓は穴のあいた金属の板）、曲がった棒の取っ手、時計の表示、ボタン、ギザギザのダイヤル、通気の穴、ゴムの足
- カメラ・光・空（HDRI）・影・SSAO・MSAA・色の仕上げも、作り直すときに決まる（`Assets/Scripts/Editor/KitchenLightingSetup.cs`）
- 包丁とにんじんは、再生したときにスクリプトで形を作る（`KnifeView`・`CarrotView`）。にんじんは最初の `cut` で一気に輪切り（18 枚＋葉の付いた端）になって、倒れて散らばる
- `add`：まな板のピースがフライパンへ飛ぶ（`zone` が `plate` なら皿へ）。サーバーの数（例：4 個）と画面のピースの数（19 個）はちがうので、「まな板の上の何割が入ったか」で飛ばす数を決める
- `fire`：右のバーナーに青い炎（強さ 0〜3）、手前のランプがオレンジに光る。フライパンがゆっくり温まり、中のピースが焼けて色が変わる（焼け具合 1 で焼けた、2.2 でこげた。`CookablePiece`）。湯気が上がり、ピースがときどき跳ねる
- `season`：塩・こしょうの入れ物がフライパンの上へ飛んで、さかさまにして振る。粒が落ちて、物に当たって止まる。続けて届くと長く振る
- `serve`：フライパンのピースが皿に移る
- 光る物（時計の表示・ランプ・電子レンジの庫内）は URP の Unlit で作る。Lit の Emission は、読み込み直すとスイッチ（`_EMISSION`）が切れてしまう
- 映り込み（`KitchenReflectionProbe`）は `Kitchen` の外に置いて、作り直しても使い回す。作り直すたびに消すと、描画がエラーになって画面が真っ白になる
- **野菜は、チームで作った 3D モデルに入れかえる予定**。今のにんじんは仮
- 映り込み（Reflection Probe）は再生したときに 1 回写すので、再生していないときのシーンでは、ステンレスやガラスの映り込みが少し少ない
- 素材（テクスチャ・HDRI）は CC0 か自作だけ。出どころは `Assets/CREDITS.md`。自作の画像の一部は `unity/tools/make_textures.py` で作る

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
| `Assets/Scripts/Kitchen/CarrotView.cs` | にんじん（位置・切る・フライパンや皿へ入れる・戻す） |
| `Assets/Scripts/Kitchen/PanView.cs` | フライパンと皿（ピースを受け取る・焼く・湯気・跳ねる・皿へ移す） |
| `Assets/Scripts/Kitchen/GasBurnerView.cs` | ガスコンロの右のバーナー（炎・炎の光・ランプ） |
| `Assets/Scripts/Kitchen/ShakerView.cs` | 塩・こしょうの入れ物（飛んでいって振る・粒） |
| `Assets/Scripts/Kitchen/CookablePiece.cs` | 焼ける食材のピース（焼け具合で色とつやが変わる。どの野菜にも付けられる） |
| `Assets/Scripts/Kitchen/Flight.cs` | 物を山なりに飛ばして置く |
| `Assets/Scripts/Kitchen/KitchenParticles.cs` | 湯気と、塩・こしょうの粒（パーティクル）の設定 |
| `Assets/Scripts/Kitchen/GasBurnerShape.cs` | バーナーの寸法（炎口の位置。形と炎の両方で使う） |
| `Assets/Scripts/Kitchen/Shapes/` | 形を作る道具（角の丸い箱・ろくろの形・板の形・曲がった棒・にんじん） |
| `Assets/Scripts/Editor/KitchenSceneBuilder.cs` | メニューの「キッチンを作り直す」（下の道具を順に呼び、動く物にスクリプトを付ける） |
| `Assets/Scripts/Editor/KitchenRoomBuilder.cs` | 天板・引き出し・壁 |
| `Assets/Scripts/Editor/KitchenStoveBuilder.cs` | ガスコンロ |
| `Assets/Scripts/Editor/KitchenMicrowaveBuilder.cs` | 電子レンジ |
| `Assets/Scripts/Editor/KitchenToolBuilder.cs` | まな板・皿・フライパン・塩とこしょうの入れ物 |
| `Assets/Scripts/Editor/KitchenLightingSetup.cs` | カメラ・光・空・映り込み・SSAO・描画の設定・色の仕上げ |
| `Assets/Scripts/Editor/KitchenMaterials.cs` | キッチンのマテリアル（色・つや・テクスチャ）。色を変えたいときはここ |
| `Assets/Scripts/Editor/KitchenBuildTools.cs` | 並べるときの小さな道具（mm で書いて m に直す、曲がった棒、同じ形をまとめる、メッシュの保存 など） |
| `tools/make_textures.py` | 炎・湯気・粒・網目・時計の表示・鋳物の画像を作る（`uv run --with pillow --with numpy python tools/make_textures.py`） |

## テスト

Unity のメニュー「Window → General → Test Runner」で流せる。

- **EditMode**（29 個）：JSON の読み方、振り分け、`seq` の重複、作るイベントの形、形（面の向き・にんじんの大きさ）、キッチンの寸法の計算、焼けたときの色
- **PlayMode**（42 個）：キー（Input System でキーを押したことにする）、デモ（20 倍速）、サーバーとの接続（テストの中ににせサーバーを立てる。`Assets/Tests/PlayMode/FakeScreenServer.cs`）、包丁、にんじん（切る・倒れる・戻す）、焼く流れ（入れる・半分入れる・皿へ・焼けて色が変わる・湯気・提供・リセット）、炎、塩・こしょう

スクリプトを変えたら、両方流して全部成功するか確かめる。

MCP for Unity からテストを流すと、Project Settings の Enter Play Mode Options が変わることがある。流したあとは `git diff unity/ProjectSettings` で確かめ、変わっていたら戻す。
