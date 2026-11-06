//! おままごとサーバーの部品。main.rs から使う
//! 入り口（ble・ws）は非同期、それ以外（イベントの型・設定・本体）は普通のコードで、cargo test で確かめる。

pub mod ble;
pub mod config;
pub mod event;
pub mod event_log;
pub mod kitchen;
pub mod line_buffer;
pub mod ws;
