# docs/data — 장면 데이터 덤프

`Manor_KHS` 장면을 Unity 없이 검토·비교하려고 내보낸 JSON. 한 줄에 레코드 하나, 위치는 m 단위 소수 둘째 자리.
좌표: x 동(+)·서(-), z 북(+)·남(-), y 높이. 장면을 다시 빌드하면 이 파일들도 다시 내보내야 한다.

## 다시 만드는 법

- Unity 메뉴 **Tools/Murder Mystery/Export Scene Data** — 아래 파일 전부 + 검사 실행
- **Tools/Murder Mystery/Run All Checks** — `checks.json`만 갱신
- MCP(execute_code)에서: `MurderMystery.EditorTools.SceneDataExporter.ExportAll()` / `.RunAllChecks()` (반환값이 요약 문자열)
- 장면 재생성은 **Tools/Manor/Build KHS Manor Scene** (`KhsManorBuilder.BuildScene`) 후 내보내기

## 파일

| 파일 | 내용 |
|---|---|
| `props.json` | 가구: 이름, 프리팹 부품, 위치, 회전(yaw), 크기, 바닥·윗면 높이, 받침(support)과 간격(gap), 몸체를 관통하는 콜라이더(through), 문제 표시(flag: overlaps/floats/sunk) |
| `clue_objects.json` | 단서 물체: 단서 id, 조사 지점, 위치, 크기, 구성 부품 |
| `spots.json` | 조사 지점: id, 이름, 위치, 층(Surface/Deep), 반경, 단서 id, 잠금 id |
| `locks.json` | 잠긴 문·울타리·봉인: 잠금 id, 오브젝트, 위치(첫 문짝 경첩), 문짝 수, 제목, 열쇠, 열리는 단계 |
| `rooms.json` | 밀담 방: id, 위치, 나가는 지점, 좌석 위치 |
| `doors.json` | 여닫는 문짝: 경첩 위치(문짝 원점), yaw, 크기 |
| `buildings.json` | 스캔한 집의 모든 조각과 추가 구조물(디딤돌, 문 앞 섬돌, 울타리, 봉인, 밀담 방)의 경계 상자 |
| `ground.json` | 마당 지면 높이 1 m 격자 (`heights[row][col]` = x = originX + col, z = originZ + row) |
| `checks.json` | 검사 결과(아래) |

## checks.json

- `summary` — 각 검사의 문제 수
- `dataIssues` — `DataValidator` (단서·잠금·열쇠·지점 데이터 정합성). 0이어야 함
- `walkability` — 토론 장소에서 모든 조사 지점까지 걷기 경로(잠금은 열린 상태로). `OK`/`ok`(봉인 구역) 외에는 문제
- `pockets` — 문 없이 막힌 걸을 수 있는 공간. 0이어야 함
- `doors` — 문짝마다 양쪽 통과 여부. `BLOCKED gate`(잠긴 문), 밀담 방 문, 봉인 문은 의도된 것. `AROUND`는 문 바로 앞이 아니라 돌아서 갈 수 있다는 뜻
- `duckOnly` — 숙여야만 갈 수 있는 곳. 문간(작은 칸)은 정상, `sealed`는 마루 밑처럼 들어갈 수 없는 곳
- `floating` — 아래가 아무것에도 닿지 않은 건물 조각. 0이어야 함
- `buried` — 대부분 땅에 묻힌 작은 조각(낙수받이 돌은 제외). 마루 밑 기둥 토막 등 안 보이는 것은 무시해도 됨
- `furnitureFlags` — 가구 점검에서 표시된 것: `too high`(지붕·벽 위), `overlaps furniture`(가구끼리 겹침), `overlaps`(벽·바닥 관통), `floats`, `sunk`. 작은 물건이 다른 물건과 살짝 겹치는 정도는 무시해도 됨
- `hiddenClues` — 가구 밑이나 장 안에 놓여 안 보이는 단서. 0이어야 함
