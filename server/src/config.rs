//! サーバーの設定ファイル（要件定義書 5.9、6.5）
//! MAC アドレス → 役割と、振り 1 回あたりのグラム数を持つ。書き換えて再起動すれば反映される。

use std::collections::HashMap;
use std::path::Path;

use anyhow::{Context, Result};
use serde::Deserialize;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Role {
    Salt,
    Pepper,
    Pan,
    Knob,
    Range,
}

impl Role {
    pub fn name(self) -> &'static str {
        match self {
            Role::Salt => "salt",
            Role::Pepper => "pepper",
            Role::Pan => "pan",
            Role::Knob => "knob",
            Role::Range => "range",
        }
    }
}

#[derive(Debug, Clone, Deserialize)]
pub struct Config {
    /// MAC アドレス → 役割。読み込むときに大文字にそろえる
    pub roles: HashMap<String, Role>,
    #[serde(default)]
    pub season: SeasonConfig,
}

#[derive(Debug, Clone, Default, Deserialize)]
pub struct SeasonConfig {
    /// 強さ 1 で振ったときに入るグラム数。強さが小さければその分少なくなる
    #[serde(default)]
    pub grams_per_shake: HashMap<Role, f64>,
}

impl Config {
    pub fn load(path: &Path) -> Result<Self> {
        let text = std::fs::read_to_string(path)
            .with_context(|| format!("設定ファイル {} が読めない", path.display()))?;
        Self::parse(&text).with_context(|| format!("設定ファイル {} の書き方が違う", path.display()))
    }

    pub fn parse(text: &str) -> Result<Self> {
        let mut config: Config = toml::from_str(text)?;
        config.roles = config.roles.into_iter().map(|(mac, role)| (mac.to_uppercase(), role)).collect();
        Ok(config)
    }

    pub fn role_of(&self, device: &str) -> Option<Role> {
        self.roles.get(&device.to_uppercase()).copied()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_requirements_example() {
        // 要件定義書 6.5 の例（[reset] はまだ使わないが、あっても読める）
        let c = Config::parse(
            r#"
[roles]
"AA:BB:CC:DD:EE:FF" = "salt"
"AA:BB:CC:DD:EE:00" = "pepper"
"AA:BB:CC:DD:EE:11" = "pan"
"11:22:33:44:55:66" = "knob"
"77:88:99:AA:BB:CC" = "range"

[season]
grams_per_shake = { salt = 0.5, pepper = 0.2 }

[reset]
idle_seconds = 90
"#,
        )
        .unwrap();
        assert_eq!(c.role_of("AA:BB:CC:DD:EE:FF"), Some(Role::Salt));
        assert_eq!(c.role_of("77:88:99:AA:BB:CC"), Some(Role::Range));
        assert_eq!(c.season.grams_per_shake[&Role::Pepper], 0.2);
    }

    #[test]
    fn mac_case_does_not_matter() {
        let c = Config::parse("[roles]\n\"aa:bb:cc:dd:ee:ff\" = \"knob\"\n").unwrap();
        assert_eq!(c.role_of("AA:BB:CC:DD:EE:FF"), Some(Role::Knob));
        assert_eq!(c.role_of("aa:bb:cc:dd:ee:ff"), Some(Role::Knob));
    }

    #[test]
    fn unknown_role_is_an_error() {
        assert!(Config::parse("[roles]\n\"AA:BB:CC:DD:EE:FF\" = \"sugar\"\n").is_err());
    }
}
