package main

import (
	"context"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/coder/websocket"
)

func startWS(t *testing.T) (string, chan NetMessage, *Hub) {
	t.Helper()
	hub := NewHub()
	rx := make(chan NetMessage, 64)
	srv := httptest.NewServer(NewWSHandler(hub, rx))
	t.Cleanup(srv.Close)
	return "ws" + strings.TrimPrefix(srv.URL, "http"), rx, hub
}

func next(t *testing.T, rx chan NetMessage) NetMessage {
	t.Helper()
	select {
	case m := <-rx:
		return m
	case <-time.After(3 * time.Second):
		t.Fatal("main に何も届かない")
	}
	return NetMessage{}
}

func dial(t *testing.T, url string, opts *websocket.DialOptions) *websocket.Conn {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	c, _, err := websocket.Dial(ctx, url, opts)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { c.CloseNow() })
	return c
}

func expect(t *testing.T, m NetMessage, kind NetKind, peer Peer) {
	t.Helper()
	if m.Kind != kind || m.Peer != peer {
		t.Fatalf("got %#v want kind %d peer %d", m, kind, peer)
	}
}

func TestScreensGetEveryEvent(t *testing.T) {
	url, rx, hub := startWS(t)
	a := dial(t, url+"/screen", nil)
	b := dial(t, url+"/screen", nil)
	expect(t, next(t, rx), NetConnected, PeerScreen)
	expect(t, next(t, rx), NetConnected, PeerScreen)

	hub.Broadcast(`{"event":"fire","level":2}`)
	for _, c := range []*websocket.Conn{a, b} {
		ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
		typ, data, err := c.Read(ctx)
		cancel()
		if err != nil {
			t.Fatal(err)
		}
		eq(t, typ, websocket.MessageText)
		eq(t, string(data), `{"event":"fire","level":2}`)
	}

	// 画面から送ったもの（hello・reset）は main に届く
	if err := a.Write(context.Background(), websocket.MessageText, []byte(`{"device":"screen","event":"hello"}`)); err != nil {
		t.Fatal(err)
	}
	m := next(t, rx)
	expect(t, m, NetLine, PeerScreen)
	eq(t, m.Line, `{"device":"screen","event":"hello"}`)

	a.Close(websocket.StatusNormalClosure, "")
	expect(t, next(t, rx), NetDisconnected, PeerScreen)
}

func TestCameraLinesReachMain(t *testing.T) {
	url, rx, _ := startWS(t)
	// camera/main.py の --server ws://PC:9000 のようにパスが無くてもカメラ
	for _, path := range []string{"/camera", "/"} {
		c := dial(t, url+path, nil)
		expect(t, next(t, rx), NetConnected, PeerCamera)
		if err := c.Write(context.Background(), websocket.MessageText, []byte("{\"event\":\"cut\"}\n{\"event\":\"add\"}\n")); err != nil {
			t.Fatal(err)
		}
		for _, want := range []string{`{"event":"cut"}`, `{"event":"add"}`} {
			m := next(t, rx)
			expect(t, m, NetLine, PeerCamera)
			eq(t, m.Line, want)
		}
		c.CloseNow()
		expect(t, next(t, rx), NetDisconnected, PeerCamera)
	}
}

func TestBrowserPreviewCanConnect(t *testing.T) {
	// screen/index.html はファイルや別のポートから開くので、Origin が違ってもつなげる
	url, rx, _ := startWS(t)
	dial(t, url+"/screen", &websocket.DialOptions{HTTPHeader: http.Header{"Origin": {"null"}}})
	expect(t, next(t, rx), NetConnected, PeerScreen)
	dial(t, url+"/screen", &websocket.DialOptions{HTTPHeader: http.Header{"Origin": {"http://localhost:5173"}}})
	expect(t, next(t, rx), NetConnected, PeerScreen)
}

func TestOtherPathsAreNotFound(t *testing.T) {
	url, _, _ := startWS(t)
	httpURL := "http" + strings.TrimPrefix(url, "ws")
	resp, err := http.Get(httpURL + "/something")
	if err != nil {
		t.Fatal(err)
	}
	resp.Body.Close()
	eq(t, resp.StatusCode, http.StatusNotFound)
}

func TestSlowScreenIsReportedAsLagged(t *testing.T) {
	url, rx, hub := startWS(t)
	c := dial(t, url+"/screen", nil)
	m := next(t, rx)
	expect(t, m, NetConnected, PeerScreen)
	// 受け取らないうちに、OS の送り口と待てる数があふれるまで送る（8 KB × 5000 = 40 MB）
	big := `{"event":"swing","item":"` + strings.Repeat("x", 8<<10) + `"}`
	for i := 0; i < 5000; i++ {
		hub.Broadcast(big)
	}
	// 読み始めると、送れなかった数が届く
	go func() {
		for {
			if _, _, err := c.Read(context.Background()); err != nil {
				return
			}
		}
	}()
	for {
		m := next(t, rx)
		if m.Kind == NetLagged {
			if m.Skipped == 0 {
				t.Fatal("送れなかった数が 0")
			}
			return
		}
	}
}
