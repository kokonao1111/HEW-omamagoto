package main

import "testing"

func mustConfig(t *testing.T, text string) Config {
	t.Helper()
	c, err := ParseConfig(text)
	if err != nil {
		t.Fatal(err)
	}
	return c
}

func roleOf(c Config, device string) Role {
	r, _ := c.RoleOf(device)
	return r
}

func TestParsesConfigRequirementsExample(t *testing.T) {
	// 要件定義書 6.5 の例（[reset] はまだ使わないが、あっても読める）
	c := mustConfig(t, `
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
`)
	eq(t, roleOf(c, "AA:BB:CC:DD:EE:FF"), RoleSalt)
	eq(t, roleOf(c, "77:88:99:AA:BB:CC"), RoleRange)
	eq(t, c.Season.GramsPerShake[RolePepper], 0.2)
	// [ws] が無ければ PC の中だけで待つ
	eq(t, c.WS.Listen, "127.0.0.1:9000")
}

func TestReadsWSListen(t *testing.T) {
	c := mustConfig(t, "[roles]\n[ws]\nlisten = \"0.0.0.0:9000\"\n")
	eq(t, c.WS.Listen, "0.0.0.0:9000")
}

func TestMacCaseDoesNotMatter(t *testing.T) {
	c := mustConfig(t, "[roles]\n\"aa:bb:cc:dd:ee:ff\" = \"knob\"\n")
	eq(t, roleOf(c, "AA:BB:CC:DD:EE:FF"), RoleKnob)
	eq(t, roleOf(c, "aa:bb:cc:dd:ee:ff"), RoleKnob)
}

func TestUnknownRoleIsAnError(t *testing.T) {
	if _, err := ParseConfig("[roles]\n\"AA:BB:CC:DD:EE:FF\" = \"sugar\"\n"); err == nil {
		t.Fatal("知らない役割なのに読めた")
	}
}

func TestConfigDetails(t *testing.T) {
	// [roles] は空でもよいが、無いのはまちがい
	eq(t, len(mustConfig(t, "[roles]\n").Roles), 0)
	for _, text := range []string{
		"[season]\ngrams_per_shake = { salt = 0.5 }\n",
		"[roles]\n[season]\ngrams_per_shake = { sugar = 0.5 }\n",
		"[roles]\n[ws]\nlisten = 9000\n",
		"[roles\n",
	} {
		if _, err := ParseConfig(text); err == nil {
			t.Errorf("読めてしまった：%q", text)
		}
	}
	// グラム数は整数で書いてもよい
	eq(t, mustConfig(t, "[roles]\n[season]\ngrams_per_shake = { salt = 1 }\n").Season.GramsPerShake[RoleSalt], 1.0)
}

func TestLoadsRepositoryConfig(t *testing.T) {
	// このフォルダーの config.toml がそのまま読める
	c, err := LoadConfig("config.toml")
	if err != nil {
		t.Fatal(err)
	}
	eq(t, roleOf(c, "AC:27:6E:D3:5C:3C"), RoleSalt)
	eq(t, c.WS.Listen, "0.0.0.0:9000")
}
