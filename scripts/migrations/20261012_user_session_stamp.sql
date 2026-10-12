-- Existing tokens without session_stamp will be rejected after this update.
ALTER TABLE app_users
    ADD COLUMN IF NOT EXISTS session_stamp uuid NOT NULL DEFAULT gen_random_uuid();
