# 작업 진행 상황 (이어서 작업용)

> 중단되면 이 파일의 첫 번째 `[ ]` 항목부터 이어서 진행한다. 항목을 끝낼 때마다 `[x]`로 바꾸고 커밋한다.
> 시작: 2026-10-11. 요청: 남은 미정 항목 전부 진행 → 자체 검토 → 요약 보고.

## 작업 목록

- [x] T0 Unity MCP 활용법 정리 (메모리 reference_unity_mcp_usage)
- [x] T1 다섯 통의 문서 본문 + 엔딩 대본 (A, B×4, C, 공통 에필로그) → `docs/design/endings.md`
- [x] T2 단서 카드 문구 + Fact 분해 + 도 검률 응답 데이터 → `Assets/_Project/Data/*.json`
- [x] T3 런타임 데이터 모델·로더, 게임 상태(조사 점수·소지품·공개 게시판·수첩), NPC 응답 선택기, 점수 판정
- [x] T4 데이터 검증 도구 + EditMode 테스트
- [x] T5 1인칭 이동 컨트롤러 + 조사 상호작용 + 최소 UI
- [x] T6 블록아웃 씬 배치 + 플레이 테스트 (2026-10-11 완료, 아래 절차는 재검증용)
  1. Unity 창을 한 번 클릭해 스크립트 임포트(컴파일) → `Window > MCP for Unity > Start Session`
  2. `Tools > Manor > Build Blockout Scene` (안채 섬돌·찬방 문 반영)
  3. `Tools > Murder Mystery > Validate Data` → 콘솔에 OK 확인
  4. `Window > General > Test Runner` > EditMode > Run All (16개, BalanceSimulation은 Explicit)
  5. `Manor_Blockout` 열고 Play: WASD/마우스, E 살펴보기, R 회수, Tab 수첩, N 단계, 1~5 인물 전환
  6. 생성된 `.meta` 파일과 재생성된 씬 커밋
- [x] T7 멀티플레이·음성 기술 결정 문서 → `docs/design/tech_decisions.md`
- [x] T8 KHS 에셋 적용 방식 + 한복 캐릭터 수급 방안 → `docs/design/asset_plan.md`
- [x] T9 밸런스 검토 (조사 점수·단서 수·시간·범인 난이도) → `docs/design/balance_review.md`
- [x] T10 전체 자체 검토 + 요약 보고 (2026-10-11)

## 2차 작업 (2026-10-11 오후, Unity 연결 없이 가능한 것)

- [x] T11 진행 흐름: 단계 타이머·자동 진행·도 검률 안내 대사 데이터 (`GameFlow`, announcements)
- [x] T12 밀담 시스템: 요청/수락/3분 타이머/라운드당 2회 (`SecretTalkManager`)
- [x] T13 투표·재투표·엔딩 선택 (A / B×4 / C) + 엔딩 데이터 JSON
- [x] T14 네트워크 준비: `ISessionGateway` 인터페이스 + 로컬 구현, 클라이언트 전송용 개인 상태 스냅샷(정보 보안)
- [x] T14+ 프로토타입 HUD에 투표·결선·엔딩·점수 화면 (혼자서 한 판 끝까지 진행 가능)
- [x] T15 Unity MCP 재연결 후 T6 진행 — Unity 테스트 24/24, 문 20곳 보행 점검, 조사·수첩·NPC·투표·엔딩 화면 확인

## 3차 작업 — 멀티플레이 (NGO + Multiplayer Services Sessions + Vivox), 2026-10-11 결정

