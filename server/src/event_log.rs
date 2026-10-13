//! 受け取ったものと出したものをすべてファイルに残す（要件定義書 5.9。展示後の分析とデバッグ用）
//! 1 行 1 JSON。dir は in（デバイスから）・out（Unity へ）・note（接続・切断など）。

use std::fs::{self, File};
use std::io::{BufWriter, Write};
use std::path::{Path, PathBuf};

use anyhow::Result;
use serde_json::{Value, json};

use crate::event::Stamped;

pub struct EventLog {
    path: PathBuf,
    file: BufWriter<File>,
}

impl EventLog {
    /// dir の中に「起動した日時.jsonl」を作る
    pub fn create(dir: &Path) -> Result<Self> {
        fs::create_dir_all(dir)?;
        let path = dir.join(chrono::Local::now().format("%Y%m%d-%H%M%S.jsonl").to_string());
        let file = BufWriter::new(File::create(&path)?);
        Ok(Self { path, file })
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    /// デバイスから届いた行。JSON として読めなければ文字列のまま残す
    pub fn incoming(&mut self, from: &str, line: &str) -> Result<()> {
        let msg = serde_json::from_str::<Value>(line).unwrap_or_else(|_| Value::String(line.to_string()));
        self.write(json!({ "t": now_ms(), "dir": "in", "from": from, "msg": msg }))
    }

    pub fn outgoing(&mut self, ev: &Stamped) -> Result<()> {
        self.write(json!({ "t": now_ms(), "dir": "out", "msg": ev }))
    }

    pub fn note(&mut self, text: &str) -> Result<()> {
        self.write(json!({ "t": now_ms(), "dir": "note", "text": text }))
    }

    fn write(&mut self, v: Value) -> Result<()> {
        serde_json::to_writer(&mut self.file, &v)?;
        self.file.write_all(b"\n")?;
        // 落ちたときに直前まで残るよう、1 行ごとに書き出す
        self.file.flush()?;
        Ok(())
    }
}

fn now_ms() -> i64 {
    chrono::Utc::now().timestamp_millis()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::event::ServerEvent;

    #[test]
    fn writes_one_json_per_line() {
        let dir = std::env::temp_dir().join(format!("omamagoto-log-test-{}", std::process::id()));
        let mut log = EventLog::create(&dir).unwrap();
        log.note("omamagoto-5C3C 接続").unwrap();
        log.incoming("omamagoto-5C3C", r#"{"event":"shake","intensity":1,"tilt":70,"device":"x","ts":1,"seq":1}"#).unwrap();
        log.incoming("omamagoto-5C3C", "壊れた行").unwrap();
        let ev = ServerEvent::Fire { level: 2 };
        log.outgoing(&Stamped { device: "server", ts: 1, seq: 1, event: &ev }).unwrap();

        let text = fs::read_to_string(log.path()).unwrap();
        let lines: Vec<Value> = text.lines().map(|l| serde_json::from_str(l).unwrap()).collect();
        assert_eq!(lines.len(), 4);
        assert_eq!(lines[0]["dir"], "note");
        assert_eq!(lines[1]["msg"]["event"], "shake");
        assert_eq!(lines[2]["msg"], "壊れた行");
        assert_eq!(lines[3]["msg"]["event"], "fire");
        assert_eq!(lines[3]["msg"]["level"], 2);
        fs::remove_dir_all(dir).unwrap();
    }
}
