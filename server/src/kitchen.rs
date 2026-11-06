//! サーバーの本体：鍋の状態（要件定義書 5.9）
//! デバイス・カメラからの 1 件を受けて状態を変え、Unity に出すイベントを返す。非同期は使わない。
//! 今あるのは調味料・火の強さ・切ったピース数・ゾーンに入った数・リセット。焼き加減・工程・採点は次の段階で足す。

use std::collections::HashMap;

use crate::config::{Config, Role};
use crate::event::{CameraEvent, CameraMessage, DeviceEvent, DeviceMessage, ServerEvent};

/// 1 件を処理した結果
#[derive(Debug, PartialEq)]
pub enum Handled {
    /// 受け付けた。Unity に出すイベント（無いこともある）
    Accepted(Vec<ServerEvent>),
    /// 同じものが 2 回届いた
    Duplicate,
    /// 設定ファイルに無いデバイス。設定に MAC アドレスを足す
    UnknownDevice,
    /// 役割に合わないイベント（塩のデバイスから来た knob など）
    NotForRole(Role),
}

pub struct Kitchen {
    config: Config,
    /// デバイスごとに、最後に受け付けた (ts, seq)
    last: HashMap<String, (u64, u64)>,
    /// 調味料ごとの合計グラム数
    seasoning: HashMap<Role, f64>,
    /// 火の強さ（0=切・1=弱・2=中・3=強）
    fire: u8,
    /// デバイスごとのバッテリー残量（%）。運営用画面で使う
    battery: HashMap<String, i32>,
    /// カメラのつながり（ws の id）ごとに、最後に受け付けた seq
    camera_last: HashMap<u64, u64>,
    /// 野菜ごとの、まな板の上のピース数（最後の cut）
    pieces: HashMap<String, u32>,
    /// (ゾーン, 野菜) ごとに入った数の合計
    zones: HashMap<(String, String), u32>,
}

impl Kitchen {
    pub fn new(config: Config) -> Self {
        Self {
            config,
            last: HashMap::new(),
            seasoning: HashMap::new(),
            fire: 0,
            battery: HashMap::new(),
            camera_last: HashMap::new(),
            pieces: HashMap::new(),
            zones: HashMap::new(),
        }
    }

    pub fn handle(&mut self, msg: &DeviceMessage) -> Handled {
        let Some(role) = self.config.role_of(&msg.device) else {
            return Handled::UnknownDevice;
        };
        if self.is_duplicate(msg) {
            return Handled::Duplicate;
        }
        self.last.insert(msg.device.to_uppercase(), (msg.ts, msg.seq));

        match (&msg.event, role) {
            (DeviceEvent::Hello { .. } | DeviceEvent::Unknown, _) => Handled::Accepted(vec![]),
            (DeviceEvent::Battery { percent }, _) => {
                self.battery.insert(msg.device.to_uppercase(), *percent);
                Handled::Accepted(vec![])
            }
            (DeviceEvent::Shake { intensity, .. }, Role::Salt | Role::Pepper) => {
                let per_shake = self.config.season.grams_per_shake.get(&role).copied().unwrap_or(0.0);
                let amount = per_shake * intensity.clamp(0.0, 1.0);
                let total = self.seasoning.entry(role).or_insert(0.0);
                *total += amount;
                Handled::Accepted(vec![ServerEvent::Season {
                    item: role.name().to_string(),
                    amount_g: round2(amount),
                    total_g: round2(*total),
                }])
            }
            (DeviceEvent::Knob { level, .. }, Role::Knob) => {
                // デバイスは段階が変わったときだけ送るが、つなぎ直したあとなどに同じ段階が来ても出さない
                if *level == self.fire {
                    return Handled::Accepted(vec![]);
                }
                self.fire = *level;
                Handled::Accepted(vec![ServerEvent::Fire { level: *level }])
            }
            _ => Handled::NotForRole(role),
        }
    }

