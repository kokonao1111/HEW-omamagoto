//! BLE の入り口（要件定義書 5.9、6.6）
//! 6.6 のサービスを持つデバイスを探して全台につなぎ、届いた JSON の行をチャンネルで本体に渡す。
//! 1 台ごとに専用のタスクでつなぎ、切れたらタスクが終わる。次に見つけたときにつなぎ直す。

use std::collections::HashMap;
use std::time::Duration;

use anyhow::{Context, Result};
use btleplug::api::{Central, Manager as _, Peripheral as _, ScanFilter};
use btleplug::platform::{Manager, Peripheral, PeripheralId};
use futures::StreamExt;
use tokio::sync::mpsc;
use tokio::task::JoinHandle;
use uuid::Uuid;

use crate::line_buffer::LineBuffer;

pub const SERVICE_UUID: Uuid = Uuid::from_u128(0x35f64d1a_5af0_4ec7_a321_3b2a485eae3f);
pub const EVENT_UUID: Uuid = Uuid::from_u128(0x2a28760f_2c0e_4e00_b5cd_4ae7faa984cb);

/// 見つかったデバイスを確かめる間隔
const SCAN_INTERVAL: Duration = Duration::from_secs(1);
/// 電源が切れたデバイスにつなぎに行って待ち続けないため
const CONNECT_TIMEOUT: Duration = Duration::from_secs(10);
/// 切断の通知が来ない OS があるので、つながっているかを自分でも確かめる間隔
const ALIVE_CHECK: Duration = Duration::from_secs(1);

/// BLE の入り口から本体に渡すもの
#[derive(Debug)]
pub enum BleMessage {
    Connected { name: String },
    Disconnected { name: String },
    Line { name: String, line: String },
}

pub async fn run(tx: mpsc::Sender<BleMessage>) -> Result<()> {
    let manager = Manager::new().await?;
    let adapter = manager
        .adapters()
        .await?
        .into_iter()
        .next()
        .context("Bluetooth のアダプタが見つからない")?;
    adapter
        .start_scan(ScanFilter { services: vec![SERVICE_UUID] })
        .await?;

    let mut tasks: HashMap<PeripheralId, JoinHandle<()>> = HashMap::new();
    let mut tick = tokio::time::interval(SCAN_INTERVAL);
    loop {
        tick.tick().await;
        for p in adapter.peripherals().await? {
            let id = p.id();
            if tasks.get(&id).is_some_and(|t| !t.is_finished()) {
                continue;
            }
            let Some(props) = p.properties().await? else { continue };
            if !props.services.contains(&SERVICE_UUID) {
                continue;
            }
            let name = props.local_name.unwrap_or_else(|| format!("{id:?}"));
            tasks.insert(id, tokio::spawn(watch(p, name, tx.clone())));
        }
    }
}

async fn watch(p: Peripheral, name: String, tx: mpsc::Sender<BleMessage>) {
    let connected = listen(&p, &name, &tx).await;
    if let Err(e) = &connected {
        // 電源が切れているデバイスは毎回ここに来るので、本体には渡さず画面に出すだけにする
        eprintln!("{name}: {e:#}");
    }
    let _ = p.disconnect().await;
    if matches!(connected, Ok(true)) {
        let _ = tx.send(BleMessage::Disconnected { name }).await;
    }
}

/// つないで通知を受け続ける。一度でもつながったら Ok(true)
async fn listen(p: &Peripheral, name: &str, tx: &mpsc::Sender<BleMessage>) -> Result<bool> {
    if !p.is_connected().await? {
        tokio::time::timeout(CONNECT_TIMEOUT, p.connect())
            .await
            .context("接続がタイムアウトした")??;
    }
    p.discover_services().await?;
    let chr = p
        .characteristics()
        .into_iter()
        .find(|c| c.uuid == EVENT_UUID)
        .context("イベントのキャラクタリスティックが無い")?;
    // 購読より先に受け口を作る（購読直後の通知を落とさない）
    let mut notes = p.notifications().await?;
    p.subscribe(&chr).await?;
    tx.send(BleMessage::Connected { name: name.to_string() }).await?;

    let mut buf = LineBuffer::default();
    let mut alive = tokio::time::interval(ALIVE_CHECK);
    loop {
        tokio::select! {
            note = notes.next() => match note {
                Some(n) if n.uuid == EVENT_UUID => {
                    for line in buf.push(&n.value) {
                        tx.send(BleMessage::Line { name: name.to_string(), line }).await?;
                    }
                }
                Some(_) => {}
                None => break,
            },
            _ = alive.tick() => {
                if !p.is_connected().await? {
                    break;
                }
            }
        }
    }
    Ok(true)
}
