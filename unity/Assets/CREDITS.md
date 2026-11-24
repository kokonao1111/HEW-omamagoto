# 素材の出どころ

使ってよいライセンス（CC0 など）の素材だけを使う。足したら、ここに 1 行書く。

| ファイル | 素材の名前 | 作った人 | 出どころ | ライセンス |
| --- | --- | --- | --- | --- |
| `Art/HDRI/lebombo_2k.hdr` | Lebombo | Greg Zaal | https://polyhaven.com/a/lebombo | CC0 |
| `Art/Textures/oak_veneer_01/` | Oak Veneer 01 | Jenelle van Heerden | https://polyhaven.com/a/oak_veneer_01 | CC0 |
| `Art/Textures/tiles133b/` | Tiles 133 B | ambientCG | https://ambientcg.com/a/Tiles133B | CC0 |
| `Art/Textures/generated/` | 白い人工大理石・白いタイル・ステンレスのヘアライン・ガスの炎・湯気・塩こしょうの粒・電子レンジの網目と時計の表示・鋳物のざらざら | このチーム（Python で作った） | — | チームの自作 |
| `Art/Models/Food/`（8 種類・16 本） | 野菜 8 種類（ピーマン・にんじん・ブロッコリー・とうがらし・なす・はくさい・たまねぎ・とうもろこし）の丸ごと版と縦二分割版 | このチーム | — | チームの自作 |

- `oak_veneer_01_mask_1k.png` は、Poly Haven のざらつきの画像（Rough）から作った（なめらかさ = 1 − ざらつき）
- `tiles133b_mask_2k.png` も、ambientCG のざらつきの画像（Roughness）から作った（`unity/tools/rough_to_mask.py`）
- 炎・湯気・粒・網目・時計の表示・鋳物の画像は `unity/tools/make_textures.py` で作り直せる
- キッチンの台・家電・道具・包丁・にんじん（仮）の形は、スクリプトで作っている（`Assets/Scripts/Kitchen/`、`Assets/Scripts/Editor/`）。外の 3D モデルは使っていない
