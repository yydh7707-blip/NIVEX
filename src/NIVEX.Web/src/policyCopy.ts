// Policy documents. Content only: the layout lives in policy.ts.
// Placeholders {{operator}} and {{email}} are replaced with the values in operator.ts, or with a
// visible "not published yet" marker. Nothing here is generated or rewritten automatically.

export type Block =
  | { t: "p"; x: string }
  | { t: "ul"; x: string[] }
  | { t: "table"; head: [string, string]; rows: [string, string][] };

export interface PolicySection {
  id: string;
  title: string;
  blocks: Block[];
}

export interface PolicyDoc {
  title: string;
  lead: string;
  sections: PolicySection[];
}

export type PolicyKey = "privacy" | "terms";
export type PolicyLang = "ko" | "en";

// ISO date of the last content edit. The effective date stays empty until the operator confirms it.
export const policyMeta: Record<PolicyKey, { updated: string; effective: string }> = {
  privacy: { updated: "2026-10-29", effective: "" },
  terms: { updated: "2026-10-29", effective: "" },
};

const p = (x: string): Block => ({ t: "p", x });
const ul = (...x: string[]): Block => ({ t: "ul", x });
const table = (head: [string, string], rows: [string, string][]): Block => ({ t: "table", head, rows });

const privacyEn: PolicyDoc = {
  title: "Privacy Policy",
  lead: "This policy explains what NIVEX collects when you use the NIVEX website, the Device ID service and the NIVEX Windows application, why it is collected, and how long it is kept. It describes how the service works at the time of writing.",
  sections: [
    { id: "scope", title: "1. Purpose and scope", blocks: [
      p("This policy applies to the NIVEX website (nivex.xyz.ci), the Device ID and activation service behind it, and the NIVEX Windows application when it contacts that service."),
      p("The PC diagnostics, monitoring, network tests, workflows, profiles and reports in the Windows application run on your PC. Their results are stored locally and are not sent to the NIVEX server. The only part of the application that contacts the NIVEX server is device authentication."),
    ] },
    { id: "items", title: "2. Personal information we process", blocks: [
      table(["Item", "Details"], [
        ["Device ID", "A public identifier generated when you create a device."],
        ["Activation and recovery codes", "Only hashes of both codes are stored for verification. Until a device is activated, its activation code is also stored in encrypted form so that the IP address that created it can look it up again. That encrypted copy is erased when the device is activated."],
        ["Installation data", "The installation public key and a random installation ID created by the application. The private key stays on your PC."],
        ["Session data", "Hashes of refresh tokens and short-lived challenge records."],
        ["IP address (hashed)", "Your connection IP address is read by the server for security checks and rate limits. It is stored only as a salted hash. The raw IP address is not stored in the database. An IP address you type in is compared with the address the server sees, and a mismatch is rejected."],
        ["Event records", "Authentication events (event type, result, Device ID, time) and abuse-prevention events."],
        ["Request-replay records", "Records that let a repeated request return the same result, which can contain the response of that request, including codes shown to you."],
        ["Security-check token", "The Cloudflare Turnstile token and your IP address are sent to Cloudflare to verify that the request is not automated."],
      ]),
      p("We do not collect a password, a personal email address for device users, payment card numbers, MAC addresses, hardware serial numbers or your name. Payments are not part of this version."),
    ] },
    { id: "purposes", title: "3. Purposes of processing", blocks: [
      ul("Creating and authenticating a Device ID and activating an installation.", "Keeping sessions, revoking access and supporting recovery or deletion of a device.", "Limiting each IP address to three Device IDs and letting the same IP address look up the devices it created, to reduce automated and repeated abuse.", "Preventing abuse, applying rate limits and investigating security problems.", "Deciding the entitlement (FREE or PRO) of a device."),
    ] },
    { id: "methods", title: "4. How information is collected", blocks: [
      p("Information is collected automatically when your browser or the Windows application calls the NIVEX API, and from the values you enter on the website (the IP address, the Device ID and the recovery code). Nothing is collected by purchasing or registering an account, because neither exists in this version."),
      p("Before the IP verification, lookup or Device ID creation on the website, you must select an unchecked consent box yourself. The server rejects these requests when they do not name the current consent version, and it stores the accepted version and the time with the new Device ID (no IP address is stored in that record). You can refuse: browsing and downloading remain available, but a Device ID cannot be created and the IP-based lookup cannot be used. Separately from this consent, hosting and Cloudflare infrastructure can process your connection IP address when you visit the website; their log retention and storage location have not been verified."),
    ] },
    { id: "retention", title: "5. Retention periods", blocks: [
      table(["Data", "Retention"], [
        ["Device record, hashed credentials, installation key", "Until the device is deleted."],
        ["Refresh tokens", "Until they expire or are revoked."],
        ["Authentication events", "180 days, then deleted by a daily job."],
        ["Abuse-prevention events", "30 days, then deleted by a daily job."],
        ["Request-replay records", "24 hours."],
        ["Hashed IP record behind the three-device limit", "Kept after a device is deleted so that the limit cannot be reset. A deletion schedule for this record has not been defined yet."],
        ["Operator audit records", "May keep the Device ID and the time of an operator action. A retention period has not been defined yet."],
      ]),
    ] },
    { id: "destruction", title: "6. Deletion procedure and method", blocks: [
      p("Using the Support page with the device's recovery code and a new security check, you can delete a device. This removes the device record, its credential, sessions, entitlements, challenges and authentication events. Expired data is removed by a daily maintenance job. Records are deleted from the database; this policy does not describe any physical media destruction."),
    ] },
    { id: "third-party", title: "7. Provision to third parties", blocks: [
      p("We do not sell personal information or provide it to third parties for their own purposes. Information may be disclosed when required by law or a valid legal process."),
    ] },
    { id: "outsourcing", title: "8. Outsourcing of processing", blocks: [
      p("The service runs on Cloudflare infrastructure (the API runs on Cloudflare Workers, the data is stored in a Cloudflare database, and the security check uses Cloudflare Turnstile). The website is also delivered through a hosting provider. These providers process data only as needed to run the service."),
      p("The exact data locations, log retention and contract terms of these providers have not been verified by this document. The operator must confirm them before release."),
    ] },
    { id: "transfer", title: "9. Transfer abroad", blocks: [
      p("Cloudflare and the hosting provider operate global networks, so data can be processed in countries other than yours. The specific countries have not been confirmed. Use of the service means your data may be transferred as described here."),
    ] },
    { id: "rights", title: "10. Your rights and how to exercise them", blocks: [
      p("You may ask to see, correct, delete or stop the processing of information about you, as far as applicable law provides."),
      ul("To see the Device IDs created from an IP address, use the IP lookup on the website.", "To delete a device, use the Support page with the recovery code.", "For other requests, write to the privacy contact in section 14."),
      p("Because the service does not store names or raw IP addresses, we may need your Device ID and recovery code to identify the records that belong to you."),
    ] },
    { id: "security", title: "11. Security measures", blocks: [
      ul("Activation codes, recovery codes, refresh tokens and IP addresses are stored as hashes. The temporary activation code copy is encrypted.", "Requests are protected by the Cloudflare Turnstile check, rate limits and the three-per-IP limit.", "Connections use HTTPS.", "Access to the operator console is separate from device sign-in."),
      p("No system is completely secure. Anyone who shares your public IP address, for example on a shared network, can look up the unactivated codes created from it, so activate a new installation promptly."),
    ] },
    { id: "cookies", title: "12. Cookies and similar technologies", blocks: [
      p("The website does not set advertising or analytics cookies. It uses your browser's storage for your language choice (localStorage) and, for the current tab only, the device you just created and a request key (sessionStorage), which are cleared when the tab is closed. Cloudflare Turnstile may use its own storage or cookies to run the security check."),
    ] },
    { id: "children", title: "13. Children and minors", blocks: [
      p("NIVEX is not aimed at children, and the service does not collect age information. If you believe a child has used the service, contact us so the related device can be deleted."),
    ] },
    { id: "contact", title: "14. Privacy contact", blocks: [
      table(["Item", "Details"], [
        ["Operator", "{{operator}}"],
        ["Privacy email", "{{email}}"],
        ["Support page", "/support"],
      ]),
      p("The operator and email values are published only once the operator has confirmed them."),
    ] },
    { id: "changes", title: "15. Changes to this policy", blocks: [
      p("When this policy changes, the updated version is published on this page with a new last-updated date. Important changes will be announced on the website before they apply where practicable."),
    ] },
  ],
};

