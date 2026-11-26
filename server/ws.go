// WebSocket の入り口（要件定義書 3 章、5.9）
//   - /screen：画面（Unity・three.js の試作）。サーバーが出したイベント（6.4）を、つながっている全部の画面に配る。
//     画面からは hello（つながった）と reset（最初に戻す）を受ける
//   - /camera：カメラ（Python、6.3）。届いた JSON を本体に渡す。パスを付けずに / につないでもカメラとして扱う
//
// 届いたものは NetMessage にして、チャンネルで 1 件ずつ main に渡す（本体は main が持つ）。

package main

import (
	"context"
	"net/http"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/coder/websocket"
)

// Peer はつないできた相手の種類
type Peer int

const (
	PeerScreen Peer = iota
	PeerCamera
)

func (p Peer) Name() string {
	if p == PeerScreen {
		return "画面"
	}
	return "カメラ"
}

type NetKind int

const (
	NetConnected NetKind = iota
	NetDisconnected
	// NetLine は届いた 1 行（JSON のはず）
	NetLine
	// NetLagged は画面の受け取りが遅れて、送れなかったイベントがあった
	NetLagged
)

// NetMessage は WebSocket の入り口から main に渡すもの。ID はつながるごとに 1 つずつ増える番号
type NetMessage struct {
	Kind    NetKind
	Peer    Peer
	ID      uint64
	Addr    string
	Line    string
	Skipped uint64
}

const (
	// 画面 1 つあたり、送るのを待てるイベントの数（あふれたら捨てて Lagged で知らせる）
	screenQueue = 1024
	// 1 回に受け取れる大きさ
	readLimit = 1 << 20
	// 画面が受け取らなくなったときに、送るのをあきらめるまでの時間
	writeTimeout = 10 * time.Second
)

// Hub はつながっている画面の一覧。Broadcast で全部の画面にイベントを配る
type Hub struct {
	mu      sync.Mutex
	screens map[uint64]*screenConn
	nextID  atomic.Uint64
}

type screenConn struct {
	queue   chan string
	skipped atomic.Uint64
}

func NewHub() *Hub {
	return &Hub{screens: map[uint64]*screenConn{}}
}

// Broadcast は全部の画面に送る。画面が 1 つもつながっていないときは、送り先が無いだけ
func (h *Hub) Broadcast(text string) {
	h.mu.Lock()
	defer h.mu.Unlock()
	for _, s := range h.screens {
		select {
		case s.queue <- text:
		default:
			s.skipped.Add(1)
		}
	}
}

func (h *Hub) add(id uint64) *screenConn {
	s := &screenConn{queue: make(chan string, screenQueue)}
	h.mu.Lock()
	h.screens[id] = s
	h.mu.Unlock()
	return s
}

func (h *Hub) remove(id uint64) {
	h.mu.Lock()
	delete(h.screens, id)
	h.mu.Unlock()
}

// NewWSHandler は /screen・/camera・/ で待つ http.Handler を作る
func NewWSHandler(h *Hub, tx chan<- NetMessage) http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /screen", func(w http.ResponseWriter, r *http.Request) { serve(w, r, PeerScreen, h, tx) })
	mux.HandleFunc("GET /camera", func(w http.ResponseWriter, r *http.Request) { serve(w, r, PeerCamera, h, tx) })
	// camera/main.py の --server ws://PC:9000 のようにパスが無くてもカメラ
	mux.HandleFunc("GET /{$}", func(w http.ResponseWriter, r *http.Request) { serve(w, r, PeerCamera, h, tx) })
	return mux
}

func serve(w http.ResponseWriter, r *http.Request, peer Peer, h *Hub, tx chan<- NetMessage) {
	// ブラウザの試作（screen/index.html）はファイルや別のポートから開くので、Origin は確かめない
	c, err := websocket.Accept(w, r, &websocket.AcceptOptions{InsecureSkipVerify: true})
	if err != nil {
		return
	}
	defer c.CloseNow()
	c.SetReadLimit(readLimit)

	id := h.nextID.Add(1)
	// 「つながった」を main に伝える前に受け口を作る（そのあとに出たイベントを落とさない）
	var screen *screenConn
	if peer == PeerScreen {
		screen = h.add(id)
		defer h.remove(id)
	}
	tx <- NetMessage{Kind: NetConnected, Peer: peer, ID: id, Addr: r.RemoteAddr}

	ctx, cancel := context.WithCancel(context.Background())
	readDone := make(chan struct{})
	go func() {
		defer close(readDone)
		for {
			typ, data, err := c.Read(ctx)
			if err != nil {
				return
			}
			// ping・pong はライブラリが返す。バイナリは使わない
			if typ != websocket.MessageText {
				continue
			}
			// 1 回に複数行が来ても 1 行ずつ渡す
			for _, line := range strings.Split(string(data), "\n") {
				if line = strings.TrimSpace(line); line != "" {
					tx <- NetMessage{Kind: NetLine, Peer: peer, ID: id, Line: line}
				}
			}
		}
	}()

	// カメラにはイベントを送らない（受け取るだけ）
	if screen != nil {
		sendToScreen(ctx, c, screen, id, tx, readDone)
	}
	<-readDone
	cancel()
	tx <- NetMessage{Kind: NetDisconnected, Peer: peer, ID: id}
}

// sendToScreen は、相手が閉じるか送れなくなるまで、たまったイベントを画面に送る
func sendToScreen(ctx context.Context, c *websocket.Conn, s *screenConn, id uint64, tx chan<- NetMessage, readDone <-chan struct{}) {
	for {
		select {
		case text := <-s.queue:
			wctx, cancel := context.WithTimeout(ctx, writeTimeout)
			err := c.Write(wctx, websocket.MessageText, []byte(text))
			cancel()
			if err != nil {
				// 送れなくなったら閉じる。読む側もそこで終わる
				c.CloseNow()
				return
			}
			if n := s.skipped.Swap(0); n > 0 {
				tx <- NetMessage{Kind: NetLagged, ID: id, Skipped: n}
			}
		case <-readDone:
			return
		}
	}
}
