-- Daily workbook readings are taken at 08:00 local site time.
-- Leave time unknown for existing/manual readings.
ALTER TABLE vehicle_hour_readings ADD COLUMN IF NOT EXISTS reading_time time without time zone;
