# 출시 전 확인 목록 (내부용, 사이트에 게시되지 않음)

공개 소개 페이지(`/about`)는 `src/NIVEX.Web/src/operator.ts`에 입력된 값만 표시합니다. 비어 있는 항목은 "아직 게시되지 않음"으로 나오며, 사업자 등록·통신판매업 신고·인증을 주장하지 않습니다.

## 운영자가 직접 입력해야 하는 값 (`operator.ts`)

- [ ] `operatorName`: 실제 운영 주체(개인 또는 상호)
- [ ] `publicContact`: 공개해도 되는 연락처 (개인 주소·개인 전화번호는 넣지 말 것)
- [ ] `supportEmail`: 실제로 수신하는 지원 이메일 (입력하면 지원 페이지의 `support@example.com` 자리표시자도 대체됨)
- [ ] `businessStatus`: 사업자 등록이 실제로 끝났을 때만 `registered`
- [ ] `businessNumber`: 실제 번호 (`registered`일 때만 표시)
- [ ] `mailOrderStatus`: 통신판매업 신고 상태 (`reported` / `not_required` / `not_reported`)
- [ ] `mailOrderNumber`: 실제 신고번호 (`reported`일 때만 표시)
- [ ] `salesNotice`: 판매·환불 등 서비스 안내

## 작성·검토가 필요한 문서

- [ ] 이용약관 (`/terms`는 영어 초안)
- [ ] 개인정보 처리방침 (`/privacy`는 영어 초안, 한국어 버전 없음)
- [ ] 라이선스 안내 (문서 없음, `/about`에 "작성 필요"로 표시 중)
- [ ] 보존 기간 확정: IP당 3개 제한용 해시 기록(`ip_claims`)은 장치 삭제 후에도 유지되며 정리되지 않음

## 법률·표시 의무 점검 (전문가 확인 권장)

- [ ] 유료 판매 시 전자상거래법상 사업자 정보 표시 의무 및 통신판매업 신고 대상 여부
- [ ] 개인정보 수집·이용 고지 및 해외 서버(Cloudflare) 처리 고지 필요 여부
- [ ] 환불·청약철회 안내 (현재 결제 기능 없음)
- [ ] 설치 파일 코드 서명 미설정(SmartScreen 경고) 및 체크섬 게시 여부
