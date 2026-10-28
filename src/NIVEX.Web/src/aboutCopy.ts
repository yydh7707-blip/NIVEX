export interface FeatureCopy {
  name: string;
  body: string;
  detail: string;
}

export interface DocCopy {
  href: string;
  title: string;
  body: string;
  status: "published" | "draft" | "missing";
}

export interface AboutCopy {
  pageTitle: string;
  langLabel: string;
  navAbout: string;
  chromeNav: [string, string][];
  chromeFooter: [string, string][];
  rights: string;
  kicker: string;
  heroTitle: string;
  heroLede: string;
  ctaDiscover: string;
  ctaFeatures: string;
  ctaDownload: string;
  factsTitle: string;
  facts: [string, string][];
  factVersionLabel: string;
  factOffline: string;
  purposeTitle: string;
  purposeBody: string[];
  featuresTitle: string;
  featuresLede: string;
  statusAvailable: string;
  features: FeatureCopy[];
  distTitle: string;
  distBody: string;
  distItems: [string, string][];
  privacyTitle: string;
  privacyLede: string;
  privacyItems: [string, string][];
  privacyLink: string;
  docsTitle: string;
  docsLede: string;
  docStatus: Record<"published" | "draft" | "missing", string>;
  docs: DocCopy[];
  operatorTitle: string;
  operatorLede: string;
  operatorEmpty: string;
  operatorNone: string;
  operatorFields: Record<"name" | "contact" | "email" | "business" | "businessNumber" | "mailOrder" | "mailOrderNumber" | "sales", string>;
  businessValues: Record<"registered" | "not_registered", string>;
  mailOrderValues: Record<"reported" | "not_required" | "not_reported", string>;
  faqTitle: string;
  faq: [string, string][];
  disclaimer: string;
}

