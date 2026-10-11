# 기술 결정 — 멀티플레이와 음성

> 상태: 결정안 v1 (2026-10-11). 구현 착수 전 사용자 확인 권장. 가격·한도는 바뀔 수 있으니 결제 전 공식 가격표 재확인.

## 1. 요구사항 정리

| 요구 | 출처 |
|---|---|
| 5인 고정, 한 판 약 100분, 친구끼리 방 만들어 플레이 | game_rules 1절 |
| 정보 비대칭이 핵심: 각자의 개인 Fact·소지품은 **본인 클라이언트에만** 전달돼야 함 | npc_rules R3, game_rules 3·4절 |
| 1:1 밀담 채널(음성·텍스트), 토론 때 전체 채널 | game_rules 5·7절 |
| 실시간 물리·액션 거의 없음. 이동 동기화 + 상태 변경 RPC 위주 | 장르 특성 |
| 소규모 개발, 무료 구간에서 시작 | 프로젝트 상황 |

## 2. 결정

| 항목 | 선택 | 대안 | 이유 |
|---|---|---|---|
| 네트워크 SDK | **Netcode for GameObjects (NGO)** | Mirror, FishNet, Photon Fusion | Unity 6 1st-party, Multiplayer Services와 바로 연동. 우리 게임은 틱 정밀도가 필요 없어 NGO의 단순한 RPC/NetworkVariable 모델로 충분 |
| 권한 구조 | **호스트 권한 (Client-Host)** | 전용 서버, Distributed Authority | 5인·턴 성격이라 호스트 1명이 `GameSession`을 돌리면 된다. 전용 서버 비용·운영 없음 |
| 방 만들기·접속 | **Unity Multiplayer Services – Sessions (Relay 포함)** | Steam 로비 | 방 코드 하나로 NAT 문제 없이 접속. Relay·Lobby는 무료 할당량 안에서 시작 가능 |
| 음성 | **Vivox** | Photon Voice, Steam Voice | UGS와 같은 인증 체계. 그룹 채널을 동적으로 만들 수 있어 밀담 채널 구현이 쉬움. 소규모에선 무료 구간(5,000 PCU)로 충분 |
| 텍스트 채팅 | Vivox 텍스트 또는 자체 RPC 채팅 | — | 음성 불가 플레이어용 필수 대안 |

## 3. 정보 보안 설계 (가장 중요)

머더미스터리는 "남의 화면에 뭐가 있는지 모르는 것"이 게임 그 자체다. 클라이언트에 정보를 보내는 순간 메모리 해킹이나 로그로 볼 수 있다고 가정한다.

- **진실의 원천은 호스트의 `GameSession` 하나.** 클라이언트는 의도(조사, 질문, 공개, 양도)만 보낸다.
- 개인 상태(보유 Fact, 소지품, 수첩, 남은 점수)는 **대상 클라이언트에게만** `ClientRpc`(Target)로 보낸다. `NetworkVariable`로 전체 복제하지 않는다.
- 공개 정보(공개 단서 게시판, 단계, 타이머, 투표 결과)만 전체 브로드캐스트한다.
- 시나리오 데이터(clues.json 등)는 클라이언트 빌드에도 들어가므로 **봉투 본문(sealedText)·다른 인물 시나리오북은 클라이언트 빌드에서 제외**하고, 호스트가 열람 시점에 해당 클라이언트에게만 보낸다. → 데이터를 "공용(카드 제목·장소)"과 "호스트 전용(본문·진상)"으로 분리하는 작업이 필요 (TODO).
- 호스트 플레이어 본인은 구조상 전체 데이터를 가진다. 친구끼리 하는 게임이라 허용하되, 공정성이 중요해지면 전용 서버나 신뢰할 수 있는 진행자 호스트로 전환.

## 4. 채널 구성 (Vivox)

| 상황 | 채널 | 비고 |
|---|---|---|
| 리딩 | 없음 (음소거) | 읽기에 집중 |
| 자기소개·토론·변론 | 전체 그룹 채널 | 사랑마당 집합 |
| 조사 단계 | 근거리 위치 채널(선택) + 밀담 채널 | 같은 방에 있으면 들리는 정도. 몰래 엿듣기 방지가 필요하면 끔 |
| 밀담 | 2인 전용 그룹 채널을 그때그때 생성 | 3분 후 호스트가 닫음 |
| 도 검률 질문 | 음성 없음 (UI) | NPC는 텍스트 |

## 5. 접속 끊김

- 같은 계정이 재접속하면 같은 좌석(인물)으로 복귀. 호스트가 좌석별 상태를 보관하므로 복원은 개인 상태 재전송으로 끝난다.
- 호스트가 끊기면 1차 버전에서는 게임 종료(호스트 이전 미지원). 로그를 남겨 재개 기능은 이후 검토.
- 투표 시점까지 돌아오지 않은 좌석은 투표 기권 처리.

## 6. 구현 순서 (제안)

1. `GameSession` 호출을 감싸는 `ISessionGateway` 인터페이스 도입 (지금의 `GameSessionHost`가 로컬 구현).
2. NGO + Sessions로 방 만들기/참가 UI, 5좌석 배정.
3. 의도 RPC (Investigate, Retrieve, Ask, Disclose, Give, Show, Open, Vote) → 호스트 처리 → 대상별 결과 전송.
4. 이동 동기화 (NetworkTransform, 낮은 전송률).
5. Vivox 전체/밀담 채널.
6. 데이터 공용/호스트 전용 분리.

## 참고

- [Unity Gaming Services 가격](https://unity.com/products/gaming-services/pricing)
- [Vivox 채널 종류](https://docs.unity.com/ugs/en-us/manual/vivox-unity/manual/Unity/developer-guide/channels/channel-types)
- [Vivox 위치 채널](https://docs.unity.com/ko-kr/vivox-core/developer-guide/channels/positional-channels)
- [UGS 2026 구성과 대안 (외부 블로그)](https://crux.supercraft.host/blog/unity-gaming-services-alternatives/)
