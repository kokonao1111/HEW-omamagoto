//! おままごとサーバー（要件定義書 3 章、5.9）
//! 入り口（BLE）は非同期のタスクで動かし、ここにはチャンネルで 1 件ずつ届く。
//! 届いたものを本体（kitchen）に渡し、出てきたイベントを表示してログに残す。Unity への送信は次の段階で足す。
//!
//!     cargo run                       # config.toml を読み、logs/ にログを書く
//!     cargo run -- 別の設定.toml

use std::collections::HashSet;
use std::path::Path;

use anyhow::Result;
use omamagoto_server::ble::{self, BleMessage};
use omamagoto_server::config::Config;
use omamagoto_server::event::{DeviceMessage, ServerEvent, Stamped};
use omamagoto_server::event_log::EventLog;
use omamagoto_server::kitchen::{Handled, Kitchen};
use tokio::sync::{mpsc, watch};

#[tokio::main]
async fn main() -> Result<()> {
    let config_path = std::env::args().nth(1).unwrap_or_else(|| "config.toml".to_string());
    let config = Config::load(Path::new(&config_path))?;
    println!("設定 {config_path}：デバイス {} 台", config.roles.len());
    let mut kitchen = Kitchen::new(config);
    let mut log = EventLog::create(Path::new("logs"))?;
    println!("ログ {}", log.path().display());
    let mut out_seq = 0;
    let mut warned_unknown = HashSet::new();

    let (tx, mut rx) = mpsc::channel(256);
    let (stop_tx, stop_rx) = watch::channel(false);
    let ble_task = tokio::spawn(ble::run(tx, stop_rx));

    let ctrl_c = tokio::signal::ctrl_c();
    tokio::pin!(ctrl_c);
    loop {
        tokio::select! {
            msg = rx.recv() => {
                // None = BLE の入り口が止まった。理由（アダプタが無い など）は下で返す
                let Some(msg) = msg else { break };
                let t = chrono::Local::now().format("%H:%M:%S%.3f");
                match msg {
                    BleMessage::Connected { name } => {
                        println!("{t} {name} 接続");
                        log.note(&format!("{name} 接続"))?;
                    }
                    BleMessage::Disconnected { name } => {
                        println!("{t} {name} 切断");
                        log.note(&format!("{name} 切断"))?;
                    }
                    BleMessage::Line { name, line } => {
                        log.incoming(&name, &line)?;
                        let msg = match serde_json::from_str::<DeviceMessage>(&line) {
                            Ok(m) => m,
                            Err(e) => {
                                println!("{t} {name} 読めない行（{e}）：{line}");
                                continue;
                            }
                        };
                        match kitchen.handle(&msg) {
                            Handled::Accepted(events) => {
                                for ev in events {
                                    out_seq += 1;
                                    let stamped = Stamped {
                                        device: "server",
                                        ts: chrono::Utc::now().timestamp_millis(),
                                        seq: out_seq,
                                        event: &ev,
                                    };
                                    log.outgoing(&stamped)?;
                                    println!("{t} → {}", describe(&ev));
                                }
                            }
                            Handled::Duplicate => println!("{t} {name} 重複を捨てた：seq {}", msg.seq),
                            Handled::UnknownDevice => {
                                if warned_unknown.insert(msg.device.clone()) {
                                    println!(
                                        "{t} {name} 設定ファイルに無いデバイス {}。{config_path} の [roles] に足すと使える",
                                        msg.device
                                    );
                                }
                            }
                            // 役割に合わないもの（塩の knob など）はたくさん来るので表示しない。ログには残っている
                            Handled::NotForRole(_) => {}
                        }
                    }
                }
            }
            _ = &mut ctrl_c => {
                println!("止めます（デバイスを切断）");
                let _ = stop_tx.send(true);
                break;
            }
        }
    }
    ble_task.await??;
    Ok(())
}

fn describe(ev: &ServerEvent) -> String {
    match ev {
        ServerEvent::Season { item, amount_g, total_g } => format!("{item} +{amount_g}g（合計 {total_g}g）"),
        ServerEvent::Fire { level } => {
            let name = ["切", "弱", "中", "強"].get(*level as usize).copied().unwrap_or("?");
            format!("火 {name}")
        }
    }
}
