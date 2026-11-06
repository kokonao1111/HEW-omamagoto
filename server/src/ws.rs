//! WebSocket の入り口（要件定義書 3 章、5.9）
//! - `/screen`：画面（Unity・three.js の試作）。サーバーが出したイベント（6.4）を、つながっている全部の画面に配る。
//!   画面からは `hello`（つながった）と `reset`（最初に戻す）を受ける
//! - `/camera`：カメラ（Python、6.3）。届いた JSON を本体に渡す。パスを付けずに `/` につないでもカメラとして扱う
//!
//! 届いたものは NetMessage にして、チャンネルで 1 件ずつ main に渡す（本体は main が持つ）。

use std::net::SocketAddr;
use std::sync::Arc;
use std::sync::atomic::{AtomicU64, Ordering};

use anyhow::Result;
use axum::Router;
use axum::extract::ws::{Message, WebSocket, WebSocketUpgrade};
use axum::extract::{ConnectInfo, State};
use axum::response::Response;
use axum::routing::get;
use futures::{SinkExt, StreamExt};
use tokio::net::TcpListener;
use tokio::sync::broadcast::error::RecvError;
use tokio::sync::{broadcast, mpsc};

/// つないできた相手の種類
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Peer {
    Screen,
    Camera,
}

impl Peer {
    pub fn name(self) -> &'static str {
        match self {
            Peer::Screen => "画面",
            Peer::Camera => "カメラ",
        }
    }
}

/// WebSocket の入り口から main に渡すもの。id はつながるごとに 1 つずつ増える番号
#[derive(Debug)]
pub enum NetMessage {
    Connected { peer: Peer, id: u64, addr: SocketAddr },
    Disconnected { peer: Peer, id: u64 },
    /// 届いた 1 行（JSON のはず）
    Line { peer: Peer, id: u64, line: String },
    /// 画面の受け取りが遅れて、送れなかったイベントがあった
    Lagged { id: u64, skipped: u64 },
}

#[derive(Clone)]
struct Shared {
    tx: mpsc::Sender<NetMessage>,
    out: broadcast::Sender<String>,
    next_id: Arc<AtomicU64>,
}

/// listener で待ち、out に流れてきた JSON を全部の画面に送る。止めるときはこのタスクを abort する
pub async fn run(listener: TcpListener, tx: mpsc::Sender<NetMessage>, out: broadcast::Sender<String>) -> Result<()> {
    let shared = Shared { tx, out, next_id: Arc::new(AtomicU64::new(1)) };
    let app = Router::new()
        .route("/screen", get(screen))
        .route("/camera", get(camera))
        .route("/", get(camera))
        .with_state(shared);
    axum::serve(listener, app.into_make_service_with_connect_info::<SocketAddr>()).await?;
    Ok(())
}

async fn screen(ws: WebSocketUpgrade, ConnectInfo(addr): ConnectInfo<SocketAddr>, State(s): State<Shared>) -> Response {
    ws.on_upgrade(move |socket| serve(socket, Peer::Screen, addr, s))
}

async fn camera(ws: WebSocketUpgrade, ConnectInfo(addr): ConnectInfo<SocketAddr>, State(s): State<Shared>) -> Response {
    ws.on_upgrade(move |socket| serve(socket, Peer::Camera, addr, s))
}

