CREATE TABLE ip_claims (
  id TEXT PRIMARY KEY NOT NULL,
  ip_hash TEXT NOT NULL,
  device_public_id TEXT NOT NULL,
  created_at TEXT NOT NULL
);

CREATE INDEX ip_claims_ip_idx ON ip_claims (ip_hash);
