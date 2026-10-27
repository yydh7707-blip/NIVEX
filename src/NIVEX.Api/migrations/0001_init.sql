CREATE TABLE devices (
  id TEXT PRIMARY KEY NOT NULL,
  public_id TEXT NOT NULL UNIQUE,
  status TEXT NOT NULL CHECK (status IN ('unclaimed', 'active', 'revoked')),
  activation_code_hash TEXT,
  recovery_code_hash TEXT NOT NULL,
  created_at TEXT NOT NULL,
  claimed_at TEXT,
  last_seen_at TEXT,
  revoked_at TEXT
);

CREATE TABLE device_credentials (
  device_id TEXT PRIMARY KEY NOT NULL REFERENCES devices(id),
  public_key_spki TEXT NOT NULL,
  installation_id TEXT NOT NULL,
  created_at TEXT NOT NULL
);

CREATE TABLE sessions (
  id TEXT PRIMARY KEY NOT NULL,
  device_id TEXT NOT NULL REFERENCES devices(id),
  refresh_token_hash TEXT NOT NULL UNIQUE,
  created_at TEXT NOT NULL,
  expires_at TEXT NOT NULL,
  revoked_at TEXT,
  replaced_by TEXT
);

CREATE INDEX sessions_device_idx ON sessions (device_id);

CREATE TABLE challenges (
  id TEXT PRIMARY KEY NOT NULL,
  device_id TEXT NOT NULL REFERENCES devices(id),
  nonce_hash TEXT NOT NULL UNIQUE,
  expires_at TEXT NOT NULL,
  used_at TEXT
);

CREATE TABLE entitlements (
  id TEXT PRIMARY KEY NOT NULL,
  device_id TEXT NOT NULL REFERENCES devices(id),
  plan TEXT NOT NULL CHECK (plan IN ('FREE', 'PRO')),
  status TEXT NOT NULL CHECK (status IN ('active', 'revoked')),
  source TEXT NOT NULL CHECK (source IN ('system', 'admin')),
  starts_at TEXT NOT NULL,
  expires_at TEXT,
  created_at TEXT NOT NULL,
  revoked_at TEXT
);

CREATE INDEX entitlements_device_idx ON entitlements (device_id);

CREATE TABLE entitlement_events (
  id TEXT PRIMARY KEY NOT NULL,
  entitlement_id TEXT,
  device_id TEXT NOT NULL,
  action TEXT NOT NULL,
  actor TEXT NOT NULL,
  detail TEXT,
  created_at TEXT NOT NULL
);

CREATE TABLE auth_events (
  id TEXT PRIMARY KEY NOT NULL,
  device_public_id TEXT,
  event_type TEXT NOT NULL,
  result TEXT NOT NULL,
  detail TEXT,
  created_at TEXT NOT NULL
);

CREATE INDEX auth_events_device_idx ON auth_events (device_public_id, created_at);

CREATE TABLE verifications (
  id TEXT PRIMARY KEY NOT NULL,
  token_hash TEXT NOT NULL UNIQUE,
  action TEXT NOT NULL,
  hostname TEXT,
  expires_at TEXT NOT NULL,
  used_at TEXT,
  created_at TEXT NOT NULL
);

CREATE TABLE verification_redemptions (
  verification_id TEXT PRIMARY KEY NOT NULL REFERENCES verifications(id),
  redeemed_at TEXT NOT NULL
);

CREATE TABLE idempotency_records (
  idempotency_key TEXT PRIMARY KEY NOT NULL,
  request_hash TEXT NOT NULL,
  state TEXT NOT NULL CHECK (state IN ('pending', 'complete')),
  status_code INTEGER,
  response_body TEXT,
  created_at TEXT NOT NULL
);

CREATE TABLE rate_limits (
  bucket_key TEXT NOT NULL,
  window_start INTEGER NOT NULL,
  count INTEGER NOT NULL,
  PRIMARY KEY (bucket_key, window_start)
);

CREATE TABLE abuse_events (
  id TEXT PRIMARY KEY NOT NULL,
  scope TEXT NOT NULL,
  reason TEXT NOT NULL,
  created_at TEXT NOT NULL
);

CREATE TABLE admin_users (
  id TEXT PRIMARY KEY NOT NULL,
  email TEXT NOT NULL UNIQUE,
  password_hash TEXT NOT NULL,
  password_salt TEXT NOT NULL,
  iterations INTEGER NOT NULL,
  created_at TEXT NOT NULL
);

CREATE TABLE admin_sessions (
  id TEXT PRIMARY KEY NOT NULL,
  admin_id TEXT NOT NULL REFERENCES admin_users(id),
  token_hash TEXT NOT NULL UNIQUE,
  expires_at TEXT NOT NULL,
  created_at TEXT NOT NULL,
  revoked_at TEXT
);

CREATE TABLE admin_audit (
  id TEXT PRIMARY KEY NOT NULL,
  admin_id TEXT,
  action TEXT NOT NULL,
  target TEXT,
  detail TEXT,
  created_at TEXT NOT NULL
);
