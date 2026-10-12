# 에셋 계획 — 고택 환경과 한복 캐릭터

> 상태: 결정안 v1 (2026-10-11). 에셋 다운로드·구매는 사용자 계정으로 직접 해야 하는 단계가 있음(아래 "사용자 작업" 표시).

## 1. 아트 방향

- **환경: 사실적 톤.** 국가유산청(KHS) 3D 스캔 자원이 사진 기반이라 사실적이다. 이를 살려 "비 갠 아침, 젖은 기와" 분위기를 조명으로 만든다.
- **캐릭터: 반사실적(세미 리얼).** 완전한 사실 인물은 비용이 크고 불쾌한 골짜기 위험이 있다. 얼굴은 단순화하고 옷(한복)의 재질과 실루엣으로 신분을 구분한다.
- 신분별 시각 구분이 추리에 쓰이므로 **실루엣 규칙**을 고정한다: 재하 = 키 크고 호리호리, 허 의원 = 작달막, 변 행수 = 몸집 크고 배가 나옴. 실내에서는 갓을 벗는다 (timeline.md 0절).

## 2. 환경 에셋

| 에셋 (Asset Store 이름) | 상태 | 용도 |
|---|---|---|
| **KHS Kim Myeong-gwans House Jeongeup** (김명관 고택) | **적용 완료** (2026-10-11), 790MB | 게임 맵 전체. 배정표는 map_layout.md 8절 |
| **KHS Joseon Royal Wear** | **적용 완료**, 3.2GB | 인물 의상 (3절) |
| **KHS - UnhyeongungCollect** (운현궁 생활유물) | **적용 완료** (2026-10-12), 1.8GB | 조사 지점 소품 — KhsManorBuilder `Props` 표 |
| KHS House of Gyedong, KHS Unhyungung | 불필요 | 김명관 고택으로 대체 |

### 받는 법과 적용 절차

1. **[사용자 작업]** Unity Asset Store에서 "Add to My Assets" → Unity의 Package Manager > My Assets에서 **Download만** 한다 (Import 누르지 않음). 받은 파일은 `%APPDATA%/Unity/Asset Store-5.x/KOREA HERITAGE SERVICE/`에 쌓인다.
2. Claude가 패키지에서 필요한 폴더만 GUID를 유지한 채 `Assets/ThirdParty/KHS/`로 푼다. KHS 패키지에는 TextMesh Pro 사본과 URP 전역 설정 파일이 섞여 있어, 그대로 Import하면 프로젝트 설정을 덮어쓴다. **원본은 git에 올리지 않는다** (`.gitignore`).
3. `Tools > Murder Mystery > KHS > Convert Built-in Materials to URP` (고택 재질 42개가 Built-in Standard). 의복은 자체 URP Shader Graph라 변환 불필요.
4. `Tools > Murder Mystery > KHS > Cap Texture Sizes`: 의복 1024, 고택 2048 (빌드 1.04GB → 686MB).
   `Tools > Murder Mystery > KHS > Compress Meshes`: 고택 Medium, 의복 Low (→ 557MB).
5. `Tools > Murder Mystery > KHS > Build Character Outfits` → `Tools > Manor > Build KHS Manor Scene`.
6. 크기: 두 패키지 모두 실측 미터 단위라 배율 조정이 필요 없었다 (기둥 높이·문 높이로 확인).

### 남은 일

- ~~실내 소품~~: 서안·촛대·문갑(머릿장)·화로·소반과 탕약 그릇·사방탁자·보료·등경·기름병·서가(2층장)·검은 항아리(쥐약 단지)·약탕관·뒤주·반닫이·약상자·방석 배치 완료. 소품에는 충돌체가 없어 몸이 통과한다.
- 스캔의 사랑채 안쪽 칸막이가 빠져 있어 한지 칸막이 3개를 세웠다 (map_layout.md 8절).
- ~~문~~: 방 문짝 127개에 `ProximityDoor` — 사람이 1.3m 안에 오면 문짝이 열리고(숨김) 떠나면 닫힌다. 여닫는 동작 애니메이션은 아직 없음.
- 성능: 메시 압축 완료. LOD·라이트맵 굽기는 남음 (스캔 메시가 2천 개 이상).

## 3. 한복 캐릭터 (6명: 플레이어 5 + 도 검률)

**적용 (2026-10-11): KHS Joseon Royal Wear 의복 조각을 인물별로 조합.** 생성기 `Assets/_Project/Scripts/Editor/CharacterOutfitBuilder.cs` → `Resources/Characters/<id>.prefab`. 원격 플레이어(`NetPlayer`)와 도 검률 NPC가 이 프리팹을 쓴다.

| 인물 | 차림 (신분 고증) | 키 | 비고 |
|---|---|---|---|
| 윤재하 (서자 선비) | 도포 · 갓 · 바지 · 버선 · 태사혜 | 1.80m | 키 크고 호리호리 |
| 허 의원 (중인 의원) | 두루마기 · 탕건(실내라 갓 없음) | 1.55m | 작달막 |
| 변 행수 (상인 행수) | 두루마기 위 쾌자(전복) · 갓 · 행전 | 1.70m | 너비·두께 1.3배 (배 나온 체격) |
| 큰며느리 정씨 (과부) | 흰 적삼 · 치마 · 쪽머리 · 당혜 | 1.58m | 수수한 차림 |
| 분이 (미혼 여종) | 속저고리 · 대슘치마 · 땋은 댕기머리 | 1.52m | |
| 도 검률 (관리) | 흑단령 · 사모 · 품대 · 흑화 | 1.68m | 사랑채 툇마루에서 마당을 향해 섬 |

- 한계: 의복은 **리깅 없는 정지 메시(A자 자세)**이고 몸이 없다. 그래서 단순한 얼굴(달걀형 머리·눈)과 목만 붙였다. 걷기 동작은 없다 (위치·방향만 동기화).
- 다음 단계 후보: (1) 리깅된 기본 인체 + 의복을 본에 붙이기(스키닝 재작업 필요), (2) 기존 안대로 AI 생성 + Mixamo 리깅. 플레이테스트에서 정지 인형이 거슬리는지 먼저 확인한다.
- 이전 후보 표(A~D)는 위 적용으로 보류.

## 4. 사용자 결정이 필요한 것

- [x] ~~김명관 고택 수급~~ → Asset Store 무료 패키지로 해결
- [ ] KHS Unhyeongung Collect 받기 (실내 소품)
- [ ] 인물 애니메이션 방식 (정지 의복 유지 / 리깅 인체 + 의복 / AI 생성 + Mixamo)
- [ ] 좌식 애니메이션 수급 방식
