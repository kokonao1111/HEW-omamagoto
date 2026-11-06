//! イベントの型（要件定義書 6.1〜6.4）
//! デバイスから届く JSON を DeviceMessage に、カメラから届く JSON を CameraMessage に、サーバーが出すものを ServerEvent にする。

use serde::{Deserialize, Serialize};

/// デバイスから届く 1 件（6.1 の共通ヘッダ + 6.2 の中身）
#[derive(Debug, Clone, PartialEq, Deserialize)]
pub struct DeviceMessage {
    /// MAC アドレス
    pub device: String,
    /// デバイスの起動からのミリ秒
    pub ts: u64,
    /// デバイスごとの連番。デバイスが再起動すると 1 から数え直す
    pub seq: u64,
    #[serde(flatten)]
    pub event: DeviceEvent,
}

#[derive(Debug, Clone, PartialEq, Deserialize)]
#[serde(tag = "event", rename_all = "snake_case")]
pub enum DeviceEvent {
    Hello { board: i32, imu: bool },
    /// 取れないときは負の値が来ることがある
    Battery { percent: i32 },
    Shake { intensity: f64, tilt: i32 },
    /// level は 0=切・1=弱・2=中・3=強
    Knob { level: u8, angle: i32 },
    Tilt { tilt: i32 },
    Switch { name: String, on: bool },
    /// 知らない種類（新しいファームが足したもの など）。読めないことで止まらないように受け流す
    #[serde(other)]
    Unknown,
}

/// カメラ（Python）から届く 1 件（6.1 の共通ヘッダ + 6.3 の中身）
#[derive(Debug, Clone, PartialEq, Deserialize)]
pub struct CameraMessage {
    /// UNIX 時刻（ミリ秒）
    pub ts: i64,
    /// カメラのプログラムが起動してからの連番。起動し直すと 1 から
    pub seq: u64,
    #[serde(flatten)]
    pub event: CameraEvent,
}

#[derive(Debug, Clone, PartialEq, Deserialize)]
#[serde(tag = "event", rename_all = "snake_case")]
pub enum CameraEvent {
    /// 物の位置（まな板の範囲で 0〜1）。包丁は tip・grip、それ以外は x・y・rot。見失ったら visible: false だけ
    Position {
        item: String,
        tip: Option<[f64; 2]>,
        grip: Option<[f64; 2]>,
        x: Option<f64>,
        y: Option<f64>,
        rot: Option<f64>,
        #[serde(default = "yes")]
        visible: bool,
    },
    /// 切った。count は手がどいたときに確定したピース数
    Cut { item: String, count: u32 },
    /// ゾーン（pan・plate）に入った。count は増えた数
    Add { item: String, zone: String, count: u32 },
    /// 包丁を振り下ろした（演出用）
    Swing { item: String },
    /// 手が映っているか
    Hand { present: bool },
    #[serde(other)]
    Unknown,
}

fn yes() -> bool {
    true
}

/// サーバーが Unity に出すもの（6.4）
#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(tag = "event", rename_all = "snake_case")]
pub enum ServerEvent {
    /// 物の位置。カメラから来たものをそのまま流す（無い項目は出さない）
    Position {
        item: String,
        #[serde(skip_serializing_if = "Option::is_none")]
        tip: Option<[f64; 2]>,
        #[serde(skip_serializing_if = "Option::is_none")]
        grip: Option<[f64; 2]>,
        #[serde(skip_serializing_if = "Option::is_none")]
        x: Option<f64>,
        #[serde(skip_serializing_if = "Option::is_none")]
        y: Option<f64>,
        #[serde(skip_serializing_if = "Option::is_none")]
        rot: Option<f64>,
        visible: bool,
    },
    /// 切った（count は今のピース数）
    Cut { item: String, count: u32 },
    /// ゾーンに入った（count は増えた数）
    Add { item: String, zone: String, count: u32 },
    /// 包丁を振り下ろした
    Swing { item: String },
    /// 調味料が入った。item は役割の名前（salt・pepper）
    Season { item: String, amount_g: f64, total_g: f64 },
    /// 火の強さが変わった
    Fire { level: u8 },
    /// 最初の状態に戻した
    Reset,
}

/// 6.1 の共通ヘッダを付けたもの。この形でログに書き、Unity に送る
#[derive(Debug, Serialize)]
pub struct Stamped<'a> {
    pub device: &'static str,
    /// UNIX 時刻（ミリ秒）
    pub ts: i64,
    pub seq: u64,
    #[serde(flatten)]
    pub event: &'a ServerEvent,
}

#[cfg(test)]
mod tests {
    use super::*;

    fn parse(json: &str) -> DeviceMessage {
        serde_json::from_str(json).unwrap()
    }