    /// カメラ（Python）からの 1 件。位置・切った・入れた・振り下ろしを Unity に流す。
    /// source はつながりの番号。カメラのプログラムが複数でも、起動し直しても、つながりごとに seq を見る
    pub fn handle_camera(&mut self, source: u64, msg: &CameraMessage) -> Handled {
        if self.camera_last.get(&source).is_some_and(|&seq| msg.seq <= seq) {
            return Handled::Duplicate;
        }
        self.camera_last.insert(source, msg.seq);

        let ev = match &msg.event {
            CameraEvent::Position { item, tip, grip, x, y, rot, visible } => ServerEvent::Position {
                item: item.clone(),
                tip: *tip,
                grip: *grip,
                x: *x,
                y: *y,
                rot: *rot,
                visible: *visible,
            },
            CameraEvent::Cut { item, count } => {
                self.pieces.insert(item.clone(), *count);
                ServerEvent::Cut { item: item.clone(), count: *count }
            }
            CameraEvent::Add { item, zone, count } => {
                *self.zones.entry((zone.clone(), item.clone())).or_insert(0) += count;
                ServerEvent::Add { item: item.clone(), zone: zone.clone(), count: *count }
            }
            CameraEvent::Swing { item } => ServerEvent::Swing { item: item.clone() },
            // 手の有無は、あとで無操作のリセットに使う。Unity には出さない
            CameraEvent::Hand { .. } | CameraEvent::Unknown => return Handled::Accepted(vec![]),
        };
        Handled::Accepted(vec![ev])
    }

    /// 来場者が入れ替わったとき。調味料・ピース数・ゾーンを空にする。
    /// 火はつまみの実際の位置なので戻さない。ついていたら、画面に今の火をもう一度伝える
    pub fn reset(&mut self) -> Vec<ServerEvent> {
        self.seasoning.clear();
        self.pieces.clear();
        self.zones.clear();
        let mut out = vec![ServerEvent::Reset];
        out.extend(self.sync());
        out
    }

    /// 画面がつながったとき（起動し直したときなど）に、今の状態に合わせるためのイベント
    pub fn sync(&self) -> Vec<ServerEvent> {
        if self.fire > 0 { vec![ServerEvent::Fire { level: self.fire }] } else { vec![] }
    }

    /// 同じデバイスから同じ起動のうちに、受け付けた連番以下が来たら重複。
    /// デバイスが再起動すると seq は 1 から、ts は 0 から数え直すので、ts が戻っていたら再起動とみなして受け付ける
    fn is_duplicate(&self, msg: &DeviceMessage) -> bool {
        match self.last.get(&msg.device.to_uppercase()) {
            Some(&(ts, seq)) => msg.ts >= ts && msg.seq <= seq,
            None => false,
        }
    }

    pub fn seasoning_g(&self, role: Role) -> f64 {
        self.seasoning.get(&role).copied().unwrap_or(0.0)
    }

    pub fn fire(&self) -> u8 {
        self.fire
    }

    pub fn battery(&self) -> &HashMap<String, i32> {
        &self.battery
    }

    pub fn pieces(&self, item: &str) -> u32 {
        self.pieces.get(item).copied().unwrap_or(0)
    }

    pub fn in_zone(&self, zone: &str, item: &str) -> u32 {
        self.zones.get(&(zone.to_string(), item.to_string())).copied().unwrap_or(0)
    }
}

/// ログと Unity で読みやすいように小数 2 桁にする
fn round2(v: f64) -> f64 {
    (v * 100.0).round() / 100.0
}

#[cfg(test)]
mod tests {
    use super::*;

    const SALT: &str = "AA:BB:CC:DD:EE:01";
    const PEPPER: &str = "AA:BB:CC:DD:EE:02";
    const KNOB: &str = "AA:BB:CC:DD:EE:03";

    fn kitchen() -> Kitchen {
        Kitchen::new(
            Config::parse(&format!(
                "[roles]\n\"{SALT}\" = \"salt\"\n\"{PEPPER}\" = \"pepper\"\n\"{KNOB}\" = \"knob\"\n\
                 [season]\ngrams_per_shake = {{ salt = 0.5, pepper = 0.2 }}\n"
            ))
            .unwrap(),
        )
    }

    fn msg(device: &str, ts: u64, seq: u64, event: DeviceEvent) -> DeviceMessage {
        DeviceMessage { device: device.into(), ts, seq, event }
    }