const privacyKo: PolicyDoc = {
  title: "개인정보 처리방침",
  lead: "이 방침은 NIVEX 웹사이트, Device ID 서비스, NIVEX Windows 프로그램을 이용할 때 NIVEX가 어떤 정보를 어떤 목적으로 얼마나 오래 처리하는지 설명합니다. 작성 시점의 실제 서비스 동작을 기준으로 합니다.",
  sections: [
    { id: "scope", title: "1. 목적 및 적용 범위", blocks: [
      p("이 방침은 NIVEX 웹사이트(nivex.xyz.ci), 그 뒤에서 동작하는 Device ID 및 활성화 서비스, 그리고 해당 서비스에 접속하는 NIVEX Windows 프로그램에 적용됩니다."),
      p("Windows 프로그램의 PC 진단, 모니터링, 네트워크 측정, 워크플로, 프로필, 보고서는 사용자의 PC에서 실행됩니다. 그 결과는 PC에 저장되며 NIVEX 서버로 전송되지 않습니다. 프로그램에서 NIVEX 서버와 통신하는 부분은 장치 인증뿐입니다."),
    ] },
    { id: "items", title: "2. 처리하는 개인정보 항목", blocks: [
      table(["항목", "내용"], [
        ["Device ID", "장치를 만들 때 생성되는 공개 식별자입니다."],
        ["활성화 코드·복구 코드", "검증용으로 두 코드의 해시만 저장합니다. 장치가 활성화되기 전까지는 만든 IP가 다시 조회할 수 있도록 활성화 코드의 암호화된 사본도 보관하며, 활성화되면 이 사본은 삭제됩니다."],
        ["설치 정보", "프로그램이 만든 설치 공개 키와 무작위 설치 ID입니다. 개인 키는 사용자의 PC에만 있습니다."],
        ["세션 정보", "리프레시 토큰의 해시와 수명이 짧은 챌린지 기록입니다."],
        ["IP 주소(해시)", "서버는 보안 확인과 요청 제한을 위해 접속 IP 주소를 읽으며, 솔트가 적용된 해시로만 저장합니다. 원본 IP 주소는 데이터베이스에 저장하지 않습니다. 사용자가 입력한 IP는 서버가 본 접속 IP와 비교하며, 다르면 거부됩니다."],
        ["이벤트 기록", "인증 이벤트(종류, 결과, Device ID, 시각)와 남용 방지 이벤트입니다."],
        ["요청 재생 기록", "같은 요청이 반복될 때 같은 결과를 돌려주기 위한 기록으로, 사용자에게 표시된 코드를 포함한 응답 내용이 들어 있을 수 있습니다."],
        ["보안 확인 토큰", "요청이 자동화된 것이 아님을 확인하기 위해 Cloudflare Turnstile 토큰과 IP 주소가 Cloudflare로 전송됩니다."],
      ]),
      p("비밀번호, 장치 사용자의 개인 이메일, 결제 카드 번호, MAC 주소, 하드웨어 일련번호, 이름은 수집하지 않습니다. 이 버전에는 결제가 포함되지 않습니다."),
    ] },
    { id: "purposes", title: "3. 개인정보 처리 목적", blocks: [
      ul("Device ID의 생성·인증과 설치의 활성화", "세션 유지, 접근 차단, 장치의 복구와 삭제 지원", "IP당 Device ID 3개 제한과, 같은 IP에서 자신이 만든 장치를 조회하는 기능 제공(자동화된 반복 남용 완화)", "남용 방지, 요청 횟수 제한, 보안 문제 조사", "장치 권한(FREE 또는 PRO) 결정"),
    ] },
    { id: "methods", title: "4. 개인정보의 수집 및 처리 방법", blocks: [
      p("브라우저나 Windows 프로그램이 NIVEX API를 호출할 때 자동으로 수집되며, 웹사이트에서 직접 입력한 값(IP 주소, Device ID, 복구 코드)도 처리합니다. 이 버전에는 회원가입이나 결제가 없으므로 이를 통한 수집은 없습니다."),
      p("웹사이트에서 IP 인증, 조회 또는 Device ID 생성을 하기 전에 처음부터 선택되어 있지 않은 동의 체크박스를 직접 선택해야 합니다. 서버는 현재 동의 버전이 포함되지 않은 요청을 거부하며, 새 Device ID와 함께 동의한 버전과 시각을 저장합니다(이 기록에는 IP 주소가 저장되지 않습니다). 동의를 거부해도 둘러보기와 다운로드는 가능하지만 Device ID를 만들거나 IP 기반 조회를 할 수 없습니다. 이 동의와는 별개로 웹사이트에 접속하면 호스팅과 Cloudflare 인프라가 접속 IP 주소를 처리할 수 있으며, 그 로그 보관 기간과 저장 위치는 확인되지 않았습니다."),
    ] },
    { id: "retention", title: "5. 보유 및 이용 기간", blocks: [
      table(["데이터", "보유 기간"], [
        ["장치 기록, 해시된 자격 증명, 설치 키", "장치가 삭제될 때까지"],
        ["리프레시 토큰", "만료되거나 폐기될 때까지"],
        ["인증 이벤트", "180일 후 매일 실행되는 작업으로 삭제"],
        ["남용 방지 이벤트", "30일 후 매일 실행되는 작업으로 삭제"],
        ["요청 재생 기록", "24시간"],
        ["3개 제한의 근거가 되는 해시된 IP 기록", "제한을 초기화할 수 없도록 장치를 삭제한 뒤에도 보관합니다. 이 기록의 삭제 일정은 아직 정해지지 않았습니다."],
        ["운영자 감사 기록", "운영자 조치의 Device ID와 시각이 남을 수 있습니다. 보유 기간은 아직 정해지지 않았습니다."],
      ]),
    ] },
    { id: "destruction", title: "6. 파기 절차 및 방법", blocks: [
      p("지원 페이지에서 장치의 복구 코드와 새 보안 확인으로 장치를 삭제할 수 있습니다. 삭제하면 장치 기록, 자격 증명, 세션, 권한, 챌린지, 인증 이벤트가 제거됩니다. 만료된 데이터는 매일 실행되는 정리 작업이 제거합니다. 기록은 데이터베이스에서 삭제되며, 이 방침은 별도의 물리 매체 파기를 설명하지 않습니다."),
    ] },
    { id: "third-party", title: "7. 개인정보의 제3자 제공", blocks: [
      p("개인정보를 판매하거나 제3자의 목적을 위해 제공하지 않습니다. 법령이나 적법한 절차에 따라 요구되는 경우에는 제공될 수 있습니다."),
    ] },
    { id: "outsourcing", title: "8. 개인정보 처리 업무의 위탁", blocks: [
      p("서비스는 Cloudflare 인프라에서 동작합니다(API는 Cloudflare Workers, 데이터는 Cloudflare 데이터베이스, 보안 확인은 Cloudflare Turnstile). 웹사이트는 호스팅 제공자를 통해서도 제공됩니다. 이 제공자들은 서비스 운영에 필요한 범위에서만 데이터를 처리합니다."),
      p("이 제공자들의 정확한 데이터 위치, 로그 보유 기간, 계약 조건은 이 문서에서 확인하지 않았으며, 운영자가 공개 전에 확인해야 합니다."),
    ] },
    { id: "transfer", title: "9. 개인정보의 국외 이전", blocks: [
      p("Cloudflare와 호스팅 제공자는 전 세계 네트워크를 운영하므로 데이터가 사용자의 국가 밖에서 처리될 수 있습니다. 구체적인 국가는 확인되지 않았습니다. 서비스를 이용하면 여기에 설명한 방식으로 데이터가 이전될 수 있습니다."),
    ] },
    { id: "rights", title: "10. 이용자의 권리 및 행사 방법", blocks: [
      p("관련 법령이 허용하는 범위에서 본인에 관한 정보의 열람, 정정, 삭제, 처리 정지를 요청할 수 있습니다."),
      ul("IP 주소로 만든 Device ID는 웹사이트의 IP 조회 기능으로 확인할 수 있습니다.", "장치 삭제는 지원 페이지에서 복구 코드로 할 수 있습니다.", "그 밖의 요청은 14항의 개인정보 문의처로 보내 주세요."),
      p("서비스는 이름이나 원본 IP 주소를 저장하지 않으므로, 본인의 기록을 식별하려면 Device ID와 복구 코드가 필요할 수 있습니다."),
    ] },
    { id: "security", title: "11. 안전성 확보 조치", blocks: [
      ul("활성화 코드, 복구 코드, 리프레시 토큰, IP 주소는 해시로 저장하며, 임시 활성화 코드 사본은 암호화합니다.", "Cloudflare Turnstile 확인, 요청 횟수 제한, IP당 3개 제한으로 요청을 보호합니다.", "연결에는 HTTPS를 사용합니다.", "운영자 콘솔은 장치 로그인과 분리되어 있습니다."),
      p("완벽하게 안전한 시스템은 없습니다. 공유 네트워크처럼 공인 IP를 함께 쓰는 사람은 그 IP로 만든 미활성 코드를 조회할 수 있으므로, 새 설치는 바로 활성화하세요."),
    ] },
    { id: "cookies", title: "12. 쿠키 및 유사 기술", blocks: [
      p("웹사이트는 광고나 분석용 쿠키를 설정하지 않습니다. 브라우저 저장소는 언어 선택(localStorage)과, 현재 탭에 한해 방금 만든 장치와 요청 키(sessionStorage)에만 사용하며, 후자는 탭을 닫으면 지워집니다. Cloudflare Turnstile은 보안 확인을 위해 자체 저장소나 쿠키를 사용할 수 있습니다."),
    ] },
    { id: "children", title: "13. 아동 및 청소년", blocks: [
      p("NIVEX는 아동을 대상으로 하지 않으며 서비스는 연령 정보를 수집하지 않습니다. 아동이 서비스를 이용했다고 생각되면 관련 장치를 삭제할 수 있도록 문의해 주세요."),
    ] },
    { id: "contact", title: "14. 개인정보 문의처", blocks: [
      table(["항목", "내용"], [
        ["운영 주체", "{{operator}}"],
        ["개인정보 문의 이메일", "{{email}}"],
        ["지원 페이지", "/support"],
      ]),
      p("운영 주체와 이메일은 운영자가 확인한 뒤에만 게시됩니다."),
    ] },
    { id: "changes", title: "15. 방침의 변경", blocks: [
      p("이 방침이 바뀌면 이 페이지에 새로운 최종 수정일과 함께 게시합니다. 중요한 변경은 가능한 경우 적용 전에 웹사이트에 안내합니다."),
    ] },
  ],
};