    #[test]
    fn parses_requirements_examples() {
        // 要件定義書 6.2 の例と、今のファームが出す形
        let m = parse(r#"{ "device": "AA:BB:CC:DD:EE:FF", "ts": 120340, "seq": 41, "event": "shake", "intensity": 0.8, "tilt": 72 }"#);
        assert_eq!(m.device, "AA:BB:CC:DD:EE:FF");
        assert_eq!((m.ts, m.seq), (120340, 41));
        assert_eq!(m.event, DeviceEvent::Shake { intensity: 0.8, tilt: 72 });

        let m = parse(r#"{"event":"knob","level":2,"angle":82,"device":"AC:27:6E:D3:5C:3C","ts":20410,"seq":4}"#);
        assert_eq!(m.event, DeviceEvent::Knob { level: 2, angle: 82 });

        let m = parse(r#"{"event":"battery","percent":63,"device":"x","ts":1,"seq":2}"#);
        assert_eq!(m.event, DeviceEvent::Battery { percent: 63 });

        let m = parse(r#"{"event":"hello","board":26,"imu":true,"device":"x","ts":765,"seq":1}"#);
        assert_eq!(m.event, DeviceEvent::Hello { board: 26, imu: true });

        let m = parse(r#"{ "device": "x", "ts": 8000, "seq": 9, "event": "switch", "name": "door", "on": true }"#);
        assert_eq!(m.event, DeviceEvent::Switch { name: "door".into(), on: true });
    }

    #[test]
    fn unknown_event_does_not_fail() {
        let m = parse(r#"{"event":"sparkle","color":"red","device":"x","ts":1,"seq":1}"#);
        assert_eq!(m.event, DeviceEvent::Unknown);
    }

    #[test]
    fn missing_header_fails() {
        assert!(serde_json::from_str::<DeviceMessage>(r#"{"event":"battery","percent":1}"#).is_err());
    }

    #[test]
    fn parses_camera_examples() {
        // 要件定義書 6.3 の例と、camera/logic.py が出す形
        let cam = |json: &str| serde_json::from_str::<CameraMessage>(json).unwrap();
        let m = cam(r#"{ "device": "camera", "ts": 1760000000123, "seq": 5001, "event": "position", "item": "knife", "tip": [0.31, 0.12], "grip": [0.55, 0.05], "visible": true, "cam": 0 }"#);
        assert_eq!((m.ts, m.seq), (1760000000123, 5001));
        assert_eq!(
            m.event,
            CameraEvent::Position { item: "knife".into(), tip: Some([0.31, 0.12]), grip: Some([0.55, 0.05]), x: None, y: None, rot: None, visible: true }
        );
        let m = cam(r#"{ "device": "camera", "ts": 1, "seq": 2, "event": "position", "item": "carrot", "x": 0.42, "y": 0.50, "rot": 15, "visible": true }"#);
        assert_eq!(
            m.event,
            CameraEvent::Position { item: "carrot".into(), tip: None, grip: None, x: Some(0.42), y: Some(0.5), rot: Some(15.0), visible: true }
        );
        let m = cam(r#"{ "device": "camera", "ts": 1, "seq": 3, "event": "position", "item": "knife", "visible": false }"#);
        assert!(matches!(m.event, CameraEvent::Position { visible: false, tip: None, .. }));
        let m = cam(r#"{ "device": "camera", "ts": 1, "seq": 4, "event": "cut", "item": "carrot", "count": 2 }"#);
        assert_eq!(m.event, CameraEvent::Cut { item: "carrot".into(), count: 2 });
        let m = cam(r#"{ "device": "camera", "ts": 1, "seq": 5, "event": "add", "item": "carrot", "zone": "pan", "count": 1 }"#);
        assert_eq!(m.event, CameraEvent::Add { item: "carrot".into(), zone: "pan".into(), count: 1 });
        let m = cam(r#"{ "device": "camera", "ts": 1, "seq": 6, "event": "swing", "item": "knife" }"#);
        assert_eq!(m.event, CameraEvent::Swing { item: "knife".into() });
        let m = cam(r#"{ "device": "camera", "ts": 1, "seq": 7, "event": "hand", "present": false }"#);
        assert_eq!(m.event, CameraEvent::Hand { present: false });
        let m = cam(r#"{ "device": "camera", "ts": 1, "seq": 8, "event": "sparkle" }"#);
        assert_eq!(m.event, CameraEvent::Unknown);
    }

    #[test]
    fn stamped_events_match_requirements() {
        // 要件定義書 6.4 の例と同じ中身になる
        let check = |ev: ServerEvent, want: &str| {
            let v = serde_json::to_value(Stamped { device: "server", ts: 1, seq: 9, event: &ev }).unwrap();
            let mut want: serde_json::Value = serde_json::from_str(want).unwrap();
            want["device"] = "server".into();
            want["ts"] = 1.into();
            want["seq"] = 9.into();
            assert_eq!(v, want);
        };
        check(
            ServerEvent::Position { item: "knife".into(), tip: Some([0.31, 0.12]), grip: Some([0.55, 0.05]), x: None, y: None, rot: None, visible: true },
            r#"{ "event": "position", "item": "knife", "tip": [0.31, 0.12], "grip": [0.55, 0.05], "visible": true }"#,
        );
        check(
            ServerEvent::Position { item: "knife".into(), tip: None, grip: None, x: None, y: None, rot: None, visible: false },
            r#"{ "event": "position", "item": "knife", "visible": false }"#,
        );
        check(ServerEvent::Cut { item: "carrot".into(), count: 2 }, r#"{ "event": "cut", "item": "carrot", "count": 2 }"#);
        check(
            ServerEvent::Add { item: "carrot".into(), zone: "pan".into(), count: 1 },
            r#"{ "event": "add", "item": "carrot", "zone": "pan", "count": 1 }"#,
        );
        check(ServerEvent::Swing { item: "knife".into() }, r#"{ "event": "swing", "item": "knife" }"#);
        check(ServerEvent::Fire { level: 2 }, r#"{ "event": "fire", "level": 2 }"#);
        check(ServerEvent::Reset, r#"{ "event": "reset" }"#);
    }

    #[test]
    fn stamped_server_event_matches_requirements() {
        let ev = ServerEvent::Season { item: "salt".into(), amount_g: 0.5, total_g: 3.2 };
        let v = serde_json::to_value(Stamped { device: "server", ts: 1760000005000, seq: 903, event: &ev }).unwrap();
        // 要件定義書 6.4 の例と同じ中身
        let want: serde_json::Value = serde_json::from_str(
            r#"{ "device": "server", "ts": 1760000005000, "seq": 903, "event": "season", "item": "salt", "amount_g": 0.5, "total_g": 3.2 }"#,
        )
        .unwrap();
        assert_eq!(v, want);
    }
}
