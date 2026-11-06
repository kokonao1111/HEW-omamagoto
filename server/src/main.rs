//! おままごとサーバー（要件定義書 3 章、5.9）
//! 入り口（BLE・WebSocket）は非同期のタスクで動かし、ここにはチャンネルで 1 件ずつ届く。
//! 届いたものを本体（kitchen）に渡し、出てきたイベントを表示してログに残し、つながっている画面に送る。
//!
//!     cargo run                       # config.toml を読み、logs/ にログを書く
//!     cargo run -- 別の設定.toml
//!     cargo run -- --no-ble           # BLE を使わない（デバイスの無い PC で、カメラと画面だけ試す）

use std::collections::HashSet;
use std::net::IpAddr;
use std::path::Path;

use anyhow::{Context, Result, bail};
use omamagoto_server::ble::{self, BleMessage};
use omamagoto_server::config::Config;
use omamagoto_server::event::{CameraMessage, DeviceMessage, ServerEvent, Stamped};
use omamagoto_server::event_log::EventLog;
use omamagoto_server::kitchen::{Handled, Kitchen};
use omamagoto_server::ws::{self, NetMessage, Peer};
use tokio::net::TcpListener;
use tokio::sync::{broadcast, mpsc, watch};

#[tokio::main]
async fn main() -> Result<()> {
    let mut config_path = "config.toml".to_string();
    let mut use_ble = true;
    for arg in std::env::args().skip(1) {
        match arg.as_str() {
            "--no-ble" => use_ble = false,
            a if a.starts_with("--") => bail!("知らない指定 {a}（使えるのは --no-ble）"),
            a => config_path = a.to_string(),
        }
    }
    let config = Config::load(Path::new(&config_path))?;
    println!("設定 {config_path}：デバイス {} 台", config.roles.len());
    let listen = config.ws.listen.clone();
    let mut kitchen = Kitchen::new(config);
    let log = EventLog::create(Path::new("logs"))?;
    println!("ログ {}", log.path().display());

    let listener = TcpListener::bind(&listen)
        .await
        .with_context(|| format!("{listen} で待てない。ほかのプログラムがこのポートを使っていないか確かめる"))?;
    print_addresses(&listener);
    let (screens, _) = broadcast::channel(1024);
    let mut out = Output { log, seq: 0, screens: screens.clone() };
    let (net_tx, mut net_rx) = mpsc::channel(1024);
    let net_task = tokio::spawn(ws::run(listener, net_tx, screens));

    let (ble_tx, mut ble_rx) = mpsc::channel(256);
    let (stop_tx, stop_rx) = watch::channel(false);
    let mut ble_task = if use_ble {
        Some(tokio::spawn(ble::run(ble_tx, stop_rx)))
    } else {
        println!("BLE は使わない（--no-ble）");
        None
    };
    let mut ble_open = ble_task.is_some();
    let mut warned_unknown = HashSet::new();

    let ctrl_c = tokio::signal::ctrl_c();
    tokio::pin!(ctrl_c);
    loop {
        tokio::select! {
            msg = ble_rx.recv(), if ble_open => {
                let Some(msg) = msg else {
                    // BLE の入り口が止まった（アダプタが無い など）。カメラと画面だけで続ける
                    ble_open = false;
                    if let Some(task) = ble_task.take() {
                        match task.await {
                            Ok(Ok(())) => {}
                            Ok(Err(e)) => println!("BLE が止まった：{e:#}。デバイスなしで続ける"),
                            Err(e) => println!("BLE が落ちた：{e}。デバイスなしで続ける"),
                        }
                    }
                    continue;
                };
                on_ble(msg, &mut kitchen, &mut out, &mut warned_unknown, &config_path)?;
            }
            msg = net_rx.recv() => {
                // None = WebSocket の入り口が止まった。理由は下で返す
                let Some(msg) = msg else { break };
                on_net(msg, &mut kitchen, &mut out)?;
            }
            _ = &mut ctrl_c => {
                println!("止めます（デバイスを切断）");
                let _ = stop_tx.send(true);
                break;
            }
        }
    }
    if let Some(task) = ble_task {
        task.await??;
    }
    net_task.abort();
    if let Ok(Err(e)) = net_task.await {
        return Err(e.context("WebSocket の入り口が止まった"));
    }
    Ok(())
}

/// 出したイベントに共通ヘッダを付けて、ログに残し、全部の画面に送り、表示する
struct Output {
    log: EventLog,
    seq: u64,
    screens: broadcast::Sender<String>,
}

impl Output {
    fn emit(&mut self, events: Vec<ServerEvent>) -> Result<()> {
        for ev in events {
            self.seq += 1;
            let stamped = Stamped { device: "server", ts: chrono::Utc::now().timestamp_millis(), seq: self.seq, event: &ev };
            self.log.outgoing(&stamped)?;
            // 画面が 1 つもつながっていないときは、送り先が無いだけ
            let _ = self.screens.send(serde_json::to_string(&stamped)?);
            if let Some(text) = describe(&ev) {
                println!("{} → {text}", now());
            }
        }
        Ok(())
    }
}

