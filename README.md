# NIVEX

NIVEX는 Windows 데스크톱용 장치 ID 인증 프로그램과, 그 장치를 만들고 관리하는 웹사이트다. 버전 0.1.0은 계정 비밀번호 없이 동작하고, 결제는 연결되어 있지 않다.

API와 웹은 Cloudflare Workers에서 제공하도록 만들어 두었다. Render의 무료 인스턴스는 유휴 시 잠들기 때문에 서버로 쓰지 않는다.

## 구성

- `src/NIVEX.Api` — Cloudflare Worker, D1, Turnstile 검증, 장치·세션·운영자 API
- `src/NIVEX.Web` — 랜딩, 장치 ID 생성, 약관 초안, 운영 화면
- `src/NIVEX.Desktop` — WPF 로그인과 PC 상태
- `src/NIVEX.Domain`, `src/NIVEX.Application`, `src/NIVEX.Infrastructure` — 데스크톱 도메인과 DPAPI 저장
- `tests` — API 통합 테스트와 .NET 단위 테스트

장치 ID 형식은 `NIV-XXXX-XXXX-XXXX-XXXX`다. 첫 활성화 때 이 PC가 P-256 키를 만들고, 서버는 공개 키만 저장한다. 보이는 장치 ID만으로는 이미 활성화된 설치에 들어갈 수 없다. 다른 PC로 옮기려면 웹의 복구 코드가 필요하다.

## 로컬 실행

1. `src/NIVEX.Api/.dev.vars.example`을 `src/NIVEX.Api/.dev.vars`로 복사한다.
2. API 의존성을 설치하고 마이그레이션을 적용한다.

```powershell
npm install --prefix src/NIVEX.Api
npm install --prefix src/NIVEX.Web
npm run build:web
npm --prefix src/NIVEX.Api exec -- wrangler d1 migrations apply nivex --local
npm run dev:api
```

3. 브라우저에서 `http://127.0.0.1:8787`을 연다. 개발 모드에서는 장치 페이지의 "개발 환경 확인"이 서버 설정을 통과한 뒤에만 동작한다.
4. 데스크톱:

```powershell
dotnet run --project src/NIVEX.Desktop
```

설정의 API 주소 기본값은 `http://127.0.0.1:8787`이다. 로그인 유지에 쓰는 갱신 토큰과 개인 키는 DPAPI로 `%LocalAppData%\NIVEX\vault.bin`에 저장한다.

## 테스트

```powershell
npm test
dotnet test NIVEX.sln
```

## 배포 전에 사람이 해야 하는 일

이 저장소에는 운영 비밀, 도메인, Turnstile 키, 게시된 설치 파일이 없다. 자세한 순서는 `docs/deploy-ko.md`에 있다.

- Cloudflare에서 D1 데이터베이스 `nivex`를 만들고 `wrangler.toml`의 `database_id`를 바꾼다.
- `AUTH_SIGNING_KEY`, `CODE_PEPPER`, `IP_HASH_PEPPER`, `TURNSTILE_SECRET`, `ADMIN_SETUP_TOKEN`을 `wrangler secret put`으로 넣는다.
- Turnstile 사이트 키와 `EXPECTED_HOSTNAME`을 운영 값으로 바꾼다. `ALLOW_DEV_VERIFICATION`은 `false`여야 한다.
- 워커에 사용자 도메인을 연결한다.
- 데스크톱을 Windows에서 게시하고, 그 주소가 생긴 뒤에만 `DOWNLOAD_URL`을 넣는다.
- 코드 서명은 아직 없다. 서명하지 않은 실행 파일은 SmartScreen 경고가 날 수 있다.
- 개인정보 처리방침과 이용약관은 초안이다. 출시 전에 법률 검토가 필요하다.
- 운영자 계정은 배포 후 `POST /api/v1/admin/bootstrap`으로 한 번 만든다. 비밀번호는 소스에 넣지 않는다.

결제 버튼은 없다. 장치 기본 이용권은 FREE이고, PRO는 운영 API로만 부여된다.