async fn serve(socket: WebSocket, peer: Peer, addr: SocketAddr, s: Shared) {
    let id = s.next_id.fetch_add(1, Ordering::Relaxed);
    // 「つながった」を main に伝える前に受け口を作る（そのあとに出たイベントを落とさない）
    let mut events = s.out.subscribe();
    if s.tx.send(NetMessage::Connected { peer, id, addr }).await.is_err() {
        return;
    }
    let (mut sink, mut stream) = socket.split();
    loop {
        tokio::select! {
            // カメラにはイベントを送らない（受け取るだけ）
            ev = events.recv(), if peer == Peer::Screen => match ev {
                Ok(text) => {
                    if sink.send(Message::Text(text.into())).await.is_err() {
                        break;
                    }
                }
                Err(RecvError::Lagged(skipped)) => {
                    let _ = s.tx.send(NetMessage::Lagged { id, skipped }).await;
                }
                Err(RecvError::Closed) => break,
            },
            msg = stream.next() => match msg {
                Some(Ok(Message::Text(text))) => {
                    // 1 回に複数行が来ても 1 行ずつ渡す
                    for line in text.lines().map(str::trim).filter(|l| !l.is_empty()) {
                        if s.tx.send(NetMessage::Line { peer, id, line: line.to_string() }).await.is_err() {
                            return;
                        }
                    }
                }
                Some(Ok(Message::Close(_))) | Some(Err(_)) | None => break,
                // ping・pong は axum が返す。バイナリは使わない
                Some(Ok(_)) => {}
            },
        }
    }
    let _ = s.tx.send(NetMessage::Disconnected { peer, id }).await;
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::Duration;
    use tokio_tungstenite::connect_async;
    use tokio_tungstenite::tungstenite::Message as WsMessage;

    async fn start() -> (SocketAddr, mpsc::Receiver<NetMessage>, broadcast::Sender<String>) {
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let addr = listener.local_addr().unwrap();
        let (tx, rx) = mpsc::channel(64);
        let (out, _) = broadcast::channel(64);
        tokio::spawn(run(listener, tx, out.clone()));
        (addr, rx, out)
    }

    async fn next(rx: &mut mpsc::Receiver<NetMessage>) -> NetMessage {
        tokio::time::timeout(Duration::from_secs(3), rx.recv()).await.unwrap().unwrap()
    }

    #[tokio::test]
    async fn screens_get_every_event() {
        let (addr, mut rx, out) = start().await;
        let (mut a, _) = connect_async(format!("ws://{addr}/screen")).await.unwrap();
        let (mut b, _) = connect_async(format!("ws://{addr}/screen")).await.unwrap();
        assert!(matches!(next(&mut rx).await, NetMessage::Connected { peer: Peer::Screen, .. }));
        assert!(matches!(next(&mut rx).await, NetMessage::Connected { peer: Peer::Screen, .. }));

        out.send(r#"{"event":"fire","level":2}"#.to_string()).unwrap();
        for ws in [&mut a, &mut b] {
            let got = tokio::time::timeout(Duration::from_secs(3), ws.next()).await.unwrap().unwrap().unwrap();
            assert_eq!(got, WsMessage::Text(r#"{"event":"fire","level":2}"#.into()));
        }

        // 画面から送ったもの（hello・reset）は main に届く
        a.send(WsMessage::Text(r#"{"device":"screen","event":"hello"}"#.into())).await.unwrap();
        match next(&mut rx).await {
            NetMessage::Line { peer: Peer::Screen, line, .. } => assert_eq!(line, r#"{"device":"screen","event":"hello"}"#),
            other => panic!("{other:?}"),
        }

        a.close(None).await.unwrap();
        assert!(matches!(next(&mut rx).await, NetMessage::Disconnected { peer: Peer::Screen, .. }));
    }

    #[tokio::test]
    async fn camera_lines_reach_main() {
        let (addr, mut rx, _out) = start().await;
        // camera/main.py の --server ws://PC:9000 のようにパスが無くてもカメラ
        for path in ["/camera", "/"] {
            let (mut c, _) = connect_async(format!("ws://{addr}{path}")).await.unwrap();
            assert!(matches!(next(&mut rx).await, NetMessage::Connected { peer: Peer::Camera, .. }));
            c.send(WsMessage::Text("{\"event\":\"cut\"}\n{\"event\":\"add\"}\n".into())).await.unwrap();
            for want in [r#"{"event":"cut"}"#, r#"{"event":"add"}"#] {
                match next(&mut rx).await {
                    NetMessage::Line { peer: Peer::Camera, line, .. } => assert_eq!(line, want),
                    other => panic!("{other:?}"),
                }
            }
            drop(c);
            assert!(matches!(next(&mut rx).await, NetMessage::Disconnected { peer: Peer::Camera, .. }));
        }
    }
}
