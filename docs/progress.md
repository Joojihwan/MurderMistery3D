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
- [ ] T6 블록아웃 씬 배치 + 플레이 테스트 (MCP 필요)
- [x] T7 멀티플레이·음성 기술 결정 문서 → `docs/design/tech_decisions.md`
- [ ] T8 KHS 에셋 적용 방식 + 한복 캐릭터 수급 방안 → `docs/design/asset_plan.md`
- [ ] T9 밸런스 검토 (조사 점수·단서 수·시간·범인 난이도) → `docs/design/balance_review.md`
- [ ] T10 전체 자체 검토 + 요약 보고

## 메모

- Unity MCP: 서버(8080)는 healthy인데 Unity 세션 미연결 상태였음 (2026-10-11 13:3x). Unity 창 `Window > MCP for Unity > Start Session` 필요. 미연결 시 Editor.log로 컴파일 오류 확인.
- T5 메모: 플레이어는 씬에 배치하지 않고 `PrototypeBootstrap`이 Play 시 자동 생성. 블록아웃 생성기에 안채 섬돌·찬방 문 위치 수정이 들어갔으므로 **씬 재생성 필요**(Tools > Manor > Build Blockout Scene).
- 오프라인 컴파일 확인: scratchpad의 Editor.csproj 방식 (Unity DLL 참조, dotnet build). 새 .cs 파일들의 .meta는 Unity가 열릴 때 생성 → 이후 커밋 필요.
