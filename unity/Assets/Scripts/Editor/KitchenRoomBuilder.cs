using Omamagoto.Kitchen;
using UnityEngine;
using static Omamagoto.Editor.KitchenBuildTools;

namespace Omamagoto.Editor
{
    /// <summary>
    /// 天板・引き出し・壁（明るい家のキッチン）。
    /// 本物の台所のように、天板と壁は遊ぶ範囲（KitchenLayout の幅）より左右に長くのばす。
    /// </summary>
    public static class KitchenRoomBuilder
    {
        const float CounterThickness = 30;  // 天板（人工大理石）の厚さ mm
        const float SideExtension = 700;    // 天板と引き出しを、左右にこれだけのばす mm
        const float WallHeight = 1500;      // 壁は天井の近くまでのばす（カメラから壁の上の端が見えないように）
        const float WallExtension = 2000;   // 壁を、左右にこれだけのばす mm

        public static void Build(Transform parent)
        {
            float w = KitchenLayout.Width, d = KitchenLayout.Depth;
            float left = -SideExtension, right = w + SideExtension;
            float counterWidth = right - left;

            // 天板：白い人工大理石。ふちは 3 mm 丸める
            RoundedBox(parent, "Countertop", new Vector3((left + right) / 2, d / 2, -CounterThickness / 2), new Vector3(counterWidth, d, CounterThickness), 3,
                KitchenMaterials.CounterQuartz, collider: true);

            BuildDrawers(parent, left, counterWidth, d);
            BuildWall(parent, w, d);
        }

        /// <summary>天板の下：白い引き出しを並べ、それぞれにステンレスの取っ手</summary>
        static void BuildDrawers(Transform parent, float left, float counterWidth, float d)
        {
            float bottom = -KitchenLayout.BaseHeight - 300; // 画面の下にはみ出すので、下に長めにしておく
            float top = -CounterThickness;
            float height = top - bottom;
            RoundedBox(parent, "Cabinet", new Vector3(left + counterWidth / 2, d / 2 + 10, bottom + height / 2), new Vector3(counterWidth - 4, d - 20, height), 0,
                KitchenMaterials.CabinetWhite);

            const int drawerCount = 4;
            const float gap = 4;
            float drawerWidth = (counterWidth - gap * (drawerCount + 1)) / drawerCount;
            float drawerHeight = 150;
            for (int i = 0; i < drawerCount; i++)
            {
                float centerX = left + gap + drawerWidth / 2 + i * (drawerWidth + gap);
                float centerZ = top - gap - drawerHeight / 2;
                RoundedBox(parent, $"Drawer{i + 1}", new Vector3(centerX, 10, centerZ), new Vector3(drawerWidth, 18, drawerHeight), 2,
                    KitchenMaterials.CabinetWhite);
                RoundedBox(parent, $"DrawerHandle{i + 1}", new Vector3(centerX, -8, top - 26), new Vector3(240, 12, 10), 4,
                    KitchenMaterials.Stainless);
            }
        }

        /// <summary>奥の壁：天板から 620 mm まで白いタイル、その上はペンキの壁</summary>
        static void BuildWall(Transform parent, float w, float d)
        {
            float tileHeight = KitchenLayout.BackHeight;
            float wallWidth = w + WallExtension * 2;
            RoundedBox(parent, "WallTiles", new Vector3(w / 2, d + 8, tileHeight / 2), new Vector3(wallWidth, 16, tileHeight), 0,
                KitchenMaterials.WallTiles);
            RoundedBox(parent, "WallPaint", new Vector3(w / 2, d + 10, tileHeight + (WallHeight - tileHeight) / 2),
                new Vector3(wallWidth, 20, WallHeight - tileHeight), 0, KitchenMaterials.WallPaint);
        }
    }
}
