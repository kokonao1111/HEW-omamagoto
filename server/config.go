// サーバーの設定ファイル（要件定義書 5.9、6.5）
// MAC アドレス → 役割、振り 1 回あたりのグラム数、WebSocket で待つ場所を持つ。書き換えて再起動すれば反映される。

package main

import (
	"errors"
	"fmt"
	"os"
	"strings"

	"github.com/BurntSushi/toml"
)

// Role はデバイスの役割
type Role string

const (
	RoleSalt   Role = "salt"
	RolePepper Role = "pepper"
	RolePan    Role = "pan"
	RoleKnob   Role = "knob"
	RoleRange  Role = "range"
)

func parseRole(s string) (Role, error) {
	switch r := Role(s); r {
	case RoleSalt, RolePepper, RolePan, RoleKnob, RoleRange:
		return r, nil
	}
	return "", fmt.Errorf("知らない役割 %q（使えるのは salt・pepper・pan・knob・range）", s)
}

type Config struct {
	// MAC アドレス → 役割。読み込むときに大文字にそろえる
	Roles  map[string]Role
	Season SeasonConfig
	WS     WSConfig
}

type SeasonConfig struct {
	// 強さ 1 で振ったときに入るグラム数。強さが小さければその分少なくなる
	GramsPerShake map[Role]float64
}

type WSConfig struct {
	// 画面とカメラがつなぎに来る所。PC の中だけなら 127.0.0.1:9000、別の PC からもつなぐなら 0.0.0.0:9000
	Listen string
}

const defaultListen = "127.0.0.1:9000"

func LoadConfig(path string) (Config, error) {
	text, err := os.ReadFile(path)
	if err != nil {
		return Config{}, fmt.Errorf("設定ファイル %s が読めない：%w", path, err)
	}
	c, err := ParseConfig(string(text))
	if err != nil {
		return Config{}, fmt.Errorf("設定ファイル %s の書き方が違う：%w", path, err)
	}
	return c, nil
}

func ParseConfig(text string) (Config, error) {
	var raw struct {
		Roles  map[string]string `toml:"roles"`
		Season struct {
			GramsPerShake map[string]float64 `toml:"grams_per_shake"`
		} `toml:"season"`
		WS struct {
			Listen *string `toml:"listen"`
		} `toml:"ws"`
	}
	md, err := toml.Decode(text, &raw)
	if err != nil {
		return Config{}, err
	}
	if !md.IsDefined("roles") {
		return Config{}, errors.New("[roles] が無い")
	}

	c := Config{
		Roles:  map[string]Role{},
		Season: SeasonConfig{GramsPerShake: map[Role]float64{}},
		WS:     WSConfig{Listen: defaultListen},
	}
	for mac, name := range raw.Roles {
		role, err := parseRole(name)
		if err != nil {
			return Config{}, fmt.Errorf("[roles] の %s：%w", mac, err)
		}
		c.Roles[strings.ToUpper(mac)] = role
	}
	for name, grams := range raw.Season.GramsPerShake {
		role, err := parseRole(name)
		if err != nil {
			return Config{}, fmt.Errorf("[season] の grams_per_shake：%w", err)
		}
		c.Season.GramsPerShake[role] = grams
	}
	if raw.WS.Listen != nil {
		c.WS.Listen = *raw.WS.Listen
	}
	return c, nil
}

// RoleOf はデバイスの役割を返す。設定に無ければ ok が false
func (c Config) RoleOf(device string) (Role, bool) {
	r, ok := c.Roles[strings.ToUpper(device)]
	return r, ok
}