export const en: AboutCopy = {
  pageTitle: "About NIVEX",
  langLabel: "Language",
  navAbout: "About",
  chromeNav: [["/", "Product"], ["/about", "About"], ["/device", "Device ID"], ["/download", "Download"], ["/releases", "Releases"], ["/support", "Support"]],
  chromeFooter: [["/guide", "Sign-in"], ["/privacy", "Privacy"], ["/terms", "Terms"], ["/support", "Support"], ["/admin", "Admin"]],
  rights: "© NIVEX. All rights reserved.",
  kicker: "Official product information",
  heroTitle: "NIVEX — PC Management, Connected",
  heroLede: "NIVEX is a Windows application designed to bring together PC diagnostics, system monitoring, network analysis, workflow automation, and diagnostic reporting in one interface.",
  ctaDiscover: "Discover NIVEX",
  ctaFeatures: "Explore Features",
  ctaDownload: "Download",
  factsTitle: "At a glance",
  facts: [
    ["Product", "NIVEX"],
    ["Platform", "Windows 10 (64-bit, 1809 or later) and Windows 11, x64"],
    ["Distribution", "Installer from this website"],
    ["Code signing", "Not configured. Windows SmartScreen may warn about the installer."],
  ],
  factVersionLabel: "Version",
  factOffline: "Shown on the download page",
  purposeTitle: "Purpose and scope",
  purposeBody: [
    "NIVEX is software designed to help users inspect their Windows environment, perform supported system diagnostics, and manage repetitive tasks.",
    "Its intended use and supported capabilities are described in the official feature documentation and applicable terms of use.",
    "NIVEX is designed to help users inspect and manage their own PC environment. Operations that modify system settings should account for their potential impact and request user confirmation when appropriate.",
  ],
  featuresTitle: "What NIVEX provides",
  featuresLede: "Every area below exists in the current desktop build. Limits are stated next to each one.",
  statusAvailable: "Available in this version",
  features: [
    {
      name: "NIVEX Fix",
      body: "Diagnose PC conditions and provide safe ways to address identified issues.",
      detail: "Checks the OS, disk space, temporary files, NIVEX files, network adapter, DNS, and internet reachability. The full scan adds the gateway and read-only SFC and DISM health checks, which need administrator rights. Repairs are limited to clearing temporary files, flushing the DNS cache, and (administrator only) Winsock and TCP/IP resets, and each asks for your confirmation first; the network resets need a restart and cannot be undone automatically. After a repair the affected check is run again and the result is shown as resolved, partially improved, not resolved, or cannot verify. A rule-based analysis lists the evidence and how certain each cause is, and a report can be exported as an HTML file you review before sharing. Read-only tools list startup entries (Run registry keys), recent Windows error records and memory dump files (listed, not analysed), and large files in a folder you choose. Startup entries can be turned off and on again with an undo log (security software entries are protected and never changed), devices reporting errors are listed, and a program troubleshooter checks a chosen .exe file. Maintenance profiles store which checks to run, and an optional scheduled scan is read-only and runs only while NIVEX is open. NIVEX does not install or remove drivers.",
    },
    {
      name: "NIVEX Monitor",
      body: "View supported system information and monitor measurable CPU, memory, and other resource metrics.",
      detail: "Shows CPU, memory, disk and GPU readings. A value that the PC does not report is shown as unavailable instead of an estimate.",
    },
    {
      name: "Network Lab",
      body: "Test network connectivity, DNS resolution, response times, and other supported network metrics.",
      detail: "Reads adapters, tests the gateway and public servers (1.1.1.1 and 8.8.8.8) with ping, measures DNS lookups, lets you test a host you enter, and compares the result with the previous run.",
    },
    {
      name: "NIVEX Flow",
      body: "Automate sequences of user-defined tasks through configurable workflows.",
      detail: "Steps can open a folder, file or URL, launch a program, wait, ask for confirmation, or check the previous step. Flow does not run anything you did not define.",
    },
    {
      name: "NIVEX Profiles",
      body: "Save and reuse preferred work environments and launch configurations.",
      detail: "A profile holds programs, folders and URLs and starts them through the same step engine as Flow.",
    },
    {
      name: "NIVEX Insight",
      body: "Review recorded diagnostic and task results and generate reports from available data.",
      detail: "Records the results of Fix, repairs, Network Lab, Flow and Profiles on this PC (up to 1,000 entries) and builds an HTML report for a date range.",
    },
  ],
  distTitle: "Distribution and authenticity",
  distBody: "NIVEX is Windows software distributed through its official website. Users can review the download source, version information, applicable usage terms, and available support resources.",
  distItems: [
    ["Official download", "The Download page of this website."],
    ["Updates", "There is no automatic updater. To update, install the newer version from the Download page."],
    ["Removal", "The installer registers NIVEX so it can be removed from the Windows installed apps list."],
    ["Integrity information", "No checksum or signature is published yet, and no security scan result is claimed. Treat this as unverified until it is published."],
    ["Support", "Use the Support page."],
  ],
  privacyTitle: "Device ID and data",
  privacyLede: "NIVEX identifies an installation with a Device ID and an activation code. There is no password. This summary follows the current server code; the policy pages are still drafts awaiting operator review.",
  privacyItems: [
    ["Device ID", "A public name for one installation. It is used to find the device record during sign-in."],
    ["Activation code", "Shown once after a Device ID is created. It activates one installation, which then signs in with its own key."],
    ["Stored on the server", "Device ID, hashes of the activation and recovery codes, the installation public key, a random installation ID, refresh-token hashes, sign-in events, and hashed IP values used for rate limits and the three-Device-IDs-per-IP limit, plus an encrypted copy of each activation code until the device is activated so the IP that created it can look it up again (erased on activation)."],
    ["Not collected", "A password, card numbers, MAC addresses, hardware serial numbers, or the raw IP address."],
    ["Where it goes", "The NIVEX API runs on Cloudflare Workers with a Cloudflare D1 database."],
    ["Retention", "Refresh tokens until expiry or revocation, sign-in events 180 days, abuse events 30 days, idempotency responses 24 hours. The hashed IP record behind the per-IP limit is currently kept after a device is deleted so that the limit cannot be reset. Final retention terms are pending operator review."],
    ["Deletion", "On the Support page, a device can be deleted with its recovery code. An operator audit entry may keep the Device ID and time."],
  ],
  privacyLink: "Read the full privacy policy",
  docsTitle: "Official documents",
  docsLede: "Each link points to a page that exists. Items marked as drafts or not yet written are not final.",
  docStatus: { published: "Available", draft: "Draft", missing: "Not written yet" },
  docs: [
    { href: "#about", title: "About NIVEX", body: "Purpose and scope.", status: "published" },
    { href: "#features", title: "Features and scope", body: "What each area does today.", status: "published" },
    { href: "/terms", title: "Terms of use", body: "Draft awaiting review.", status: "draft" },
    { href: "/privacy", title: "Privacy policy", body: "Draft awaiting review.", status: "draft" },
    { href: "", title: "License notice", body: "Not written yet. It will be linked here once it exists.", status: "missing" },
    { href: "/download", title: "Download and updates", body: "Installer and version.", status: "published" },
    { href: "/support", title: "Customer support", body: "Contact, recovery and device deletion.", status: "published" },
    { href: "#operator", title: "Operator and business information", body: "Shows only what the operator has published.", status: "published" },
  ],
  operatorTitle: "Operator and business information",
  operatorLede: "Only details the operator has entered and confirmed are shown. NIVEX does not claim any certification or approval.",
  operatorEmpty: "Operator and business details have not been published yet. No business registration, mail-order report, certification or approval is claimed on this page.",
  operatorNone: "Not published yet",
  operatorFields: {
    name: "Operator",
    contact: "Public contact",
    email: "Support email",
    business: "Business registration",
    businessNumber: "Business registration number",
    mailOrder: "Mail-order business report",
    mailOrderNumber: "Mail-order report number",
    sales: "Sales and service notice",
  },
  businessValues: { registered: "Registered", not_registered: "Not registered" },
  mailOrderValues: { reported: "Reported", not_required: "Not required", not_reported: "Not reported" },
  faqTitle: "Frequently asked questions",
  faq: [
    ["What is NIVEX?", "A Windows application that combines PC diagnostics, monitoring, network analysis, workflow automation, profiles and reporting. It supports Windows 10 (64-bit, 1809 or later) and Windows 11 on x64."],
    ["Why does NIVEX use a Device ID?", "A Device ID is the public name of one installation. It is not a password. The installation proves itself with its own key after activation, and the server decides whether a device is valid or revoked."],
    ["Does NIVEX change my PC settings?", "Only some actions do. Clearing temporary files and flushing the DNS cache are repairs you start yourself, and both ask for confirmation first (the cleanup shows its list). Monitoring, network tests and the SFC and DISM health checks only read. Flow and Profiles open or launch items you defined, and a Flow can ask for confirmation."],
    ["Can I uninstall NIVEX or sign out?", "Yes. The desktop app has a sign-out command, and the installer registers an entry in the Windows installed apps list. Deleting the device record is done on the Support page with the recovery code."],
    ["Where can I get help?", "Open the Support page. If no support email is shown in the operator section, it has not been published yet."],
  ],
  disclaimer: "This page describes the product as it is today. It is not legal advice, a certification, or a guarantee of suitability for any particular use.",
};

