# Room2Scan 작업 진행 기록

## 프로젝트 개요
스마트폰으로 방을 스캔하고 Unity 3D 에디터에서 가구를 배치하는 인테리어 앱.
- **room2scan_app**: React Native (Expo SDK 51) 모바일 앱
- **room2scan_unity**: Unity 3D 에디터 프로젝트
- **통신**: `unity-bridge/v1` JSON 메시지 프로토콜

---

## 마일스톤

### P0 — Unity Bridge 스파이크 ✅
- [x] unity-bridge/v1 메시지 스키마 정의
- [x] room-json/v1, layout-json/v1, furniture-catalog-json/v1 스키마 정의
- [x] `UnityBridge.cs` 메시지 수신/응답 구현
- [x] `RoomManager.cs` PLY + GLB 메시 로드
- [x] `unityBridge.ts` RN 브리지 명령 생성
- [x] `UnityEditorScreen.tsx` 폴백 시뮬레이터

### P0.5 — 네이티브 Unity 뷰 연동 🔄 진행 중
> 목표: 실제 Android 기기에서 Unity 뷰가 RN 안에 렌더링되는 것 확인

- [x] `@azesmway/react-native-unity@1.0.11` package.json 등록
- [x] Android 네이티브 코드 (prebuild 결과물) 커밋
- [x] `settings.gradle`에 `:unityLibrary` 경로 등록 (`unity/builds/android/unityLibrary`)
- [x] `UPlayer.java` 캐스팅 버그 패치 적용 (`patches/`)
- [x] `npm install` — node_modules 설치 및 patch-package 적용
- [x] Unity 프로젝트 → Android Library 내보내기
  - 실제 내보낸 경로: `C:\Users\park\room2scan_app\unity\builds\android\unityLibrary`
  - Unity 메뉴: `Room2Scan > Build > Export Android Unity Library`
- [x] `npm run android` 빌드 및 실행 확인
- [x] Unity 뷰 렌더링 확인 (에뮬레이터) ✅

### P1 — 가구 조작 기능 ✅
> 전제: P0.5 완료 (2026-05-29) — 에뮬레이터에서 Unity 뷰 RN 앱 내 렌더링 확인
- [x] 가구 추가 (AddFurniture 명령) — 큐브 프리미티브, 파란색 머티리얼
- [x] 가구 선택 (SelectFurniture) — 선택 시 노란색으로 색상 변경
- [x] 가구 회전 (RotateSelected, deltaDeg=45)
- [x] 가구 삭제 (DeleteSelected)
- [x] 에디터 리셋 (ResetEditor — 룸 + 가구 전체 초기화)
- [x] layout-json/v1 저장 — FurnitureManager.GetLayoutItems() 실제 데이터 직렬화
- [ ] 충돌 검사 및 유효성 검사 (P2)

---

## 작업 로그

### 2026-05-29 (P1 완료)
- **FurnitureManager.cs 신규**: AddFurniture / SelectFurniture / RotateSelected / DeleteSelected / ClearAll / GetLayoutItems 완전 구현
- **UnityBridge.cs 업데이트**: P1 명령 핸들러 추가, BridgeAddFurnitureEnvelope / BridgeRotateEnvelope 직렬화 클래스 추가, SendLayoutSaved가 실제 GetLayoutItems() 데이터를 직렬화
- **unityBridge.ts 업데이트**: createAddFurniturePayload / createSelectFurniturePayload / createRotateSelectedPayload / createDeleteSelectedPayload / createResetEditorPayload / createLoadFurnitureCatalogPayload 추가
- **UnityEditorScreen.tsx 업데이트**: 가구 툴바 (Add / Select / Rotate / Delete) + ResetEditor 버튼 추가, 비활성화 상태 처리, 아이템 카운터 표시

### 2026-05-29
- **두 레포 상태 점검**
  - `room2scan_unity`: `main` 최신, 로컬 미커밋 변경사항 8개 파일 (Bridge 리팩터, Rooms/ 신규)
  - `room2scan_app`: `origin/main`보다 1커밋 뒤처짐 → `git pull` 완료
  - 당겨온 커밋: `05936ec Add Unity bridge P0 integration` (Android 네이티브 코드, UnityEditorScreen, unityBridge.ts 등 41개 파일)
- **P0.5 착수**: npm install 및 Unity Android 내보내기 진행 중
- **버그 수정**: `AndroidExportBuilder.cs`의 `DefaultOutputPath`가 `C:/Users/park/room2scan_app`으로 잘못 설정되어 있어 `E:/unity/room2scan_app/unity/builds/android`로 수정
- **버그 수정**: `patches/@azesmway+react-native-unity+1.0.11.patch` 파일에 `index` 행 누락으로 patch-package가 파싱 실패 → 수동 패치 적용 후 `npx patch-package`로 파일 재생성
- **asmdef 추가**: `glTFast.Export`가 `autoReferenced: false`라 컴파일 에러 발생 → `Room2Scan.Runtime`, `Room2Scan.RoomsEditor`, `Room2Scan.BridgeEditor` 3개 asmdef 생성으로 해결
- **unityLibrary 경로 문제**: E: 드라이브가 exFAT이라 심볼릭 링크 불가 → `android/settings.gradle`을 `unity.local.properties`에서 경로를 읽도록 수정, `android/unity.local.properties`에 C: 절대경로 지정 (gitignore에 추가)
