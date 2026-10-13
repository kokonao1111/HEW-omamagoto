//! サーバーの本体：鍋の状態（要件定義書 5.9）
//! デバイスからの 1 件を受けて状態を変え、Unity に出すイベントを返す。非同期は使わない。
//! 今あるのは調味料と火の強さ。切った野菜・フライパンの中身・工程・採点・リセットは次の段階で足す。

use std::collections::HashMap;

use crate::config::{Config, Role};
use crate::event::{DeviceEvent, DeviceMessage, ServerEvent};

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
}

impl Kitchen {
    pub fn new(config: Config) -> Self {
        Self { config, last: HashMap::new(), seasoning: HashMap::new(), fire: 0, battery: HashMap::new() }
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

    #[test]
    fn keeps_battery_per_device() {
        let mut k = kitchen();
        k.handle(&msg(SALT, 100, 1, DeviceEvent::Battery { percent: 63 }));
        assert_eq!(k.battery()[SALT], 63);
    }
}