const termsEn: PolicyDoc = {
  title: "Terms of Use",
  lead: "These terms govern your use of the NIVEX website, the Device ID service and the NIVEX Windows application. They describe the service as it works today and do not add accounts, subscriptions or refunds that do not exist.",
  sections: [
    { id: "purpose", title: "1. Purpose and scope", blocks: [
      p("These terms set out the rights and duties between the operator of NIVEX and the people who use the NIVEX website, the Device ID service and the Windows application. Using the service means you accept them."),
    ] },
    { id: "definitions", title: "2. Definitions", blocks: [
      table(["Term", "Meaning"], [
        ["Service", "The NIVEX website, the Device ID and activation API, and the Windows application."],
        ["Device ID", "A public identifier created for one installation."],
        ["Activation code", "A one-time code, shown after creation, used to activate an installation."],
        ["Recovery code", "A secret code, shown once, used to detach an installation or delete a device."],
        ["Entitlement", "The plan of a device: FREE by default, or PRO when granted by the server."],
      ]),
    ] },
    { id: "service", title: "3. Nature of the service and features", blocks: [
      p("NIVEX is Windows software for checking the state of your PC, supported diagnostics, monitoring, network tests, task workflows, profiles and reports, plus a Device ID service that authenticates an installation. Features may be in development and are described on the About page."),
      p("NIVEX does not promise a performance increase or that every problem is found or fixed. Diagnostic findings are based on what Windows reports."),
    ] },
    { id: "conditions", title: "4. Conditions of use", blocks: [
      ul("You need a Windows PC that meets the system requirements and an internet connection for activation.", "You must follow these terms and applicable law.", "You are responsible for confirming that an action that changes system settings is right for your PC. NIVEX asks for confirmation before such actions."),
    ] },
    { id: "license", title: "5. Software license and permitted use", blocks: [
      p("The operator grants you a limited, non-exclusive, non-transferable, revocable right to install and use NIVEX on PCs you own or are authorised to manage. The software is licensed, not sold. All other rights are reserved."),
      p("Sale conditions, prices and any refund terms are not part of this version because payments are not connected. If paid features are offered later, their conditions will be shown at the time of sale, and your statutory consumer rights are not limited by these terms."),
    ] },
    { id: "device-codes", title: "6. Device ID and activation codes", blocks: [
      ul("A Device ID is created on the website after a security check and confirmation of your IP address. At most three Device IDs can be created per IP address.", "The activation code activates one installation. The recovery code can detach the installation and issue a new activation code, or delete the device.", "A Device ID alone does not grant access to an activated installation.", "FREE is the default plan. PRO is decided by the server, and a local setting cannot grant it.", "No fixed expiry date is defined for a device. Sessions use short-lived tokens that are renewed automatically while the device remains valid."),
    ] },
    { id: "credentials", title: "7. Your responsibility for credentials", blocks: [
      p("Keep the activation code and recovery code private and do not give them to others. Without the recovery code, an activated installation can be restored only by operator deletion. You are responsible for use made with codes you disclosed."),
    ] },
    { id: "acceptable", title: "8. Permitted and prohibited use", blocks: [
      p("Do not:"),
      ul("create Device IDs by automated means or evade the per-IP limit or security checks;", "attempt to take over, guess or attack another installation or code;", "interfere with or overload the service, or probe it for weaknesses;", "reverse engineer, copy, redistribute or modify the software except where the law permits;", "use the service for unlawful purposes."),
    ] },
    { id: "restriction", title: "9. Restriction and blocking", blocks: [
      p("The operator may revoke a device, restore it, or limit access if these terms are breached, if abuse or a security risk is detected, or if law requires it. Where practicable the operator will give the reason when you contact support. You can ask for review through the support channel."),
      p("A blocked device can be reissued only through the recovery process or by the operator's decision."),
    ] },
    { id: "updates", title: "10. Updates and changes to features", blocks: [
      p("The software and website may be updated, and features may be added, changed or removed. Updates are offered through the official download page. Material changes that affect you will be shown on the website."),
    ] },
    { id: "suspension", title: "11. Interruption and termination", blocks: [
      p("The service may be interrupted for maintenance, by failures of infrastructure providers, or for reasons outside the operator's control. The operator may end the service with notice on the website. You may stop using NIVEX at any time and delete your device on the Support page."),
    ] },
    { id: "ip", title: "12. Intellectual property", blocks: [
      p("The NIVEX software, design and documentation in which NIVEX holds rights are protected by applicable law. Third-party components, libraries and fonts remain the property of their owners and are used under their own licences. Using the service does not transfer any rights to you except the licence above."),
    ] },
    { id: "feedback", title: "13. User content and feedback", blocks: [
      p("The service does not host user content. Feedback you send to support may be used to improve NIVEX without obligation to you. Do not send secrets such as recovery codes."),
    ] },
    { id: "privacy", title: "14. Relationship to the Privacy Policy", blocks: [
      p("How information is handled is described in the Privacy Policy, which forms part of these terms."),
    ] },
    { id: "liability", title: "15. Warranty and limitation of liability", blocks: [
      p("The service is provided as it is. To the extent permitted by law, the operator does not warrant that it is uninterrupted, error-free or fits a particular purpose, and is not liable for indirect or consequential loss."),
      p("Nothing in these terms excludes or limits liability that cannot be excluded by law, including for intent or gross negligence, or limits your statutory consumer rights. You are responsible for backing up your data before changing system settings."),
    ] },
    { id: "disputes", title: "16. Remedies and dispute handling", blocks: [
      p("Contact support first so the issue can be resolved. The governing law and the competent court have not been specified yet. The operator must state them before release. Mandatory consumer-protection rules of your place of residence are not affected."),
    ] },
    { id: "changes", title: "17. Changes to these terms", blocks: [
      p("When the terms change, the new version is published here with a new last-updated date. Continued use after the effective date means you accept the change. If you disagree, stop using the service."),
    ] },
    { id: "contact", title: "18. Contact and effective date", blocks: [
      table(["Item", "Details"], [
        ["Operator", "{{operator}}"],
        ["Support email", "{{email}}"],
        ["Support page", "/support"],
      ]),
      p("The operator and email values are published only once the operator has confirmed them. The effective date is shown at the top of this page once the operator sets it."),
    ] },
  ],
};

