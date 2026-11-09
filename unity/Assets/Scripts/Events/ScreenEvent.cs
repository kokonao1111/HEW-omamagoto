using Newtonsoft.Json;

namespace Omamagoto.Events
{
    /// <summary>
    /// サーバーから届く 1 件のイベント（要件定義書 6.4）。
    /// 種類によって使う項目がちがうので、全部の項目を 1 つにまとめている。
    /// 届かなかった項目は null になる（「0」と「届いていない」を見分けるため）。
    /// </summary>
    public class ScreenEvent
    {
        // ---- 共通ヘッダ（6.1） ----

        /// <summary>送ったもの。サーバーは "server"、キーで出したものは "keyboard"、デモは "demo"</summary>
        [JsonProperty("device")] public string Device;

        /// <summary>送った時刻（ミリ秒）</summary>
        [JsonProperty("ts")] public long? Timestamp;

        /// <summary>送ったものごとの連番。同じ番号以下が来たら捨てる</summary>
        [JsonProperty("seq")] public long? Seq;

        /// <summary>種類。"position" "cut" "add" など（EventNames を見る）</summary>
        [JsonProperty("event")] public string Type;

        // ---- 物（position・swing・cut・add・season） ----

        /// <summary>何の物か。"knife" "carrot" "salt" "pepper" など</summary>
        [JsonProperty("item")] public string Item;

        /// <summary>見えているか（position）。false のときは見えなくなった</summary>
        [JsonProperty("visible")] public bool? Visible;

        /// <summary>包丁の刃先の位置 [x, y]（まな板の範囲で 0〜1）</summary>
        [JsonProperty("tip")] public float[] Tip;

        /// <summary>包丁の柄のはしの位置 [x, y]（まな板の範囲で 0〜1）</summary>
        [JsonProperty("grip")] public float[] Grip;

        /// <summary>野菜の位置 x（まな板の範囲で 0〜1。右へ）</summary>
        [JsonProperty("x")] public float? X;

        /// <summary>野菜の位置 y（まな板の範囲で 0〜1。奥へ）</summary>
        [JsonProperty("y")] public float? Y;

        /// <summary>野菜の向き（度）</summary>
        [JsonProperty("rot")] public float? Rotation;

        /// <summary>cut：今のピースの数。add：入ったピースの数（増えた分）</summary>
        [JsonProperty("count")] public int? Count;

        /// <summary>どこに入ったか（add）。"pan" "plate"</summary>
        [JsonProperty("zone")] public string Zone;

        // ---- 塩・こしょう（season） ----

        /// <summary>今回振った量（グラム）</summary>
        [JsonProperty("amount_g")] public float? AmountGrams;

        /// <summary>今までに振った合計（グラム）</summary>
        [JsonProperty("total_g")] public float? TotalGrams;

        // ---- 火（fire） ----

        /// <summary>火の強さ。0=切、1=弱、2=中、3=強</summary>
        [JsonProperty("level")] public int? Level;

        // ---- 提供（serve） ----

        /// <summary>点数（0〜100）</summary>
        [JsonProperty("score")] public int? Score;

        /// <summary>ひとこと。"good" "too_salty" "undercooked" など</summary>
        [JsonProperty("comment")] public string Comment;

        // ---- デバイスの状態（status） ----

        [JsonProperty("devices")] public DeviceStatus[] Devices;

        public override string ToString()
        {
            // デバッグの表示用。null の項目は出さない
            return JsonConvert.SerializeObject(this, Formatting.None,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }
    }

    /// <summary>status で届く、デバイス 1 台の状態</summary>
    public class DeviceStatus
    {
        /// <summary>役割。"salt" "pepper" "pan" "knob" "range"</summary>
        [JsonProperty("role")] public string Role;

        [JsonProperty("connected")] public bool Connected;

        /// <summary>バッテリーの残り（%）</summary>
        [JsonProperty("battery")] public int? Battery;
    }

    /// <summary>イベントの種類の名前（つづりのまちがいを防ぐため、ここにまとめる）</summary>
    public static class EventNames
    {
        public const string Position = "position";
        public const string Swing = "swing";
        public const string Cut = "cut";
        public const string Add = "add";
        public const string Season = "season";
        public const string Fire = "fire";
        public const string Serve = "serve";
        public const string Reset = "reset";
        public const string Status = "status";
    }
}