export const ko: AboutCopy = {
  pageTitle: "NIVEX 소개",
  langLabel: "언어",
  navAbout: "소개",
  chromeNav: [["/", "제품"], ["/about", "소개"], ["/device", "Device ID"], ["/download", "다운로드"], ["/releases", "릴리스"], ["/support", "지원"]],
  chromeFooter: [["/guide", "로그인 안내"], ["/privacy", "개인정보"], ["/terms", "이용약관"], ["/support", "지원"], ["/admin", "관리"]],
  rights: "© NIVEX. 모든 권리 보유.",
  kicker: "공식 제품 안내",
  heroTitle: "NIVEX — PC 관리와 작업 환경을 하나로",
  heroLede: "NIVEX는 PC 상태 진단, 시스템 모니터링, 네트워크 분석, 작업 자동화 및 진단 보고서를 하나의 환경에서 사용할 수 있도록 설계된 Windows 소프트웨어입니다.",
  ctaDiscover: "NIVEX 알아보기",
  ctaFeatures: "주요 기능 확인",
  ctaDownload: "다운로드",
  factsTitle: "한눈에 보기",
  facts: [
    ["제품명", "NIVEX"],
    ["지원 환경", "Windows 10(64비트, 1809 이상) 및 Windows 11, x64"],
    ["배포 방식", "이 웹사이트의 설치 파일"],
    ["코드 서명", "설정되어 있지 않습니다. Windows SmartScreen 경고가 표시될 수 있습니다."],
  ],
  factVersionLabel: "버전",
  factOffline: "다운로드 페이지에서 확인",
  purposeTitle: "개발 목적과 사용 범위",
  purposeBody: [
    "NIVEX는 Windows 환경에서 PC 상태를 확인하고, 지원되는 시스템 진단을 수행하며, 반복적인 작업을 관리하기 위한 소프트웨어입니다.",
    "프로그램의 기능과 사용 목적은 공식 기능 안내 및 이용약관에 설명된 범위를 따릅니다.",
    "NIVEX는 사용자가 자신의 PC에서 정보를 확인하고 작업을 관리할 수 있도록 설계됩니다. 시스템을 변경하는 기능은 작업의 영향을 고려하고 필요한 경우 사용자 확인을 거쳐야 합니다.",
  ],
  featuresTitle: "NIVEX가 제공하는 기능",
  featuresLede: "아래 기능은 모두 현재 데스크톱 버전에 구현되어 있습니다. 각 기능의 제한 사항도 함께 적었습니다.",
  statusAvailable: "현재 버전에서 제공",
  features: [
    {
      name: "NIVEX Fix",
      body: "PC 상태를 검사하고 확인된 문제에 대한 안전한 해결 방법을 제공합니다.",
      detail: "운영체제, 디스크 여유 공간, 임시 파일, NIVEX 파일, 네트워크 어댑터, DNS, 인터넷 연결을 검사합니다. 전체 검사는 게이트웨이와 읽기 전용 SFC·DISM 상태 검사를 추가하며, 이 둘은 관리자 권한이 필요합니다. 복구는 임시 파일 정리, DNS 캐시 초기화, (관리자 전용) Winsock·TCP/IP 초기화로 한정되며, 각각 실행 전에 사용자 확인을 요청합니다. 네트워크 초기화는 재시작이 필요하고 자동으로 되돌릴 수 없습니다. 복구 후에는 해당 항목을 다시 검사해 해결 확인·부분 개선·해결되지 않음·검증 불가로 표시합니다. 규칙 기반 원인 분석은 근거와 판단의 확실성을 함께 보여주며, 보고서는 HTML 파일로 내보내 공유 전에 직접 확인할 수 있습니다. 읽기 전용 도구로 시작 프로그램(Run 레지스트리 키), 최근 Windows 오류 기록, 메모리 덤프 파일(목록만, 분석 없음), 선택한 폴더의 큰 파일을 조회할 수 있습니다. 시작 프로그램은 되돌리기 기록과 함께 끄고 켤 수 있으며(보안 소프트웨어 항목은 보호되어 변경되지 않음), 오류를 보고하는 장치 목록과 선택한 .exe 파일의 프로그램 문제 해결 점검을 제공합니다. 유지 관리 프로필은 실행할 검사 항목만 저장하고, 선택 사항인 예약 검사는 읽기 전용이며 NIVEX가 실행 중일 때만 동작합니다. NIVEX는 드라이버를 설치하거나 제거하지 않습니다.",
    },
    {
      name: "NIVEX Monitor",
      body: "지원되는 시스템 정보를 확인하고 CPU, 메모리 및 기타 측정 가능한 자원의 상태를 보여줍니다.",
      detail: "CPU, 메모리, 디스크, GPU 수치를 표시합니다. PC가 제공하지 않는 값은 추정하지 않고 확인 불가로 표시합니다.",
    },
    {
      name: "Network Lab",
      body: "네트워크 연결 상태, DNS 조회 및 응답 시간 등의 측정을 지원합니다.",
      detail: "어댑터 정보를 읽고, 게이트웨이와 공개 서버(1.1.1.1, 8.8.8.8)에 핑을 보내며, DNS 조회 시간을 측정합니다. 직접 입력한 호스트도 점검할 수 있고, 이전 결과와 비교해 보여줍니다.",
    },
    {
      name: "NIVEX Flow",
      body: "사용자가 정의한 작업을 순서대로 실행하는 워크플로 자동화 기능입니다.",
      detail: "단계로 폴더·파일·URL 열기, 프로그램 실행, 대기, 사용자 확인, 이전 단계 결과 확인을 지정할 수 있습니다. 사용자가 정의하지 않은 작업은 실행하지 않습니다.",
    },
    {
      name: "NIVEX Profiles",
      body: "자주 사용하는 작업 환경과 실행 항목을 프로필로 저장하고 재사용합니다.",
      detail: "프로필에는 프로그램, 폴더, URL을 담을 수 있으며 Flow와 같은 단계 실행 엔진으로 시작됩니다.",
    },
    {
      name: "NIVEX Insight",
      body: "실제 수행한 진단 및 작업 결과를 기록하고 보고서로 확인할 수 있도록 지원합니다.",
      detail: "Fix, 복구, Network Lab, Flow, Profiles의 결과를 이 PC에 기록하고(최대 1,000건) 기간을 정해 HTML 보고서를 만들 수 있습니다.",
    },
  ],
  distTitle: "정품 소프트웨어 및 배포 정보",
  distBody: "NIVEX는 공식 웹사이트를 통해 제공되는 Windows 소프트웨어입니다. 사용자는 다운로드한 파일의 출처와 버전을 확인하고, 프로그램의 공식 사용 안내 및 이용약관을 확인할 수 있습니다.",
  distItems: [
    ["공식 다운로드", "이 웹사이트의 다운로드 페이지입니다."],
    ["업데이트", "자동 업데이트 기능은 없습니다. 새 버전은 다운로드 페이지에서 받아 설치하세요."],
    ["제거", "설치 프로그램이 Windows 설치된 앱 목록에 등록하므로 그곳에서 제거할 수 있습니다."],
    ["무결성 정보", "체크섬과 전자서명은 아직 게시되지 않았고, 보안 검사 결과도 주장하지 않습니다. 게시되기 전까지는 확인되지 않은 상태로 보세요."],
    ["기술 지원", "지원 페이지를 이용하세요."],
  ],
  privacyTitle: "Device ID와 데이터",
  privacyLede: "NIVEX는 Device ID와 활성화 코드로 설치본을 식별하며 비밀번호는 없습니다. 아래 요약은 현재 서버 코드를 기준으로 하며, 정책 페이지는 아직 운영자 검토를 기다리는 초안입니다.",
  privacyItems: [
    ["Device ID", "설치본 하나를 가리키는 공개 이름입니다. 로그인할 때 장치 기록을 찾는 데 쓰입니다."],
    ["활성화 코드", "Device ID를 만든 뒤 한 번만 표시됩니다. 설치본 하나를 활성화하며, 이후 그 설치본은 자체 키로 로그인합니다."],
    ["서버에 저장되는 정보", "Device ID, 활성화 코드와 복구 코드의 해시, 설치본 공개 키, 무작위 설치 ID, 리프레시 토큰 해시, 로그인 이벤트, 요청 제한과 IP당 Device ID 3개 제한에 쓰는 IP 해시값, 활성화 전까지 같은 IP에서 다시 조회할 수 있도록 암호화해 보관하는 활성화 코드(활성화 시 삭제)."],
    ["수집하지 않는 정보", "비밀번호, 카드 번호, MAC 주소, 하드웨어 일련번호, 원본 IP 주소."],
    ["전달되는 서버", "NIVEX API는 Cloudflare Workers에서 동작하고 Cloudflare D1 데이터베이스를 사용합니다."],
    ["보존 기간", "리프레시 토큰은 만료 또는 폐기 시까지, 로그인 이벤트 180일, 남용 이벤트 30일, 멱등성 응답 24시간입니다. IP당 제한에 쓰는 IP 해시 기록은 제한을 초기화할 수 없도록 장치를 삭제해도 현재는 유지됩니다. 최종 보존 기준은 운영자 검토 후 확정됩니다."],
    ["삭제", "지원 페이지에서 복구 코드로 장치를 삭제할 수 있습니다. 운영자 감사 기록에는 Device ID와 시각이 남을 수 있습니다."],
  ],
  privacyLink: "개인정보 처리방침 전체 보기",
  docsTitle: "공식 문서",
  docsLede: "각 링크는 실제로 존재하는 페이지로 연결됩니다. 초안이거나 아직 작성되지 않은 항목은 표시되어 있습니다.",
  docStatus: { published: "제공 중", draft: "초안", missing: "작성 필요" },
  docs: [
    { href: "#about", title: "NIVEX 소개", body: "개발 목적과 사용 범위.", status: "published" },
    { href: "#features", title: "주요 기능 및 사용 범위", body: "각 기능이 현재 하는 일.", status: "published" },
    { href: "/terms", title: "이용약관", body: "검토 전 초안입니다.", status: "draft" },
    { href: "/privacy", title: "개인정보 처리방침", body: "검토 전 초안입니다.", status: "draft" },
    { href: "", title: "라이선스 안내", body: "아직 작성되지 않았습니다. 작성되면 이곳에 연결합니다.", status: "missing" },
    { href: "/download", title: "다운로드 및 업데이트 안내", body: "설치 파일과 버전.", status: "published" },
    { href: "/support", title: "고객 지원", body: "문의, 복구, 장치 삭제.", status: "published" },
    { href: "#operator", title: "운영자 및 사업자 정보", body: "운영자가 게시한 내용만 표시합니다.", status: "published" },
  ],
  operatorTitle: "운영자 및 사업자 정보",
  operatorLede: "운영자가 입력하고 확인한 정보만 표시합니다. NIVEX는 어떠한 인증이나 승인도 받았다고 주장하지 않습니다.",
  operatorEmpty: "운영자 및 사업자 정보는 아직 게시되지 않았습니다. 이 페이지는 사업자 등록, 통신판매업 신고, 인증 또는 승인을 받았다고 주장하지 않습니다.",
  operatorNone: "아직 게시되지 않음",
  operatorFields: {
    name: "운영 주체",
    contact: "공개용 연락처",
    email: "공식 지원 이메일",
    business: "사업자 등록 상태",
    businessNumber: "사업자 등록번호",
    mailOrder: "통신판매업 신고",
    mailOrderNumber: "통신판매업 신고번호",
    sales: "서비스 및 판매 관련 안내",
  },
  businessValues: { registered: "등록 완료", not_registered: "등록하지 않음" },
  mailOrderValues: { reported: "신고 완료", not_required: "신고 대상 아님", not_reported: "신고하지 않음" },
  faqTitle: "자주 묻는 질문",
  faq: [
    ["NIVEX는 어떤 프로그램인가요?", "PC 진단, 모니터링, 네트워크 분석, 작업 자동화, 프로필, 보고서를 하나로 묶은 Windows 프로그램입니다. Windows 10(64비트, 1809 이상)과 Windows 11의 x64 환경을 지원합니다."],
    ["Device ID는 왜 사용하나요?", "Device ID는 설치본 하나의 공개 이름이며 비밀번호가 아닙니다. 활성화 이후 설치본은 자체 키로 자신을 증명하고, 장치가 유효한지 폐기되었는지는 서버가 판단합니다."],
    ["NIVEX가 제 PC의 설정을 변경하나요?", "일부 기능만 그렇습니다. 임시 파일 정리와 DNS 캐시 초기화는 사용자가 직접 시작하는 복구이며 둘 다 실행 전에 확인을 요청합니다(정리는 목록을 보여줍니다). 모니터링, 네트워크 테스트, SFC·DISM 상태 검사는 읽기만 합니다. Flow와 Profiles는 사용자가 정의한 항목을 열거나 실행하며, Flow는 확인 단계를 둘 수 있습니다."],
    ["프로그램을 제거하거나 인증을 해제할 수 있나요?", "가능합니다. 데스크톱 앱에 로그아웃 명령이 있고, 설치 프로그램이 Windows 설치된 앱 목록에 항목을 등록합니다. 장치 기록 삭제는 지원 페이지에서 복구 코드로 진행합니다."],
    ["문제가 발생하면 어떻게 도움을 받을 수 있나요?", "지원 페이지를 이용하세요. 운영자 정보에 지원 이메일이 없다면 아직 게시되지 않은 것입니다."],
  ],
  disclaimer: "이 페이지는 제품의 현재 상태를 설명합니다. 법률 자문, 인증, 또는 특정 용도에 대한 적합성 보증이 아닙니다.",
};
