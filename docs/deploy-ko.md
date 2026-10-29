# Cloudflare 배포

Render는 사용하지 않는다. 무료 웹 서비스는 요청이 없으면 잠들기 때문에, NIVEX의 사이트와 API는 하나의 Cloudflare Worker로 둔다. 정적 페이지와 `/api/v1`이 같은 출처에서 제공된다.

## 1. 데이터베이스

```powershell
npx wrangler d1 create nivex
```

출력된 id를 `src/NIVEX.Api/wrangler.toml`의 `database_id`에 넣는다.

```powershell
npm run build:web
npx wrangler d1 migrations apply nivex --remote
```

명령은 `src/NIVEX.Api`에서 실행한다.

## 2. 비밀

운영 값으로 바꾼다. 저장소에 커밋하지 않는다.

```powershell
npx wrangler secret put AUTH_SIGNING_KEY
npx wrangler secret put CODE_PEPPER
npx wrangler secret put IP_HASH_PEPPER
npx wrangler secret put TURNSTILE_SECRET
npx wrangler secret put ADMIN_SETUP_TOKEN
npx wrangler secret put GEMINI_API_KEY
```

`GEMINI_API_KEY`는 NIVEX Worker가 Gemini 모델 목록과 연결 테스트를 서버에서 호출할 때만 사용한다. WPF 앱과 웹 브라우저에는 전달되지 않는다. Gemini 기능을 사용하지 않으면 설정하지 않아도 되며, 설정하지 않은 경우 앱은 AI 미구성 상태를 표시한다. 로컬 개발은 추적되지 않는 `.dev.vars`에만 넣는다. 현재 API는 모델 목록과 연결 테스트만 제공하며 AI Investigator, 진단 설명, Repair Chain 분석 생성은 아직 구현되어 있지 않다.

`wrangler.toml`의 `[vars]`에서 다음을 실제 값으로 수정한다.

- `TURNSTILE_MODE` = `production`
- `ALLOW_DEV_VERIFICATION` = `false`
- `TURNSTILE_SITE_KEY` = Turnstile 위젯의 사이트 키
- `EXPECTED_HOSTNAME` = 위젯이 열리는 호스트. 비어 있으면 운영 검증은 실패한다.
- `WEB_ORIGIN` = 브라우저 출처가 워커와 다를 때만 설정한다. 같은 워커에서 사이트를 제공하면 비워 둔다.
- `DOWNLOAD_URL` = `https://`로 시작하는 실제 설치 파일 주소. 없으면 다운로드 버튼이 나오지 않는다.

Turnstile 위젯의 action은 `device_create`, `device_recover`, `device_delete`를 그대로 전달해야 한다. 서버는 Cloudflare 응답의 action과 hostname이 둘 다 맞을 때만 통과시킨다.

## 3. 배포

```powershell
npx wrangler deploy
```

Workers 사용자 도메인을 연결하면 HTTPS가 적용된다. 배포 직후 `https://도메인/api/v1/health`가 `{"status":"ok"}`를 반환하는지 확인한다. 확인하기 전에는 서비스가 공개된 것으로 적지 않는다.

매일 03:17 UTC에 만료된 챌린지, 180일이 지난 인증 이벤트, 30일이 지난 남용 기록, 24시간이 지난 멱등 응답을 지운다.

## 4. 운영자

`X-Setup-Token`에 `ADMIN_SETUP_TOKEN`을 넣고, 운영자가 한 명도 없을 때만 동작한다.

```http
POST /api/v1/admin/bootstrap
{"email":"operator@example.com","password":"12자 이상의 비밀번호"}
```

이후 `/admin`에서 이메일과 비밀번호로 로그인한다. 장치 ID 로그인으로는 운영 API를 호출할 수 없다.

## 5. 데스크톱 게시

Windows에서:

```powershell
dotnet publish src/NIVEX.Desktop/NIVEX.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

게시 전에 사용자가 설정할 API 주소의 기본값은 로컬 주소다. 배포 빌드에서는 설정 파일 또는 첫 실행 설정에서 실제 워커 주소로 바꾸게 한다. 코드 서명 인증서는 아직 연결하지 않았다.

설치 파일을 실제 URL에 올린 뒤에만 `DOWNLOAD_URL`을 설정한다.

## 6. 백업

D1은 Cloudflare 대시보드의 export 또는 `wrangler d1 export`로 백업한다. 워커 로그에는 토큰, 활성화 코드, 복구 코드, 비밀번호를 남기지 않도록 필드 이름을 가린다.
