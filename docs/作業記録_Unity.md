# 作業記録（Unity の画面）

Unity を作る PC の Claude が、やったことを書く。新しいものを上に足していく。
ほかの PC の Claude やチームのメンバーは、会話の履歴を見られない。このファイルと `docs/Unity_引き継ぎ.md` だけで、今の状態と次にやることがわかるように書く。

## 書き方

作業の区切りごと（ユーザーにコミットを勧める前）に、下の形で 1 つ足す。

```
### 2026-11-07 Unity のプロジェクトを作った

- やったこと：何を作った・変えたか（ファイルやフォルダーの名前も）
- 決まったこと：ユーザーと決めたこと（Unity のバージョン、描画の方式 など）。要件定義書に関わるものは、要件定義書を直す案もユーザーに出す
- 確かめたこと：動かして確かめたこと（何をして、どうなったか）
- まだ確かめていないこと・困っていること
- 次にやること
```

- 決まったことは、`docs/Unity_引き継ぎ.md` の 8 章「まだ決まっていないこと」からも消す
- サーバーやカメラなど、ほかの担当に頼みたいことがあれば「頼みたいこと」として書く

## 記録

### 2026-11-09 イベントを受け取る・サーバーなしで試す（引き継ぎ 5.2・5.3）

- やったこと：
  - `unity/Assets/Scripts/` にスクリプトを作った（くわしくは `unity/README.md`）
    - `Events/ScreenEvent.cs`（6.4 のイベントの形）、`Events/EventRouter.cs`（種類ごとに振り分け、同じ `seq` 以下は捨てる）
    - `Network/ServerConnection.cs`：`System.Net.WebSockets.ClientWebSocket` でつなぐ（NativeWebSocket は使っていない。引き継ぎ 5.2 でどちらでもよいとなっていた）。つながったら `hello`、切れたら 1 秒〜最大 10 秒おきにつなぎ直す、つなぎ直したら `seq` を忘れる、R キーでサーバーに `{"event":"reset"}`
    - `Testing/`：キー（three.js 版と同じ K・C・A・S・P・0〜3・Enter・R・D、F1 で表示切りかえ）、デモ（three.js 版と同じ台本。サーバーがないと 3 秒後に自動で始まる）、左上の状態表示
  - シーン `SampleScene` に `ServerEvents`（上のコンポーネント全部）を置いた
  - スクリプトを asmdef（`Omamagoto`）にまとめた。テスト（`Assets/Tests/EditMode` 14 個、`Assets/Tests/PlayMode` 17 個）を作った
  - パッケージ：Newtonsoft Json（3.2.2）を足した。Input System のテスト道具を使うため `manifest.json` に `testables` を足した
  - Player Settings の **Run In Background をオン**にした（展示中にほかのウィンドウに触れても画面が止まらないように）
  - `.gitignore` に `unity/.vscode/` と、MCP のスクリーンショット（`Assets/Screenshots/`）を足した
- 決まったこと：テストは Unity の Test Runner で、誰でも流せるようにする。スクリプトを変えたら EditMode・PlayMode の両方を流す
- 確かめたこと：
  - テスト 31 個が全部成功（EditMode 14・PlayMode 17）。わざと「つなぎ直したら seq を忘れる」を壊すと、2 つのテストが失敗することも確かめた（テストがまちがいを見つけられる）
  - Python のにせサーバー（一時フォルダー。リポジトリには入れていない）とつないで：`hello` が届く、`fire`・`cut`・`add`・`season`・`reset` を受け取る、重複と壊れた JSON は捨てる、リセットを送るとサーバーの `reset` が返る、切れたら自動でつなぎ直して `seq` 1 から受け付ける
  - サーバーなしで再生すると、デモが自動で流れる（ユーザーが画面で確かめた）。左上の日本語もきちんと出る
- まだ確かめていないこと・困っていること：
  - 本物の Rust サーバーとはまだつないでいない（この PC に Rust がない。`cargo run -- --no-ble` で試せる）
  - ビルドした実行ファイル（IL2CPP）ではまだ試していない
  - Unity の中の .NET では `HttpListener` の WebSocket が使えない（NotImplemented）。テストのにせサーバーは TCP で自分でハンドシェイクしている
  - MCP for Unity からテストを流すと、Run In Background と Enter Play Mode Options が変わることがあった。流したあとは `git diff unity/ProjectSettings` で確かめる
