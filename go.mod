module golem-sample-unity

go 1.27.1

require (
	github.com/demiurgos-hub/golem-engine v0.0.0
	github.com/demiurgos-hub/golem-engine/golem/collision v0.3.1-0.20261115012239-c9fd88dc8adc
	github.com/demiurgos-hub/golem-engine/golem/collision/resolv v0.0.0-00010101000000-000000000000
	github.com/demiurgos-hub/golem-engine/golem/objecttemplate v0.0.0-20261116120853-bdf3d60d7b11
	gopkg.in/yaml.v3 v3.0.1
	modernc.org/sqlite v1.54.0
)

require (
	github.com/alitto/pond/v2 v2.7.1 // indirect
	github.com/coder/websocket v1.8.14 // indirect
	github.com/demiurgos-hub/golem-engine/golem/geometry v0.0.0-20261115012239-c9fd88dc8adc // indirect
	github.com/demiurgos-hub/golem-engine/golem/nav v0.3.1-0.20261113161118-0c6a59582abb // indirect
	github.com/demiurgos-hub/golem-engine/golem/physics3d v0.0.0-20261115012239-c9fd88dc8adc // indirect
	github.com/demiurgos-hub/navmesh-go v0.0.0-20261116105330-b17cd9f21c78 // indirect
	github.com/dunglas/httpsfv v1.1.0 // indirect
	github.com/dustin/go-humanize v1.0.1 // indirect
	github.com/google/uuid v1.6.0 // indirect
	github.com/mattn/go-isatty v0.0.20 // indirect
	github.com/ncruces/go-strftime v1.0.0 // indirect
	github.com/quic-go/qpack v0.6.0 // indirect
	github.com/quic-go/quic-go v0.59.0 // indirect
	github.com/quic-go/webtransport-go v0.10.0 // indirect
	github.com/remyoudompheng/bigfft v0.0.0-20230129092748-24d4a6f8daec // indirect
	github.com/solarlune/resolv v0.8.1 // indirect
	golang.org/x/crypto v0.41.0 // indirect
	golang.org/x/net v0.43.0 // indirect
	golang.org/x/sys v0.46.0 // indirect
	golang.org/x/text v0.29.0 // indirect
	modernc.org/libc v1.74.1 // indirect
	modernc.org/mathutil v1.7.1 // indirect
	modernc.org/memory v1.11.0 // indirect
)

replace github.com/demiurgos-hub/golem-engine => ../../golem-engine

replace github.com/demiurgos-hub/golem-engine/golem/collision => ../../golem-engine/golem/collision

replace github.com/demiurgos-hub/golem-engine/golem/collision/resolv => ../../golem-engine/golem/collision/resolv

replace github.com/demiurgos-hub/golem-engine/golem/nav => ../../golem-engine/golem/nav

replace github.com/demiurgos-hub/golem-engine/golem/objecttemplate => ../../golem-engine/golem/objecttemplate