- [x] M1 패키지 설치 (NGO 2.13.3, Multiplayer 2.4.0, Vivox 16.12.1): com.unity.netcode.gameobjects, com.unity.services.multiplayer, com.unity.services.vivox
- [x] M2 API 확인(unity_reflect) → 네트워크 설계 메모 (tech_decisions 갱신)
- [x] M3 접속: LAN 호스트(에디터) + 빌드 클라이언트 접속 확인. 온라인(Sessions/Relay)은 M9 이후 검증. (MCP execute_code는 CodeDom 컴파일러라 로컬 함수 불가 → 리플렉션 코드로 호출)
- [x] M4 좌석: 배역 선택·이름 표시, 진행 중 재접속 시 같은 인물·기존 단서 복구, 미배정 인물은 진행 중 선택 거부
- [x] M5 의도 RPC: 단계 진행·공지 브로드캐스트, 클라이언트 조사 → 본인에게만 결과, 호스트 스냅샷에 남의 단서 없음(격리 확인), 밀담 요청→수락 양쪽 반영, 2인 투표→엔딩·점수 양쪽 수신. 밀담 요청 결과에 talk id가 문구로 찍히던 문제 수정(ActionResult.TalkId), 접속 끊긴 사람의 밀담 자동 종료
- [x] M6 플레이어: 원격 플레이어가 다른 스폰 위치에 인물 체형 캡슐로 표시, 퇴장 시 제거
- [x] M7 Vivox: 온라인 방에서 호스트·클라이언트가 같은 전체 채널(참가자 2) 접속, 조사 단계 송신 끔, 밀담 수락 시 2인 밀담 채널로 전환·종료 시 퇴장, 엔딩에서 전체 채널 복귀. 방 키를 호스트가 NetworkVariable로 배포(세션 await 전에 NetGame이 스폰돼 "lan" 채널로 섞이던 문제 수정), 시작 중 들어온 채널 전환 요청을 큐로 보존
- [x] M8 다중 인스턴스: `Builds/Win64` 개발 빌드 + `CommandLineAutoPlay` 테스트 훅(-mmJoin/-mmSeat/-mmInvestigate/-mmAcceptTalk/-mmVote/-mmQuitAfter, -batchmode 가능). 빌드 설정의 옛 SampleScene 항목 제거
- [x] M9 사용자 작업 완료(2026-10-11): Unity Cloud 연결(MurderMistery3D), Vivox·Relay 활성화. 온라인 방 만들기(Relay)→코드 참가→배역→조사→밀담→투표→엔딩 2인 검증, 호스트 퇴장 시 클라이언트 세션 자동 정리. tokenKey는 비워 둠(UGS 인증 토큰 사용)

## 메모

- MCP 원인: 이 세션의 `unityMCP` 도구는 데스크톱 확장으로 뜬 **stdio 서버 v9.7.3**이고, Unity는 **HTTP 서버 v10.3.0(8080)**에 연결됨. 세션의 HTTP 서버 항목 `UnityMCP`는 세션 시작 시 서버가 꺼져 있어 failed. → `reconnect_session_connector("UnityMCP")` 후 다음 턴부터 `mcp__UnityMCP__*` 도구 사용. 안 되면 사용자가 /mcp에서 재연결하거나 세션 재시작.

- Unity MCP: 서버(8080)는 healthy인데 Unity 세션 미연결 상태였음 (2026-10-11 13:3x). Unity 창 `Window > MCP for Unity > Start Session` 필요. 미연결 시 Editor.log로 컴파일 오류 확인.
- T5 메모: 플레이어는 씬에 배치하지 않고 `PrototypeBootstrap`이 Play 시 자동 생성. 블록아웃 생성기에 안채 섬돌·찬방 문 위치 수정이 들어갔으므로 **씬 재생성 필요**(Tools > Manor > Build Blockout Scene).
- 오프라인 컴파일 확인: scratchpad의 Editor.csproj 방식 (Unity DLL 참조, dotnet build). 새 .cs 파일들의 .meta는 Unity가 열릴 때 생성 → 이후 커밋 필요.

## 4차 작업 — KHS 에셋 적용, 2026-10-11

- [x] K1 지인 테스트 안내서 → `docs/playtest_guide.md`
- [x] K2 KHS 패키지 2종(김명관 고택, Joseon Royal Wear)을 필요한 폴더만 `Assets/ThirdParty/KHS/`로 추출 (TMP·URP 설정 제외), 고택 재질 URP 변환, 텍스처 상한
- [x] K3 `Manor_KHS` 씬 생성기: 실제 건물에 장소 배정(map_layout.md 8절), 충돌체 복구, 섬돌·차단벽, 시야·동선 재검증
- [x] K4 플레이어 몸 프로필 + 계단 보조(StepAssist), NavMesh 검증 + 실제 주행 테스트 21/21
- [x] K5 인물 6명 한복 조합(CharacterOutfitBuilder), 원격 플레이어·도 검률 적용, LAN 2인 테스트로 확인
- [x] K6 KHS UnhyeongungCollect 추출·URP 변환, 조사 지점 소품 20점 배치, 사랑채 한지 칸막이 3개(스캔에 없는 벽), 주행 21/21
- [~] K7 방 문 열림(근접 시 열림, 127짝)·메시 압축(빌드 557MB) 완료. LOD·라이트 굽기 남음
- [x] K8 Mixamo 리깅 + 서기·걷기 적용 (6명, 원격 플레이어·도 검률). 치마·머리 뼈 부착, 발 높이 보정 (asset_plan.md 3절)
- [x] 콘솔 오류 정리: URP 패키지 셰이더 캐시가 깨져 "Host type is not matching" 반복 → 해당 파일 강제 재임포트로 해결 (2026-10-12)


## 5차 작업 — 플레이 다듬기 · 확장 준비, 2026-10-13 (이어서 작업할 때 여기부터)