- 次にやること：5.4 キッチンの画面（まな板・コンロ・皿・電子レンジを現実と同じ配置で置き、包丁・にんじんをイベントで動かす）。切る演出の見せ方をユーザーと決める
- 要件定義書について（ユーザーに案を出した）：10 章の画面のライブラリ「NativeWebSocket」を「ClientWebSocket（.NET 標準）、Newtonsoft Json」にする

### 2026-11-09 Unity のプロジェクトを作った（引き継ぎ 5.1）

- やったこと：
  - この PC に Unity 6000.3.28f1 を入れた（6.5 はほかの個人のプロジェクトで使うので残した）
  - ブランチ `feature/unity-screen` を作った
  - `unity/` に Unity のプロジェクトを作った。テンプレートは「Universal 3D」（URP 17.3.0）。コマンドで作った（`Unity.exe -createProject … -cloneFromTemplate com.unity.template.3d-cross-platform-17.0.14.tgz`）
  - `unity/.gitignore`（Unity の標準）、`unity/.gitattributes`（画像・3D モデル・音・動画・フォントを LFS へ。`unity/` の中だけ）、`unity/README.md`（開き方）を置いた
  - この PC で `git lfs install` をした。`unity/` の `.png` は LFS、`screen/` の `.png` は LFS にならないことを `git check-attr` で確かめた
  - Claude が Unity を操作できるように、MCP for Unity（`com.coplaydev.unity-mcp`）を `unity/Packages/manifest.json` に入れた。チームで同じものが入るように v10.3.0 に固定した。エディターの中だけで動く
- 決まったこと：コミット・プッシュはユーザーがする（ルールどおり）。Claude が Unity を操作するのは MCP for Unity で
- 確かめたこと：`ProjectVersion.txt` が 6000.3.28f1、GraphicsSettings に URP の設定が入っている。プロジェクトを作ったときのコンパイルエラーは 0。エディターを開くと MCP for Unity のサーバーが 8080 番で自動で立ち上がった
- まだ確かめていないこと・困っていること：テンプレートに入っている Unity Version Control（`com.unity.collab-proxy`）は git を使うので要らないが、まだ残している
- 次にやること：5.2 イベントを受け取る（WebSocket）、5.3 キーでイベントを出す
- 頼みたいこと（チーム）：ブランチ `feature/unity-screen` を pull して、Unity Hub で `unity` フォルダーを「追加」すれば開ける（`unity/README.md`）

### 2026-11-09 Unity のバージョンと描画の方式を決めた（引き継ぎ 5.0）

- やったこと：引き継ぎと作業記録を読んだ。Unity の今の状況を調べた。`docs/Unity_引き継ぎ.md` の 3 章に決まったことを足し、8 章から消した
- 決まったこと：
  - Unity は **6.3 LTS（6000.3.28f1）**。この PC に入っていた 6000.5（6.5）はサポートが終わっていたのでやめる。6.6 は LTS ではなく、6.7 が出るとサポートが終わるのでやめる。6.3 LTS は 2027 年 12 月までサポートがあり、HEW まで使える
  - 描画は **URP**。理由：Unity は 2026 年から HDRP には新しい機能を足さず、URP に力を入れる。3DGS のプラグイン（UnityGaussianSplatting）は Unity 6 の URP で動くと書いてある（Render Graph の Compatibility Mode はオフにする）。開発する PC の GPU が GTX 1650（4GB）で、HDRP は重い。展示用 PC の GPU はカメラ 3 台の認識にも使う。ユーザーの希望は「リアルがよいが、よいものが作れる方法で」。リアルさは PBR の素材（CC0）、HDRI の光、ポストプロセス、ライトのベイクで出す
  - 要件定義書を直した（ユーザーの了承済み）：冒頭の更新履歴、10 章の画面の行、12.1 に行を足し、12.2 から消した
- 確かめたこと：`git fetch` して main が origin/main と同じ。この PC に Git LFS 3.7.1 と Unity 6000.5.2f1 が入っている。GPU は NVIDIA GeForce GTX 1650
- まだ確かめていないこと・困っていること：3DGS のプラグインが 6.3 LTS の URP で本当に動くか（実際に入れて確かめる）
- 次にやること：Unity Hub で 6.3 LTS を入れる → 5.1 の準備（ブランチ、`unity/` のプロジェクト、`.gitignore`・`.gitattributes`）
- 頼みたいこと（チーム）：Unity を開く人は全員 **6000.3.28f1** を入れる（Unity Hub の「エディターをインストール」→「正式リリース」→ Unity 6.3 LTS。モジュールは Windows Build Support (IL2CPP)）。6.5 で作ったほかのプロジェクトがあれば、6.5 は消さずに残してよい
