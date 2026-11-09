using Omamagoto.Kitchen;
using UnityEngine;
using static Omamagoto.Editor.KitchenBuildTools;

namespace Omamagoto.Editor
{
    /// <summary>家電：IH コンロ（2 口）と電子レンジ</summary>
    public static class KitchenApplianceBuilder
    {
        public static void Build(Transform parent)
        {
            BuildCooktop(parent);
            BuildMicrowave(parent);
        }

        /// <summary>IH コンロ：黒いガラスの板に、加熱する所の丸い線と、操作の印</summary>
        static void BuildCooktop(Transform parent)
        {
            float x = KitchenLayout.StoveX, y = KitchenLayout.StoveY;
            float w = KitchenLayout.StoveWidth, d = KitchenLayout.StoveDepth, top = KitchenLayout.StoveTopZ;
            var cooktop = Group(parent, "Cooktop");

            RoundedBox(cooktop, "CooktopGlass", new Vector3(x + w / 2, y + d / 2, top / 2), new Vector3(w, d, top), 3,
                KitchenMaterials.BlackGlass, collider: true);

            // 加熱する所の丸い線（ガラスの上に印刷された線のつもり）
            float lineZ = top + 0.15f;
            var zoneRing = new[] { Part(Mm(96, lineZ), Mm(93, lineZ)) };
            foreach (bool right in new[] { false, true })
            {
                Vector3 center = BurnerCenterMm(right);
                Lathe(cooktop, right ? "ZoneRight" : "ZoneLeft", zoneRing, new Vector3(center.x, center.y, 0), KitchenMaterials.CooktopPrint,
                    meshName: "CooktopZoneRing");
            }

            // 手前の操作の印（小さな丸を 5 つ）
            var button = new[] { Part(Mm(6, lineZ), Mm(4.5f, lineZ)) };
            for (int i = 0; i < 5; i++)
            {
                Lathe(cooktop, $"TouchKey{i + 1}", button, new Vector3(x + 120 + i * 40, y + 25, 0), KitchenMaterials.CooktopPrint,
                    meshName: "CooktopTouchKey");
            }
        }

        /// <summary>電子レンジ：白い本体、黒いガラスの扉、ステンレスの取っ手、操作パネル（時計・ダイヤル・ボタン）</summary>
        static void BuildMicrowave(Transform parent)
        {
            float x = KitchenLayout.MicrowaveX, y = KitchenLayout.MicrowaveY;
            float w = KitchenLayout.MicrowaveWidth, d = KitchenLayout.MicrowaveDepth, h = KitchenLayout.MicrowaveHeight;
            var microwave = Group(parent, "Microwave");

            RoundedBox(microwave, "MicrowaveBody", new Vector3(x + w / 2, y + d / 2, h / 2), new Vector3(w, d, h), 10,
                KitchenMaterials.ApplianceWhite);

            // 扉（手前の面より 4 mm 出す）と、中の見える窓（少し暗い）
            float doorWidth = 192, doorLeft = x + 10;
            RoundedBox(microwave, "MicrowaveDoor", new Vector3(doorLeft + doorWidth / 2, y - 2, h / 2), new Vector3(doorWidth, 6, h - 22), 5,
                KitchenMaterials.BlackGlass);
            RoundedBox(microwave, "MicrowaveDoorHandle", new Vector3(doorLeft + doorWidth - 14, y - 14, h / 2), new Vector3(12, 14, h - 90), 5,
                KitchenMaterials.Stainless);

            // 操作パネル
            float panelLeft = doorLeft + doorWidth + 6;
            float panelWidth = x + w - 8 - panelLeft;
            float panelCenterX = panelLeft + panelWidth / 2;
            RoundedBox(microwave, "MicrowavePanel", new Vector3(panelCenterX, y - 1, h / 2), new Vector3(panelWidth, 4, h - 22), 4,
                KitchenMaterials.BlackGlass);
            RoundedBox(microwave, "MicrowaveDisplay", new Vector3(panelCenterX, y - 3.2f, h - 45), new Vector3(panelWidth - 22, 1, 14), 1,
                KitchenMaterials.DisplayGlow);
            for (int i = 0; i < 2; i++)
            {
                RoundedBox(microwave, $"MicrowaveButton{i + 1}", new Vector3(panelCenterX, y - 4, h - 75 - i * 18), new Vector3(panelWidth - 26, 4, 10), 2,
                    KitchenMaterials.ButtonGrey);
            }

            // ダイヤル：ろくろの形を、手前（-Y の向き）に向ける
            var knobParts = new[]
            {
                Part(Mm(0, 0), Mm(17, 0)),
                Part(Mm(17, 0), Mm(17, 12), Mm(15.5f, 15)),
                Part(Mm(15.5f, 15), Mm(0, 15)),
            };
            var knob = Lathe(microwave, "MicrowaveKnob", knobParts, new Vector3(panelCenterX, y - 3, 70), KitchenMaterials.Stainless);
            knob.rotation = Quaternion.Euler(-90, 0, 0);
        }

        /// <summary>バーナー（加熱する所）の真ん中。現実の座標（mm）</summary>
        static Vector3 BurnerCenterMm(bool right)
        {
            float x = KitchenLayout.StoveX + (right ? KitchenLayout.RightBurnerX : KitchenLayout.LeftBurnerX);
            return new Vector3(x, KitchenLayout.StoveY + KitchenLayout.StoveDepth / 2, KitchenLayout.StoveTopZ);
        }
    }
}
