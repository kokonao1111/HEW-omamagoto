//! おままごとサーバー（要件定義書 3 章、5.9）
//! 今は BLE で届いたイベントの JSON を表示するだけ。鍋の状態・工程・採点は次の段階で足す。
//! 入り口（BLE）は非同期のタスクで動かし、ここにはチャンネルで 1 件ずつ届く。

mod ble;
mod line_buffer;

use ble::BleMessage;
use tokio::sync::mpsc;

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    let (tx, mut rx) = mpsc::channel(256);
    let ble_task = tokio::spawn(ble::run(tx));

    while let Some(msg) = rx.recv().await {
        let t = chrono::Local::now().format("%H:%M:%S%.3f");
        match msg {
            BleMessage::Connected { name } => println!("{t} {name} 接続"),
            BleMessage::Disconnected { name } => println!("{t} {name} 切断"),
            BleMessage::Line { name, line } => println!("{t} {name} {line}"),
        }
    }
    // チャンネルが閉じた = BLE の入り口が止まった。理由（アダプタが無い など）を返す
    ble_task.await??;
    Ok(())
}