const termsKo: PolicyDoc = {
  title: "이용약관",
  lead: "이 약관은 NIVEX 웹사이트, Device ID 서비스, NIVEX Windows 프로그램의 이용에 적용됩니다. 현재 서비스가 실제로 동작하는 방식을 설명하며, 존재하지 않는 계정, 구독, 환불 제도를 추가하지 않습니다.",
  sections: [
    { id: "purpose", title: "1. 목적 및 적용 범위", blocks: [
      p("이 약관은 NIVEX 운영자와 NIVEX 웹사이트, Device ID 서비스, Windows 프로그램을 이용하는 사람 사이의 권리와 의무를 정합니다. 서비스를 이용하면 이 약관에 동의한 것으로 봅니다."),
    ] },
    { id: "definitions", title: "2. 용어의 정의", blocks: [
      table(["용어", "의미"], [
        ["서비스", "NIVEX 웹사이트, Device ID 및 활성화 API, Windows 프로그램입니다."],
        ["Device ID", "설치 하나를 위해 만들어지는 공개 식별자입니다."],
        ["활성화 코드", "생성 후 표시되며 설치를 활성화하는 데 쓰는 코드입니다."],
        ["복구 코드", "한 번만 표시되며 설치를 분리하거나 장치를 삭제할 때 쓰는 비밀 코드입니다."],
        ["권한", "장치의 플랜으로, 기본은 FREE이며 서버가 부여하면 PRO입니다."],
      ]),
    ] },
    { id: "service", title: "3. 서비스의 성격과 제공 기능", blocks: [
      p("NIVEX는 PC 상태 확인, 지원되는 진단, 모니터링, 네트워크 측정, 작업 워크플로, 프로필, 보고서를 제공하는 Windows 소프트웨어이며, 설치를 인증하는 Device ID 서비스를 포함합니다. 일부 기능은 개발 중일 수 있으며 소개 페이지에 설명되어 있습니다."),
      p("NIVEX는 성능 향상이나 모든 문제의 발견·해결을 약속하지 않습니다. 진단 결과는 Windows가 보고하는 정보에 근거합니다."),
    ] },
    { id: "conditions", title: "4. 이용 조건", blocks: [
      ul("시스템 요구 사항을 충족하는 Windows PC와 활성화를 위한 인터넷 연결이 필요합니다.", "이 약관과 관련 법령을 지켜야 합니다.", "시스템 설정을 바꾸는 작업이 내 PC에 맞는지는 사용자가 확인해야 합니다. NIVEX는 그런 작업 전에 확인을 요청합니다."),
    ] },
    { id: "license", title: "5. 소프트웨어 라이선스 및 사용 권한", blocks: [
      p("운영자는 사용자가 소유하거나 관리할 권한이 있는 PC에 NIVEX를 설치해 사용할 수 있는 제한적, 비독점적, 양도 불가, 철회 가능한 권리를 부여합니다. 소프트웨어는 판매되는 것이 아니라 사용이 허락되는 것이며, 그 밖의 권리는 운영자에게 있습니다."),
      p("이 버전은 결제가 연결되어 있지 않으므로 판매 조건, 가격, 환불 조건은 없습니다. 이후 유료 기능이 제공되면 그 조건을 판매 시점에 안내하며, 이 약관은 소비자의 법적 권리를 제한하지 않습니다."),
    ] },
    { id: "device-codes", title: "6. Device ID와 활성화 코드", blocks: [
      ul("Device ID는 웹사이트에서 보안 확인과 IP 주소 확인을 거쳐 만듭니다. IP 주소당 Device ID는 최대 3개까지 만들 수 있습니다.", "활성화 코드는 설치 하나를 활성화합니다. 복구 코드는 설치를 분리하고 새 활성화 코드를 발급하거나 장치를 삭제할 수 있습니다.", "Device ID만으로는 이미 활성화된 설치에 접근할 수 없습니다.", "기본 플랜은 FREE입니다. PRO는 서버가 결정하며 로컬 설정으로 부여할 수 없습니다.", "장치에 정해진 만료일은 없습니다. 세션은 수명이 짧은 토큰을 쓰며, 장치가 유효한 동안 자동으로 갱신됩니다."),
    ] },
    { id: "credentials", title: "7. 인증 정보 관리 책임", blocks: [
      p("활성화 코드와 복구 코드는 비공개로 보관하고 다른 사람에게 알려주지 마세요. 복구 코드가 없으면 활성화된 설치는 운영자의 삭제로만 복원할 수 있습니다. 공개한 코드로 이루어진 이용은 사용자의 책임입니다."),
    ] },
    { id: "acceptable", title: "8. 허용되는 사용과 금지되는 행위", blocks: [
      p("다음 행위를 하지 마세요."),
      ul("자동화된 방법으로 Device ID를 만들거나 IP당 제한 또는 보안 확인을 회피하는 행위", "다른 설치나 코드를 가로채려 하거나 추측·공격하는 행위", "서비스를 방해하거나 과부하를 일으키거나 취약점을 탐색하는 행위", "법령이 허용하는 경우를 제외하고 소프트웨어를 역공학하거나 복제, 재배포, 수정하는 행위", "불법적인 목적으로 서비스를 이용하는 행위"),
    ] },
    { id: "restriction", title: "9. 이용 제한 및 차단", blocks: [
      p("약관 위반, 남용이나 보안 위험의 발견, 법령상 요구가 있으면 운영자는 장치를 차단하거나 복원하거나 접근을 제한할 수 있습니다. 가능한 경우 지원에 문의할 때 사유를 안내하며, 지원 채널로 재검토를 요청할 수 있습니다."),
      p("차단된 장치는 복구 절차나 운영자의 결정으로만 다시 발급될 수 있습니다."),
    ] },
    { id: "updates", title: "10. 업데이트와 기능 변경", blocks: [
      p("소프트웨어와 웹사이트는 업데이트될 수 있으며 기능이 추가, 변경, 삭제될 수 있습니다. 업데이트는 공식 다운로드 페이지로 제공됩니다. 이용자에게 영향을 주는 중요한 변경은 웹사이트에 안내합니다."),
    ] },
    { id: "suspension", title: "11. 서비스 중단 및 종료", blocks: [
      p("점검, 인프라 제공자의 장애, 운영자가 통제할 수 없는 사유로 서비스가 중단될 수 있습니다. 운영자는 웹사이트에 안내한 뒤 서비스를 종료할 수 있습니다. 사용자는 언제든 NIVEX 사용을 멈추고 지원 페이지에서 장치를 삭제할 수 있습니다."),
    ] },
    { id: "ip", title: "12. 지식재산권", blocks: [
      p("NIVEX가 권리를 가진 소프트웨어, 디자인, 문서는 관련 법령에 따라 보호됩니다. 제3자의 구성 요소, 라이브러리, 글꼴은 각 권리자에게 속하며 각자의 라이선스에 따라 사용됩니다. 서비스 이용으로 위 라이선스 외의 권리가 이전되지는 않습니다."),
    ] },
    { id: "feedback", title: "13. 이용자 콘텐츠 및 피드백", blocks: [
      p("서비스는 이용자 콘텐츠를 호스팅하지 않습니다. 지원에 보낸 피드백은 이용자에 대한 의무 없이 NIVEX를 개선하는 데 쓰일 수 있습니다. 복구 코드 같은 비밀 정보는 보내지 마세요."),
    ] },
    { id: "privacy", title: "14. 개인정보 처리방침과의 관계", blocks: [
      p("정보를 어떻게 처리하는지는 개인정보 처리방침에 설명되어 있으며, 이 방침은 이 약관의 일부입니다."),
    ] },
    { id: "liability", title: "15. 보증 및 책임 제한", blocks: [
      p("서비스는 현재 상태대로 제공됩니다. 법령이 허용하는 범위에서 운영자는 서비스가 중단되지 않거나 오류가 없거나 특정 목적에 맞는다고 보증하지 않으며, 간접 손해나 결과적 손해에 대해 책임지지 않습니다."),
      p("이 약관은 고의 또는 중대한 과실 등 법령상 배제할 수 없는 책임을 배제하거나 소비자의 법적 권리를 제한하지 않습니다. 시스템 설정을 바꾸기 전에 데이터를 백업할 책임은 사용자에게 있습니다."),
    ] },
    { id: "disputes", title: "16. 권리 구제 및 분쟁 처리", blocks: [
      p("먼저 지원에 문의해 문제를 해결하세요. 준거법과 관할 법원은 아직 정해지지 않았으며, 운영자가 공개 전에 명시해야 합니다. 거주 지역의 강행 소비자 보호 규정은 영향을 받지 않습니다."),
    ] },
    { id: "changes", title: "17. 약관의 변경", blocks: [
      p("약관이 바뀌면 새 버전을 최종 수정일과 함께 이 페이지에 게시합니다. 시행일 이후에도 계속 이용하면 변경에 동의한 것으로 봅니다. 동의하지 않으면 서비스 이용을 중단하세요."),
    ] },
    { id: "contact", title: "18. 문의처 및 시행일", blocks: [
      table(["항목", "내용"], [
        ["운영 주체", "{{operator}}"],
        ["지원 이메일", "{{email}}"],
        ["지원 페이지", "/support"],
      ]),
      p("운영 주체와 이메일은 운영자가 확인한 뒤에만 게시됩니다. 시행일은 운영자가 정하면 이 페이지 상단에 표시됩니다."),
    ] },
  ],
};