    fn shake(intensity: f64) -> DeviceEvent {
        DeviceEvent::Shake { intensity, tilt: 70 }
    }

    fn knob(level: u8) -> DeviceEvent {
        DeviceEvent::Knob { level, angle: 0 }
    }

    #[test]
    fn shakes_add_grams_by_intensity() {
        let mut k = kitchen();
        assert_eq!(
            k.handle(&msg(SALT, 100, 1, shake(1.0))),
            Handled::Accepted(vec![ServerEvent::Season { item: "salt".into(), amount_g: 0.5, total_g: 0.5 }])
        );
        // 弱い振り（持ち替えの誤検知など）は少ししか入らない
        assert_eq!(
            k.handle(&msg(SALT, 200, 2, shake(0.1))),
            Handled::Accepted(vec![ServerEvent::Season { item: "salt".into(), amount_g: 0.05, total_g: 0.55 }])
        );
        k.handle(&msg(PEPPER, 300, 1, shake(1.0)));
        assert!((k.seasoning_g(Role::Salt) - 0.55).abs() < 1e-9);
        assert!((k.seasoning_g(Role::Pepper) - 0.2).abs() < 1e-9);
    }

    #[test]
    fn ten_shakes_make_five_grams_of_salt() {
        let mut k = kitchen();
        for i in 1..=10 {
            k.handle(&msg(SALT, i * 300, i, shake(1.0)));
        }
        assert!((k.seasoning_g(Role::Salt) - 5.0).abs() < 1e-9);
    }

    #[test]
    fn knob_changes_fire_only_when_level_changes() {
        let mut k = kitchen();
        assert_eq!(k.handle(&msg(KNOB, 100, 1, knob(2))), Handled::Accepted(vec![ServerEvent::Fire { level: 2 }]));
        assert_eq!(k.handle(&msg(KNOB, 200, 2, knob(2))), Handled::Accepted(vec![]));
        assert_eq!(k.handle(&msg(KNOB, 300, 3, knob(0))), Handled::Accepted(vec![ServerEvent::Fire { level: 0 }]));
        assert_eq!(k.fire(), 0);
    }

    #[test]
    fn ignores_events_that_do_not_fit_the_role() {
        // 振ると knob も出る（2026-10-13 の実機）。塩のデバイスの knob は火を変えない
        let mut k = kitchen();
        assert_eq!(k.handle(&msg(SALT, 100, 1, knob(3))), Handled::NotForRole(Role::Salt));
        assert_eq!(k.handle(&msg(KNOB, 200, 1, shake(1.0))), Handled::NotForRole(Role::Knob));
        assert_eq!(k.fire(), 0);
        assert_eq!(k.seasoning_g(Role::Salt), 0.0);
    }

    #[test]
    fn drops_duplicates() {
        let mut k = kitchen();
        k.handle(&msg(SALT, 100, 5, shake(1.0)));
        assert_eq!(k.handle(&msg(SALT, 100, 5, shake(1.0))), Handled::Duplicate);
        assert_eq!(k.handle(&msg(SALT, 150, 4, shake(1.0))), Handled::Duplicate);
        assert!((k.seasoning_g(Role::Salt) - 0.5).abs() < 1e-9);
    }

    #[test]
    fn accepts_events_after_device_restart() {
        let mut k = kitchen();
        k.handle(&msg(SALT, 90_000, 120, shake(1.0)));
        // 再起動：ts も seq も小さくなる
        assert!(matches!(k.handle(&msg(SALT, 800, 1, shake(1.0))), Handled::Accepted(_)));
        assert!(matches!(k.handle(&msg(SALT, 900, 2, shake(1.0))), Handled::Accepted(_)));
        assert!((k.seasoning_g(Role::Salt) - 1.5).abs() < 1e-9);
    }

    #[test]
    fn unknown_device_is_reported() {
        let mut k = kitchen();
        assert_eq!(k.handle(&msg("11:22:33:44:55:66", 1, 1, shake(1.0))), Handled::UnknownDevice);
    }

    fn cam(ts: i64, seq: u64, event: CameraEvent) -> CameraMessage {
        CameraMessage { ts, seq, event }
    }