fn on_ble(
    msg: BleMessage,
    kitchen: &mut Kitchen,
    out: &mut Output,
    warned_unknown: &mut HashSet<String>,
    config_path: &str,
) -> Result<()> {
    let t = now();
    match msg {
        BleMessage::Connected { name } => {
            println!("{t} {name} 接続");
            out.log.note(&format!("{name} 接続"))?;
        }
        BleMessage::Disconnected { name } => {
            println!("{t} {name} 切断");
            out.log.note(&format!("{name} 切断"))?;
        }
        BleMessage::Line { name, line } => {
            out.log.incoming(&name, &line)?;
            let msg = match serde_json::from_str::<DeviceMessage>(&line) {
                Ok(m) => m,
                Err(e) => {
                    println!("{t} {name} 読めない行（{e}）：{line}");
                    return Ok(());
                }
            };
            match kitchen.handle(&msg) {
                Handled::Accepted(events) => out.emit(events)?,
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
    Ok(())
}

fn on_net(msg: NetMessage, kitchen: &mut Kitchen, out: &mut Output) -> Result<()> {
    let t = now();
    match msg {
        NetMessage::Connected { peer, id, addr } => {
            println!("{t} {} #{id} 接続（{addr}）", peer.name());
            out.log.note(&format!("{} #{id} 接続（{addr}）", peer.name()))?;
        }
        NetMessage::Disconnected { peer, id } => {
            println!("{t} {} #{id} 切断", peer.name());
            out.log.note(&format!("{} #{id} 切断", peer.name()))?;
        }
        NetMessage::Lagged { id, skipped } => {
            println!("{t} 画面 #{id} の受け取りが遅れて、{skipped} 件送れなかった");
            out.log.note(&format!("画面 #{id} に {skipped} 件送れなかった"))?;
        }
        NetMessage::Line { peer: Peer::Camera, id, line } => {
            out.log.incoming(&format!("camera#{id}"), &line)?;
            match serde_json::from_str::<CameraMessage>(&line) {
                Ok(m) => match kitchen.handle_camera(id, &m) {
                    Handled::Accepted(events) => out.emit(events)?,
                    Handled::Duplicate => println!("{t} カメラ #{id} 重複を捨てた：seq {}", m.seq),
                    Handled::UnknownDevice | Handled::NotForRole(_) => {}
                },
                Err(e) => println!("{t} カメラ #{id} 読めない行（{e}）：{line}"),
            }
        }
        NetMessage::Line { peer: Peer::Screen, id, line } => {
            out.log.incoming(&format!("screen#{id}"), &line)?;
            let event = serde_json::from_str::<serde_json::Value>(&line)
                .ok()
                .and_then(|v| v.get("event")?.as_str().map(str::to_string));
            match event.as_deref() {
                // 画面が起動し直したときなどに、今の状態（火）に合わせる
                Some("hello") => out.emit(kitchen.sync())?,
                Some("reset") => {
                    println!("{t} 画面 #{id} からリセット");
                    out.emit(kitchen.reset())?;
                }
                _ => {}
            }
        }
    }
    Ok(())
}

fn now() -> impl std::fmt::Display {
    chrono::Local::now().format("%H:%M:%S%.3f")
}

/// 位置は 1 秒に何十回も来るので表示しない（ログには残る）
fn describe(ev: &ServerEvent) -> Option<String> {
    Some(match ev {
        ServerEvent::Position { .. } => return None,
        ServerEvent::Cut { item, count } => format!("{item} を切った（{count} 個）"),
        ServerEvent::Add { item, zone, count } => {
            let zone = match zone.as_str() {
                "pan" => "フライパン",
                "plate" => "皿",
                z => z,
            };
            format!("{item} を{zone}に +{count}")
        }
        ServerEvent::Swing { .. } => "振り下ろし".to_string(),
        ServerEvent::Season { item, amount_g, total_g } => format!("{item} +{amount_g}g（合計 {total_g}g）"),
        ServerEvent::Fire { level } => {
            let name = ["切", "弱", "中", "強"].get(*level as usize).copied().unwrap_or("?");
            format!("火 {name}")
        }
        ServerEvent::Reset => "リセット".to_string(),
    })
}

fn print_addresses(listener: &TcpListener) {
    let Ok(addr) = listener.local_addr() else { return };
    let port = addr.port();
    println!("画面は ws://localhost:{port}/screen、カメラは ws://localhost:{port}/camera でつなぐ");
    if addr.ip().is_unspecified()
        && let Some(ip) = lan_ip()
    {
        println!("別の PC からは ws://{ip}:{port}/screen（カメラは /camera）");
    }
}

/// この PC の LAN のアドレス。UDP で行き先を決めるだけで、何も送らない
fn lan_ip() -> Option<IpAddr> {
    let s = std::net::UdpSocket::bind("0.0.0.0:0").ok()?;
    s.connect("8.8.8.8:80").ok()?;
    Some(s.local_addr().ok()?.ip())
}