export const policies: Record<PolicyKey, Record<PolicyLang, PolicyDoc>> = {
  privacy: { en: privacyEn, ko: privacyKo },
  terms: { en: termsEn, ko: termsKo },
};

export interface PolicyUi {
  toc: string;
  updated: string;
  effective: string;
  effectivePending: string;
  notPublished: string;
  draftTitle: string;
  draftBody: string;
  prev: string;
  next: string;
  top: string;
  other: Record<PolicyKey, string>;
  supportTitle: string;
  supportBody: string;
  supportLink: string;
  backHome: string;
  langLabel: string;
}

export const policyUi: Record<PolicyLang, PolicyUi> = {
  en: {
    toc: "Contents",
    updated: "Last updated",
    effective: "Effective date",
    effectivePending: "Not set yet",
    notPublished: "Not published yet",
    draftTitle: "Awaiting operator confirmation",
    draftBody: "The operator details, effective date and legal review for this document are not complete yet, so it must not be treated as a finished legal document.",
    prev: "Previous",
    next: "Next",
    top: "Back to top",
    other: { privacy: "Privacy Policy", terms: "Terms of Use" },
    supportTitle: "Questions?",
    supportBody: "Contact support for questions about this document, or to delete a device.",
    supportLink: "Go to Support",
    backHome: "NIVEX home",
    langLabel: "Language",
  },
  ko: {
    toc: "목차",
    updated: "최종 수정일",
    effective: "시행일",
    effectivePending: "아직 정해지지 않음",
    notPublished: "아직 게시되지 않음",
    draftTitle: "운영자 확인 대기 중",
    draftBody: "이 문서의 운영자 정보, 시행일, 법률 검토가 아직 끝나지 않았으므로 완성된 법적 문서로 취급해서는 안 됩니다.",
    prev: "이전",
    next: "다음",
    top: "맨 위로",
    other: { privacy: "개인정보 처리방침", terms: "이용약관" },
    supportTitle: "문의가 있으신가요?",
    supportBody: "이 문서에 대한 문의나 장치 삭제는 지원 페이지에서 할 수 있습니다.",
    supportLink: "지원 페이지로 이동",
    backHome: "NIVEX 홈",
    langLabel: "언어",
  },
};

export const footerCopy: Record<PolicyLang, { rights: string; notice: string }> = {
  en: {
    rights: "© 2026 NIVEX. All rights reserved.",
    notice: "The software, designs, documentation, and other original materials in which NIVEX holds rights are protected under applicable copyright laws. Unauthorized copying, distribution, or modification is prohibited except where permitted by law or authorized by the rights holder.",
  },
  ko: {
    rights: "© 2026 NIVEX. All rights reserved.",
    notice: "NIVEX의 소프트웨어, 디자인, 문서 및 기타 독창적인 콘텐츠 중 NIVEX가 권리를 보유한 자료는 관련 법령에 따라 보호됩니다. 법령상 허용되거나 권리자의 승인을 받은 경우를 제외하고 무단 복제, 배포 또는 수정을 금합니다.",
  },
};
