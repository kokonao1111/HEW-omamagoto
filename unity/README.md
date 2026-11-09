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
| MCP for Unity（`com.coplaydev.unity-mcp` v10.3.0） | Claude Code が Unity のエディターを操作するため。エディターの中だけで動き、ビルドした実行ファイルには入らない。使わない人は気にしなくてよい |

## 動かし方

（これから書く：サーバーとのつなぎ方、キーで試す方法、2 画面の出し方）
