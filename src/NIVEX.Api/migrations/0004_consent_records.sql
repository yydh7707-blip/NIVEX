CREATE TABLE consent_records (
  id TEXT PRIMARY KEY NOT NULL,
  device_public_id TEXT NOT NULL,
  policy_version TEXT NOT NULL,
  accepted_at TEXT NOT NULL
);

CREATE INDEX consent_records_device_idx ON consent_records (device_public_id);