- [x] P1 뜬 돌·항아리 내려앉힘(KhsManorBuilder.SettleFloatingPieces), 발 접지(CharacterAnimator), 분이 고개, 얼굴(눈썹·코·입)·손
- [x] P2 점프(Space, 0.45m, 프레임 무관 적분)·앉기(C, 눈높이 62%, 네트워크 동기화)
- [ ] P3 Mixamo 추가 동작(사용자): 바닥에 앉기 → `Sitting.fbx`, 점프 → `Jump.fbx` (Without Skin, In Place). 넣고 Build Rigged Characters 재실행 (없어도 동작은 함)
- [x] P4 HUD·안내서 키 갱신, 주행 21/21, 콘솔 오류 0. 남음: 소품 충돌체 없음(통과됨)
- [x] P5 `docs/design/character_staging.md`
- [x] P6 `docs/design/scenario_platform_proposal.md` (8절 결정 사항은 사용자 답변 대기)
- [x] P7 시작 화면(Title 씬): 애니메이션 타이틀 → 메인 메뉴 → 사건첩(시나리오 카드·봉인 카드·해 봄 도장) → 방 열기/코드 참가/설정 창 → 로딩 → 고택 안 대기실(인물 카드·초대 코드·준비·시작) → 나가기. 온라인·LAN 2인 검증
  - 생성기: `Tools > Murder Mystery > Front End > Build Title Scene` / `Render Pictures` (배경·표지·초상은 고택에서 렌더)
  - 시나리오 목록: `Resources/Scenarios/<id>/scenario.json` (공개 정보만). 새 시나리오는 폴더 하나 추가
- [ ] P8 제안서 8절 결정 사항 (사용자) → 진행자·관전자 모드, 시나리오 패키지 이전
- [x] P9 알파 0.1.0 배포 (비공개 저장소 MurderMistery3D-release, playtest_guide 6절)
- [x] P10 뜬 구조물·문 보정 (2026-10-16): 땅 메시가 받침 밑으로 솟음, 자동 숙이기, 문 앞 디딤돌 자동 배치, 바깥마당 차단벽 보강. 조사 지점 주행 21/21, 문 직진 137짝 중 112짝 통과(나머지는 닫힌 문·봉인 구역·측정 오류, map_layout 10절)
- [x] P11 실제 키 입력 테스트(InputPlaytest)로 숙이기·문지방·점프·앉기 확인 및 보정, 숙이기 1.0 m·벽 앞 떨림 제거·숙인 자세 동기화, 문 137짝 중 실패 16짝(모두 의도), 2인 온라인 테스트, 0.1.1-alpha 빌드
- [x] P12 뜸·묻힘 전수조사 후 땅 복원 방식으로 재작성(뜬 조각 0, 묻힌 조각 0), 누락 초석 25개, 문짝 경첩 여닫이, 창문 앞 디딤돌 제거, 계단 오를 때 시점 완화, 0.1.2-alpha 빌드
- [x] P13 문 경첩: 문짝 원점(경첩 쪽)을 축으로 열림. 문턱이 바깥보다 0.25 m 넘게 높은 문은 낮은 쪽에 섬돌(0.3 m마다 한 단). 씬 재생성·봉인 확인까지 완료
- [x] P13 확인: 문 여닫이 방향(경첩=문짝 원점) Play 확인, 쌍문은 출입구 단위로 섬돌, 조사 지점 21/21·문 실패 23짝(모두 잠금·봉인·창문)
- [x] P14 (사용자 요청 2026-10-16) 스토리·단서 확장 — story_v2.md
  1. 잠긴 문에 다가가면 표시: "열쇠가 필요하다" / "열 수 없다" (LockedGates)
  2. 열쇠 줍기 → 해당 잠긴 문 열림. 의미 없는 열쇠·단서도 섞기
  3. 단서 대폭 추가: 의미 없는 단서 + 결정적이지만 바로 범인을 특정하지 못하는 단서를 섞어, 많이 찾아도 헷갈리게
  4. 인물마다 동기만이 아니라 지목될 만한 결정적 증거·행동을 부여, 서로 얽혀 사인(死因)·범행을 복잡하게 (설정집·시나리오북·Data json 갱신, 원작 독창성 유지)
  5. 건물 없는 큰 빈 공간: 용도는 사용자가 정함 → 작업 끝나면 사용자에게 물어볼 것
  - 완료: 잠긴 곳 6(표시·열쇠), 열쇠 3(하나는 미끼), 단서 51·지점 35, 인물마다 의심 물증, 사인 흐리는 물증(부자·이마 멍·흰 가루 둘·잠 오는 약), 도 검률 주제 27, 시나리오북 5권 갱신, 게임 안 시나리오북(수첩 → 시나리오북, 리딩 때 자동), 조사 점수 4점
  - [x] 빈 공간 용도: 밀담 방 (사용자 답변 2026-10-16)
- [x] P15 밀담 방: 1:1 밀담을 방 방식으로 교체(작은 방 2·큰 방 1, 동의·거절 3회·정원), 밀담 시간 단계 추가, 방별 음성 채널
