# Art/Models（3D モデルの置き場所）

外から持ってきた 3D モデル（GLB）を置くフォルダー。人間もほかの PC の AI も、GLB を足すときはここの決まりに従う。

## 置き方

```
Art/Models/
  Food/        食材（にんじん・きゅうり・たまねぎ など）
  Utensils/    道具（包丁・フライパン・皿・調味料の入れ物 など）
  Kitchen/     キッチンの家具・家電
  Characters/  料理を食べるキャラクター
```

- フォルダーは要るときに作る。上に当てはまらなければ、わかりやすい英語の名前のフォルダーを足してよい
- くまのお客さん（`Art/Characters/GuestBear/guest_bear_meshy.glb`）は、このフォルダーができる前に `Art/Characters/` に入った。動かすかはチームで決める（それまではそのまま使ってよい）
- ファイル名は英語の小文字とアンダースコア（例：`carrot.glb`、`frying_pan.glb`）
- 1 つのモデルは 1 つの `.glb` にする（テクスチャを中に入れた形）。`.gltf` と画像に分かれた形は使わない

## モデルの決まり

| 項目 | 決まり |
| --- | --- |
| 形式 | `.glb`（glTF 2.0）。Unity では glTFast（`com.unity.cloud.gltfast`）が読み込む |
| 大きさ | 現実と同じ大きさ。1 = 1 m |
| 向き | 上が +Y、正面が +Z（glTF の決まり）。glTFast が Unity の向きに直して読み込む |
| 原点 | 底面の中心（机や床にそのまま置けるように） |
| テクスチャ | 1 枚 2048 px まで（できれば 1024 px） |
| 軽さ（目安） | 食材・道具は 1 つ数万三角形まで。展示用 PC で 60 fps を保つため。重いモデルは減らしてから入れる |

## 今入っているモデル

### Food/（野菜 8 種類。チームの自作）

野菜ごとにフォルダーを分け、丸ごと版（`whole_…`）と、縦に二つに切った版（`…_halves`）を 1 本ずつ置いた。二分割版は、左右の半分が別の部品（`LeftHalf`・`RightHalf`）になっている。

| フォルダー | 野菜 | 丸ごと版 | 二分割版 |
| --- | --- | --- | --- |
| `green_pepper/` | ピーマン | `whole_green_pepper.glb` | `pepper_halves.glb` |
| `carrot/` | にんじん | `whole_carrot_aligned.glb`（二分割版と向きをそろえた版） | `carrot_halves.glb` |
| `broccoli/` | ブロッコリー | `whole_broccoli.glb` | `broccoli_halves.glb` |
| `chili/` | とうがらし | `whole_chili.glb` | `chili_halves.glb` |
| `eggplant/` | なす | `whole_eggplant.glb` | `eggplant_halves.glb` |
| `napa_cabbage/` | はくさい | `whole_napa_cabbage.glb` | `napa_cabbage_halves.glb` |
| `onion/` | たまねぎ | `whole_onion.glb` | `onion_halves.glb` |
| `corn/` | とうもろこし | `whole_corn.glb` | `corn_halves.glb` |

- **とても重い**。1 本 11 万〜170 万三角形、ファイルは 5〜86 MB（16 本で 554 MB）。上の「軽さ（目安）」を大きくこえている。展示でたくさん並べるときは、軽くした版を作ってから使う（作るかはチームで決める）
- テクスチャは 1 枚 2048 px まで（決まりの中）
- Unity で読み込むと、16 本とも立った向き（長さが +Y）で、現実の大きさになる（高さ 4.7〜24 cm）。丸ごとのブロッコリーと丸ごとのにんじんは、ルートの Transform に縮小・回転がかかっている（メッシュだけを見ると大きさ・向きがちがって見えるが、それで正しい）
- とうもろこしは、原点が底より 9 mm 上にある（少し沈んで見える）
- 透明（たまねぎ）・クリアコート（なす・とうがらし・ピーマン・とうもろこし）の材質は、URP では元のとおりには出ないことがある（まだ見た目は確かめていない）

## 足すときの手順

1. ライセンスを確かめる。使ってよいのは **CC0 か、チームの自作**だけ
2. このフォルダーの下に `.glb` を置く
3. `unity/Assets/CREDITS.md` に 1 行書く（ファイル・名前・作った人・出どころ・ライセンス）
4. Unity で開き、Console に赤いエラーが出ていないこと、Project でモデルの中身（メッシュ・マテリアル）が見えることを確かめる
5. 大きさと向きを確かめる（シーンに置いて、現実の大きさで、正面がカメラを向くか）

## 知っておくこと

- `.glb` は Git LFS に入る（`unity/.gitattributes`）。新しい PC では、最初に 1 回 `git lfs install` が要る。忘れると、モデルのかわりに小さな目印のファイルだけが取れて、Unity で読み込めない
- 読み込みの設定は、Project で `.glb` を選んだときの Inspector（glTFast の Importer）で変えられる
- 今のキッチン・道具・にんじん（仮）の形は、GLB ではなくスクリプトで作っている（`Assets/Scripts/Kitchen/`、`Assets/Scripts/Editor/`、出来た形は `Art/Meshes/`）。それを GLB に置き換えるかは、まだ決めていない
