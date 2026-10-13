//! イベントの型（要件定義書 6.1〜6.4）
//! デバイスから届く JSON を DeviceMessage に、サーバーが出すものを ServerEvent にする。

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

/// サーバーが Unity に出すもの（6.4）
#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(tag = "event", rename_all = "snake_case")]
pub enum ServerEvent {
    /// 調味料が入った。item は役割の名前（salt・pepper）
    Season { item: String, amount_g: f64, total_g: f64 },
    /// 火の強さが変わった
    Fire { level: u8 },
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