    #[test]
    fn camera_events_go_to_unity() {
        let mut k = kitchen();
        let knife = CameraEvent::Position { item: "knife".into(), tip: Some([0.3, 0.1]), grip: Some([0.5, 0.0]), x: None, y: None, rot: None, visible: true };
        assert_eq!(
            k.handle_camera(1, &cam(1000, 1, knife)),
            Handled::Accepted(vec![ServerEvent::Position { item: "knife".into(), tip: Some([0.3, 0.1]), grip: Some([0.5, 0.0]), x: None, y: None, rot: None, visible: true }])
        );
        assert_eq!(
            k.handle_camera(1, &cam(1100, 2, CameraEvent::Cut { item: "carrot".into(), count: 3 })),
            Handled::Accepted(vec![ServerEvent::Cut { item: "carrot".into(), count: 3 }])
        );
        assert_eq!(
            k.handle_camera(1, &cam(1200, 3, CameraEvent::Add { item: "carrot".into(), zone: "pan".into(), count: 2 })),
            Handled::Accepted(vec![ServerEvent::Add { item: "carrot".into(), zone: "pan".into(), count: 2 }])
        );
        k.handle_camera(1, &cam(1300, 4, CameraEvent::Add { item: "carrot".into(), zone: "pan".into(), count: 1 }));
        assert_eq!(k.pieces("carrot"), 3);
        assert_eq!(k.in_zone("pan", "carrot"), 3);
        // 手の有無は画面に出さない
        assert_eq!(k.handle_camera(1, &cam(1400, 5, CameraEvent::Hand { present: true })), Handled::Accepted(vec![]));
    }

    #[test]
    fn camera_duplicates_are_per_connection() {
        let mut k = kitchen();
        let swing = || CameraEvent::Swing { item: "knife".into() };
        k.handle_camera(1, &cam(1000, 10, swing()));
        assert_eq!(k.handle_camera(1, &cam(1000, 10, swing())), Handled::Duplicate);
        // 別のカメラのプログラム（つながりが別）は、seq が小さくても受け付ける
        assert!(matches!(k.handle_camera(2, &cam(1000, 3, swing())), Handled::Accepted(_)));
        assert!(matches!(k.handle_camera(1, &cam(1001, 11, swing())), Handled::Accepted(_)));
        assert!(matches!(k.handle_camera(2, &cam(1001, 4, swing())), Handled::Accepted(_)));
        // 起動し直してつなぎ直した（新しいつながり）：seq は 1 から
        assert!(matches!(k.handle_camera(3, &cam(5000, 1, swing())), Handled::Accepted(_)));
    }

    #[test]
    fn reset_clears_food_but_keeps_fire() {
        let mut k = kitchen();
        k.handle(&msg(SALT, 100, 1, shake(1.0)));
        k.handle(&msg(KNOB, 100, 1, knob(2)));
        k.handle_camera(1, &cam(1000, 1, CameraEvent::Cut { item: "carrot".into(), count: 4 }));
        k.handle_camera(1, &cam(1100, 2, CameraEvent::Add { item: "carrot".into(), zone: "pan".into(), count: 4 }));
        // 火はつまみの位置のままなので、リセットのあとに今の火をもう一度伝える
        assert_eq!(k.reset(), vec![ServerEvent::Reset, ServerEvent::Fire { level: 2 }]);
        assert_eq!(k.seasoning_g(Role::Salt), 0.0);
        assert_eq!(k.pieces("carrot"), 0);
        assert_eq!(k.in_zone("pan", "carrot"), 0);
        assert_eq!(k.fire(), 2);
        // 次の人の塩は 0 から数える
        assert_eq!(
            k.handle(&msg(SALT, 200, 2, shake(1.0))),
            Handled::Accepted(vec![ServerEvent::Season { item: "salt".into(), amount_g: 0.5, total_g: 0.5 }])
        );
        k.handle(&msg(KNOB, 300, 2, knob(0)));
        assert_eq!(k.reset(), vec![ServerEvent::Reset]);
    }

    #[test]
    fn keeps_battery_per_device() {
        let mut k = kitchen();
        k.handle(&msg(SALT, 100, 1, DeviceEvent::Battery { percent: 63 }));
        assert_eq!(k.battery()[SALT], 63);
    }
}
